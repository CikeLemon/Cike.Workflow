using Cike.Workflow.Domain.Filters;

namespace Cike.Workflow.Application.WorkflowInstances;

public class WorkflowInstanceQueryHandler(IWorkflowInstanceRepository workflowInstanceRepository,
    IWorkflowDefinitionRepository workflowDefinitionRepository,
    IActivityInstanceExecutionRecordRepository activityInstanceExecutionRecordRepository)
{
    [LocalEventHandler]
    public async Task GetPagedListAsync(GetPagedWorkflowInstanceListQuery query, CancellationToken cancellationToken = default)
    {
        // 列表默认隐藏调试实例（试跑记录不是生产行为），要看时显式传 IsDebug 过滤
        query.Filter.IsDebug ??= false;

        // 先校验时间戳过滤列白名单，把非法列转成业务 400，而不是落到 Apply() 里的原始 ArgumentException（500）
        var timestampErrors = WorkflowInstanceFilter.ValidateTimestampFilters(query.Filter.TimestampFilters).ToList();
        if (timestampErrors.Count > 0)
            throw new UserFriendlyException(string.Join(" ", timestampErrors));

        var (total, items) = await workflowInstanceRepository.GetPagedListAsync(
            query.Filter, query.PageDto.Sorting, Math.Max(1, query.PageDto.Page), Math.Max(1, query.PageDto.PageSize), cancellationToken);

        var dtos = items.Adapt<List<WorkflowInstanceItemDto>>();
        await FillDefinitionNamesAsync(dtos, cancellationToken);

        query.Result = new PagedResultDto<WorkflowInstanceItemDto>
        {
            Total = total,
            Items = dtos
        };
    }

    [LocalEventHandler]
    public async Task GetDebugRunsAsync(GetWorkflowDebugRunsQuery query, CancellationToken cancellationToken = default)
    {
        var instances = await workflowInstanceRepository.FindDebugRunsAsync(query.DefinitionVersionId, maxCount: 20, cancellationToken);

        var dtos = instances.Adapt<List<WorkflowInstanceItemDto>>();
        await FillDefinitionNamesAsync(dtos, cancellationToken);

        query.Result = dtos;
    }

    [LocalEventHandler]
    public async Task GetAsync(GetWorkflowInstanceQuery query, CancellationToken cancellationToken = default)
    {
        // 不加 BeginAsNoTracking：影子属性 SerializedWorkflowState 只在跟踪条目上可读，
        // 仓储读路径（OnLoadAsync）需要它还原 WorkflowState
        var instance = await workflowInstanceRepository.FindAsync(query.Id, cancellationToken)
            ?? throw new UserFriendlyException("工作流实例不存在，请检查后重试。");

        var records = await activityInstanceExecutionRecordRepository.FindByWorkflowInstanceAsync(query.Id, cancellationToken);

        query.Result = instance.Adapt<WorkflowInstanceDetailDto>();
        query.Result.ActivityInstances = records.Adapt<List<ActivityInstanceExecutionRecordDto>>();
        await FillDefinitionNamesAsync([query.Result], cancellationToken);
    }

    /// <summary>实例不冗余存定义名称，按 DefinitionVersionId 关联版本行批量补齐（数据库端投影 Id/Name）。</summary>
    private async Task FillDefinitionNamesAsync(List<WorkflowInstanceItemDto> items, CancellationToken cancellationToken)
    {
        var versionIds = items.Select(x => x.DefinitionVersionId).Where(x => x > 0).Distinct().ToList();
        if (versionIds.Count == 0)
            return;

        var names = await workflowDefinitionRepository.GetNamesByIdsAsync(versionIds, cancellationToken);

        foreach (var item in items)
            item.DefinitionName = names.GetValueOrDefault(item.DefinitionVersionId, string.Empty);
    }
}
