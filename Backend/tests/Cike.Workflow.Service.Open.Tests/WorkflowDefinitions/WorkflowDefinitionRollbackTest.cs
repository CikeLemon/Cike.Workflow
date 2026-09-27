using System.Net;

namespace Cike.Workflow.Service.Open.Tests.WorkflowDefinitions;

/// <summary>票③：回滚工作流定义到历史版本（覆盖草稿 / 生成新草稿）。入参：DefinitionId + 目标版本行 Id。</summary>
internal class WorkflowDefinitionRollbackTest : WorkflowDefinitionTestBase
{
    private Task<HttpResponseMessage> PostRollbackAsync(string definitionId, long definitionVersionId)
        => CreateClient().PostAsJsonAsync("/api/v1/WorkflowDefinitions/Rollback", new { definitionId, definitionVersionId });

    private async Task<long> SaveAsync(long id, string prefix)
    {
        var response = await CreateClient().PostAsJsonAsync($"/api/v1/WorkflowDefinitions/Save/{id}", new { root = CreateValidCanvas(prefix) });
        await EnsureSuccessAsync(response);
        return await ReadLongAsync(response);
    }

    private async Task PublishAsync(long id, string prefix)
        => await EnsureSuccessAsync(await PostPublishAsync(id, new { root = CreateValidCanvas(prefix), publishedNote = "发布" }));

    private async Task<(string DefinitionId, long RowId)> PrepareAsync()
    {
        var workspaceId = await CreateWorkspaceAsync();
        var definitionId = $"WF_{Guid.NewGuid():N}";
        var rowId = await CreateDefinitionAsync(workspaceId, 0, definitionId);
        return (definitionId, rowId);
    }

    [Test]
    public async Task RollbackAsync_有未发布草稿_草稿内容被覆盖且版本号不变()
    {
        var (definitionId, rowId) = await PrepareAsync();
        // v1 保存并发布 → v2 草稿（内容 rb2）
        await SaveAsync(rowId, "rb1");
        await PublishAsync(rowId, "rb1");
        await SaveAsync(rowId, "rb2");

        var response = await PostRollbackAsync(definitionId, rowId);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var detail = (await GetDetailAsync(rowId)).RootElement;
        // v1 行是已发布历史版本，内容不变
        Assert.That(detail.GetProperty("root").GetRawText(), Does.Contain("rb1_start"));

        var versions = await GetVersionListAsync(definitionId);
        Assert.That(versions, Has.Count.EqualTo(2));
        // 最新行仍是 v2：版本号不变、未发布，内容已还原为 v1
        var latest = versions.Single(x => GetInt(x, "version") == 2);
        Assert.That(GetBool(latest, "isPublished"), Is.False);
        var latestDetail = (await GetDetailAsync(GetLong(latest, "id"))).RootElement;
        Assert.That(latestDetail.GetProperty("root").GetRawText(), Does.Contain("rb1_start"));
    }

    [Test]
    public async Task RollbackAsync_最新版已发布_生成v3新草稿且内容来自目标版本()
    {
        var (definitionId, rowId) = await PrepareAsync();
        // v1 发布（rb1）→ v2（rb2）发布 → 最新为已发布 v2，无草稿
        await SaveAsync(rowId, "rb1");
        await PublishAsync(rowId, "rb1");
        await SaveAsync(rowId, "rb2");
        await PublishAsync(rowId, "rb2");

        var response = await PostRollbackAsync(definitionId, rowId);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var versions = await GetVersionListAsync(definitionId);
        Assert.That(versions, Has.Count.EqualTo(3));

        // 已发布行 v1 / v2 内容与标记不变
        var v1 = versions.Single(x => GetInt(x, "version") == 1);
        var v2 = versions.Single(x => GetInt(x, "version") == 2);
        Assert.That(GetBool(v1, "isPublished"), Is.True);
        Assert.That(GetBool(v2, "isPublished"), Is.True);
        var v1Detail = (await GetDetailAsync(GetLong(v1, "id"))).RootElement;
        var v2Detail = (await GetDetailAsync(GetLong(v2, "id"))).RootElement;
        Assert.That(v1Detail.GetProperty("root").GetRawText(), Does.Contain("rb1_start"));
        Assert.That(v2Detail.GetProperty("root").GetRawText(), Does.Contain("rb2_start"));

        // 新草稿 v3：IsLatest 转移，内容复制自 v1
        var v3 = versions.Single(x => GetInt(x, "version") == 3);
        Assert.That(GetBool(v3, "isLatest"), Is.True);
        Assert.That(GetBool(v3, "isPublished"), Is.False);
        var v3Detail = (await GetDetailAsync(GetLong(v3, "id"))).RootElement;
        Assert.That(v3Detail.GetProperty("root").GetRawText(), Does.Contain("rb1_start"));
        Assert.That(GetBool(v2Detail, "isLatest"), Is.False);
    }

