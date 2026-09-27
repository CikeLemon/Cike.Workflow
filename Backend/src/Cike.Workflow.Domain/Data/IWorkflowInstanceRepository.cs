using Cike.Workflow.Domain.Filters;

namespace Cike.Workflow.Domain.Data;

public interface IWorkflowInstanceRepository : IRepository<WorkflowInstance, long>
{
    ValueTask<WorkflowInstance?> FindAsync(WorkflowInstanceFilter filter, CancellationToken cancellationToken = default);

    ValueTask<IEnumerable<WorkflowInstance>> FindManyAsync(WorkflowInstanceFilter filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// 分页查询实例：过滤（WorkflowInstanceFilter.Apply）与排序（System.Linq.Dynamic.Core）都在数据库端完成。
    /// 返回实体不还原影子属性（列表不读 WorkflowState）。
    /// </summary>
    Task<(long Total, List<WorkflowInstance> Items)> GetPagedListAsync(WorkflowInstanceFilter filter, string? sorting, int page, int pageSize, CancellationToken cancellationToken = default);
}
