using System.Net;

namespace Cike.Workflow.Service.Open.Tests.WorkflowDefinitions;

/// <summary>
/// 定义列表的已发布版本信息：列表条目（type=2 的 data）携带最新已发布版本的行 Id（publishedVersionId）
/// 与版本号（publishedVersion），草稿要能回链到"当前已发布版本"。
/// </summary>
[Category("Integration")]
internal class WorkflowDefinitionListTest : WorkflowDefinitionTestBase
{
    /// <summary>取列表中指定 DefinitionId 的定义条目 data 节点。</summary>
    private async Task<JsonElement> GetDefinitionItemAsync(long workspaceId, string definitionId)
    {
        var items = await GetFolderListAsync(workspaceId, 0);
        var entry = items.Single(x => GetInt(x, "type") == 2
            && GetString(x.GetProperty("data"), "definitionId") == definitionId);
        return entry.GetProperty("data");
    }

    [Test]
    public async Task GetListAsync_多次发布_返回最高已发布版本的行Id()
    {
        var workspaceId = await CreateWorkspaceAsync();
        var definitionId = $"WF_{Guid.NewGuid():N}";
        var rowId = await CreateDefinitionAsync(workspaceId, 0, definitionId);
        await EnsureSuccessAsync(await PostPublishAsync(rowId, new { root = CreateValidCanvas("l1") }));
        await EnsureSuccessAsync(await PostPublishAsync(rowId, new { root = CreateValidCanvas("l2") }));
        var latestRowId = await GetVersionRowIdAsync(definitionId, 2);

        var data = await GetDefinitionItemAsync(workspaceId, definitionId);

        Assert.That(GetLong(data, "publishedVersionId"), Is.EqualTo(latestRowId));
        Assert.That(GetInt(data, "publishedVersion"), Is.EqualTo(2));
    }

    [Test]
    public async Task GetListAsync_发布后存草稿_返回已发布版本的行Id()
    {
        var workspaceId = await CreateWorkspaceAsync();
        var definitionId = $"WF_{Guid.NewGuid():N}";
        var rowId = await CreateDefinitionAsync(workspaceId, 0, definitionId);
        await EnsureSuccessAsync(await PostPublishAsync(rowId, new { root = CreateValidCanvas("l1") }));
        var publishedRowId = await GetVersionRowIdAsync(definitionId, 1);
        // 发布后保存产生 v2 草稿（最新行未发布）
        await EnsureSuccessAsync(await CreateClient().PostAsJsonAsync(
            $"/api/v1/WorkflowDefinitions/Save/{rowId}", new { root = CreateValidCanvas("draft") }));

        var data = await GetDefinitionItemAsync(workspaceId, definitionId);

        // 列表只出最新行（草稿），回链已发布版本
        Assert.That(GetInt(data, "version"), Is.EqualTo(2));
        Assert.That(GetLong(data, "publishedVersionId"), Is.EqualTo(publishedRowId));
        Assert.That(GetInt(data, "publishedVersion"), Is.EqualTo(1));
    }

    [Test]
    public async Task GetListAsync_从未发布_已发布版本Id为零()
    {
        var workspaceId = await CreateWorkspaceAsync();
        var definitionId = $"WF_{Guid.NewGuid():N}";
        await CreateDefinitionAsync(workspaceId, 0, definitionId);

        var data = await GetDefinitionItemAsync(workspaceId, definitionId);

        // 从未发布：long? 为 null 时 STJ 默认 HandleNull=false 直接输出 JSON null（不走转换器）
        Assert.That(data.GetProperty("publishedVersionId").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(GetInt(data, "publishedVersion"), Is.EqualTo(0));
    }
}