    [Test]
    public async Task RollbackAsync_只回滚画布内容_元数据保持当前值()
    {
        var (definitionId, rowId) = await PrepareAsync();
        await SaveAsync(rowId, "rb1");
        await PublishAsync(rowId, "rb1");
        var draftId = await SaveAsync(rowId, "rb2");
        // v2 草稿期间改名（元数据走已有 Update 命令，作用于草稿行）
        await EnsureSuccessAsync(await CreateClient().PutAsJsonAsync($"/api/v1/WorkflowDefinitions/{draftId}", new
        {
            name = $"流程_{Guid.NewGuid():N}".Substring(0, 20),
            description = "改名后的描述",
            type = 0,
            usableAsActivity = false,
        }));

        await EnsureSuccessAsync(await PostRollbackAsync(definitionId, rowId));

        var detail = (await GetDetailAsync(draftId)).RootElement;
        Assert.That(GetString(detail, "description"), Is.EqualTo("改名后的描述"));
        Assert.That(detail.GetProperty("root").GetRawText(), Does.Contain("rb1_start"));
    }

    [Test]
    public async Task RollbackAsync_回滚目标为当前行_返回400()
    {
        var (definitionId, rowId) = await PrepareAsync();
        await SaveAsync(rowId, "cur");

        var response = await PostRollbackAsync(definitionId, rowId);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("相同"));
    }

    [Test]
    public async Task RollbackAsync_回滚目标版本不存在_返回400()
    {
        var (definitionId, rowId) = await PrepareAsync();
        await SaveAsync(rowId, "nb");
        await PublishAsync(rowId, "nb");
        await SaveAsync(rowId, "nb2");

        var response = await PostRollbackAsync(definitionId, 999_999);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("不存在"));
    }

    [Test]
    public async Task RollbackAsync_回滚目标版本不属于该定义_返回400()
    {
        var (definitionId, rowId) = await PrepareAsync();
        await SaveAsync(rowId, "own");
        await PublishAsync(rowId, "own");
        await SaveAsync(rowId, "own2");
        // 另一个定义的版本行
        var otherDefinitionId = $"WF_{Guid.NewGuid():N}";
        var otherRowId = await CreateDefinitionAsync(await CreateWorkspaceAsync(), 0, otherDefinitionId);

        var response = await PostRollbackAsync(definitionId, otherRowId);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("不存在"));
    }

    [Test]
    public async Task RollbackAsync_系统内置定义_返回400()
    {
        var (definitionId, rowId) = await PrepareAsync();
        await SaveAsync(rowId, "sys");
        await PublishAsync(rowId, "sys");
        await SaveAsync(rowId, "sys2");
        await SeedDefinitionAsync(definitionId, e => e.IsSystem = true);

        var response = await PostRollbackAsync(definitionId, rowId);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("系统内置"));
    }

    [Test]
    public async Task RollbackAsync_只读定义_返回400()
    {
        var (definitionId, rowId) = await PrepareAsync();
        await SaveAsync(rowId, "ro");
        await PublishAsync(rowId, "ro");
        await SaveAsync(rowId, "ro2");
        await SeedDefinitionAsync(definitionId, e => e.IsReadonly = true);

        var response = await PostRollbackAsync(definitionId, rowId);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("只读"));
    }
}
