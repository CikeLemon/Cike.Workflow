using Cike.Core.Extensions.System;
using Cike.Core.Hashers;
using Cike.Workflow.Core.ActivityDescriptors;
using Cike.Workflow.Core.Contexts.Models;
using Cike.Workflow.Core.Exceptions;
using Cike.Workflow.Core.Helpers;
using Cike.Workflow.Core.Schedulers;
using Cike.Workflow.Core.Schedulers.Models;
using Cike.Workflow.Core.Variables;
using Cike.Workflow.Core.WorkflowGraphs.Models;
using Cike.Workflow.Expressions.LiteralExpressions;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;

namespace Cike.Workflow.Core.Contexts;

public class ActivityExecutionContext : IExecutionContext
{
    private ActivityExecutionContext? _parentActivityExecutionContext;
    private List<Bookmark> _newBookmarks = [];
    private Exception? _exception;

    public ActivityExecutionContext(
        long id,
        WorkflowExecutionContext workflowExecutionContext,
        ActivityExecutionContext? parentActivityExecutionContext,
        IActivity activity,
        ActivityDescriptor activityDescriptor,
        DateTime startedAt,
        CancellationToken cancellationToken)
    {
        Properties = new ChangeTrackingDictionary<string, object>(Taint);
        Metadata = new ChangeTrackingDictionary<string, object>(Taint);
        ActivityState = new ChangeTrackingDictionary<string, object>(Taint);
        ActivityInput = new ChangeTrackingDictionary<string, object>(Taint);
        WorkflowExecutionContext = workflowExecutionContext;
        ParentActivityExecutionContext = parentActivityExecutionContext;
        var expressionExecutionContextProps = ExpressionExecutionContextExtensions.CreateActivityExecutionContextPropertiesFrom(workflowExecutionContext, workflowExecutionContext.Input);
        expressionExecutionContextProps[ExpressionExecutionContextExtensions.ActivityKey] = activity;
        ExpressionExecutionContext = new(workflowExecutionContext.ServiceProvider, new(), parentActivityExecutionContext?.ExpressionExecutionContext ?? workflowExecutionContext.ExpressionExecutionContext, expressionExecutionContextProps, Taint, cancellationToken);
        Activity = activity;
        ActivityDescriptor = activityDescriptor;
        CreatedAt = startedAt;
        Status = ActivityStatus.Pending;
        CancellationToken = cancellationToken;
        Id = id;
    }

    public long Id { get; set; }

    public ActivityStatus Status { get; set; }

    public IActivity Activity { get; set; } = null!;

    public IEnumerable<Variable> Variables
    {
        get
        {
            var containerVariables = (Activity as IVariableContainer)?.Variables ?? Enumerable.Empty<Variable>();
            var dynamicVariables = DynamicVariables;
            var mergedVariables = new Dictionary<string, Variable>();

            foreach (var containerVariable in containerVariables)
            {
                var name = !string.IsNullOrEmpty(containerVariable.Name) ? containerVariable.Name : containerVariable.Id;
                mergedVariables[name] = containerVariable;
            }

            foreach (var dynamicVariable in dynamicVariables)
            {
                var name = !string.IsNullOrEmpty(dynamicVariable.Name) ? dynamicVariable.Name : dynamicVariable.Id;
                mergedVariables[name] = dynamicVariable;
            }
            return mergedVariables.Values;
        }
    }

    public ICollection<Variable> DynamicVariables { get; set; } = new List<Variable>();

    public IDictionary<string, object> Properties { get; private set; }

    public IDictionary<string, object> ActivityState { get; }

