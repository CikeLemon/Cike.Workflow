using Cike.Core.Extensions.System;
using Cike.Workflow.Core.ActivityDescriptors;
using Cike.Workflow.Core.ActivityDescriptors.Internals;
using Cike.Workflow.Core.Contexts.Models;
using Cike.Workflow.Core.Exceptions;
using Cike.Workflow.Core.Helpers;
using Cike.Workflow.Core.Runners;
using Cike.Workflow.Core.Schedulers;
using Cike.Workflow.Core.Schedulers.Models;
using Cike.Workflow.Core.Variables;
using Cike.Workflow.Core.WorkflowGraphs.Models;
using System.Text.Json;
using System.Xml.Linq;

namespace Cike.Workflow.Core.Contexts;

public record ActivityCompletionCallbackEntry(ActivityExecutionContext Owner, ActivityNode Child, ActivityCompletionCallback? CompletionCallback);

public class WorkflowExecutionContext : IExecutionContext
{
    private ICollection<CancellationTokenSource> _cancellationTokenSources = new List<CancellationTokenSource>();
    private ICollection<CancellationTokenRegistration> _cancellationRegistrations = new List<CancellationTokenRegistration>();
    private IList<ActivityExecutionContext> _activityExecutionContexts;
    private readonly IList<ActivityCompletionCallbackEntry> _completionCallbackEntries = new List<ActivityCompletionCallbackEntry>();
    internal static ValueTask Complete(ActivityExecutionContext context) => context.CompleteActivityAsync();
    internal static ValueTask Noop(ActivityExecutionContext context) => default;

    private WorkflowExecutionContext(
        IServiceProvider serviceProvider,
        WorkflowGraph workflowGraph,
        long id,
        string correlationId,
        long? parentWorkflowInstanceId,
        IDictionary<string, object>? input,
        IDictionary<string, object>? properties,
        ExecuteActivityDelegate? executeDelegate,
        string? triggerActivityId,
        IEnumerable<ActivityIncident> incidents,
        IEnumerable<Bookmark> originalBookmarks,
        DateTime createdAt,
        CancellationToken cancellationToken)
    {
        ServiceProvider = serviceProvider;
        ActivityRegistry = serviceProvider.GetRequiredService<IActivityRegistry>();
        ActivityRegistryLookup = serviceProvider.GetRequiredService<IActivityRegistryLookupService>();
        Scheduler = serviceProvider.GetRequiredService<IActivitySchedulerFactory>().CreateScheduler();

        Status = WorkflowStatus.Pending;
        Id = id;
        CorrelationId = correlationId;
        ParentWorkflowInstanceId = parentWorkflowInstanceId;
        _activityExecutionContexts = new List<ActivityExecutionContext>();
        Input = input != null ? new(input, StringComparer.OrdinalIgnoreCase) : new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        Properties = properties != null ? new(properties, StringComparer.OrdinalIgnoreCase) : new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        ExecuteDelegate = executeDelegate;
        TriggerActivityId = triggerActivityId;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        CancellationToken = cancellationToken;
        Incidents = incidents.ToList();
        OriginalBookmarks = originalBookmarks.ToList();
        WorkflowGraph = workflowGraph;
        var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cancellationTokenSources.Add(linkedCancellationTokenSource);
        _cancellationRegistrations.Add(linkedCancellationTokenSource.Token.Register(CancelWorkflow));
    }

    public static async Task<WorkflowExecutionContext> CreateAsync(
        IServiceProvider serviceProvider,
        WorkflowGraph workflowGraph,
        long id,
        CancellationToken cancellationToken = default)
    {
        return await CreateAsync(
            serviceProvider,
            workflowGraph,
            id,
            new List<ActivityIncident>(),
            new List<Bookmark>(),
            DateTime.Now,
            cancellationToken: cancellationToken
        );
    }

