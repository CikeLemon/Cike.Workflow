using Cike.Workflow.Domain.Filters;

namespace Cike.Workflow.Application.WorkflowInstances.Queries;

/// <summary>查某草稿版本行的调试记录（调试实例），按创建时间倒序，最多返回 20 条。</summary>
public record GetWorkflowDebugRunsQuery(long DefinitionVersionId) : Query<List<WorkflowInstanceItemDto>>;
