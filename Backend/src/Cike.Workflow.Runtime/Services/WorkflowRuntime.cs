using Cike.Core.DependencyInjection;
using Cike.EventBus.Local;
using Cike.Locks.Abstracts;
using Cike.UniversalId.ULong;
using Cike.Workflow.Core.Runners;
using Cike.Workflow.Domain.Data;
using Cike.Workflow.Domain.Managers;
using Microsoft.Extensions.DependencyInjection;

namespace Cike.Workflow.Runtime.Internals;

internal class WorkflowRuntime(IServiceProvider serviceProvider) : IWorkflowRuntime, IScopedDependency
{
    public ValueTask<IWorkflowClient> CreateClientAsync(long? workflowInstanceId, CancellationToken cancellationToken = default)
    {
        // WorkflowClient 以实例 Id 为身份参数（构造期决定操作哪个实例），其余依赖从当前 Scope 解析；
        // 其构造器非 public，ActivatorUtilities 不适用，直接手工组装
        var client = new WorkflowClient(
            workflowInstanceId,
            serviceProvider.GetRequiredService<IWorkflowDefinitionService>(),
            serviceProvider.GetRequiredService<WorkflowInstanceManager>(),
            serviceProvider.GetRequiredService<IWorkflowInstanceRepository>(),
            serviceProvider.GetRequiredService<IWorkflowRunner>(),
            serviceProvider.GetRequiredService<ISnowflakeIdGenerator>(),
            serviceProvider.GetRequiredService<ILock>(),
            serviceProvider.GetRequiredService<ILocalEventBus>(),
            serviceProvider);
        return ValueTask.FromResult<IWorkflowClient>(client);
    }
}
