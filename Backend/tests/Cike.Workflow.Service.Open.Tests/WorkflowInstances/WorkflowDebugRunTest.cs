using System.Net;
using Cike.Workflow.Core.Enums;
using Cike.Workflow.Service.Open.Tests.WorkflowDefinitions;

namespace Cike.Workflow.Service.Open.Tests.WorkflowInstances;

/// <summary>
/// 调试（试跑）全链路：发起调试、实例可见性、保存/回滚放行与删除禁令。
/// 守卫类用例以直库播种实例作证据（绕过引擎），规则断言一律走 HTTP 行为。
/// </summary>
[Category("Integration")]
internal class WorkflowDebugRunTest : WorkflowDefinitionTestBase
{
    private Task<HttpResponseMessage> PostDebugRunAsync(long id, object? dto = null)
        => CreateClient().PostAsJsonAsync($"/api/v1/WorkflowInstances/DebugRun/{id}", dto ?? new { });

    private Task<HttpResponseMessage> PostSaveCanvasAsync(long id, string prefix)
        => CreateClient().PostAsJsonAsync($"/api/v1/WorkflowDefinitions/Save/{id}", new { root = CreateValidCanvas(prefix) });

    private Task<HttpResponseMessage> PostPagedListAsync(object filter)
        => CreateClient().PostAsJsonAsync("/api/v1/WorkflowInstances/PagedList?page=1&pageSize=10", filter);

    private async Task<JsonDocument?> TryGetInstanceAsync(long id)
    {
        var response = await CreateClient().GetAsync($"/api/v1/WorkflowInstances/{id}");
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<JsonDocument>() : null;
    }

    private static long GetTotal(JsonDocument doc)
    {
        var raw = doc.RootElement.GetProperty("total");
        return raw.ValueKind == JsonValueKind.String ? long.Parse(raw.GetString()!) : raw.GetInt64();
    }

    private async Task<(string DefinitionId, long RowId)> PrepareDraftAsync()
    {
        var workspaceId = await CreateWorkspaceAsync();
        var definitionId = $"WF_{Guid.NewGuid():N}";
        var rowId = await CreateDefinitionAsync(workspaceId, 0, definitionId);
        return (definitionId, rowId);
    }

    private async Task<long> PreparePublishedAsync(string prefix = "pub")
    {
        var (definitionId, rowId) = await PrepareDraftAsync();
        await EnsureSuccessAsync(await PostPublishAsync(rowId, new { root = CreateValidCanvas(prefix) }));
        return rowId;
    }

    // ---------- 发起调试 ----------

    [Test]
    public async Task RunDebugAsync_WithDraftRow_CreatesIsDebugInstance()
    {
        var (definitionId, rowId) = await PrepareDraftAsync();
        await EnsureSuccessAsync(await PostSaveCanvasAsync(rowId, "dbg"));

        var response = await PostDebugRunAsync(rowId);

        // 失败时携带响应体，便于定位后台派发链路的服务端异常
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            () => response.IsSuccessStatusCode ? string.Empty : response.Content.ReadAsStringAsync().Result);
        var instanceId = await ReadLongAsync(response);
        Assert.That(instanceId, Is.GreaterThan(0));

