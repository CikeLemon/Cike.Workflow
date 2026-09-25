namespace Cike.Workflow.Application.Contracts.WorkflowInstances;

/// <summary>发起调试（试跑）的请求体：输入变量留空则不传输入。</summary>
public class RunDebugWorkflowDto
{
    public IDictionary<string, object>? Input { get; set; }
}
