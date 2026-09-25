using Cike.Workflow.Core.Models;

namespace Cike.Workflow.Runtime.Models;

public class CreateAndRunWorkflowInstanceRequest
{
    public WorkflowDefinitionHandle WorkflowDefinitionHandle { get; set; } = null!;

    public long? BookmarkId { get; set; }

    public string? CorrelationId { get; set; }

    public string? Name { get; set; }

    public IDictionary<string, object>? Input { get; set; }

    public IDictionary<string, object>? Variables { get; set; }

    public IDictionary<string, object>? Properties { get; set; }

    public long? ParentId { get; set; }

    public bool IsDebug { get; set; }

    public string? TriggerActivityId { get; set; }

    public ActivityHandle? ActivityHandle { get; set; }

    public bool IncludeWorkflowOutput { get; set; }

    public long? SchedulingActivityExecutionId { get; set; }

    public long? SchedulingWorkflowInstanceId { get; set; }

    public int? SchedulingCallStackDepth { get; set; }
}
