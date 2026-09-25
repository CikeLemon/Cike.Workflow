using Cike.Workflow.Core.Enums;

namespace Cike.Workflow.Application.Contracts.WorkflowInstances;

public class WorkflowInstanceItemDto : AuditedEntityDto<long>
{
    public string DefinitionId { get; set; } = null!;

    public long DefinitionVersionId { get; set; }

    public int Version { get; set; }

    public string DefinitionName { get; set; } = null!;

    public long ParentWorkflowInstanceId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string CorrelationId { get; set; } = string.Empty;

    public bool IsExecuting { get; set; }

    public bool IsDebug { get; set; }

    public int IncidentCount { get; set; }

    public WorkflowStatus Status { get; set; }

    public DateTime FinishedAt { get; set; }
}
