using Cike.Core.DependencyInjection;
using Cike.EventBus.Local;
using Cike.Workflow.Core.Models;
using Cike.Workflow.Runtime.Internals.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cike.Workflow.Runtime.Internals;

internal class WorkflowDispatcher(ILocalEventBus localEventBus, IWorkflowRuntime workflowRuntime) : IWorkflowDispatcher, IScopedDependency
{
    public async Task DispatchAsync(DispatchWorkflowDefinitionRequest request, DispatchWorkflowOptions? options, CancellationToken cancellationToken = default)
    {
        await localEventBus.PublishAsync(new DispatcherWorkflowDefinitionCommand(request), cancellationToken);
    }

    public async Task DispatchAsync(DispatchWorkflowInstanceRequest request, DispatchWorkflowOptions? options, CancellationToken cancellationToken = default)
    {
        await localEventBus.PublishAsync(new DispatcherWorkflowInstanceCommand(request), cancellationToken);
    }

    public async Task DispatchAsync(DispatchCancelWorkflowRequest request, CancellationToken cancellationToken = default)
    {
        await localEventBus.PublishAsync(new DispatcherCancelWorkflowCommand(request), cancellationToken);
    }

    [LocalEventHandler]
    public async Task HandlerDispatcherWorkflowDefinitionAsync(DispatcherWorkflowDefinitionCommand command, CancellationToken cancellationToken = default)
    {
        var client = await workflowRuntime.CreateClientAsync(command.Request.InstanceId, cancellationToken);

        if (command.Request.InstanceId > 0 && await client.InstanceExistsAsync(cancellationToken))
            return;

        var createRequest = new CreateAndRunWorkflowInstanceRequest
        {
            WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionVersionId(command.Request.DefinitionVersionId),
            CorrelationId = command.Request.CorrelationId,
            Input = command.Request.Input,
            Properties = command.Request.Properties,
            ParentId = command.Request.ParentWorkflowInstanceId,
            TriggerActivityId = command.Request.TriggerActivityId,
            IsDebug = command.Request.IsDebug,
            SchedulingActivityExecutionId = command.Request.SchedulingActivityExecutionId,
            SchedulingWorkflowInstanceId = command.Request.SchedulingWorkflowInstanceId,
            SchedulingCallStackDepth = command.Request.SchedulingCallStackDepth
        };
        await client.CreateAndRunInstanceAsync(createRequest, cancellationToken);

    }

    [LocalEventHandler]
    public async Task HandlerDispatcherWorkflowInstanceAsync(DispatcherWorkflowInstanceCommand command, CancellationToken cancellationToken = default)
    {
        var runRequest = new RunWorkflowInstanceRequest
        {
            BookmarkId = command.Request.BookmarkId,
            ActivityHandle = command.Request.ActivityHandle,
            Input = command.Request.Input,
            Properties = command.Request.Properties
        };
        var client = await workflowRuntime.CreateClientAsync(command.Request.InstanceId, cancellationToken);
        await client.RunInstanceAsync(runRequest, cancellationToken);
    }

    [LocalEventHandler]
    public async Task HandlerDispatcherCancelWorkflowAsync(DispatcherCancelWorkflowCommand command, CancellationToken cancellationToken = default)
    {
        var workflowInstanceId = command.Request.WorkflowInstanceId;
        var workflowClient = await workflowRuntime.CreateClientAsync(workflowInstanceId, cancellationToken);
        await workflowClient.CancelAsync(cancellationToken);
    }
}
