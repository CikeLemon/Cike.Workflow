namespace Cike.Workflow.Runtime.Models;

public class DispatchWorkflowDefinitionRequest
{
    public DispatchWorkflowDefinitionRequest()
    {
    }

    public DispatchWorkflowDefinitionRequest(long definitionVersionId)
    {
        DefinitionVersionId = definitionVersionId;
    }

    public long DefinitionVersionId { get; set; } = default!;

    public long? ParentWorkflowInstanceId { get; set; }

    public IDictionary<string, object> Input { get; set; } = new Dictionary<string, object>();

    public IDictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();

    public string? CorrelationId { get; set; }

    public long? InstanceId { get; set; }

    public string? TriggerActivityId { get; set; }

    public bool IsDebug { get; set; }

    public long? SchedulingActivityExecutionId { get; set; }

    public long? SchedulingWorkflowInstanceId { get; set; }

    public int? SchedulingCallStackDepth { get; set; }
}
