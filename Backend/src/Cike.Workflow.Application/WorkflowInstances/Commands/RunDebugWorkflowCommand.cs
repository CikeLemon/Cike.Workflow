namespace Cike.Workflow.Application.WorkflowInstances.Commands;

/// <summary>
/// 对草稿版本行发起调试（试跑）：从开始节点启动，实例标记 IsDebug，副作用真实发生。
/// 命令处理时预生成实例 Id；派发经本地事件总线同步完成，返回 Id 时实例已创建并执行（未挂起即到终态）。
/// </summary>
public record RunDebugWorkflowCommand(long DefinitionVersionId, IDictionary<string, object>? Input) : Command
{
    /// <summary>本次调试实例的预生成 Id。</summary>
    public long WorkflowInstanceId { get; set; }
}
