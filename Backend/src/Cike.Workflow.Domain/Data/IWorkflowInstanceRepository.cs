using Cike.Workflow.Domain.Filters;

namespace Cike.Workflow.Domain.Data;

public interface IWorkflowInstanceRepository : IRepository<WorkflowInstance, long>
{
    ValueTask<WorkflowInstance?> FindAsync(WorkflowInstanceFilter filter, CancellationToken cancellationToken = default);

    ValueTask<IEnumerable<WorkflowInstance>> FindManyAsync(WorkflowInstanceFilter filter, CancellationToken cancellationToken = default);

    /// <summary>取草稿行最新一次调试实例（按创建时间倒序，数据库端截断），无则返回 null。</summary>
    ValueTask<WorkflowInstance?> FindLatestDebugRunAsync(string definitionId, long definitionVersionRowId, CancellationToken cancellationToken = default);

    /// <summary>取草稿行的调试记录（按创建时间倒序，数据库端截断 maxCount 条）。</summary>
    ValueTask<List<WorkflowInstance>> FindDebugRunsAsync(long definitionVersionRowId, int maxCount, CancellationToken cancellationToken = default);

    /// <summary>
    /// 分页查询实例：过滤（WorkflowInstanceFilter.Apply）与排序（System.Linq.Dynamic.Core）都在数据库端完成。
    /// 返回实体不还原影子属性（列表不读 WorkflowState）。
    /// </summary>
    Task<(long Total, List<WorkflowInstance> Items)> GetPagedListAsync(WorkflowInstanceFilter filter, string? sorting, int page, int pageSize, CancellationToken cancellationToken = default);
}
