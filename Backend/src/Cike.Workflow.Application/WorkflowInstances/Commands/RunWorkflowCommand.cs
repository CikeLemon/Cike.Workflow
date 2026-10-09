namespace Cike.Workflow.Application.WorkflowInstances.Commands;

public record RunWorkflowCommand(DispatchWorkflowDefinitionRequest Request) : Command
{
    public long Id { get; set; }
}