    public static async Task<WorkflowExecutionContext> CreateAsync(
        IServiceProvider serviceProvider,
        WorkflowGraph workflowGraph,
        long id,
        string? correlationId,
        long? parentWorkflowInstanceId = null,
        IDictionary<string, object>? input = null,
        IDictionary<string, object>? properties = null,
        ExecuteActivityDelegate? executeDelegate = null,
        string? triggerActivityId = null,
        CancellationToken cancellationToken = default)
    {
        return await CreateAsync(
            serviceProvider,
            workflowGraph,
            id,
            new List<ActivityIncident>(),
            new List<Bookmark>(),
            DateTime.Now,
            correlationId,
            parentWorkflowInstanceId,
            input,
            properties,
            executeDelegate,
            triggerActivityId,
            cancellationToken
        );
    }

    public static async Task<WorkflowExecutionContext> CreateAsync(
        IServiceProvider serviceProvider,
        WorkflowGraph workflowGraph,
        WorkflowState workflowState,
        string? correlationId = null,
        long? parentWorkflowInstanceId = null,
        IDictionary<string, object>? input = null,
        IDictionary<string, object>? properties = null,
        ExecuteActivityDelegate? executeDelegate = null,
        string? triggerActivityId = null,
        CancellationToken cancellationToken = default)
    {
        var workflowExecutionContext = await CreateAsync(
            serviceProvider,
            workflowGraph,
            workflowState.Id,
            workflowState.Incidents,
            workflowState.Bookmarks,
            workflowState.CreatedAt,
            correlationId,
            parentWorkflowInstanceId,
            input,
            properties,
            executeDelegate,
            triggerActivityId,
            cancellationToken);

        var workflowStateExtractor = serviceProvider.GetRequiredService<IWorkflowStateExtractor>();
        await workflowStateExtractor.ApplyAsync(workflowExecutionContext, workflowState);

        return workflowExecutionContext;
    }

    public static async Task<WorkflowExecutionContext> CreateAsync(
        IServiceProvider serviceProvider,
        WorkflowGraph workflowGraph,
        long id,
        IEnumerable<ActivityIncident> incidents,
        IEnumerable<Bookmark> originalBookmarks,
        DateTime createdAt,
        string? correlationId = null,
        long? parentWorkflowInstanceId = null,
        IDictionary<string, object>? input = null,
        IDictionary<string, object>? properties = null,
        ExecuteActivityDelegate? executeDelegate = null,
        string? triggerActivityId = null,
        CancellationToken cancellationToken = default)
    {
        // Set up a workflow execution context.
        var workflowExecutionContext = new WorkflowExecutionContext(
            serviceProvider,
            workflowGraph,
            id,
            correlationId ?? "",
            parentWorkflowInstanceId,
            input,
            properties,
            executeDelegate,
            triggerActivityId,
            incidents,
            originalBookmarks,
            createdAt,
            cancellationToken)
        {
            MemoryRegister = workflowGraph.Workflow.CreateRegister()
        };

        workflowExecutionContext.ExpressionExecutionContext = new(serviceProvider, workflowExecutionContext.MemoryRegister, cancellationToken: cancellationToken);

        await workflowExecutionContext.SetWorkflowGraphAsync(workflowGraph);
        return workflowExecutionContext;
    }

    public async Task SetWorkflowGraphAsync(WorkflowGraph workflowGraph)
    {
        WorkflowGraph = workflowGraph;
        var nodes = workflowGraph.Nodes;

        // Register activity types.
        var activityTypes = nodes.Select(x => x.Activity.GetType()).Distinct().ToList();
        await ActivityRegistry.RegisterAsync(activityTypes, CancellationToken);

        // Update the activity execution contexts with the actual activity instances.
        foreach (var activityExecutionContext in ActivityExecutionContexts)
            activityExecutionContext.Activity = workflowGraph.NodeIdLookup[activityExecutionContext.Activity.NodeId].Activity;
    }

    public long Id { get; set; }

    public string? Name { get; set; }

    public long? ParentWorkflowInstanceId { get; set; }

    public string? TriggerActivityId { get; set; }

    public string CorrelationId { get; set; }

    public IDictionary<string, object> Properties { get; set; } = null!;

