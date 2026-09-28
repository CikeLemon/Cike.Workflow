namespace Cike.Workflow.Application.WorkflowDefinitions.Queries;

public record GetWorkflowDefinitionOptionListQuery(long WorkspaceId) : Query<List<WorkflowDefinitionOptionDto>>
{
}
