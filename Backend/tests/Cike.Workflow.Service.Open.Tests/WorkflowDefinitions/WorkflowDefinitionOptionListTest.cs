namespace Cike.Workflow.Service.Open.Tests.WorkflowDefinitions;

/// <summary>
/// 工作流定义下拉选项端点：按空间返回全部定义的 DefinitionId + Name，作为实例列表筛选数据源。
/// 每个定义只出一项（IsLatest 行），发布产生多版本行后不重复计数。
/// </summary>
[Category("Integration")]
internal class WorkflowDefinitionOptionListTest : WorkflowDefinitionTestBase
{
    private async Task<List<JsonElement>> GetOptionListAsync(long workspaceId)
    {
        var response = await CreateClient().GetAsync($"/api/v1/WorkflowDefinitions/OptionList?workspaceId={workspaceId}");
        await EnsureSuccessAsync(response);
        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>();
        return doc!.RootElement.EnumerateArray().ToList();
    }

    [Test]
    public async Task GetOptionListAsync_WithWorkspaceId_ReturnsDefinitionIdAndNameOfItsDefinitions()
    {
        var workspaceId = await CreateWorkspaceAsync();
        var definitionId1 = $"WF_{Guid.NewGuid():N}";
        var definitionId2 = $"WF_{Guid.NewGuid():N}";
        await CreateDefinitionAsync(workspaceId, 0, definitionId1);
        await CreateDefinitionAsync(workspaceId, 0, definitionId2);

        var options = await GetOptionListAsync(workspaceId);

        Assert.That(options, Has.Count.EqualTo(2));
        var ids = options.Select(x => GetString(x, "definitionId")).ToList();
        Assert.That(ids, Is.EquivalentTo(new[] { definitionId1, definitionId2 }));
        Assert.That(options.All(x => GetString(x, "name").Length > 0), Is.True);
    }

    [Test]
    public async Task GetOptionListAsync_WhenDefinitionHasPublishedVersions_ReturnsOneEntryPerDefinition()
    {
        var workspaceId = await CreateWorkspaceAsync();
        var definitionId = $"WF_{Guid.NewGuid():N}";
        var rowId = await CreateDefinitionAsync(workspaceId, 0, definitionId);
        await EnsureSuccessAsync(await PostPublishAsync(rowId, new { root = CreateValidCanvas("opt") }));
        await EnsureSuccessAsync(await PostPublishAsync(rowId, new { root = CreateValidCanvas("opt2") }));
        var versions = await GetVersionListAsync(definitionId);
        Assert.That(versions, Has.Count.EqualTo(2));

        var options = await GetOptionListAsync(workspaceId);

        Assert.That(options, Has.Count.EqualTo(1));
        Assert.That(GetString(options[0], "definitionId"), Is.EqualTo(definitionId));
    }

    [Test]
    public async Task GetOptionListAsync_WithOtherWorkspaceId_ExcludesDefinitionsOfOtherWorkspaces()
    {
        var workspaceId = await CreateWorkspaceAsync();
        var otherWorkspaceId = await CreateWorkspaceAsync();
        var definitionId = $"WF_{Guid.NewGuid():N}";
        await CreateDefinitionAsync(otherWorkspaceId, 0, definitionId);

        var options = await GetOptionListAsync(workspaceId);

        Assert.That(options, Has.Count.EqualTo(0));
    }

    [Test]
    public async Task GetOptionListAsync_WhenWorkspaceHasNoDefinitions_ReturnsEmptyList()
    {
        var workspaceId = await CreateWorkspaceAsync();

        var options = await GetOptionListAsync(workspaceId);

        Assert.That(options, Has.Count.EqualTo(0));
    }
}