    public IDictionary<object, object> TransientProperties { get; set; } = new Dictionary<object, object>();

    public IDictionary<string, object> Input { get; set; } = null!;

    public IDictionary<string, object> Output { get; set; } = new Dictionary<string, object>();

    public WorkflowStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    public bool IsExecuting { get; set; }

    /// <summary>调试实例标记：由 WorkflowState 还原 / 回写，活动执行期可读。</summary>
    public bool IsDebug { get; set; }

    public ICollection<ActivityIncident> Incidents { get; set; }

    public ICollection<Bookmark> OriginalBookmarks { get; set; }

    public ICollection<Bookmark> Bookmarks { get; set; } = new List<Bookmark>();

    public Diff<Bookmark> BookmarksDiff => Diff.For(OriginalBookmarks, Bookmarks);

    public ExecuteActivityDelegate? ExecuteDelegate { get; set; }

    public ResumedBookmarkContext? ResumedBookmarkContext { get; set; }

    public ExpressionExecutionContext ExpressionExecutionContext { get; private set; } = null!;

    public MemoryRegister MemoryRegister { get; private set; } = null!;

    public ActivityOutputRegister ActivityOutputRegister { get; private set; } = new ActivityOutputRegister();

    public WorkflowGraph WorkflowGraph { get; private set; } = null!;

    public CancellationToken CancellationToken { get; }

    public IReadOnlyCollection<ActivityExecutionContext> ActivityExecutionContexts
    {
        get => _activityExecutionContexts.AsReadOnly();
        internal set => _activityExecutionContexts = value.ToList();
    }

    public IActivityScheduler Scheduler { get; }

    public IActivityRegistry ActivityRegistry { get; }

    public IActivityRegistryLookupService ActivityRegistryLookup { get; }

    public IActivity Activity => WorkflowGraph.Workflow;

    public WorkflowActivity Workflow => WorkflowGraph.Workflow;

    public IEnumerable<Variable> Variables => WorkflowGraph.Workflow.Variables;

    public ICollection<ActivityCompletionCallbackEntry> CompletionCallbacks => new ReadOnlyCollection<ActivityCompletionCallbackEntry>(_completionCallbackEntries);

    public IServiceProvider ServiceProvider { get; } = null!;

    public T GetRequiredService<T>() where T : notnull => ServiceProvider.GetRequiredService<T>();

    public object GetRequiredService(Type serviceType) => ServiceProvider.GetRequiredService(serviceType);

    internal void TransitionTo(WorkflowStatus status)
    {
        if (Status.IsFinished())
            throw new($"Cannot transition from {Status} to {status}");

        Status = status;
        UpdatedAt = DateTime.Now;

        if (status.IsFinished())
            FinishedAt = UpdatedAt;

        if (status.IsFinished() || status == WorkflowStatus.Suspended)
        {
            foreach (var registration in _cancellationRegistrations)
                registration.Dispose();
        }
    }

    public ActivityWorkItem Schedule(ActivityNode activityNode, ActivityExecutionContext owner, ScheduleWorkOptions? options = null)
    {
        var schedulerStrategy = GetRequiredService<IWorkflowExecutionContextSchedulerStrategy>();
        return schedulerStrategy.Schedule(this, activityNode, owner, options);
    }

    public ActivityWorkItem ScheduleWorkflow(
        IDictionary<string, object>? input = null,
        IEnumerable<Variable>? variables = null,
        long? schedulingActivityExecutionId = null,
        long? schedulingWorkflowInstanceId = null,
        int? schedulingCallStackDepth = null)
    {
        var workflow = Workflow;
        var workItem = new ActivityWorkItem(
            workflow,
            input: input,
            variables: variables,
            schedulingActivityExecutionId: schedulingActivityExecutionId,
            schedulingWorkflowInstanceId: schedulingWorkflowInstanceId,
            schedulingCallStackDepth: schedulingCallStackDepth);
        Scheduler.Schedule(workItem);
        return workItem;
    }

