using Cike.Core.Exceptions;
using Cike.Workflow.Core.Enums;
using Cike.Workflow.Domain.Data;
using Cike.Workflow.Domain.Filters;

namespace Cike.Workflow.Domain.Managers;

/// <summary>
/// 调试相关领域守卫：调试锁定（草稿内容变更禁令）、删除禁令与发布门禁的实例侧查询。
/// </summary>
public class WorkflowDebugRunGuard(IWorkflowInstanceRepository workflowInstanceRepository) : IScopedDependency
{
    /// <summary>实例终态集合：Finished / Cancelled / Faulted 之外都视为仍在运行（Interrupted 保守按运行中处理）。</summary>
    private static readonly WorkflowStatus[] TerminalStatuses = [WorkflowStatus.Finished, WorkflowStatus.Cancelled, WorkflowStatus.Faulted];

    /// <summary>草稿行上存在未终态调试实例时抛出：调试期间草稿内容不可变更。</summary>
    public async Task EnsureNoActiveDebugRunAsync(string definitionId, long definitionVersionRowId, CancellationToken cancellationToken = default)
    {
        var exists = await workflowInstanceRepository.AnyAsync(
            x => x.DefinitionId == definitionId
                 && x.DefinitionVersionId == definitionVersionRowId
                 && x.IsDebug
                 && !TerminalStatuses.Contains(x.Status), cancellationToken);
        if (exists)
            throw new UserFriendlyException("该草稿存在进行中的调试运行，请等待其结束或取消调试后再操作。");
    }

    /// <summary>取草稿行最新一次调试实例（按创建时间倒序），无则返回 null。</summary>
    public async Task<WorkflowInstance?> FindLatestDebugRunAsync(string definitionId, long definitionVersionRowId, CancellationToken cancellationToken = default)
    {
        var instances = await workflowInstanceRepository.FindManyAsync(new WorkflowInstanceFilter
        {
            DefinitionId = definitionId,
            DefinitionVersionId = definitionVersionRowId,
            IsDebug = true,
        }, cancellationToken);

        return instances.OrderByDescending(x => x.CreatedAt).FirstOrDefault();
    }

    /// <summary>定义下存在任何未终态实例（含调试实例）时抛出：运行中的实例禁止随定义删除。</summary>
    public async Task EnsureNoRunningInstanceAsync(string definitionId, CancellationToken cancellationToken = default)
    {
        var exists = await workflowInstanceRepository.AnyAsync(
            x => x.DefinitionId == definitionId && !TerminalStatuses.Contains(x.Status), cancellationToken);
        if (exists)
            throw new UserFriendlyException("该工作流存在运行中的实例，不允许删除。");
    }
}
