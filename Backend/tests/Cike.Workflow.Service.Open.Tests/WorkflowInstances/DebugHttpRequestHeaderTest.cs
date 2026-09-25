using System.Net;
using System.Net.Sockets;
using Cike.Workflow.Core.Enums;
using Cike.Workflow.Runtime;
using Cike.Workflow.Runtime.Models;
using Cike.Workflow.Service.Open.Tests.WorkflowDefinitions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Cike.Workflow.Service.Open.Tests.WorkflowInstances;

/// <summary>
/// 调试实例的出站 HTTP 请求打标：SendHttpRequest 在调试实例中附加 isDebug 请求头，正式实例不带。
/// 接收端为测试进程内自起的 Kestrel（本机回环），经真实引擎执行验证。
/// </summary>
[Category("Integration")]
internal class DebugHttpRequestHeaderTest : WorkflowDefinitionTestBase
{
    /// <summary>进程内 Kestrel 接收端：捕获请求头，收到首个请求后置完成信号。</summary>
    private sealed class CaptureReceiver : IAsyncDisposable
    {
        public const string HeaderName = "isDebug";

        private readonly TaskCompletionSource _captured = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private WebApplication _app = default!;
        private Task _serverTask = Task.CompletedTask;

        public string Url { get; private set; } = default!;
        public string? DebugHeader { get; private set; }
        public Task Captured => _captured.Task;

        public async Task StartAsync()
        {
            var port = GetFreePort();
            Url = $"http://127.0.0.1:{port}/";

            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
            _app = builder.Build();

            _app.MapGet("/", (HttpContext context) =>
            {
                DebugHeader = context.Request.Headers[HeaderName].ToString();
                _captured.TrySetResult();
                return Results.Ok(new { ok = true });
            });

            _serverTask = _app.RunAsync();
            await WaitForReadyAsync(Url);
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _serverTask;
            _app.DisposeAsync().AsTask().Wait();
        }

        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static async Task WaitForReadyAsync(string url)
        {
            using var client = new HttpClient();
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    await client.GetAsync(url);
                    return;
                }
                catch
                {
                    await Task.Delay(100);
                }
            }

            throw new TimeoutException("测试接收端未在超时时间内就绪。");
        }
    }

    /// <summary>字面量输入（与前端画布线格式一致）：声明字段全量落 JSON，未赋值的用 null Literal。</summary>
    private static object LiteralInput(string id, object? value)
        => new { memoryBlockReference = new { id }, expression = new { type = "Literal", value } };

    private static object CreateHttpCanvas(string prefix, string url)
        => new
        {
            type = "Cike.Flowchart",
            id = $"{prefix}_flowchart",
            activities = new object[]
            {
                new { type = "Cike.Start", id = $"{prefix}_start" },
                new
                {
                    type = "Cike.Workflow.Http.Activities.SendHttpRequest",
                    id = $"{prefix}_http",
                    url = LiteralInput($"{prefix}_url", url),
                    method = LiteralInput($"{prefix}_method", "GET"),
                    content = LiteralInput($"{prefix}_content", null),
                    contentType = LiteralInput($"{prefix}_contentType", null),
                    authorization = LiteralInput($"{prefix}_authorization", null),
                    requestHeaders = LiteralInput($"{prefix}_requestHeaders", null),
                    timeoutInterval = LiteralInput($"{prefix}_timeout", 60),
                    waitForCompletion = LiteralInput($"{prefix}_wait", false),
                    waitForCompletionOnStatusCodes = LiteralInput($"{prefix}_waitCodes", null),
                    responseErrorCodes = LiteralInput($"{prefix}_errorCodes", null),
                },
                new { type = "Cike.End", id = $"{prefix}_end" },
            },
            connections = new object[]
            {
                new { source = new { activityId = $"{prefix}_start" }, target = new { activityId = $"{prefix}_http" } },
                new { source = new { activityId = $"{prefix}_http" }, target = new { activityId = $"{prefix}_end" } },
            },
        };

    private async Task<(string DefinitionId, long RowId)> PrepareWithHttpCanvasAsync(string prefix, string url)
    {
        var workspaceId = await CreateWorkspaceAsync();
        var definitionId = $"WF_{Guid.NewGuid():N}";
        var rowId = await CreateDefinitionAsync(workspaceId, 0, definitionId);
        await EnsureSuccessAsync(await CreateClient().PostAsJsonAsync($"/api/v1/WorkflowDefinitions/Save/{rowId}",
            new { root = CreateHttpCanvas(prefix, url) }));
        return (definitionId, rowId);
    }

    [Test]
    public async Task RunDebugAsync_调试实例_出站请求携带isDebug头()
    {
        await using var receiver = new CaptureReceiver();
        await receiver.StartAsync();
        var (_, rowId) = await PrepareWithHttpCanvasAsync("dbg", receiver.Url);

        var runResponse = await CreateClient().PostAsJsonAsync($"/api/v1/WorkflowInstances/DebugRun/{rowId}", new { });
        await EnsureSuccessAsync(runResponse);
        var instanceId = await ReadLongAsync(runResponse);

        await receiver.Captured.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.That(receiver.DebugHeader, Is.EqualTo("true"));

        // 调试实例正常走完
        var detail = await WaitUntilAsync(async () =>
        {
            var response = await CreateClient().GetAsync($"/api/v1/WorkflowInstances/{instanceId}");
            return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<JsonDocument>() : null;
        });
        var rawStatus = detail!.RootElement.GetProperty("status");
        var statusText = rawStatus.ValueKind == JsonValueKind.String ? rawStatus.GetString()! : ((WorkflowStatus)rawStatus.GetInt32()).ToString();
        Assert.That(statusText, Is.EqualTo(nameof(WorkflowStatus.Finished)));
    }

    [Test]
    public async Task DispatchAsync_正式实例_出站请求不携带isDebug头()
    {
        await using var receiver = new CaptureReceiver();
        await receiver.StartAsync();
        var (_, rowId) = await PrepareWithHttpCanvasAsync("prod", receiver.Url);

        // 正式运行路径（无 HTTP 端点，触发器即走此派发）：IsDebug=false
        var dispatcher = serviceProvider.GetRequiredService<IWorkflowDispatcher>();
        await dispatcher.DispatchAsync(new DispatchWorkflowDefinitionRequest(rowId), new DispatchWorkflowOptions());

        await receiver.Captured.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.That(receiver.DebugHeader, Is.EqualTo(string.Empty));
    }
}
