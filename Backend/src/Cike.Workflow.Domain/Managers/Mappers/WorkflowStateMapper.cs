using Cike.Workflow.Core.Runners.Models;

namespace Cike.Workflow.Domain.Managers.Mappers;

/// <summary>
/// Maps a workflow state to a workflow instance.
/// </summary>
public class WorkflowStateMapper : ISingletonDependency
{
    /// <summary>
    /// Maps a workflow state to a workflow instance.
    /// </summary>
    public WorkflowInstance? Map(WorkflowState? source)
    {
        if (source == null)
            return null;

        var workflowInstance = new WorkflowInstance();
        Apply(source, workflowInstance);

        return workflowInstance;
    }

    /// <summary>
    /// Maps a workflow state to a workflow instance.
    /// </summary>
    public void Apply(WorkflowState source, WorkflowInstance target)
    {
        target.Id = source.Id;
        target.CreatedAt = source.CreatedAt;
        target.DefinitionId = source.DefinitionId;
        target.DefinitionVersionId = source.DefinitionVersionId;
        target.Version = source.DefinitionVersion;
        target.ParentWorkflowInstanceId = source.ParentWorkflowInstanceId ?? 0;
        target.Status = source.Status;
        target.IsExecuting = source.IsExecuting;
        target.CorrelationId = source.CorrelationId ?? "";
        target.Name = source.Name ?? "";
        target.IncidentCount = source.Incidents.Count;
        target.UpdatedAt = source.UpdatedAt;
        target.FinishedAt = source.FinishedAt ?? default;
        target.WorkflowState = source;
        target.IsSystem = source.IsSystem;
        target.IsDebug = source.IsDebug;
    }

    /// <summary>
    /// Maps a workflow instance to a workflow state.
    /// </summary>
    public WorkflowState? Map(WorkflowInstance? source)
    {
        if (source == null)
            return null;

        var workflowState = source.WorkflowState;
        workflowState.Id = source.Id;
        workflowState.CreatedAt = source.CreatedAt;
        workflowState.DefinitionId = source.DefinitionId;
        workflowState.DefinitionVersionId = source.DefinitionVersionId;
        workflowState.DefinitionVersion = source.Version;
        workflowState.ParentWorkflowInstanceId = source.ParentWorkflowInstanceId;
        workflowState.Status = source.Status;
        workflowState.IsExecuting = source.IsExecuting;
        workflowState.CorrelationId = source.CorrelationId;
        workflowState.Name = source.Name;
        workflowState.UpdatedAt = source.UpdatedAt;
        workflowState.FinishedAt = source.FinishedAt;
        workflowState.IsSystem = source.IsSystem;
        workflowState.IsDebug = source.IsDebug;

        return workflowState;
    }
}