        // 后台事件异步创建实例，轮询到详情后断言调试标记
        var detail = await WaitUntilAsync(() => TryGetInstanceAsync(instanceId));
        Assert.That(GetBool(detail!.RootElement, "isDebug"), Is.True);
        Assert.That(GetInstanceStatus(detail.RootElement), Is.EqualTo(nameof(WorkflowStatus.Finished)));
    }

    [Test]
    public async Task RunDebugAsync_WithPublishedRow_ReturnsBadRequest()
    {
        var rowId = await PreparePublishedAsync("pubd");

        var response = await PostDebugRunAsync(rowId);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("先保存草稿"));
    }

    // ---------- 实例可见性 ----------

    [Test]
    public async Task PostPagedListAsync_WithDefaultFilter_HidesDebugInstances()
    {
        var (definitionId, rowId) = await PrepareDraftAsync();
        await SeedInstanceAsync(definitionId, rowId, isDebug: true, WorkflowStatus.Finished);

        // 类内共享宿主库，按 DefinitionId 圈定断言范围
        var defaultList = await PostPagedListAsync(new { definitionId });
        Assert.That(defaultList.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var defaultDoc = await defaultList.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.That(GetTotal(defaultDoc!), Is.EqualTo(0));

        var debugList = await PostPagedListAsync(new { definitionId, isDebug = true });
        var debugDoc = await debugList.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.That(GetTotal(debugDoc!), Is.EqualTo(1));
        Assert.That(GetBool(debugDoc.RootElement.GetProperty("items")[0], "isDebug"), Is.True);
    }

    [Test]
    public async Task PostPagedListAsync_WithVersionAndDebugFilter_ReturnsRowDebugRunsDescending()
    {
        var (definitionId, rowId) = await PrepareDraftAsync();
        await SeedInstanceAsync(definitionId, rowId, isDebug: true, WorkflowStatus.Finished, DateTime.Now.AddHours(-2));
        await SeedInstanceAsync(definitionId, rowId, isDebug: true, WorkflowStatus.Faulted, DateTime.Now.AddHours(-1));
        await SeedInstanceAsync(definitionId, rowId, isDebug: false, WorkflowStatus.Finished);
        // 其他草稿行上的调试记录不串
        var (_, otherRowId) = await PrepareDraftAsync();
        await SeedInstanceAsync(definitionId, otherRowId, isDebug: true, WorkflowStatus.Finished);

        // 调试记录走通用实例分页列表：按草稿行 + IsDebug 圈定，排序默认创建时间倒序
        var response = await PostPagedListAsync(new { definitionVersionId = rowId, isDebug = true });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var items = doc!.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.That(items, Has.Count.EqualTo(2));
        // 创建时间倒序：晚播种的 Faulted 在前
        Assert.That(GetInstanceStatus(items[0]), Is.EqualTo(nameof(WorkflowStatus.Faulted)));
        Assert.That(GetInstanceStatus(items[1]), Is.EqualTo(nameof(WorkflowStatus.Finished)));
    }

    // ---------- 保存 / 回滚不拦调试运行，删除禁令 ----------

    [Test]
    public async Task SaveAsync_WhileDebugRunning_Succeeds()
    {
        var (definitionId, rowId) = await PrepareDraftAsync();
        await SeedInstanceAsync(definitionId, rowId, isDebug: true, WorkflowStatus.Executing);

        // 前端流程是"改 → 存 → 调"：保存不被调试运行拦截，已有调试证据由时间戳过期
        var response = await PostSaveCanvasAsync(rowId, "save_during_debug");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task RunDebugAsync_WithRunningDebugRun_StartsAnotherConcurrently()
    {
        var (definitionId, rowId) = await PrepareDraftAsync();
        await EnsureSuccessAsync(await PostSaveCanvasAsync(rowId, "concurrent"));
        await SeedInstanceAsync(definitionId, rowId, isDebug: true, WorkflowStatus.Executing);

        var response = await PostDebugRunAsync(rowId);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var instanceId = await ReadLongAsync(response);
        Assert.That(instanceId, Is.GreaterThan(0));
        var detail = await WaitUntilAsync(() => TryGetInstanceAsync(instanceId));
        Assert.That(GetBool(detail!.RootElement, "isDebug"), Is.True);
    }

    [Test]
    public async Task RollbackAsync_WhileDebugRunning_Succeeds()
    {
        var (definitionId, firstRowId) = await PrepareDraftAsync();
        await SeedInstanceAsync(definitionId, firstRowId, isDebug: true, WorkflowStatus.Finished, DateTime.Now.AddMinutes(1));
        await EnsureSuccessAsync(await PostPublishAsync(firstRowId, new { root = CreateValidCanvas("rb1") }));
        // 保存生成 v+1 草稿
        await EnsureSuccessAsync(await PostSaveCanvasAsync(firstRowId, "rb2"));
        var secondRowId = await GetVersionRowIdAsync(definitionId, 2);
        await SeedInstanceAsync(definitionId, secondRowId, isDebug: true, WorkflowStatus.Executing);

        var response = await CreateClient().PostAsJsonAsync("/api/v1/WorkflowDefinitions/Rollback",
            new { definitionId, definitionVersionId = firstRowId });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task DeleteAsync_WithRunningInstance_ReturnsBadRequest()
    {
        var (definitionId, rowId) = await PrepareDraftAsync();
        await SeedInstanceAsync(definitionId, rowId, isDebug: false, WorkflowStatus.Executing);

        var response = await CreateClient().DeleteAsync($"/api/v1/WorkflowDefinitions/{rowId}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("运行中的实例"));
    }

    [Test]
    public async Task DeleteAsync_WithAllTerminalInstances_Succeeds()
    {
        var (definitionId, rowId) = await PrepareDraftAsync();
        await SeedInstanceAsync(definitionId, rowId, isDebug: false, WorkflowStatus.Finished);
        await SeedInstanceAsync(definitionId, rowId, isDebug: true, WorkflowStatus.Cancelled);

        var response = await CreateClient().DeleteAsync($"/api/v1/WorkflowDefinitions/{rowId}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
}
