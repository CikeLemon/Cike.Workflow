using System.Net;
using Cike.Workflow.Core.Enums;
using Cike.Workflow.Service.Open.Tests.WorkflowDefinitions;

namespace Cike.Workflow.Service.Open.Tests.WorkflowInstances;

/// <summary>
/// 实例取消端点：运行中实例取消落库为 Cancelled、终态实例幂等不动、未知实例 400。
/// 取消经后台事件异步执行（DispatcherCancelWorkflowCommand 显式走 Channel），
/// 返回时状态尚未变更，状态断言轮询实例详情。
/// </summary>
[Category("Integration")]
internal class WorkflowInstanceCancelTest : WorkflowDefinitionTestBase
{
    private Task<HttpResponseMessage> PostSaveCanvasAsync(long id, string prefix)
        => CreateClient().PostAsJsonAsync($"/api/v1/WorkflowDefinitions/Save/{id}", new { root = CreateValidCanvas(prefix) });

    private Task<HttpResponseMessage> PostCancelAsync(long id)
        => CreateClient().PostAsync($"/api/v1/WorkflowInstances/Cancel/{id}", content: null);

    private async Task<JsonDocument> GetInstanceAsync(long id)
    {
        var response = await CreateClient().GetAsync($"/api/v1/WorkflowInstances/{id}");
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<JsonDocument>();
    }

    /// <summary>建草稿并保存合法画布，播种一条运行中的非调试实例。</summary>
    private async Task<long> PrepareRunningInstanceAsync(string prefix)
    {
        var workspaceId = await CreateWorkspaceAsync();
        var definitionId = $"WF_{Guid.NewGuid():N}";
        var rowId = await CreateDefinitionAsync(workspaceId, 0, definitionId);
        await EnsureSuccessAsync(await PostSaveCanvasAsync(rowId, prefix));
        return await SeedInstanceAsync(definitionId, rowId, isDebug: false, WorkflowStatus.Executing);
    }

    private async Task<(string DefinitionId, long RowId)> PrepareDraftRowAsync()
    {
        var workspaceId = await CreateWorkspaceAsync();
        var definitionId = $"WF_{Guid.NewGuid():N}";
        var rowId = await CreateDefinitionAsync(workspaceId, 0, definitionId);
        return (definitionId, rowId);
    }

    [Test]
    public async Task PostCancelAsync_WithRunningInstance_MarksInstanceCancelled()
    {
        var instanceId = await PrepareRunningInstanceAsync("cancel_run");

        var response = await PostCancelAsync(instanceId);

        // 失败时携带响应体，便于定位后台派发链路的服务端异常
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            () => response.IsSuccessStatusCode ? string.Empty : response.Content.ReadAsStringAsync().Result);
        var detail = await WaitForInstanceStatusAsync(instanceId, WorkflowStatus.Cancelled);
        Assert.That(GetInstanceStatus(detail.RootElement), Is.EqualTo(nameof(WorkflowStatus.Cancelled)));
    }

    [Test]
    public async Task PostCancelAsync_WithFinishedInstance_ReturnsOkAndKeepsStatus()
    {
        var (definitionId, rowId) = await PrepareDraftRowAsync();
        var instanceId = await SeedInstanceAsync(definitionId, rowId, isDebug: false, WorkflowStatus.Finished);

        var response = await PostCancelAsync(instanceId);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(GetInstanceStatus((await GetInstanceAsync(instanceId)).RootElement), Is.EqualTo(nameof(WorkflowStatus.Finished)));
    }

    [Test]
    public async Task PostCancelAsync_WithUnknownInstance_ReturnsBadRequest()
    {
        var response = await PostCancelAsync(1);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("不存在"));
    }
}
