using Cike.Workflow.Domain.Shared.ValueObjects;

namespace Cike.Workflow.Application.WorkflowDefinitions.Queries;

/// <summary>按版本行 Id 取该版本的 Options（变量 / 输入 / 输出参数定义等全量配置）。</summary>
public record GetWorkflowDefinitionOptionsQuery(long Id) : Query<WorkflowDefinitionOptionsValueObject>
{
}