    public IDictionary<string, object> ActivityInput { get; private set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    public bool IsDirty { get; private set; }

    public bool IsExecuting { get; set; }

    public int AggregateFaultCount { get; set; }

    public IDictionary<object, object> TransientProperties { get; private set; } = new Dictionary<object, object>();

    public IDictionary<string, object> Metadata { get; private set; }

    public ExpressionExecutionContext ExpressionExecutionContext { get; } = null!;

    public ActivityDescriptor ActivityDescriptor { get; } = null!;

    public CancellationToken CancellationToken { get; }

    public WorkflowExecutionContext WorkflowExecutionContext { get; } = null!;

    public ActivityNode ActivityNode => WorkflowExecutionContext.FindNodeByActivity(Activity)!;

    public string NodeId => ActivityNode.NodeId;

    public long? SchedulingActivityExecutionId { get; set; }

    public string? SchedulingActivityId { get; set; }

    public long? SchedulingWorkflowInstanceId { get; set; }

    public int CallStackDepth { get; set; }

    public Exception? Exception
    {
        get => _exception;
        set
        {
            _exception = value;
            Taint();
        }
    }

    public ActivityExecutionContext? ParentActivityExecutionContext
    {
        get => _parentActivityExecutionContext;
        internal set
        {
            _parentActivityExecutionContext = value;
            _parentActivityExecutionContext?.Children.Add(this);
        }
    }

    public ISet<ActivityExecutionContext> Children { get; } = new HashSet<ActivityExecutionContext>();

    public IEnumerable<Bookmark> NewBookmarks => _newBookmarks.AsReadOnly();

    public bool IsCompleted => Status is ActivityStatus.Completed or ActivityStatus.Canceled;

    public IDisposable EnterExecution()
    {
        this.IsExecuting = WorkflowExecutionContext.IsExecuting = true;
        return new DisposeAction(() =>
        {
            this.IsExecuting = WorkflowExecutionContext.IsExecuting = false;
        });
    }

    #region Properties
    public T? GetProperty<T>(string key) => Properties.TryGetValue<T?>(key, out var value) ? value : default;

    public T GetProperty<T>(string key, Func<T> defaultValue)
    {
        if (Properties.TryGetValue<T?>(key, out var value))
            return value!;

        value = defaultValue();
        Properties[key] = value!;

        return value!;
    }

    public void SetProperty<T>(string key, T? value) => Properties[key] = value!;

    public T UpdateProperty<T>(string key, Func<T?, T> updater) where T : notnull
    {
        var value = GetProperty<T?>(key);
        value = updater(value);
        Properties[key] = value;
        return value;
    }

    public void RemoveProperty(string key) => Properties.Remove(key);

    public bool GetIsBreakingProperty() => GetProperty<bool>("IsBreaking");

    public void SetIsBreakingProperty() => SetProperty<bool>("IsBreaking", true);
    #endregion

    #region Parent/Children/FindActivity
    public async IAsyncEnumerable<(IActivity Activity, ActivityDescriptor ActivityDescriptor)> GetActivitiesWithOutputs()
    {
        // Get current container.
        var currentContainerNode = FindParentWithVariableContainer()?.ActivityNode;

        if (currentContainerNode == null)
            yield break;

        // Get all nodes in the current container
        var workflowExecutionContext = WorkflowExecutionContext;
        var containedNodes = workflowExecutionContext.WorkflowGraph.Nodes.Where(x => x.Parents.Contains(currentContainerNode)).Distinct().ToList();

        // Select activities with outputs.
        var activityRegistry = workflowExecutionContext.GetRequiredService<IActivityRegistryLookupService>();

        foreach (var node in containedNodes)
        {
            var activity = node.Activity;
            var activityDescriptor = await activityRegistry.FindAsync(activity.Type, activity.Version);
            if (activityDescriptor != null && activityDescriptor.Outputs.Any())
                yield return (activity, activityDescriptor);
        }
    }

    public IActivity? FindActivityByIdOrName(string idOrName)
    {
        // Get current container.
        var currentContainerNode = FindParentWithVariableContainer()?.ActivityNode;

        if (currentContainerNode == null)
            return null;

        // Get all nodes in the current container
        var workflowExecutionContext = WorkflowExecutionContext;
        var containedNodes = workflowExecutionContext.WorkflowGraph.Nodes.Where(x => x.Parents.Contains(currentContainerNode)).Distinct().ToList();
        var node = containedNodes.FirstOrDefault(x => x.Activity.Name == idOrName || x.Activity.Id == idOrName);
        return node?.Activity;
    }

    public ActivityExecutionContext? FindParentWithVariableContainer()
    {
        return FindParent(x => x.Activity is IVariableContainer);
    }

    public ActivityExecutionContext? FindParent(Func<ActivityExecutionContext, bool> predicate)
    {
        var currentContext = this;

        while (currentContext != null)
        {
            if (predicate(currentContext))
                return currentContext;

            currentContext = currentContext.ParentActivityExecutionContext;
        }

        return null;
    }

    public IEnumerable<ActivityExecutionContext> GetAncestors()
    {
        var current = ParentActivityExecutionContext;

        while (current != null)
        {
            yield return current;
            current = current.ParentActivityExecutionContext;
        }
    }

    public IEnumerable<ActivityExecutionContext> GetChildren()
    {
        return Children;
    }

    public IEnumerable<ActivityExecutionContext> GetDescendants()
    {
        var children = Children.ToList();

        foreach (var child in children)
        {
            yield return child;

            foreach (var descendant in child.GetDescendants())
                yield return descendant;
        }
    }
    #endregion

    public void Taint()
    {
        if (!IsDirty)
            IsDirty = true;
    }

    public void ClearTaint()
    {
        if (IsDirty)
            IsDirty = false;
    }

    #region Scheduler Activity
    public ValueTask ScheduleActivityAsync(IActivity? activity, ActivityCompletionCallback? completionCallback, IEnumerable<Variable>? variables = null)
    {
        var options = new ScheduleWorkOptions
        {
            CompletionCallback = completionCallback,
            Variables = variables?.ToList()
        };
        return ScheduleActivityAsync(activity, options);
    }

    public async ValueTask ScheduleActivityAsync(IActivity? activity, ScheduleWorkOptions? options = null)
    {
        await ScheduleActivityAsync(activity, this, options);
    }

    public async ValueTask ScheduleActivityAsync(IActivity? activity, ActivityExecutionContext? owner, ScheduleWorkOptions? options = null)
    {
        var schedulerStrategy = GetRequiredService<IActivityExecutionContextSchedulerStrategy>();
        await schedulerStrategy.ScheduleActivityAsync(this, activity, owner, options);
    }

    public async Task ScheduleActivityAsync(ActivityNode? activityNode, ActivityExecutionContext? owner = null, ScheduleWorkOptions? options = null)
    {
        var schedulerStrategy = GetRequiredService<IActivityExecutionContextSchedulerStrategy>();
        await schedulerStrategy.ScheduleActivityAsync(this, activityNode, owner, options);
    }

    public async ValueTask ScheduleActivitiesAsync(params IActivity?[] activities) => await ScheduleActivities(activities);

    public ValueTask ScheduleActivities(IEnumerable<IActivity?> activities, ActivityCompletionCallback? completionCallback, IEnumerable<Variable>? variables = null)
    {
        var options = new ScheduleWorkOptions
        {
            CompletionCallback = completionCallback,
            Variables = variables?.ToList()
        };
        return ScheduleActivities(activities, options);
    }

    public async ValueTask ScheduleActivities(IEnumerable<IActivity?> activities, ScheduleWorkOptions? options = null)
    {
        foreach (var activity in activities)
            await ScheduleActivityAsync(activity, options);
    }
    #endregion

    #region Bookmarks
    public void CreateBookmarks(IEnumerable<object> payloads, ExecuteActivityDelegate? callback = null, bool includeActivityInstanceId = true, string? bookmarkName = null)
    {
        foreach (var payload in payloads)
            CreateBookmark(new()
            {
                Stimulus = payload,
                Callback = callback,
                BookmarkName = bookmarkName,
                IncludeActivityInstanceId = includeActivityInstanceId
            });
    }

    public void AddBookmarks(IEnumerable<Bookmark> bookmarks)
    {
        WorkflowExecutionContext.Bookmarks.AddRange(bookmarks);
        Taint();
    }

    public void AddBookmark(Bookmark bookmark)
    {
        _newBookmarks.Add(bookmark);
        WorkflowExecutionContext.Bookmarks.Add(bookmark);
        Taint();
    }

    public Bookmark CreateBookmark(ExecuteActivityDelegate callback, IDictionary<string, string>? metadata = null)
    {
        return CreateBookmark(new()
        {
            Callback = callback,
            Metadata = metadata
        });
    }

    public Bookmark CreateBookmark(object stimulus, ExecuteActivityDelegate? callback, bool includeActivityInstanceId = true, IDictionary<string, string>? customProperties = null)
    {
        return CreateBookmark(new()
        {
            Stimulus = stimulus,
            Callback = callback,
            IncludeActivityInstanceId = includeActivityInstanceId,
            Metadata = customProperties
        });
    }

    public Bookmark CreateBookmark(object stimulus, bool includeActivityInstanceId, IDictionary<string, string>? customProperties = null)
    {
        return CreateBookmark(new()
        {
            Stimulus = stimulus,
            IncludeActivityInstanceId = includeActivityInstanceId,
            Metadata = customProperties
        });
    }

    public Bookmark CreateBookmark(object stimulus, IDictionary<string, string>? metadata = null)
    {
        return CreateBookmark(new()
        {
            Stimulus = stimulus,
            Metadata = metadata
        });
    }

    public Bookmark CreateBookmark(CreateBookmarkArgs? options = null)
    {
        var payload = options?.Stimulus;
        var callback = options?.Callback;
        var bookmarkName = options?.BookmarkName ?? Activity.Type;
        var bookmarkHasher = GetRequiredService<IHasher>();
        var identityGenerator = GetRequiredService<ISnowflakeIdGenerator>();
        var includeActivityInstanceId = options?.IncludeActivityInstanceId ?? true;
        var hash = bookmarkHasher.Hash(new object?[] { bookmarkName, payload, includeActivityInstanceId ? Id : null });
        var bookmarkId = options?.BookmarkId ?? identityGenerator.NextId();

        var bookmark = new Bookmark(
            bookmarkId,
            bookmarkName,
            hash,
            payload,
            Activity.Id,
            ActivityNode.NodeId,
            Id,
            DateTime.Now,
            options?.AutoBurn ?? true,
            callback?.Method.Name,
            options?.AutoComplete ?? true,
            options?.Metadata);

        AddBookmark(bookmark);
        return bookmark;
    }

    public void ClearBookmarks()
    {
        _newBookmarks.Clear();
        WorkflowExecutionContext.Bookmarks.RemoveAll(x => x.ActivityInstanceId == Id);
        Taint();
    }
    #endregion


    #region Get/Set Variable Input Output
    public T? Get<T>(Input<T>? input) => input == null ? default : Get<T>(input.MemoryBlockReference);

    public T? Get<T>(Output<T>? output) => output == null ? default : Get<T>(output.MemoryBlockReference);

    public object? Get(Output? output) => output == null ? null : Get(output.MemoryBlockReference);

    public object? Get(MemoryBlockReference blockReference)
    {
        return !TryGet(blockReference, out var value)
            ? throw new InvalidOperationException($"The memory block '{blockReference}' does not exist.")
            : value;
    }

    public T? Get<T>(MemoryBlockReference blockReference)
    {
        var value = Get(blockReference);
        return value != null ? value.ConvertTo<T>() : default;
    }

    public bool TryGet(MemoryBlockReference blockReference, out object? value)
    {
        // First, try to get the value from the memory register
        var memoryBlock = GetMemoryBlock(blockReference);

        if (memoryBlock != null)
        {
            value = memoryBlock.Value;
            return true;
        }

        // Handle Literal references as a fallback - they can hold their value directly
        if (blockReference is Literal literal)
        {
            value = literal.Value;
            return true;
        }

        value = null;
        return false;
    }

    public void Set(MemoryBlockReference blockReference, object? value, Action<MemoryBlock>? configure = null) => ExpressionExecutionContext.Set(blockReference, value, configure);

    public void Set<T>(Output<T>? output, T? value, [CallerArgumentExpression("output")] string? outputName = null) => Set((Output?)output, value, outputName);

    public void Set(Output? output, object? value, [CallerArgumentExpression("output")] string? outputName = null)
    {
        // Store the value in the expression execution memory block.
        ExpressionExecutionContext.Set(output, value);

        // Also store the value in the workflow execution transient activity output register.
        WorkflowExecutionContext.RecordActivityOutput(this, outputName, value);
    }

    private MemoryBlock? GetMemoryBlock(MemoryBlockReference locationBlockReference)
    {
        return ExpressionExecutionContext.TryGetBlock(locationBlockReference, out var memoryBlock) ? memoryBlock : null;
    }

    public Variable SetDynamicVariable<T>(string name, T value, Action<MemoryBlock>? configure = null)
    {
        // Check if a predefined variable already exists.
        var predefinedVariable = ExpressionExecutionContext.GetVariable(name);

        if (predefinedVariable != null)
        {
            SetVariable(name, value);
            return predefinedVariable;
        }

        // No predefined variable exists, so we will add a dynamic variable to the current container in scope.
        var container = FindParentWithVariableContainer();

        if (container == null)
            throw new("No parent variable container found");

        var existingVariable = container.DynamicVariables.FirstOrDefault(x => x.Name == name);

        if (existingVariable == null)
            container.DynamicVariables.Add(new Variable<T>(name, value));

        return SetVariable(name, value);
    }

    public Variable SetVariable(string name, object? value, Action<MemoryBlock>? configure = null) =>
        ExpressionExecutionContext.SetVariable(name, value, configure);


    public IDictionary<string, object> GetInputs()
    {
        return this.ActivityState!;
    }

    public IDictionary<string, object> GetOutputs()
    {
        var activity = this.Activity;
        var expressionExecutionContext = this.ExpressionExecutionContext;
        var activityDescriptor = this.ActivityDescriptor;
        var outputDescriptors = activityDescriptor.Outputs;

        var outputs = outputDescriptors.ToDictionary(x => x.Name, x =>
        {
            if (x.IsSerializable == false)
                return "(not serializable)";

            // 活动在当前容器解析不到时（如根 Workflow 自身的执行记录，画布节点集中没有它），
            // 寄存值无从取起，跳过缓存查询、直接走 ValueGetter 兜底
            object? cachedValue = null;
            try
            {
                cachedValue = expressionExecutionContext.GetOutput(activity.Id, x.Name);
            }
            catch (InvalidOperationException)
            {
            }

            if (cachedValue != null)
                return cachedValue;

            if (x.ValueGetter(activity) is Output output && this.TryGet(output.MemoryBlockReference, out var outputValue))
                return outputValue!;

            return null!;
        });

        return outputs;
    }
    #endregion

    public T GetRequiredService<T>() where T : notnull => WorkflowExecutionContext.GetRequiredService<T>();

    public object GetRequiredService(Type serviceType) => WorkflowExecutionContext.GetRequiredService(serviceType);

    public async ValueTask SendSignalAsync(object signal)
    {
        var receivingContexts = new[]
        {
                this
            }.Concat(this.GetAncestors()).ToList();
        var logger = this.GetRequiredService<ILogger<ActivityExecutionContext>>();
        var signalType = signal.GetType();
        var signalTypeName = signalType.Name;

        // Let all ancestors receive the signal.
        foreach (var ancestorContext in receivingContexts)
        {
            var signalContext = new SignalContext(ancestorContext, this, WorkflowExecutionContext.CancellationToken);

            if (ancestorContext.Activity is not ISignalHandler handler)
                continue;

            logger.LogDebug("Receiving signal {SignalType} on activity {ActivityId} of type {ActivityType}", signalTypeName, ancestorContext.Activity.Id, ancestorContext.Activity.Type);
            await handler.ReceiveSignalAsync(signal, signalContext);

            if (signalContext.StopPropagationRequested)
            {
                logger.LogDebug("Propagation of signal {SignalType} on activity {ActivityId} of type {ActivityType} was stopped", signalTypeName, ancestorContext.Activity.Id, ancestorContext.Activity.Type);
                return;
            }
        }
    }

    public async ValueTask CompleteActivityAsync(Outcomes? result = null)
    {
        // If the activity is not running, do nothing.
        if (Status != ActivityStatus.Running)
            return;

        // Cancel any non-completed child activities.
        var childContexts = Children.Where(x => x.Status.CanCancelActivity()).ToList();

        foreach (var childContext in childContexts)
            await childContext.CancelActivityAsync();

        // Mark the activity as complete.
        TransitionTo(ActivityStatus.Completed);

        // Add an execution log entry.
        AddExecutionLogEntry("Completed");

        // Send a signal.
        await this.SendSignalAsync(new ActivityCompletedSignal(result));

        // Clear bookmarks.
        ClearBookmarks();
        WorkflowExecutionContext.Bookmarks.RemoveAll(x => x.ActivityInstanceId == Id);

        // Remove completion callbacks.
        ClearCompletionCallbacks();

        // Remove all associated variables, unless this is the root context - in which case we want to keep the variables since we're not deleting that one.
        if (ParentActivityExecutionContext != null)
        {
            var variablePersistenceManager = GetRequiredService<IVariablePersistenceManager>();
            await variablePersistenceManager.DeleteVariablesAsync(this);
        }

        // Update the completed at timestamp.
        FinishedAt = DateTime.Now;
    }

    internal void WithdrawScheduledWork()
    {
        WorkflowExecutionContext.Scheduler.RemoveWhere(workItem => workItem.ExistingActivityExecutionContext == this || workItem.Owner == this);
    }

    internal async Task CancelActivityAsync()
    {
        if (!Status.CanCancelActivity())
            return;

        WithdrawScheduledWork();
        TransitionTo(ActivityStatus.Canceled);
        ClearBookmarks();
        ClearCompletionCallbacks();
        WorkflowExecutionContext.Bookmarks.RemoveAll(x => x.ActivityNodeId == ActivityNode.NodeId);
        AddExecutionLogEntry("Canceled");
        await this.SendSignalAsync(new CancelSignal());
        await CancelChildActivitiesAsync();

        // ReSharper disable once MethodSupportsCancellation
        //await _publisher.SendAsync(new ActivityCancelled(this));
    }

    private async Task CancelChildActivitiesAsync()
    {
        var childContexts = WorkflowExecutionContext.ActivityExecutionContexts.Where(x => x.ParentActivityExecutionContext == this && x.Status.CanCancelActivity()).ToList();

        foreach (var childContext in childContexts)
            await childContext.CancelActivityAsync();
    }

    public void ClearCompletionCallbacks()
    {
        var entriesToRemove = WorkflowExecutionContext.CompletionCallbacks.Where(x => x.Owner == this).ToList();
        WorkflowExecutionContext.RemoveCompletionCallbacks(entriesToRemove);
    }

    public void Fault(Exception e)
    {
        Exception = e;
        TransitionTo(ActivityStatus.Faulted);
        var activity = Activity;
        var exceptionState = ExceptionState.FromException(e);
        var now = DateTime.Now;
        var incident = new ActivityIncident(activity.Id, activity.NodeId, activity.Type, e.Message, exceptionState, now, Id);
        WorkflowExecutionContext.Incidents.Add(incident);
        AggregateFaultCount++;

        var ancestors = GetAncestors();

        foreach (var ancestor in ancestors)
            ancestor.AggregateFaultCount++;
    }

    public void TransitionTo(ActivityStatus status)
    {
        Status = status;
    }

    public WorkflowExecutionLogEntry AddExecutionLogEntry(string eventName, string? message = null, string? source = null, object? payload = null, LogLevel logLevel = LogLevel.Information)
    {
        var logger = WorkflowExecutionContext.GetRequiredService<ILogger<WorkflowExecutionContext>>();
        //var payloadSerializer = ServiceProvider.GetRequiredService<IPayloadSerializer>();

        var serializedPayload = payload != null ? JsonSerializer.Serialize(payload) : null;

        var logEntry = new WorkflowExecutionLogEntry(
            Id,
            ParentActivityExecutionContext?.Id ?? 0,
            Activity.Id,
            Activity.Type,
            Activity.Version,
            Activity.Name,
            Activity.NodeId,
            WorkflowExecutionContext.Id,
            DateTime.Now,
            eventName,
            message,
            serializedPayload,
            logLevel.ToString());

        using var scope = logger.BeginScope(DictionaryConvert.Convert(logEntry));
        logger.Log(logLevel, $"Workflow Execution LOG:{eventName};message:{message}");

        return logEntry;
    }

    #region Evaluate Input Properties
    public bool GetHasEvaluatedProperties() => TransientProperties.TryGetValue<bool>("HasEvaluatedProperties", out var value) && value;
    public void SetHasEvaluatedProperties() => TransientProperties["HasEvaluatedProperties"] = true;



    public async Task EvaluateInputPropertiesAsync()
    {
        // Evaluate containing composite input properties, if any.
        var compositeContainerContexts = GetAncestors().Where(x => x.Activity is CompositeActivity).ToList();

        foreach (var activityExecutionContext in compositeContainerContexts)
        {
            if (!activityExecutionContext.GetHasEvaluatedProperties())
                await activityExecutionContext.InternalEvaluateInputPropertiesAsync();
        }

        // Evaluate input properties.
        await InternalEvaluateInputPropertiesAsync();
    }

    /// <summary>
    /// Evaluates the specified input property of the activity.
    /// </summary>
    public async Task<T?> EvaluateInputPropertyAsync<TActivity, T>(Expression<Func<TActivity, Input<T>>> propertyExpression)
    {
        var inputName = propertyExpression.GetProperty()!.Name;
        var input = await EvaluateInputPropertyAsync(inputName);
        return input.ConvertTo<T>();
    }

    /// <summary>
    /// Evaluates a specific input property of the activity.
    /// </summary>
    public async Task<object?> EvaluateInputPropertyAsync(string inputName)
    {
        var activity = Activity;
        var activityRegistryLookup = GetRequiredService<IActivityRegistryLookupService>();
        var activityDescriptor = await activityRegistryLookup.FindAsync(activity.Type) ?? throw new Exception("Activity descriptor not found");
        var inputDescriptor = activityDescriptor.Inputs.FirstOrDefault(x => x.Name == inputName && x.IsWrapped);

        if (inputDescriptor == null)
            throw new Exception($"No input with name {inputName} could be found");

        return await EvaluateInputPropertyAsync(activityDescriptor, inputDescriptor);
    }

    private async Task InternalEvaluateInputPropertiesAsync()
    {
        var activityDescriptor = ActivityDescriptor;
        var inputDescriptors = activityDescriptor.Inputs.Where(x => x.AutoEvaluate).ToList();

        // Evaluate inputs.
        foreach (var inputDescriptor in inputDescriptors)
            await EvaluateInputPropertyAsync(activityDescriptor, inputDescriptor);

        SetHasEvaluatedProperties();
    }

    private async Task<object?> EvaluateInputPropertyAsync(ActivityDescriptor activityDescriptor, InputDescriptor inputDescriptor)
    {
        try
        {
            return await EvaluateInputPropertyCoreAsync(activityDescriptor, inputDescriptor);
        }
        catch (Exception e)
        {
            throw new InputEvaluationException(inputDescriptor.Name, $"Failed to evaluate activity input '{inputDescriptor.Name}'", e);
        }
    }

    private async Task<object?> EvaluateInputPropertyCoreAsync(ActivityDescriptor activityDescriptor, InputDescriptor inputDescriptor)
    {
        var activity = Activity;
        object? value = null;
        var input = inputDescriptor.ValueGetter(activity);
        var identityGenerator = GetRequiredService<ISnowflakeIdGenerator>();

        if (inputDescriptor.IsWrapped)
        {
            var wrappedInput = (Input?)input;

            var expressionEvaluator = GetRequiredService<IExpressionEvaluator>();
            var expressionExecutionContext = ExpressionExecutionContext;

            if (wrappedInput?.Expression != null)
            {
                value = await expressionEvaluator.EvaluateAsync(wrappedInput.Expression!, wrappedInput.Type, expressionExecutionContext); ;
            }

            var memoryReference = wrappedInput?.MemoryBlockReference;

            if (memoryReference != null)
            {
                // When input is created from an activity provider, there may be no memory block reference ID.
                if (memoryReference.Id == null!)
                    memoryReference.Id = $"{activity.NodeId}.{inputDescriptor.Name}"; // Construct a deterministic ID.

                // Declare the input memory block in the current context. 
                ExpressionExecutionContext.Set(memoryReference, value!);
            }
        }
        else
        {
            value = input;
        }

        //Store Input Value
        if (inputDescriptor.IsSerializable != false)
        {
            ActivityState[inputDescriptor.Name] = value;
        }

        return value;
    }
    #endregion
}
