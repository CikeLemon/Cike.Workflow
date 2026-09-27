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

    /// <summary>
    /// 草稿行最新一次调试实例仍在运行时抛出：调试期间草稿内容不可变更。
    /// 判定只看最新一条（并发调试允许发起，旧调试卡死时重跑新的、或等新的到终态即解锁）。
    /// </summary>
    public async Task EnsureNoActiveDebugRunAsync(string definitionId, long definitionVersionRowId, CancellationToken cancellationToken = default)
    {
        var latest = await FindLatestDebugRunAsync(definitionId, definitionVersionRowId, cancellationToken);
        if (latest != null && !TerminalStatuses.Contains(latest.Status))
            throw new UserFriendlyException("该草稿存在进行中的调试运行，请等待其结束或取消调试后再操作。");
    }

    /// <summary>取草稿行最新一次调试实例（按创建时间倒序，数据库端截断），无则返回 null。</summary>
    public ValueTask<WorkflowInstance?> FindLatestDebugRunAsync(string definitionId, long definitionVersionRowId, CancellationToken cancellationToken = default)
        => workflowInstanceRepository.FindLatestDebugRunAsync(definitionId, definitionVersionRowId, cancellationToken);

    /// <summary>
    /// 发布门禁：仅草稿行可发布，且该行最新一次调试实例 Finished（Incident 不参与判定）、
    /// 创建时间晚于行内容的最后一次变更，保证"发布即所调"。
    /// </summary>
    public async Task EnsurePublishableAsync(WorkflowDefinition draftRow, CancellationToken cancellationToken = default)
    {
        if (draftRow.IsPublished)
            throw new UserFriendlyException("当前没有待发布的草稿，请先保存草稿并调试成功后再发布。");

        var debugRun = await FindLatestDebugRunAsync(draftRow.DefinitionId, draftRow.Id, cancellationToken);
        if (debugRun is null)
            throw new UserFriendlyException("该草稿尚未调试，请先完成一次调试运行再发布。");
        if (debugRun.Status != WorkflowStatus.Finished)
            throw new UserFriendlyException("最近一次调试未成功，请重新调试通过后再发布。");
        if (debugRun.CreatedAt < draftRow.UpdatedAt)
            throw new UserFriendlyException("草稿在最近一次调试后已有变更，请重新调试通过后再发布。");
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
