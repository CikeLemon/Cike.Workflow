using Cike.UniversalId.ULong;
using Cike.Workflow.Core.Enums;
using Cike.Workflow.Core.Runners.Models;

namespace Cike.Workflow.Domain.Managers;

/// <inheritdoc />
public class WorkflowInstanceFactory(ISnowflakeIdGenerator identityGenerator) : ISingletonDependency
{
    /// <inheritdoc />
    public WorkflowState CreateWorkflowState(WorkflowActivity workflow, WorkflowInstanceOptions? options = null)
    {
        var now = DateTime.Now;
        return new()
        {
            Id = options?.WorkflowInstanceId > 0 ? options.WorkflowInstanceId.Value : identityGenerator.NextId(),
            DefinitionId = workflow.DefinitionInfo.DefinitionId,
            DefinitionVersionId = workflow.DefinitionInfo.Id,
            DefinitionVersion = workflow.DefinitionInfo.Version,
            CorrelationId = options?.CorrelationId,
            Name = options?.Name,
            Input = options?.Input ?? new Dictionary<string, object>(),
            Properties = options?.Properties ?? new Dictionary<string, object>(),
            Status = WorkflowStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
            ParentWorkflowInstanceId = options?.ParentWorkflowInstanceId,
            IsSystem = workflow.IsSystem,
            IsDebug = options?.IsDebug ?? false
        };
    }

    /// <inheritdoc />
    public WorkflowInstance CreateWorkflowInstance(WorkflowActivity workflow, WorkflowInstanceOptions? options = null)
    {
        var workflowState = CreateWorkflowState(workflow, options);
        return new()
        {
            Id = workflowState.Id,
            ParentWorkflowInstanceId = workflowState.ParentWorkflowInstanceId ?? 0,
            WorkspaceId = workflow.DefinitionInfo.WorkspaceId,
            WorkflowState = workflowState,
            DefinitionId = workflowState.DefinitionId,
            DefinitionVersionId = workflowState.DefinitionVersionId,
            Version = workflowState.DefinitionVersion,
            CorrelationId = workflowState.CorrelationId ?? "",
            Name = workflowState.Name ?? "",
            Status = workflowState.Status,
            IncidentCount = workflowState.Incidents.Count,
            IsSystem = workflowState.IsSystem,
            IsDebug = workflowState.IsDebug,
            CreatedAt = workflowState.CreatedAt,
            UpdatedAt = workflowState.UpdatedAt,
            FinishedAt = workflowState.FinishedAt ?? default,
        };
    }
}
