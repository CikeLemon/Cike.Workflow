using Cike.Workflow.Caching;
using Cike.Workflow.Common.Versions;
using Cike.Workflow.Core.Models;
using Cike.Workflow.Core.Serialization;
using Cike.Workflow.Domain.Shared.CacheModels;
using Cike.Workflow.Domain.Shared.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using System.Net;

namespace Cike.Workflow.Service.Open.Tests.WorkflowDefinitions;

/// <summary>
/// 票1（示踪弹）：定义运行时缓存 write-through 与按版本读取最小闭环。
/// 写侧全部经真实 HTTP 端点驱动；断言侧解析缓存真实实现（内存介质为 InMemoryMultilevelCacheClient），
/// 条目设计 / 行 Id 影射 / 版本覆盖与 handle 解析等逻辑被真实执行。
/// </summary>
internal class WorkflowDefinitionCacheTest : WorkflowDefinitionTestBase
{
    /// <summary>缓存真实实现（Scoped），从测试宿主 Scope 解析。</summary>
    private IWorkflowDefinitionCache Cache => serviceProvider.GetRequiredService<IWorkflowDefinitionCache>();

    // ── 读口助手：原三读口语义映射到唯一 handle 读口 ─────────────────────

    private Task<WorkflowDefinitionCacheModel?> GetAsync(string definitionId, int version)
        => Cache.GetAsync(WorkflowDefinitionHandle.ByDefinitionId(definitionId, VersionOptions.SpecificVersion(version)));

    private Task<WorkflowDefinitionCacheModel?> GetLatestAsync(string definitionId)
        => Cache.GetAsync(WorkflowDefinitionHandle.ByDefinitionId(definitionId, VersionOptions.Latest));

    private Task<WorkflowDefinitionCacheModel?> GetLatestPublishedAsync(string definitionId)
        => Cache.GetAsync(WorkflowDefinitionHandle.ByDefinitionId(definitionId, VersionOptions.Published));

    private async Task<(string DefinitionId, long RowId)> PrepareAsync()
    {
        var workspaceId = await CreateWorkspaceAsync();
        var definitionId = $"WF_{Guid.NewGuid():N}";
        var rowId = await CreateDefinitionAsync(workspaceId, 0, definitionId);
        return (definitionId, rowId);
    }

    private Task<HttpResponseMessage> PostSaveAsync(long id, object dto)
        => CreateClient().PostAsJsonAsync($"/api/v1/WorkflowDefinitions/Save/{id}", dto);

