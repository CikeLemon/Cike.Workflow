using Cike.Workflow.Core.Serialization;

namespace Cike.Workflow.Application.WorkflowDefinitions;

public class WorkflowDefinitionQueryHandler(ICacheService<FolderCacheModel> folderCacheService,
    IActivitySerializer activitySerializer,
    IWorkflowDefinitionRepository workflowDefinitionRepository,
    IWorkflowValidator workflowValidator)
{
    [LocalEventHandler]
    public async Task GetListAsync(GetWorkflowDefinitionFolderListQuery query, CancellationToken cancellationToken = default)
    {
        using var _ = workflowDefinitionRepository.BeginAsNoTracking();
        Expression<Func<FolderCacheModel, bool>> filter = e => e.WorkspaceId == query.WorkspaceId && e.ParentId == query.FolderId;
        if (query.Keyword.IsNullOrEmpty() == false)
        {
            filter = e => e.WorkspaceId == query.WorkspaceId && e.ParentId == query.FolderId && e.Name.Contains(query.Keyword);
        }
        var allFolders = await folderCacheService.GetListAsync(filter, cancellationToken);

        Expression<Func<WorkflowDefinition, bool>> workflowFilter = e => e.WorkspaceId == query.WorkspaceId && e.FolderId == query.FolderId && e.IsLatest;
        if (query.Keyword.IsNullOrEmpty() == false)
        {
            workflowFilter = e => e.WorkspaceId == query.WorkspaceId && e.FolderId == query.FolderId && e.IsLatest && (e.Name.Contains(query.Keyword) || e.DefinitionId.Contains(query.Keyword));
        }
        var latestWorkflows = await workflowDefinitionRepository.GetListAsync(workflowFilter, query.Sorting, cancellationToken);

        var draftDefinitionIds = latestWorkflows.Where(x => !x.IsPublished).Select(x => x.DefinitionId).ToList();
        var publishedVersionMap = draftDefinitionIds.Count > 0
            ? await workflowDefinitionRepository.GetPublishedVersionMapAsync(draftDefinitionIds, cancellationToken)
            : new Dictionary<string, (long Id, int Version)>();

        query.Result = new List<WorkflowDefinitionFolderItemDto>();
        foreach (var item in allFolders.AsQueryable().OrderBy(query.Sorting))
        {
            var dto = item.Adapt<WorkflowDefinitionFolderItemDto>();
            dto.Type = WorkflowDefinitionFolderBaseType.Folder;
            dto.Data = new FolderItemDto
            {
                Name = item.Name,
                Path = item.BuildPath(allFolders).Select(e => new FolderPathDto { Id = e.Id, Name = e.Name }).ToList()
            };
            query.Result.Add(dto);
        }
        foreach (var item in latestWorkflows)
        {
            var data = item.Adapt<WorkflowDefinitionItemDto>();
            // 未发布的定义不在 map 中：元组默认值 (0, 0)，行 Id 为雪花 Id 不会是 0，0 即"从未发布"
            var (publishedId, publishedVersion) = item.IsPublished
                ? (Id: item.Id, Version: item.Version)
                : publishedVersionMap.GetValueOrDefault(item.DefinitionId);
            data.PublishedVersion = publishedVersion;
            data.PublishedVersionId = publishedId == 0 ? null : publishedId;
            query.Result.Add(new WorkflowDefinitionFolderItemDto
            {
                Id = item.Id,
                CreatedAt = item.CreatedAt,
                CreatedBy = item.CreatedBy,
                UpdatedBy = item.UpdatedBy,
                UpdatedAt = item.UpdatedAt,
                Type = WorkflowDefinitionFolderBaseType.WorkflowDefinition,
                Data = data
            });
        }
    }

    /// <summary>下拉选项数据源（实例列表筛选）：按空间取最新版定义行，只投影 DefinitionId + Name，每定义一项。</summary>
    [LocalEventHandler]
    public async Task GetOptionListAsync(GetWorkflowDefinitionOptionListQuery query, CancellationToken cancellationToken = default)
    {
        using var _ = workflowDefinitionRepository.BeginAsNoTracking();
        var latestWorkflows = await workflowDefinitionRepository.GetListAsync(
            x => x.WorkspaceId == query.WorkspaceId && x.IsLatest, "Name asc", cancellationToken);

        query.Result = latestWorkflows.Adapt<List<WorkflowDefinitionOptionDto>>();
    }

    [LocalEventHandler]
    public async Task GetAsync(GetWorkflowDefinitionQuery query, CancellationToken cancellationToken)
    {
        // 不加 BeginAsNoTracking：影子属性 SerializedOptions 只在跟踪条目上可读，
        // 仓储读路径（OnLoadAsync）需要它还原 Options（与 WorkflowInstanceQueryHandler 同源纪律）
        var entity = await workflowDefinitionRepository.GetAsync(query.Id, cancellationToken);
        query.Result = entity.Adapt<WorkflowDefinitionDetailDto>();
        query.Result.Root = activitySerializer.Deserialize<IActivity>(entity!.OriginalStringData);
    }

    [LocalEventHandler]
    public async Task GetVersionListAsync(GetWorkflowDefinitionVersionListQuery query, CancellationToken cancellationToken = default)
    {
        using var _ = workflowDefinitionRepository.BeginAsNoTracking();
        var versions = await workflowDefinitionRepository.GetListAsync(
            x => x.DefinitionId == query.DefinitionId, "Version desc", cancellationToken);

        query.Result = versions.Adapt<List<WorkflowDefinitionVersionItemDto>>();
    }

    /// <summary>按版本行 Id 取该版本 Options：跟踪条目上影子属性才可读（OnLoadAsync 还原），与 GetAsync 同源纪律。</summary>
    [LocalEventHandler]
    public async Task GetVersionOptionsAsync(GetWorkflowDefinitionOptionsQuery query, CancellationToken cancellationToken)
    {
        var entity = await workflowDefinitionRepository.GetAsync(query.Id, cancellationToken);
        query.Result = entity.Options;
    }

    [LocalEventHandler]
    public void ValidateCanvasAsync(ValidateWorkflowCanvasQuery query, CancellationToken cancellationToken = default)
    {
        var variables = query.Options.Variables
            .Select(x => new WorkflowVariableDefinition(x.Id, x.Name, x.TypeName, x.IsArray))
            .ToList();
        var errors = workflowValidator.Validate(new WorkflowValidationContext(query.Root, variables));

        query.Result = errors
            .Select(x => new WorkflowCanvasValidationErrorDto { ActivityId = x.ActivityId, NodeId = x.NodeId, Name = x.Name, Message = x.Message })
            .ToList();
    }
}