    public ActivityWorkItem? ScheduleBookmark(Bookmark bookmark, IDictionary<string, object>? input = null, IEnumerable<Variable>? variables = null)
    {
        // Get the activity execution context that owns the bookmark.
        var bookmarkedActivityContext = ActivityExecutionContexts.FirstOrDefault(x => x.Id == bookmark.ActivityInstanceId);
        var logger = GetRequiredService<ILogger<WorkflowExecutionContext>>();

        if (bookmarkedActivityContext == null)
        {
            logger.LogWarning("Could not find activity execution context with ID {ActivityInstanceId} for bookmark {BookmarkId}", bookmark.ActivityInstanceId, bookmark.Id);
            return null;
        }

        var bookmarkedActivity = bookmarkedActivityContext.Activity;

        // Schedule the activity to resume.
        var workItem = new ActivityWorkItem(bookmarkedActivity)
        {
            ExistingActivityExecutionContext = bookmarkedActivityContext,
            Input = input ?? new Dictionary<string, object>(),
            Variables = variables
        };
        Scheduler.Schedule(workItem);

        // If no resumption point was specified, use a "noop" to prevent the regular "ExecuteAsync" method to be invoked and instead complete the activity.
        // Unless the bookmark is configured to auto-complete, in which case we'll just complete the activity.
        ExecuteDelegate = bookmark.CallbackMethodName != null
            ? bookmarkedActivity.GetResumeActivityDelegate(bookmark.CallbackMethodName)
            : bookmark.AutoComplete
                ? WorkflowExecutionContext.Complete
                : WorkflowExecutionContext.Noop;

        // Store the bookmark to resume in the context.
        ResumedBookmarkContext = new(bookmark);
        logger.LogDebug("Scheduled activity {ActivityId} to resume from bookmark {BookmarkId}", bookmarkedActivity.Id, bookmark.Id);

        return workItem;
    }
    /// <summary>
    /// Schedules the specified activity of the workflow.
    /// </summary>
    public ActivityWorkItem ScheduleActivity(IActivity activity, IDictionary<string, object>? input = null, IEnumerable<Variable>? variables = null)
    {
        var workItem = new ActivityWorkItem(activity, input: input, variables: variables);
        Scheduler.Schedule(workItem);
        return workItem;
    }

    /// <summary>
    /// Schedules the specified activity execution context of the workflow.
    /// </summary>
    public ActivityWorkItem ScheduleActivityExecutionContext(ActivityExecutionContext activityExecutionContext, IDictionary<string, object>? input = null, IEnumerable<Variable>? variables = null)
    {
        var workItem = new ActivityWorkItem(
            activityExecutionContext.Activity,
            input: input,
            variables: variables,
            existingActivityExecutionContext: activityExecutionContext);
        Scheduler.Schedule(workItem);
        return workItem;
    }

    #region CompletionCallback
    public void AddCompletionCallback(ActivityExecutionContext owner, ActivityNode child, ActivityCompletionCallback? completionCallback = null)
    {
        var entry = new ActivityCompletionCallbackEntry(owner, child, completionCallback);
        _completionCallbackEntries.Add(entry);
    }

    public ActivityCompletionCallbackEntry? PopCompletionCallback(ActivityExecutionContext owner, ActivityNode child)
    {
        var entry = _completionCallbackEntries.FirstOrDefault(x => x.Owner == owner && x.Child == child);

        if (entry == null)
            return null;

        RemoveCompletionCallback(entry);
        return entry;
    }

    public void RemoveCompletionCallback(ActivityCompletionCallbackEntry entry) => _completionCallbackEntries.Remove(entry);

    public void RemoveCompletionCallbacks(IEnumerable<ActivityCompletionCallbackEntry> entries)
    {
        foreach (var entry in entries.ToList())
            _completionCallbackEntries.Remove(entry);
    }

    public void ClearCompletionCallbacks()
    {
        _completionCallbackEntries.Clear();
    }
    #endregion

