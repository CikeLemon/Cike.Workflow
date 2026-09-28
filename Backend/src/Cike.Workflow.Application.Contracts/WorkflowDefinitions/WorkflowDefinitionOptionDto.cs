namespace Cike.Workflow.Application.Contracts.WorkflowDefinitions;

/// <summary>工作流定义下拉选项（实例列表筛选数据源）：只带业务定义 Id 与名称。</summary>
public class WorkflowDefinitionOptionDto
{
    /// <summary>业务定义 Id（跨版本不变，对应实例过滤 DefinitionIds）。</summary>
    public string DefinitionId { get; set; } = null!;

    /// <summary>定义名称。</summary>
    public string Name { get; set; } = null!;
}
