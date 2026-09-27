using Cike.Core.Exceptions;
using Cike.Workflow.Core.Enums;
using Cike.Workflow.Domain.Data;

namespace Cike.Workflow.Domain.Managers;

/// <summary>
/// 调试相关领域守卫：删除禁令的实例侧查询（发布门禁暂未启用，证据仅作记录）。
/// </summary>
public class WorkflowDebugRunGuard(IWorkflowInstanceRepository workflowInstanceRepository) : IScopedDependency
{
    /// <summary>实例终态集合：Finished / Cancelled / Faulted 之外都视为仍在运行（Interrupted 保守按运行中处理）。</summary>
    private static readonly WorkflowStatus[] TerminalStatuses = [WorkflowStatus.Finished, WorkflowStatus.Cancelled, WorkflowStatus.Faulted];

    /// <summary>定义下存在任何未终态实例（含调试实例）时抛出：运行中的实例禁止随定义删除。</summary>
    public async Task EnsureNoRunningInstanceAsync(string definitionId, CancellationToken cancellationToken = default)
    {
        var exists = await workflowInstanceRepository.AnyAsync(
            x => x.DefinitionId == definitionId && !TerminalStatuses.Contains(x.Status), cancellationToken);
        if (exists)
            throw new UserFriendlyException("该工作流存在运行中的实例，不允许删除。");
    }
}
