using System.Net;
using Cike.Workflow.Caching;
using Cike.Workflow.Common.Versions;
using Cike.Workflow.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Cike.Workflow.Service.Open.Tests.WorkflowDefinitions;

/// <summary>发布携带完整画布：先校验后写库，草稿就地发布 / 已发布生成 v+1 直接发布，返回发布后版本行 Id。</summary>
internal class WorkflowDefinitionPublishTest : WorkflowDefinitionTestBase
{
    /// <summary>详情查询走 BeginAsNoTracking，影子属性 Options 不还原；变量落库经运行时缓存断言（与 CacheTest 同源读法）。</summary>
    private IWorkflowDefinitionCache Cache => serviceProvider.GetRequiredService<IWorkflowDefinitionCache>();

    private Task<HttpResponseMessage> PostPublishCanvasAsync(long id, string? note = null, string prefix = "pub", object? options = null)
        => PostPublishAsync(id, options == null
            ? new { root = CreateValidCanvas(prefix), publishedNote = note }
            : (object)new { root = CreateValidCanvas(prefix), options, publishedNote = note });

    private async Task<(string DefinitionId, long RowId)> PrepareAsync()
    {
        var workspaceId = await CreateWorkspaceAsync();
        var definitionId = $"WF_{Guid.NewGuid():N}";
        var rowId = await CreateDefinitionAsync(workspaceId, 0, definitionId);
        return (definitionId, rowId);
    }

    [Test]
    public async Task PublishAsync_携带画布直接发布_落库内容与提交画布一致且打发布标记()
    {
        var (definitionId, rowId) = await PrepareAsync();

        var response = await PostPublishCanvasAsync(rowId, "首个正式版本");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        // 返回发布后的版本行 Id
        Assert.That(await ReadLongAsync(response), Is.EqualTo(await GetVersionRowIdAsync(definitionId, 1)));

        var versions = await GetVersionListAsync(definitionId);
        Assert.That(versions, Has.Count.EqualTo(1));
        var version = versions[0];
        Assert.That(GetBool(version, "isPublished"), Is.True);
        Assert.That(GetString(version, "publishedNote"), Is.EqualTo("首个正式版本"));
        Assert.That(GetString(version, "publishedBy"), Is.EqualTo(TestAuthHandler.TestUserId));
        Assert.That(GetDateTime(version, "publishedAt"), Is.GreaterThan(DateTime.MinValue));

        // 落库内容与提交画布一致（未保存的画布随发布生效）
        var detail = (await GetDetailAsync(rowId)).RootElement;
        Assert.That(detail.GetProperty("root").GetRawText(), Does.Contain("pub_start"));
        Assert.That(GetBool(detail, "isPublished"), Is.True);
    }

    [Test]
    public async Task PublishAsync_携带变量选项_变量定义随发布落库()
    {
        var (definitionId, rowId) = await PrepareAsync();
        var variables = new object[]
        {
            new { id = "var1", name = "count", typeName = "Int32", isArray = false },
        };

        var response = await PostPublishCanvasAsync(rowId, options: new { variables });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        // 变量落库经运行时缓存断言（详情读口不还原影子 Options，为既有行为）
        var cached = await Cache.GetAsync(WorkflowDefinitionHandle.ByDefinitionId(definitionId, VersionOptions.Latest));
        Assert.That(cached, Is.Not.Null);
        Assert.That(cached!.OptionsPayload, Does.Contain("count"));
    }