    [Test]
    public async Task GetAsync_新增定义后_读到版本行且画布roundtrip可解析()
    {
        var (definitionId, rowId) = await PrepareAsync();

        var cached = await GetAsync(definitionId, 1);

        Assert.That(cached, Is.Not.Null);
        Assert.That(cached!.Id, Is.EqualTo(rowId));
        Assert.That(cached.DefinitionId, Is.EqualTo(definitionId));
        Assert.That(cached.Version, Is.EqualTo(1));
        Assert.That(cached.IsLatest, Is.True);
        Assert.That(cached.IsPublished, Is.False);
        var detail = (await GetDetailAsync(rowId)).RootElement;
        Assert.That(cached.Name, Is.EqualTo(GetString(detail, "name")));
        // 画布全文经 write-through + JSON round-trip 后仍是合法 JSON 对象（内容一致性由保存场景断言覆盖）
        Assert.That(cached.OriginalStringData, Is.Not.Null.And.Not.Empty);
        using var cachedCanvas = JsonDocument.Parse(cached.OriginalStringData);
        Assert.That(cachedCanvas.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Object));
    }

    [Test]
    public async Task GetAsync_保存草稿后_同版本条目被覆盖为新画布()
    {
        var (definitionId, rowId) = await PrepareAsync();

        await EnsureSuccessAsync(await PostSaveAsync(rowId, new { root = CreateValidCanvas("cache1") }));

        var cached = await GetAsync(definitionId, 1);
        Assert.That(cached, Is.Not.Null);
        Assert.That(cached!.OriginalStringData, Does.Contain("cache1_start"));

        // 再次保存：仍覆盖同一版本条目，不产生新键
        await EnsureSuccessAsync(await PostSaveAsync(rowId, new { root = CreateValidCanvas("cache2") }));
        cached = await GetAsync(definitionId, 1);
        Assert.That(cached!.OriginalStringData, Does.Contain("cache2_start"));
        Assert.That(cached.OriginalStringData, Does.Not.Contain("cache1_start"));
    }

    [Test]
    public async Task GetAsync_保存带变量草稿后_Options载荷经IPayloadSerializer还原等价()
    {
        var (definitionId, rowId) = await PrepareAsync();

        await EnsureSuccessAsync(await PostSaveAsync(rowId, new
        {
            root = CreateValidCanvas("opt"),
            options = new
            {
                variables = new object[] { new { id = "var1", name = "count", typeName = "Int32", isArray = false } },
            },
        }));

        var cached = await GetAsync(definitionId, 1);

        // 缓存模型只存不透明 OptionsPayload；用与影子列同一序列化器还原（项目 polymorphic/Type 转换器链路）
        var options = serviceProvider.GetRequiredService<IPayloadSerializer>()
            .Deserialize<WorkflowDefinitionOptionsValueObject>(cached!.OptionsPayload);
        Assert.That(options.Variables, Has.Count.EqualTo(1));
        Assert.That(options.Variables[0].Name, Is.EqualTo("count"));
        Assert.That(options.Variables[0].TypeName, Is.EqualTo("Int32"));
    }

    [Test]
    public async Task GetAsync_保存含输入默认值草稿后_OptionsPayload与影子列序列化同源()
    {
        var (definitionId, rowId) = await PrepareAsync();

        await EnsureSuccessAsync(await PostSaveAsync(rowId, new
        {
            root = CreateValidCanvas("poly"),
            options = new
            {
                inputs = new object[]
                {
                    new { name = "amount", type = "Decimal", defaultValue = new { type = "Literal", value = 42 } },
                },
            },
        }));

        var cached = await GetAsync(definitionId, 1);
        var options = serviceProvider.GetRequiredService<IPayloadSerializer>()
            .Deserialize<WorkflowDefinitionOptionsValueObject>(cached!.OptionsPayload);

        // object 多态成员（Expression.Value）经 IPayloadSerializer 链路可还原，非 JsonElement 裸漂移
        Assert.That(options.Inputs, Has.Count.EqualTo(1));
        Assert.That(options.Inputs[0].DefaultValue.Type, Is.EqualTo("Literal"));
        Assert.That(options.Inputs[0].DefaultValue.Value, Is.Not.Null);
        Assert.That(Convert.ToInt64(options.Inputs[0].DefaultValue.Value), Is.EqualTo(42));
    }

    [Test]
    public async Task GetAsync_删除定义后_版本条目不可见()
    {
        var (definitionId, rowId) = await PrepareAsync();
        await EnsureSuccessAsync(await PostSaveAsync(rowId, new { root = CreateValidCanvas("gone") }));

        var response = await CreateClient().DeleteAsync($"/api/v1/WorkflowDefinitions/{rowId}");
        await EnsureSuccessAsync(response);

        Assert.That(await GetAsync(definitionId, 1), Is.Null);
    }

    // ── 票2：派生读口与失效矩阵 ─────────────────────────────────────────

    private Task<HttpResponseMessage> PostRollbackAsync(string definitionId, long definitionVersionId)
        => CreateClient().PostAsJsonAsync("/api/v1/WorkflowDefinitions/Rollback", new { definitionId, definitionVersionId });

    /// <summary>准备一个已发布 v1（画布含 pub1）的定义。</summary>
    private async Task<(string DefinitionId, long V1RowId)> PreparePublishedAsync()
    {
        var (definitionId, rowId) = await PrepareAsync();
        await EnsureSuccessAsync(await PostSaveAsync(rowId, new { root = CreateValidCanvas("pub1") }));
        await EnsureSuccessAsync(await PostPublishAsync(rowId, new { root = CreateValidCanvas("pub1"), publishedNote = "发布" }));
        return (definitionId, rowId);
    }

    [Test]
    public async Task UpdateAsync_改名后_缓存行名称更新()
    {
        var (definitionId, rowId) = await PrepareAsync();
        var newName = $"改名_{Guid.NewGuid():N}".Substring(0, 20);

        var response = await CreateClient().PutAsJsonAsync($"/api/v1/WorkflowDefinitions/{rowId}", new
        {
            name = newName,
            description = "改名测试",
            type = 0,
            usableAsActivity = false,
        });
        await EnsureSuccessAsync(response);

        var cached = await GetAsync(definitionId, 1);
        Assert.That(cached!.Name, Is.EqualTo(newName));
    }

    [Test]
    public async Task GetLatestPublishedAsync_发布后_指向已发布行()
    {
        var (definitionId, _) = await PreparePublishedAsync();

        var published = await GetLatestPublishedAsync(definitionId);

        Assert.That(published, Is.Not.Null);
        Assert.That(published!.Version, Is.EqualTo(1));
        Assert.That(published.IsPublished, Is.True);
        Assert.That(published.OriginalStringData, Does.Contain("pub1_start"));
        Assert.That(await GetLatestAsync(definitionId), Is.Not.Null);
    }

    [Test]
    public async Task GetLatestAsync_发布后存草稿_最新指草稿且最新发布仍指已发布版()
    {
        var (definitionId, v1RowId) = await PreparePublishedAsync();

        var save = await PostSaveAsync(v1RowId, new { root = CreateValidCanvas("draft2") });
        await EnsureSuccessAsync(save);

        var latest = await GetLatestAsync(definitionId);
        Assert.That(latest!.Version, Is.EqualTo(2));
        Assert.That(latest.IsLatest, Is.True);
        Assert.That(latest.IsPublished, Is.False);
        Assert.That(latest.OriginalStringData, Does.Contain("draft2_start"));

        // 已发布指针不受草稿影响
        var published = await GetLatestPublishedAsync(definitionId);
        Assert.That(published!.Version, Is.EqualTo(1));
        Assert.That(published.IsLatest, Is.False);

        // v1 行缓存条目被更新（IsLatest 转移），但内容不变
        var v1 = await GetAsync(definitionId, 1);
        Assert.That(v1!.IsLatest, Is.False);
        Assert.That(v1.OriginalStringData, Does.Contain("pub1_start"));
    }

    [Test]
    public async Task GetLatestPublishedAsync_连续发布两版本_取版本号大者()
    {
        var (definitionId, v1RowId) = await PreparePublishedAsync();
        var save = await PostSaveAsync(v1RowId, new { root = CreateValidCanvas("pub2") });
        var draftId = await ReadLongAsync(save);
        await EnsureSuccessAsync(await PostPublishAsync(draftId, new { root = CreateValidCanvas("pub2"), publishedNote = "发布" }));

        var published = await GetLatestPublishedAsync(definitionId);

        Assert.That(published!.Version, Is.EqualTo(2));
        Assert.That(published.OriginalStringData, Does.Contain("pub2_start"));
    }

    [Test]
    public async Task RollbackAsync_有未发布草稿_覆盖草稿内容且最新版本指针不变()
    {
        var (definitionId, v1RowId) = await PreparePublishedAsync();
        var save = await PostSaveAsync(v1RowId, new { root = CreateValidCanvas("dirty") });
        await EnsureSuccessAsync(save);

        await EnsureSuccessAsync(await PostRollbackAsync(definitionId, v1RowId));

        var latest = await GetLatestAsync(definitionId);
        Assert.That(latest!.Version, Is.EqualTo(2));
        // 回滚用 v1 画布内容覆盖 v2 草稿
        Assert.That(latest.OriginalStringData, Does.Contain("pub1_start"));
        Assert.That(latest.OriginalStringData, Does.Not.Contain("dirty_start"));
    }

    [Test]
    public async Task RollbackAsync_最新版已发布_生成新草稿条目()
    {
        var (definitionId, v1RowId) = await PreparePublishedAsync();
        // v2 也发布 → 最新为已发布 v2，无草稿
        var save = await PostSaveAsync(v1RowId, new { root = CreateValidCanvas("pub2") });
        var v2RowId = await ReadLongAsync(save);
        await EnsureSuccessAsync(await PostPublishAsync(v2RowId, new { root = CreateValidCanvas("pub2"), publishedNote = "发布" }));

        // 回滚到 v1 → 生成 v3 草稿
        await EnsureSuccessAsync(await PostRollbackAsync(definitionId, v1RowId));

        var latest = await GetLatestAsync(definitionId);
        Assert.That(latest!.Version, Is.EqualTo(3));
        Assert.That(latest.IsPublished, Is.False);
        Assert.That(latest.OriginalStringData, Does.Contain("pub1_start"));
        var published = await GetLatestPublishedAsync(definitionId);
        Assert.That(published!.Version, Is.EqualTo(2));
    }

    [Test]
    public async Task MoveAsync_移动定义_全部版本行缓存FolderId更新()
    {
        var workspaceId = await CreateWorkspaceAsync();
        var definitionId = $"WF_{Guid.NewGuid():N}";
        var v1RowId = await CreateDefinitionAsync(workspaceId, 0, definitionId);
        await EnsureSuccessAsync(await PostSaveAsync(v1RowId, new { root = CreateValidCanvas("pub1") }));
        await EnsureSuccessAsync(await PostPublishAsync(v1RowId, new { root = CreateValidCanvas("pub1"), publishedNote = "发布" }));
        var save = await PostSaveAsync(v1RowId, new { root = CreateValidCanvas("mv2") });
        await EnsureSuccessAsync(save);

        var targetFolderId = await CreateFolderAsync(workspaceId);
        await EnsureSuccessAsync(await CreateClient().PostAsJsonAsync($"/api/v1/WorkflowDefinitions/Move/{v1RowId}", new { folderId = targetFolderId }));

        foreach (var version in new[] { 1, 2 })
        {
            var row = await GetAsync(definitionId, version);
            Assert.That(row!.FolderId, Is.EqualTo(targetFolderId));
        }
        // 移动不改内容
        Assert.That((await GetAsync(definitionId, 1))!.OriginalStringData, Does.Contain("pub1_start"));
        Assert.That((await GetAsync(definitionId, 2))!.OriginalStringData, Does.Contain("mv2_start"));
    }

    [Test]
    public async Task DeleteAsync_删除定义全部版本_三个读口均不可见()
    {
        var (definitionId, v1RowId) = await PreparePublishedAsync();
        var save = await PostSaveAsync(v1RowId, new { root = CreateValidCanvas("del2") });
        var v2RowId = await ReadLongAsync(save);

        // 删除按行：先删草稿再删已发布（Delete 命令连带全部版本行，任一行入参都会删全量）
        await EnsureSuccessAsync(await CreateClient().DeleteAsync($"/api/v1/WorkflowDefinitions/{v2RowId}"));

        Assert.That(await GetAsync(definitionId, 1), Is.Null);
        Assert.That(await GetAsync(definitionId, 2), Is.Null);
        Assert.That(await GetLatestAsync(definitionId), Is.Null);
        Assert.That(await GetLatestPublishedAsync(definitionId), Is.Null);
    }
}
