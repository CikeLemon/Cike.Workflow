using Cike.Workflow.Caching;
using Cike.Workflow.Core.Serialization;
using Cike.Workflow.Domain.Shared.CacheModels;
using Cike.Workflow.Domain.Shared.ValueObjects;
using Microsoft.Extensions.Logging;
using System.Linq.Dynamic.Core;

namespace Cike.EntityFrameworkCore.Repositories;

/// <summary>
/// 工作流定义仓储：影子属性序列化 + 运行时缓存 write-through（写库成功后同步定义条目与行 Id 影射）。
/// <para>
/// 两条纪律：
/// ① 经本仓储 Update 进缓存的实体，其 <c>Options</c> 必须是已还原对象图的行——取行须经
///    <c>FindAsync</c>（OnLoadAsync 还原影子属性），用 <c>GetQueryable()</c> / 列表重载取回的未还原实体
///    不得回写，否则空 Options 会被静默刷进影子列与缓存（与 MoveWorkflowDefinitionCommandHandler 的警示同源）。
/// ② 免失效兜底依据（见 .scratch/workflow-definition-runtime-cache/PRD.md）：已发布版本行不可变
///    （改动走新版本），WorkflowRuntime 只读已发布条目、草稿调试实例在 Application 层建好后交调度，
///    故不依赖 TTL / DB 回退 / 跨实例失效广播。
/// 已知取舍（与 Folder / Workspace 的 <c>CachedEfCoreRepository</c> 同源）：缓存同步发生在
/// SaveChanges 之后、请求管道最终提交之前——极端情况下（提交失败回滚）可能残留幻影条目；
/// 对"发布是显式操作 + 已发布行不可变"的场景风险可控。
/// </para>
/// </summary>
public class WorkflowDefinitionRepository(CikeWorkflowDbContext context, IPayloadSerializer payloadSerializer,
    ILogger<WorkflowDefinitionRepository> logger, IWorkflowDefinitionCache workflowDefinitionCache)
    : SerializedEfCoreRepository<CikeWorkflowDbContext, WorkflowDefinition>(context), IWorkflowDefinitionRepository, IScopedDependency
{
    public override async Task InsertManyAsync(IEnumerable<WorkflowDefinition> entities, bool autoSave = true, CancellationToken cancellationToken = default)
    {
        await base.InsertManyAsync(entities, autoSave, cancellationToken);
        await SetCacheAsync(entities, cancellationToken);
    }

    public override async Task UpdateManyAsync(IEnumerable<WorkflowDefinition> entities, bool autoSave = true, CancellationToken cancellationToken = default)
    {
        await base.UpdateManyAsync(entities, autoSave, cancellationToken);
        await SetCacheAsync(entities, cancellationToken);
    }

    public override async Task DeleteManyAsync(IEnumerable<WorkflowDefinition> entities, bool autoSave = true, CancellationToken cancellationToken = default)
    {
        await base.DeleteManyAsync(entities, autoSave, cancellationToken);
        foreach (var entity in entities)
        {
            await workflowDefinitionCache.RemoveAsync(entity.DefinitionId, entity.Version, cancellationToken);
        }
    }

    /// <summary>框架的单数写方法内部委托批量方法，覆写批量口即覆盖全部写路径（与 CachedEfCoreRepository 同理）。</summary>
    private async Task SetCacheAsync(IEnumerable<WorkflowDefinition> entities, CancellationToken cancellationToken)
    {
        foreach (var entity in entities)
        {
            var model = entity.Adapt<WorkflowDefinitionCacheModel>();
            // Options 走与影子列同一个 IPayloadSerializer：保留 Expression.Value / CustomProperties
            // 等 object 多态成员的类型保真；缓存客户端自带序列化器没有项目的转换器，只准存不透明字符串。
            model.OptionsPayload = payloadSerializer.Serialize(entity.Options);
            await workflowDefinitionCache.SetAsync(model, cancellationToken);
        }
    }

    /// <summary>版本过滤下推数据库（WithVersion 与缓存解析同源），命中行再经 FindAsync 取回以还原影子属性 Options。</summary>
    public async Task<WorkflowDefinition?> FindWorkflowDefinitionAsync(string definitionId, VersionOptions versionOptions, CancellationToken cancellationToken = default)
    {
        var rowId = await GetQueryable().AsNoTracking()
            .Where(e => e.DefinitionId == definitionId)
            .WithVersion(versionOptions)
            .OrderByDescending(e => e.Version)
            .Select(e => e.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (rowId == 0)
            return null;

        return await FindAsync(rowId, cancellationToken);
    }

    public async Task<Dictionary<string, (long Id, int Version)>> GetPublishedVersionMapAsync(IReadOnlyCollection<string> definitionIds, CancellationToken cancellationToken = default)
    {
        var rows = await GetQueryable().AsNoTracking()
            .Where(x => x.IsPublished && definitionIds.Contains(x.DefinitionId))
            .GroupBy(x => x.DefinitionId)
            .Select(g => g.OrderByDescending(x => x.Version).First())
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(x => x.DefinitionId, x => (Id: x.Id, Version: x.Version));
    }

    public async Task<Dictionary<long, string>> GetNamesByIdsAsync(IReadOnlyCollection<long> versionIds, CancellationToken cancellationToken = default)
    {
        return await GetQueryable().AsNoTracking()
            .Where(x => versionIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
    }

    public async Task MoveAsync(string definitionId, long folderId, CancellationToken cancellationToken = default)
    {
        // 数据库端 ExecuteUpdate 批量更新，不物化实体
        await GetQueryable()
            .Where(x => x.DefinitionId == definitionId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.FolderId, folderId), cancellationToken);

        // ExecuteUpdate 绕过跟踪器：请求内已被跟踪的同键实体（如处理器入口 GetEntityAsync 取回的行）
        // 经身份解析会以陈旧值返回，缓存会被旧 FolderId 刷写——先清空跟踪器再重读
        context.ChangeTracker.Clear();

        // 同步运行时缓存：重读受影响行（跟踪物化 + OnLoadAsync 还原 Options，纪律①），write-through 刷新旧条目
        var versions = await GetQueryable()
            .Where(x => x.DefinitionId == definitionId)
            .ToListAsync(cancellationToken);
        foreach (var version in versions)
            await OnLoadAsync(version, cancellationToken);
        await SetCacheAsync(versions, cancellationToken);
    }

    public async Task DeleteVersionsAsync(string definitionId, CancellationToken cancellationToken = default)
    {
        // 跟踪物化（同键实体经身份解析合并，不冲突）后走 DeleteManyAsync 覆写口：软删 + 按版本清理缓存
        var versions = await GetQueryable()
            .Where(x => x.DefinitionId == definitionId)
            .ToListAsync(cancellationToken);

        await DeleteManyAsync(versions, cancellationToken: cancellationToken);
    }

    protected override ValueTask OnSaveAsync(WorkflowDefinition entity, CancellationToken cancellationToken)
    {
        DbContext.Entry(entity).Property("SerializedOptions").CurrentValue = payloadSerializer.Serialize(entity.Options);
        return ValueTask.CompletedTask;
    }

    protected override async ValueTask OnLoadAsync(WorkflowDefinition? entity, CancellationToken cancellationToken)
    {
        if (entity == null)
            return;

        var json = (string?)DbContext.Entry(entity).Property("SerializedOptions").CurrentValue;

        // no-tracking 查询不携带影子属性值（值只存在于跟踪条目）：回落到按 Id 投影直读影子列，
        // 避免 no-tracking 读路径把 Options 静默降级为空对象
        if (string.IsNullOrWhiteSpace(json))
            json = await DbContext.Set<WorkflowDefinition>()
                .Where(x => x.Id == entity.Id)
                .Select(x => EF.Property<string>(x, "SerializedOptions"))
                .FirstOrDefaultAsync(cancellationToken);

        try
        {
            if (!string.IsNullOrWhiteSpace(json))
                entity.Options = payloadSerializer.Deserialize<WorkflowDefinitionOptionsValueObject>(json);
        }
        catch (Exception exp)
        {
            logger.LogError(exp, "Could not deserialize workflow definition state: {DefinitionId}. Reverting to default state", entity.DefinitionId);
        }

        return;
    }
}