    [Test]
    public async Task PublishAsync_缺少开始节点_返回400且消息可定位()
    {
        var (_, rowId) = await PrepareAsync();
        var canvas = new
        {
            type = "Cike.Flowchart",
            id = "nos_flowchart",
            activities = new object[] { new { type = "Cike.End", id = "nos_end" } },
            connections = Array.Empty<object>(),
        };

        var response = await PostPublishAsync(rowId, new { root = canvas });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("开始节点"));
    }

    [Test]
    public async Task PublishAsync_存在孤立节点_返回400且消息可定位()
    {
        var (_, rowId) = await PrepareAsync();
        var canvas = new
        {
            type = "Cike.Flowchart",
            id = "orphan_flowchart",
            activities = new object[]
            {
                new { type = "Cike.Start", id = "orphan_start" },
                new { type = "Cike.End", id = "orphan_end" },
                new { type = "Cike.WriteLine", id = "orphan_isolated", text = new { } },
            },
            connections = new object[]
            {
                new { source = new { activityId = "orphan_start" }, target = new { activityId = "orphan_end" } },
            },
        };

        var response = await PostPublishAsync(rowId, new { root = canvas });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var message = await response.Content.ReadAsStringAsync();
        Assert.That(message, Does.Contain("孤立节点"));
        Assert.That(message, Does.Contain("WriteLine"));
        Assert.That(message, Does.Not.Contain("orphan_isolated"));
    }

    [Test]
    public async Task PublishAsync_变量定义非法_返回400且消息可定位()
    {
        var (_, rowId) = await PrepareAsync();
        var variables = new object[]
        {
            new { id = "var1", name = "count", typeName = "Int32", isArray = false },
            new { id = "var2", name = "count", typeName = "Int32", isArray = false },
        };

        var response = await PostPublishCanvasAsync(rowId, prefix: "var", options: new { variables });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var message = await response.Content.ReadAsStringAsync();
        Assert.That(message, Does.Contain("变量"));
        Assert.That(message, Does.Contain("重复"));
    }

    [Test]
    public async Task PublishAsync_活动必填属性缺失_返回400且消息可定位()
    {
        var (_, rowId) = await PrepareAsync();
        var canvas = new
        {
            type = "Cike.Flowchart",
            id = "req_flowchart",
            activities = new object[]
            {
                new { type = "Cike.Start", id = "req_start" },
                new { type = "Cike.WriteLine", id = "req_write" },
                new { type = "Cike.End", id = "req_end" },
            },
            connections = new object[]
            {
                new { source = new { activityId = "req_start" }, target = new { activityId = "req_write" } },
                new { source = new { activityId = "req_write" }, target = new { activityId = "req_end" } },
            },
        };

        var response = await PostPublishAsync(rowId, new { root = canvas });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var message = await response.Content.ReadAsStringAsync();
        Assert.That(message, Does.Contain("必填属性"));
        Assert.That(message, Does.Contain("WriteLine"));
        Assert.That(message, Does.Not.Contain("req_write"));
    }

    [Test]
    public async Task PublishAsync_画布Root缺失_返回400()
    {
        var (_, rowId) = await PrepareAsync();

        var response = await PostPublishAsync(rowId, new { publishedNote = "无画布" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("画布内容不能为空"));
    }

    [Test]
    public async Task PublishAsync_最新行已是发布态_生成新草稿版本并直接发布()
    {
        var (definitionId, rowId) = await PrepareAsync();
        await EnsureSuccessAsync(await PostPublishCanvasAsync(rowId));
        var firstId = await GetVersionRowIdAsync(definitionId, 1);

        var response = await PostPublishCanvasAsync(rowId, "第二版");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var secondId = await ReadLongAsync(response);
        Assert.That(secondId, Is.Not.EqualTo(firstId));
        Assert.That(secondId, Is.EqualTo(await GetVersionRowIdAsync(definitionId, 2)));

        var versions = await GetVersionListAsync(definitionId);
        Assert.That(versions, Has.Count.EqualTo(2));
        Assert.That(versions.All(x => GetBool(x, "isPublished")), Is.True);
        // IsLatest 转移到新版本行
        Assert.That(GetBool(versions[0], "isLatest"), Is.True);
        Assert.That(GetBool(versions[1], "isLatest"), Is.False);
        Assert.That(GetString(versions[0], "publishedNote"), Is.EqualTo("第二版"));
        Assert.That(GetInt(versions[0], "version"), Is.EqualTo(2));
    }

    [Test]
    public async Task PublishAsync_校验失败_落库内容保持不变且未打发布标记()
    {
        var (_, rowId) = await PrepareAsync();
        var invalidCanvas = new
        {
            type = "Cike.Flowchart",
            id = "zero_flowchart",
            activities = new object[] { new { type = "Cike.End", id = "zero_bad_end" } },
            connections = Array.Empty<object>(),
        };

        var response = await PostPublishAsync(rowId, new { root = invalidCanvas });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        // 数据库零写入：内容仍是建行时的空画布，未打发布标记
        var detail = (await GetDetailAsync(rowId)).RootElement;
        Assert.That(detail.GetProperty("root").GetRawText(), Does.Not.Contain("zero_bad_end"));
        Assert.That(GetBool(detail, "isPublished"), Is.False);
        Assert.That(GetInt(detail, "version"), Is.EqualTo(1));
    }

    [Test]
    public async Task PublishAsync_后续发布_历史版本发布标记保留()
    {
        var (definitionId, rowId) = await PrepareAsync();
        await EnsureSuccessAsync(await PostPublishCanvasAsync(rowId, "v1", prefix: "hist"));
        await EnsureSuccessAsync(await PostPublishCanvasAsync(rowId, "v2", prefix: "hist2"));

        var versions = await GetVersionListAsync(definitionId);
        Assert.That(versions, Has.Count.EqualTo(2));
        Assert.That(versions.All(x => GetBool(x, "isPublished")), Is.True);
        Assert.That(GetString(versions[0], "publishedNote"), Is.EqualTo("v2"));
        Assert.That(GetString(versions[1], "publishedNote"), Is.EqualTo("v1"));
    }

    [Test]
    public async Task PublishAsync_系统内置定义_返回400()
    {
        var (definitionId, rowId) = await PrepareAsync();
        await SeedDefinitionAsync(definitionId, e => e.IsSystem = true);

        var response = await PostPublishCanvasAsync(rowId, prefix: "sys");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("系统内置"));
    }
}
