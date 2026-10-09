using System.Net;

namespace Cike.Workflow.Service.Open.Tests.WorkflowDefinitions;

/// <summary>
/// 按版本行 Id 取该版本的 Options（变量 / 输入参数定义等全量配置）：
/// 各版本行返回各自的 Options，历史版本不随新发布变化。
/// </summary>
[Category("Integration")]
internal class WorkflowDefinitionVersionOptionsTest : WorkflowDefinitionTestBase
{
    private async Task<JsonElement> GetVersionOptionsAsync(long rowId)
    {
        var response = await CreateClient().GetAsync($"/api/v1/WorkflowDefinitions/VersionOptions/{rowId}");
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement;
    }

    private static object OptionsWithVariable(string name)
        => new { variables = new object[] { new { id = $"var_{name}", name, typeName = "Int32", isArray = false } } };

    [Test]
    public async Task GetVersionOptionsAsync_发布携带变量_返回该版本的Options()
    {
        var workspaceId = await CreateWorkspaceAsync();
        var definitionId = $"WF_{Guid.NewGuid():N}";
        var rowId = await CreateDefinitionAsync(workspaceId, 0, definitionId);
        await EnsureSuccessAsync(await PostPublishAsync(rowId,
            new { root = CreateValidCanvas("opt1"), options = OptionsWithVariable("count") }));

        var options = await GetVersionOptionsAsync(rowId);

        var variables = options.GetProperty("variables").EnumerateArray().ToList();
        Assert.That(variables, Has.Count.EqualTo(1));
        Assert.That(GetString(variables[0], "name"), Is.EqualTo("count"));
        Assert.That(GetString(variables[0], "typeName"), Is.EqualTo("Int32"));
        Assert.That(GetBool(variables[0], "isArray"), Is.False);
    }

    [Test]
    public async Task GetVersionOptionsAsync_多个已发布版本_各版本返回各自的Options()
    {
        var workspaceId = await CreateWorkspaceAsync();
        var definitionId = $"WF_{Guid.NewGuid():N}";
        var rowId = await CreateDefinitionAsync(workspaceId, 0, definitionId);
        await EnsureSuccessAsync(await PostPublishAsync(rowId,
            new { root = CreateValidCanvas("opt1"), options = OptionsWithVariable("alpha") }));
        await EnsureSuccessAsync(await PostPublishAsync(rowId,
            new { root = CreateValidCanvas("opt2"), options = OptionsWithVariable("beta") }));
        var v1RowId = await GetVersionRowIdAsync(definitionId, 1);
        var v2RowId = await GetVersionRowIdAsync(definitionId, 2);

        var v1Options = await GetVersionOptionsAsync(v1RowId);
        var v2Options = await GetVersionOptionsAsync(v2RowId);

        // 历史版本 Options 不随新发布变化
        Assert.That(GetString(v1Options.GetProperty("variables").EnumerateArray().Single(), "name"), Is.EqualTo("alpha"));
        Assert.That(GetString(v2Options.GetProperty("variables").EnumerateArray().Single(), "name"), Is.EqualTo("beta"));
    }

    [Test]
    public async Task GetVersionOptionsAsync_草稿版本行_返回草稿的Options()
    {
        var workspaceId = await CreateWorkspaceAsync();
        var definitionId = $"WF_{Guid.NewGuid():N}";
        var rowId = await CreateDefinitionAsync(workspaceId, 0, definitionId);
        await EnsureSuccessAsync(await PostPublishAsync(rowId, new { root = CreateValidCanvas("opt1") }));
        var draftResponse = await CreateClient().PostAsJsonAsync(
            $"/api/v1/WorkflowDefinitions/Save/{rowId}",
            new { root = CreateValidCanvas("draft"), options = OptionsWithVariable("draft_var") });
        await EnsureSuccessAsync(draftResponse);
        var draftRowId = await ReadLongAsync(draftResponse);

        var options = await GetVersionOptionsAsync(draftRowId);

        Assert.That(GetString(options.GetProperty("variables").EnumerateArray().Single(), "name"), Is.EqualTo("draft_var"));
    }
}
