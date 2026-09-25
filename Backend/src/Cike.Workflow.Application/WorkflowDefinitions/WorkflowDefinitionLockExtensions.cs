namespace Cike.Workflow.Application.WorkflowDefinitions;

/// <summary>
/// 定义级短锁的统一获取口：等待上限 30 分钟仅作互斥安全阀，获取失败按操作冲突拒绝。
/// 保存/回滚/发布/删除/发起调试共用，保证"检查调试状态 → 变更内容"的临界区互斥。
/// </summary>
internal static class WorkflowDefinitionLockExtensions
{
    internal static async Task<IAsyncDisposable> AcquireAsync(this ILock lockService, string definitionId, CancellationToken cancellationToken = default)
    {
        var handle = await lockService.TryGetAsync(WorkflowDefinitionLock.GetKey(definitionId), WorkflowDefinitionLock.Timeout, cancellationToken);
        return handle ?? throw new UserFriendlyException("当前有其他操作正在进行，请稍后重试。");
    }
}
