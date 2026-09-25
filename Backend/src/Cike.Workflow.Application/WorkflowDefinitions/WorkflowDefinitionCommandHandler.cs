using Cike.Workflow.Common.Versions;
using Cike.Workflow.Core.Activities.FlowchartActivity;
using Cike.Workflow.Core.Activities.FlowchartActivity.Models;
using Cike.Workflow.Core.Enums;
using Cike.Workflow.Core.Serialization;
using Cike.Workflow.Core.Validation;
using Cike.Workflow.Domain.Managers;

namespace Cike.Workflow.Application.WorkflowDefinitions;

public class WorkflowDefinitionCommandHandler(
    IWorkflowDefinitionRepository workflowDefinitionRepository,
    IWorkspaceRepository workspaceRepository,
    IFolderRepository folderRepository,
    IDistributedCacheClient distributedCacheClient,
    IActivitySerializer activitySerializer,
    ICurrentUser currentUser,
    IWorkflowValidator workflowValidator,
    ILock lockService,
    WorkflowDebugRunGuard debugRunGuard)
{
    private const string DefinitionIdSeqKey = "cike:workflow:workflow-definition:code:seq";

    [LocalEventHandler]
    public async Task AddAsync(AddWorkflowDefinitionCommand command, CancellationToken cancellationToken = default)
    {
        var dto = command.Dto;

        if (!await workspaceRepository.AnyAsync(x => x.Id == dto.WorkspaceId, cancellationToken))
            throw new UserFriendlyException("所属工作空间不存在，请检查后重试。");

        if (dto.FolderId != 0
            && !await folderRepository.AnyAsync(x => x.Id == dto.FolderId && x.WorkspaceId == dto.WorkspaceId, cancellationToken))
            throw new UserFriendlyException("目录不属于该工作空间，请检查后重试。");

        if (dto.DefinitionId.IsNullOrEmpty())
        {
            dto.DefinitionId = $"WF_{await distributedCacheClient.HashIncrementAsync(DefinitionIdSeqKey, 1)}";
        }

        await ValidateDuplicateAsync(dto.WorkspaceId, dto.Name, dto.DefinitionId, null, cancellationToken);

        var entity = dto.Adapt<WorkflowDefinition>();
        entity.OriginalStringData = activitySerializer.Serialize(new Flowchart()
        {
        });

        await workflowDefinitionRepository.InsertAsync(entity, cancellationToken: cancellationToken);
        command.Id = entity.Id;
    }

    [LocalEventHandler]
    public async Task UpdateAsync(UpdateWorkflowDefinitionCommand command, CancellationToken cancellationToken = default)
    {
        var entity = await GetEntityAsync(command.Id, cancellationToken);

        if (entity.IsSystem)
            throw new UserFriendlyException("系统内置工作流不允许修改。");

        await ValidateDuplicateAsync(entity.WorkspaceId, command.Dto.Name, null, entity.DefinitionId, cancellationToken);

        command.Dto.Adapt(entity);
        await workflowDefinitionRepository.UpdateAsync(entity, cancellationToken: cancellationToken);
    }

    [LocalEventHandler]
    public async Task SaveAsync(SaveWorkflowDefinitionCommand command, CancellationToken cancellationToken = default)
    {
        var entity = await GetEntityAsync(command.Id, cancellationToken);

        if (entity.IsSystem)
            throw new UserFriendlyException("系统内置工作流不允许保存。");
        if (entity.IsReadonly)
            throw new UserFriendlyException("只读工作流不允许保存。");

        var data = activitySerializer.Serialize(command.Dto.Root);

        await using var handle = await AcquireDefinitionLockAsync(entity.DefinitionId, cancellationToken);

        var latest = await GetLatestAsync(entity.DefinitionId, cancellationToken);

        // 调试锁定：保存会就地覆盖草稿内容，草稿上还有未终态调试实例时拒绝
        if (!latest.IsPublished)
            await debugRunGuard.EnsureNoActiveDebugRunAsync(latest.DefinitionId, latest.Id, cancellationToken);

        var row = await PersistDraftAsync(latest, data, command.Dto.Options, null, cancellationToken);
        command.DraftId = row.Id;
    }

    [LocalEventHandler]
    public async Task DeleteAsync(DeleteWorkflowDefinitionCommand command, CancellationToken cancellationToken = default)
    {
        var entity = await GetEntityAsync(command.Id, cancellationToken);

        if (entity.IsSystem)
            throw new UserFriendlyException("系统内置工作流不允许删除。");

        await using var handle = await AcquireDefinitionLockAsync(entity.DefinitionId, cancellationToken);

        // 删除禁令：定义下存在任何未终态实例（含调试实例）时不允许删除
        await debugRunGuard.EnsureNoRunningInstanceAsync(entity.DefinitionId, cancellationToken);

        // 仓储内跟踪物化后批量软删（软删 + 按版本清理运行时缓存都在仓储覆写口完成）
        await workflowDefinitionRepository.DeleteVersionsAsync(entity.DefinitionId, cancellationToken);
    }

    [LocalEventHandler]
    public async Task PublishAsync(PublishWorkflowDefinitionCommand command, CancellationToken cancellationToken = default)
    {
        var entity = await GetEntityAsync(command.Id, cancellationToken);

        if (entity.IsSystem)
            throw new UserFriendlyException("系统内置工作流不允许发布。");

        // 先校验后写库：严格画布校验全部通过才允许落库（Core 层 WorkflowValidator），失败时零写入
        var data = activitySerializer.Serialize(command.Root);
        var variables = command.Options.Variables
            .Select(x => new WorkflowVariableDefinition(x.Id, x.Name, x.TypeName, x.IsArray))
            .ToList();
        var errors = workflowValidator.Validate(new WorkflowValidationContext(command.Root, variables));
        if (errors.Count > 0)
            throw new UserFriendlyException(string.Join("；", errors.Select(x => x.Message)));

        await using var handle = await AcquireDefinitionLockAsync(entity.DefinitionId, cancellationToken);

        var latest = await GetLatestAsync(entity.DefinitionId, cancellationToken);

        // 发布门禁：保证"发布即所调"（未调试 / 未成功 / 调试后已变更 均拒绝）
        await debugRunGuard.EnsurePublishableAsync(latest, cancellationToken);

        var note = command.PublishedNote ?? string.Empty;
        var row = await PersistDraftAsync(latest, data, command.Options, row =>
        {
            row.IsPublished = true;
            row.PublishedNote = note;
            row.PublishedBy = long.TryParse(currentUser.Id, out var userid) ? userid : 0;
            row.PublishedAt = DateTime.Now;
        }, cancellationToken);
        command.PublishedId = row.Id;
    }

    [LocalEventHandler]
    public async Task MoveAsync(MoveWorkflowDefinitionCommand command, CancellationToken cancellationToken = default)
    {
        var entity = await GetEntityAsync(command.Id, cancellationToken);

        if (entity.IsSystem)
            throw new UserFriendlyException("系统内置工作流不允许移动。");

        if (command.FolderId != 0)
        {
            var folderExists = await folderRepository.AnyAsync(x => x.Id == command.FolderId && x.WorkspaceId == entity.WorkspaceId, cancellationToken);
            if (!folderExists)
                throw new UserFriendlyException("目标目录不存在或不属于该工作空间，请检查后重试。");
        }

        // 该定义的所有版本行一起移动（移动不改内容，IsReadonly 不拦截）：数据库端批量更新，不物化实体
        await workflowDefinitionRepository.MoveAsync(entity.DefinitionId, command.FolderId, cancellationToken);
    }

    [LocalEventHandler]
    public async Task RollbackAsync(RollbackWorkflowDefinitionCommand command, CancellationToken cancellationToken = default)
    {
        // 目标版本行直接经 FindAsync 取回（同时还原 Options 影子属性，回滚要复制画布内容含 Options）
        var target = await workflowDefinitionRepository.FindAsync(command.DefinitionVersionId, cancellationToken)
            ?? throw new UserFriendlyException("回滚目标版本不存在，请检查后重试。");

        if (target.DefinitionId != command.DefinitionId)
            throw new UserFriendlyException("回滚目标版本不存在，请检查后重试。");

        if (target.IsSystem)
            throw new UserFriendlyException("系统内置工作流不允许回滚。");
        if (target.IsReadonly)
            throw new UserFriendlyException("只读工作流不允许回滚。");

        await using var handle = await AcquireDefinitionLockAsync(target.DefinitionId, cancellationToken);

        var latest = await GetLatestAsync(target.DefinitionId, cancellationToken);

        if (latest.Id == target.Id)
            throw new UserFriendlyException("回滚目标版本与当前版本相同，无需回滚。");

        if (!latest.IsPublished)
        {
            // 调试锁定：回滚会就地覆盖草稿内容，草稿上还有未终态调试实例时拒绝
            await debugRunGuard.EnsureNoActiveDebugRunAsync(latest.DefinitionId, latest.Id, cancellationToken);

            // 有未发布草稿：用目标版本的画布内容覆盖草稿，版本号不变（原草稿内容丢弃——已接受的取舍）
            latest.OriginalStringData = target.OriginalStringData;
            latest.Options = target.Options;
            await workflowDefinitionRepository.UpdateAsync(latest, cancellationToken: cancellationToken);
            return;
        }

        // 最新版已发布：生成 v+1 新草稿，画布内容复制自目标版本，元数据沿用最新行
        latest.IsLatest = false;
        await workflowDefinitionRepository.UpdateAsync(latest, cancellationToken: cancellationToken);

        var draft = CreateDraft(latest, target.OriginalStringData, target.Options);
        await workflowDefinitionRepository.InsertAsync(draft, cancellationToken: cancellationToken);
    }

    private async Task ValidateDuplicateAsync(long workspaceId, string name, string? definitionId, string? excludeDefinitionId, CancellationToken cancellationToken = default)
    {
        if (definitionId != null
            && await workflowDefinitionRepository.AnyAsync(x => x.IsLatest && x.DefinitionId == definitionId, cancellationToken))
            throw new UserFriendlyException("工作流编号已存在，请使用其他编号。");

        var nameExists = await workflowDefinitionRepository.AnyAsync(x => x.IsLatest
            && x.WorkspaceId == workspaceId && x.Name == name
            && (excludeDefinitionId == null || x.DefinitionId != excludeDefinitionId), cancellationToken);
        if (nameExists)
            throw new UserFriendlyException("工作流名称在该工作空间内已存在，请使用其他名称。");
    }

    private async Task<WorkflowDefinition> GetEntityAsync(long id, CancellationToken cancellationToken = default)
    {
        return await workflowDefinitionRepository.FindAsync(id, cancellationToken)
            ?? throw new UserFriendlyException("工作流定义不存在，请检查后重试。");
    }

    /// <summary>定义级短锁：串行化"检查调试状态 → 变更内容"的临界区（统一获取口见 WorkflowDefinitionLockExtensions）。</summary>
    private Task<IAsyncDisposable> AcquireDefinitionLockAsync(string definitionId, CancellationToken cancellationToken = default)
        => lockService.AcquireAsync(definitionId, cancellationToken);

    /// <summary>
    /// 草稿落库（保存与发布共用）：最新行未发布时就地覆盖内容（版本号、IsLatest、IsPublished 不变）；
    /// 已发布则生成 v+1 新草稿，IsLatest 转移、元数据沿用最新行。<paramref name="finalize"/> 在写库前
    /// 套用到持有内容的行上（发布用其打发布标记），保证每条路径只落一次库。返回持有内容的行（已跟踪）。
    /// </summary>
    private async Task<WorkflowDefinition> PersistDraftAsync(WorkflowDefinition latest, string originalStringData,
        WorkflowDefinitionOptionsValueObject options, Action<WorkflowDefinition>? finalize, CancellationToken cancellationToken = default)
    {
        if (!latest.IsPublished)
        {
            latest.OriginalStringData = originalStringData;
            latest.Options = options;
            finalize?.Invoke(latest);
            await workflowDefinitionRepository.UpdateAsync(latest, cancellationToken: cancellationToken);
            return latest;
        }

        latest.IsLatest = false;
        await workflowDefinitionRepository.UpdateAsync(latest, cancellationToken: cancellationToken);

        var draft = CreateDraft(latest, originalStringData, options);
        finalize?.Invoke(draft);
        await workflowDefinitionRepository.InsertAsync(draft, cancellationToken: cancellationToken);
        return draft;
    }

    /// <summary>基于最新行创建 v+1 新草稿：元数据沿用最新行，画布内容取传入值。</summary>
    private static WorkflowDefinition CreateDraft(WorkflowDefinition latest, string originalStringData, WorkflowDefinitionOptionsValueObject options)
        => new()
        {
            WorkspaceId = latest.WorkspaceId,
            FolderId = latest.FolderId,
            DefinitionId = latest.DefinitionId,
            Name = latest.Name,
            Description = latest.Description,
            Type = latest.Type,
            UsableAsActivity = latest.UsableAsActivity,
            MaterializerName = latest.MaterializerName,
            IsReadonly = latest.IsReadonly,
            IsSystem = latest.IsSystem,
            Version = latest.Version + 1,
            IsLatest = true,
            IsPublished = false,
            OriginalStringData = originalStringData,
            Options = options,
        };

    private async Task<WorkflowDefinition> GetLatestAsync(string definitionId, CancellationToken cancellationToken = default)
    {
        // 经 FindWorkflowDefinitionAsync（内部 FindAsync）取回，确保影子属性（SerializedOptions）反序列化还原
        return await workflowDefinitionRepository.FindWorkflowDefinitionAsync(definitionId, VersionOptions.Latest, cancellationToken)
            ?? throw new UserFriendlyException("工作流定义不存在，请检查后重试。");
    }
}