    #region Activity Method
    public IActivity? FindActivity(ActivityHandle handle)
    {
        return handle.ActivityId != null
            ? FindActivityById(handle.ActivityId)
            : handle.ActivityNodeId != null
                ? FindActivityByNodeId(handle.ActivityNodeId)
                : handle.ActivityInstanceId != null
                    ? FindActivityByInstanceId(handle.ActivityInstanceId ?? 0)
                        : null;
    }

    public ActivityNode? FindNodeById(string nodeId) => WorkflowGraph.NodeIdLookup.TryGetValue(nodeId, out var node) ? node : null;

    public ActivityNode? FindNodeByActivity(IActivity activity)
    {
        return WorkflowGraph.NodeActivityLookup.TryGetValue(activity, out var node) ? node : null;
    }

    public ActivityNode? FindNodeByActivityId(string activityId) => WorkflowGraph.Nodes.FirstOrDefault(x => x.Activity.Id == activityId);

    public IActivity? FindActivityByNodeId(string nodeId) => FindNodeById(nodeId)?.Activity;

    public IActivity? FindActivityById(string activityId) => FindNodeById(WorkflowGraph.NodeIdLookup.SingleOrDefault(n => n.Key.EndsWith(activityId)).Value.NodeId)?.Activity;

    public IActivity? FindActivityByInstanceId(long activityInstanceId) => _activityExecutionContexts.FirstOrDefault(x => x.Id == activityInstanceId)?.Activity;
    #endregion

    #region Property Method
    public T? GetProperty<T>(string key) => Properties.TryGetValue(key, out var value) ? value.ConvertTo<T>() : default;

    public void SetProperty<T>(string key, T value) => Properties[key] = value!;

    public T UpdateProperty<T>(string key, Func<T?, T> updater)
    {
        var value = GetProperty<T?>(key);
        value = updater(value);
        Properties[key] = value!;
        return value;
    }

    public bool HasProperty(string name) => Properties.ContainsKey(name);
    #endregion

    public void Cancel()
    {
        foreach (var source in _cancellationTokenSources)
            source.Cancel();

        _cancellationTokenSources.Clear();
    }

    private void CancelWorkflow()
    {
        Bookmarks.Clear();
        _completionCallbackEntries.Clear();

        if (Status.IsFinished())
            return;

        AddExecutionLogEntry("Workflow cancelled");

        TransitionTo(WorkflowStatus.Cancelled);

        foreach (var registration in _cancellationRegistrations)
            registration.Dispose();
    }

    public WorkflowExecutionLogEntry AddExecutionLogEntry(string eventName, string? message = null, object? payload = null, LogLevel logLevel = LogLevel.Information)
    {
        var logger = ServiceProvider.GetRequiredService<ILogger<WorkflowExecutionContext>>();
        //var payloadSerializer = ServiceProvider.GetRequiredService<IPayloadSerializer>();

        var serializedPayload = payload != null ? JsonSerializer.Serialize(payload) : null;

        var logEntry = new WorkflowExecutionLogEntry(
            Id,
            null,
            WorkflowGraph.Workflow.Id,
            WorkflowGraph.Workflow.Type,
            WorkflowGraph.Workflow.DefinitionInfo.Version,
            WorkflowGraph.Workflow.Name,
            WorkflowGraph.Workflow.DefinitionInfo.DefinitionId,
            Id,
            DateTime.Now,
            eventName,
            message,
            serializedPayload,
            logLevel.ToString());

        using var scope = logger.BeginScope(DictionaryConvert.Convert(logEntry));
        logger.Log(logLevel, $"Workflow Execution LOG:{eventName};message:{message}");

        return logEntry;
    }

    internal void RecordActivityOutput(ActivityExecutionContext activityExecutionContext, string? outputName, object? value)
    {
        ActivityOutputRegister.Record(activityExecutionContext, outputName, value);
    }

