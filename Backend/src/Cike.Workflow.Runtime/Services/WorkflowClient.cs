using Cike.EventBus.Local;
using Cike.Locks.Abstracts;
using Cike.UniversalId.ULong;
using Cike.Workflow.Core.Contexts;
using Cike.Workflow.Core.Enums;
using Cike.Workflow.Core.Models;
using Cike.Workflow.Core.Runners;
using Cike.Workflow.Core.Runners.Internals.Commands;
using Cike.Workflow.Core.Runners.Models;
using Cike.Workflow.Core.WorkflowGraphs.Models;
using Cike.Workflow.Domain.Managers;
using Cike.Workflow.Runtime.Exceptions;

namespace Cike.Workflow.Runtime.Internals;

internal class WorkflowClient(
    long? workflowInstanceId,
    IWorkflowDefinitionService workflowDefinitionService,
    WorkflowInstanceManager workflowInstanceManager,
    IWorkflowInstanceRepository workflowInstanceRepository,
    IWorkflowRunner workflowRunner,
    ISnowflakeIdGenerator identityGenerator,
    ILock lockService,
    ILocalEventBus localEventBus,
    IServiceProvider serviceProvider
    ) : IWorkflowClient
{
    public long WorkflowInstanceId { get; } = workflowInstanceId > 0 ? workflowInstanceId.Value : identityGenerator.NextId();

    public async Task CancelAsync(CancellationToken cancellationToken = default)
    {
        var workflowInstance = await workflowInstanceManager.FindByIdAsync(WorkflowInstanceId, cancellationToken);
        if (workflowInstance == null) throw new WorkflowInstanceNotFoundException("Workflow instance not found.", WorkflowInstanceId);

        if (workflowInstance.Status.GetMainStatus() != WorkflowMainStatus.Running) return;
        var workflowGraph = await workflowDefinitionService.GetWorkflowGraphAsync(WorkflowDefinitionHandle.ByDefinitionVersionId(workflowInstance.DefinitionVersionId), cancellationToken);
        var workflowExecutionContext = await WorkflowExecutionContext.CreateAsync(serviceProvider, workflowGraph, workflowInstance.WorkflowState, cancellationToken: cancellationToken);
        await localEventBus.PublishAsync(new RunCancelWorkflowCommand(workflowExecutionContext), cancellationToken);
    }

    public Task<RunWorkflowInstanceResponse> CreateAndRunInstanceAsync(CreateAndRunWorkflowInstanceRequest request, CancellationToken cancellationToken = default)
    {
        return RunUnderInstanceLockAsync(() => CreateAndRunInstanceCoreAsync(request, cancellationToken), cancellationToken);
    }

    public Task<bool> InstanceExistsAsync(CancellationToken cancellationToken = default)
    {
        return workflowInstanceRepository.AnyAsync(x => x.Id == WorkflowInstanceId, cancellationToken);
    }

    public Task<RunWorkflowInstanceResponse> RunInstanceAsync(RunWorkflowInstanceRequest request, CancellationToken cancellationToken = default)
    {
        return RunUnderInstanceLockAsync(() => RunInstanceCoreAsync(request, cancellationToken), cancellationToken);
    }

    private async Task<RunWorkflowInstanceResponse> RunUnderInstanceLockAsync(Func<Task<RunWorkflowInstanceResponse>> workload, CancellationToken cancellationToken)
    {
        var handle = await lockService.TryGetAsync(WorkflowInstanceLock.GetKey(WorkflowInstanceId), WorkflowInstanceLock.Timeout, cancellationToken);

        if (handle is null)
            return await MapCurrentStateResponseAsync(cancellationToken);

        await using (handle)
        {
            return await workload();
        }
    }

    private async Task<RunWorkflowInstanceResponse> MapCurrentStateResponseAsync(CancellationToken cancellationToken)
    {
        var workflowInstance = await workflowInstanceRepository.FindAsync(WorkflowInstanceId, cancellationToken);

        return workflowInstance is null
            ? new RunWorkflowInstanceResponse { WorkflowInstanceId = WorkflowInstanceId }
            : MapResponse(includeWorkflowOutput: false, workflowInstance.WorkflowState);
    }

    private async Task<RunWorkflowInstanceResponse> CreateAndRunInstanceCoreAsync(CreateAndRunWorkflowInstanceRequest request, CancellationToken cancellationToken)
    {
        var workflowGraph = await workflowDefinitionService.GetWorkflowGraphAsync(request.WorkflowDefinitionHandle, cancellationToken);

        var instanceOptions = new WorkflowInstanceOptions
        {
            WorkflowInstanceId = WorkflowInstanceId,
            CorrelationId = request.CorrelationId,
            Name = request.Name,
            Input = request.Input,
            Properties = request.Properties,
            ParentWorkflowInstanceId = request.ParentId,
            IsDebug = request.IsDebug,
        };

        var workflowInstance = await workflowInstanceManager.CreateAndCommitWorkflowInstanceAsync(workflowGraph.Workflow, instanceOptions, cancellationToken);

        var runOptions = new RunWorkflowOptions
        {
            BookmarkId = request.BookmarkId,
            ActivityHandle = request.ActivityHandle,
            TriggerActivityId = request.TriggerActivityId,
            Input = request.Input,
            Variables = request.Variables,
            Properties = request.Properties,
            SchedulingActivityExecutionId = request.SchedulingActivityExecutionId,
            SchedulingWorkflowInstanceId = request.SchedulingWorkflowInstanceId,
            SchedulingCallStackDepth = request.SchedulingCallStackDepth,
        };

        var result = await RunAsync(workflowGraph, workflowInstance.WorkflowState, runOptions, cancellationToken);

        return MapResponse(request.IncludeWorkflowOutput, result.WorkflowState);
    }

    private async Task<RunWorkflowInstanceResponse> RunInstanceCoreAsync(RunWorkflowInstanceRequest request, CancellationToken cancellationToken)
    {
        var workflowInstance = await workflowInstanceRepository.FindAsync(WorkflowInstanceId, cancellationToken)
                               ?? throw new WorkflowInstanceNotFoundException("Workflow instance not found.", WorkflowInstanceId);

        var workflowState = workflowInstance.WorkflowState;

        if (workflowState.Status.IsFinished())
            return MapResponse(includeWorkflowOutput: false, workflowState);

        var workflowGraph = await workflowDefinitionService.GetWorkflowGraphAsync(
            WorkflowDefinitionHandle.ByDefinitionVersionId(workflowInstance.DefinitionVersionId), cancellationToken);

        var runOptions = new RunWorkflowOptions
        {
            BookmarkId = request.BookmarkId,
            ActivityHandle = request.ActivityHandle,
            TriggerActivityId = request.TriggerActivityId,
            Input = request.Input,
            Variables = request.Variables,
            Properties = request.Properties,
            SchedulingActivityExecutionId = request.SchedulingActivityExecutionId,
            SchedulingWorkflowInstanceId = request.SchedulingWorkflowInstanceId,
            SchedulingCallStackDepth = request.SchedulingCallStackDepth,
        };

        var result = await RunAsync(workflowGraph, workflowState, runOptions, cancellationToken);

        return MapResponse(request.IncludeWorkflowOutput, result.WorkflowState);
    }

    private async Task<RunWorkflowResult> RunAsync(
        WorkflowGraph workflowGraph,
        WorkflowState workflowState,
        RunWorkflowOptions runOptions,
        CancellationToken cancellationToken)
    {
        var result = await workflowRunner.RunAsync(workflowGraph, workflowState, runOptions, cancellationToken);

        return result;
    }

    private RunWorkflowInstanceResponse MapResponse(bool includeWorkflowOutput, WorkflowState workflowState)
    {
        return new RunWorkflowInstanceResponse
        {
            WorkflowInstanceId = WorkflowInstanceId,
            Status = workflowState.Status,
            Bookmarks = workflowState.Bookmarks,
            Incidents = workflowState.Incidents,
            Output = includeWorkflowOutput ? workflowState.Output : null,
        };
    }
}
