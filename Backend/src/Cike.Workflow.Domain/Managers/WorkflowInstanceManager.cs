using Cike.Uow;
using Cike.Workflow.Core.Runners.Models;
using Cike.Workflow.Domain.Data;
using Cike.Workflow.Domain.Managers.Mappers;

namespace Cike.Workflow.Domain.Managers
{
    public class WorkflowInstanceManager(
    IWorkflowInstanceRepository store,
    WorkflowInstanceFactory workflowInstanceFactory,
    IUnitOfWork unitOfWork,
    WorkflowStateMapper workflowStateMapper) : IScopedDependency
    {
        public async Task<WorkflowInstance?> FindByIdAsync(long instanceId, CancellationToken cancellationToken = default)
        {
            return await store.FindAsync(instanceId, cancellationToken);
        }

        public async Task<WorkflowInstance> SaveAsync(WorkflowState workflowState, CancellationToken cancellationToken)
        {
            // 优先取已跟踪实体就地应用状态：实例创建与运行落库在同一 Scope 时，
            // 映射出的新实体与已跟踪条目同键会触发 EF 身份冲突（Attach 失败）
            var existing = await store.FindAsync(workflowState.Id, cancellationToken);
            if (existing != null)
            {
                workflowStateMapper.Apply(workflowState, existing);
                await store.UpdateAsync(existing, cancellationToken: cancellationToken);
                return existing;
            }

            var workflowInstance = workflowStateMapper.Map(workflowState)!;
            await store.InsertAsync(workflowInstance, cancellationToken: cancellationToken);
            return workflowInstance;
        }

        public async Task<WorkflowInstance> CreateAndCommitWorkflowInstanceAsync(WorkflowActivity workflow, WorkflowInstanceOptions? options = null, CancellationToken cancellationToken = default)
        {
            var workflowInstance = CreateWorkflowInstance(workflow, options);
            await store.InsertAsync(workflowInstance, cancellationToken: cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return workflowInstance;
        }

        /// <inheritdoc />
        private WorkflowInstance CreateWorkflowInstance(WorkflowActivity workflow, WorkflowInstanceOptions? options = null)
        {
            return workflowInstanceFactory.CreateWorkflowInstance(workflow, options);
        }
    }
}