    #region ActivityExecutionContext
    public async Task<ActivityExecutionContext> CreateActivityExecutionContextAsync(IActivity activity, ActivityInvocationOptions? options = null)
    {
        var activityDescriptor = await ActivityRegistryLookup.FindAsync(activity) ?? throw new ActivityNotFoundException(activity.Type);
        var parentContext = options?.Owner;
        var now = DateTime.Now;
        var id = ServiceProvider.GetRequiredService<ISnowflakeIdGenerator>().NextId();
        var activityExecutionContext = new ActivityExecutionContext(id, this, parentContext, activity, activityDescriptor, now, CancellationToken);
        var variablesToDeclare = options?.Variables ?? [];
        var variableContainer = new[]
        {
            activityExecutionContext.ActivityNode
        }.Concat(activityExecutionContext.ActivityNode.Ancestors()).FirstOrDefault(x => x.Activity is IVariableContainer)?.Activity as IVariableContainer;
        activityExecutionContext.ExpressionExecutionContext.TransientProperties[ExpressionExecutionContextExtensions.ActivityExecutionContextKey] = activityExecutionContext;

        if (variableContainer != null)
        {
            foreach (var variable in variablesToDeclare)
            {
                // Declare a dynamic variable on the activity execution context.
                activityExecutionContext.DynamicVariables.RemoveAll(x => x.Name == variable.Name);
                activityExecutionContext.DynamicVariables.Add(variable);

                // Assign the variable to the expression execution context.
                activityExecutionContext.ExpressionExecutionContext.CreateVariable(variable.Name, variable.Value);
            }
        }

        var activityInput = options?.Input ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        activityExecutionContext.ActivityInput.Merge(activityInput);

        // Populate call stack fields from options
        activityExecutionContext.SchedulingActivityExecutionId = options?.SchedulingActivityExecutionId;
        activityExecutionContext.SchedulingWorkflowInstanceId = options?.SchedulingWorkflowInstanceId;

        // Calculate call stack depth
        if (options?.SchedulingActivityExecutionId != null)
        {
            // First, try to find the scheduling context in the current workflow
            var schedulingContext = ActivityExecutionContexts.FirstOrDefault(x => x.Id == options.SchedulingActivityExecutionId);
            if (schedulingContext != null)
            {
                // Found in current workflow - use its depth
                activityExecutionContext.SchedulingActivityId = schedulingContext.Activity.Id;
                activityExecutionContext.CallStackDepth = schedulingContext.CallStackDepth + 1;
            }
            else if (options.SchedulingCallStackDepth.HasValue)
            {
                // Not found but caller provided depth (e.g., cross-workflow invocation)
                activityExecutionContext.CallStackDepth = options.SchedulingCallStackDepth.Value + 1;
            }
            // else: scheduling context not found and no depth provided.
            // Depth stays at default (0), which may result in incorrect call stack depth tracking
            // if the scheduling context should have been present but wasn't found.
        }

        return activityExecutionContext;
    }

    public IEnumerable<ActivityExecutionContext> GetActiveActivityExecutionContexts()
    {
        // Filter out completed activity execution contexts, except for the root Workflow activity context, which stores workflow-level variables.
        // This will currently break scripts accessing activity output directly, but there's a workaround for that via variable capturing.
        // We may ultimately restore direct output access, but differently.
        return ActivityExecutionContexts.Where(x => !x.IsCompleted || x.ParentActivityExecutionContext == null);
    }

    public void AddActivityExecutionContext(ActivityExecutionContext context) => _activityExecutionContexts.Add(context);

    public void RemoveActivityExecutionContext(ActivityExecutionContext context)
    {
        _activityExecutionContexts.Remove(context);
        context.ParentActivityExecutionContext?.Children.Remove(context);
    }

    public void RemoveActivityExecutionContexts(Func<ActivityExecutionContext, bool> predicate)
    {
        var itemsToRemove = _activityExecutionContexts.Where(predicate).ToList();
        foreach (var item in itemsToRemove)
            RemoveActivityExecutionContext(item);
    }

    public void ClearCompletedActivityExecutionContexts()
    {
        RemoveActivityExecutionContexts(x => x is { IsCompleted: true, ParentActivityExecutionContext: not null });
    }
    #endregion
}
