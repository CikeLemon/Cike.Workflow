using System.Linq.Expressions;
using Cike.Workflow.Common.Versions;

namespace Cike.Workflow.Domain.Data;

public interface IWorkflowDefinitionRepository : IRepository<WorkflowDefinition, long>
{
    /// <summary>
    /// 按定义 Id + <see cref="VersionOptions"/> 寻址单条版本行：版本过滤在数据库端完成，
    /// Version 降序取首（与缓存解析同源语义）；返回实体已经 <c>FindAsync</c> 还原影子属性 Options。
    /// 未命中返回 null。
    /// </summary>
    Task<WorkflowDefinition?> FindWorkflowDefinitionAsync(string definitionId, VersionOptions versionOptions, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量取各定义已发布的最高版本行（数据库端 GroupBy，行 Id + 版本号）：
    /// 未发布草稿要展示"当前已发布版本"及其版本行 Id 时用，未发布过的定义不在结果中。
    /// </summary>
    Task<Dictionary<string, (long Id, int Version)>> GetPublishedVersionMapAsync(IReadOnlyCollection<string> definitionIds, CancellationToken cancellationToken = default);

    /// <summary>按版本行 Id 批量取行名称（数据库端投影）。</summary>
    Task<Dictionary<long, string>> GetNamesByIdsAsync(IReadOnlyCollection<long> versionIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// 移动某定义的全部版本行到目标目录：数据库端 ExecuteUpdate 批量更新，
    /// 随后重读受影响行 write-through 同步运行时缓存。
    /// </summary>
    Task MoveAsync(string definitionId, long folderId, CancellationToken cancellationToken = default);

    /// <summary>删除某定义的全部版本行（软删 + 按版本清理运行时缓存）。</summary>
    Task DeleteVersionsAsync(string definitionId, CancellationToken cancellationToken = default);
}
