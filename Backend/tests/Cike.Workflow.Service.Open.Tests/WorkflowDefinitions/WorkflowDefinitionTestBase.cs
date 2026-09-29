using Cike.EntityFrameworkCore;
using Cike.UniversalId.ULong;
using Cike.Workflow.Core.Enums;
using Cike.Workflow.Core.Runners.Models;
using Cike.Workflow.Core.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cike.Workflow.Service.Open.Tests.WorkflowDefinitions;

/// <summary>
/// 工作流定义生命周期测试基类：封装"建工作空间 → 建目录 → 建定义"的 API 数据准备、
/// 查询端点断言取数，以及无法经 API 构造的数据（IsSystem / IsReadonly / 已发布行）的直库播种。
/// 测试数据一律显式传 Code / DefinitionId，避免触发分布式缓存序列号路径（Redis）。
/// </summary>
[Category("Integration")]
public abstract class WorkflowDefinitionTestBase : BaseIntegrationTest
{
    /// <summary>合法画布：Start → End（发布校验可通过）。</summary>
    protected static object CreateValidCanvas(string prefix = "f")
        => new
        {
            type = "Cike.Flowchart",
            id = $"{prefix}_flowchart",
            activities = new object[]
            {
                new { type = "Cike.Start", id = $"{prefix}_start" },
                new { type = "Cike.End", id = $"{prefix}_end" },
            },
            connections = new object[]
            {
                new { source = new { activityId = $"{prefix}_start" }, target = new { activityId = $"{prefix}_end" } },
            },
        };

    protected async Task<long> CreateWorkspaceAsync()
    {
        var response = await CreateClient().PostAsJsonAsync("/api/v1/Workspaces", new
        {
            code = $"WS_{Guid.NewGuid():N}",
            name = $"工作空间_{Guid.NewGuid():N}".Substring(0, 20),
            description = "集成测试",
        });
        await EnsureSuccessAsync(response);
        return await ReadLongAsync(response);
    }

    protected async Task<long> CreateFolderAsync(long workspaceId, long parentId = 0)
    {
        var response = await CreateClient().PostAsJsonAsync("/api/v1/Folders", new
        {
            workspaceId,
            parentId,
            name = $"目录_{Guid.NewGuid():N}".Substring(0, 20),
        });
        await EnsureSuccessAsync(response);
        return await ReadLongAsync(response);
    }

    protected async Task<long> CreateDefinitionAsync(long workspaceId, long folderId, string definitionId)
    {
        var response = await CreateClient().PostAsJsonAsync("/api/v1/WorkflowDefinitions", new
        {
            workspaceId,
            folderId,
            definitionId,
            name = $"流程_{Guid.NewGuid():N}".Substring(0, 20),
            description = "集成测试",
        });
        await EnsureSuccessAsync(response);
        return await ReadLongAsync(response);
    }

    /// <summary>发布端点调用（负载由调用方组：root / options / publishedNote）。</summary>
    protected Task<HttpResponseMessage> PostPublishAsync(long id, object dto)
        => CreateClient().PostAsJsonAsync($"/api/v1/WorkflowDefinitions/Publish/{id}", dto);

    /// <summary>详情端点取数（状态断言一律经查询端点）。</summary>
    protected async Task<JsonDocument> GetDetailAsync(long id)
    {
        var response = await CreateClient().GetAsync($"/api/v1/WorkflowDefinitions/{id}");
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<JsonDocument>();
    }

    /// <summary>版本列表端点取数（按 Version 降序）。</summary>
    protected async Task<List<JsonElement>> GetVersionListAsync(string definitionId)
    {
        var response = await CreateClient().GetAsync($"/api/v1/WorkflowDefinitions/VersionList?definitionId={definitionId}");
        await EnsureSuccessAsync(response);
        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>();
        return doc!.RootElement.EnumerateArray().ToList();
    }

    /// <summary>取指定版本号的版本行 Id。</summary>
    protected async Task<long> GetVersionRowIdAsync(string definitionId, int version)
    {
        var versions = await GetVersionListAsync(definitionId);
        return GetLong(versions.Single(x => GetInt(x, "version") == version), "id");
    }

    /// <summary>目录树列表端点取数（type=2 为工作流定义条目）。</summary>
    protected async Task<List<JsonElement>> GetFolderListAsync(long workspaceId, long folderId)
    {
        var response = await CreateClient().GetAsync($"/api/v1/WorkflowDefinitions/List?workspaceId={workspaceId}&folderId={folderId}");
        await EnsureSuccessAsync(response);
        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>();
        return doc!.RootElement.EnumerateArray().ToList();
    }

    /// <summary>
    /// 直库播种（IsSystem / IsReadonly 等无法经 API 构造的数据），作用于该定义的所有版本行。
    /// CikeDbContext 跟踪即自动开事务，需显式提交，否则 Scope 释放时回滚。
    /// </summary>
    protected async Task SeedDefinitionAsync(string definitionId, Action<WorkflowDefinition> configure)
    {
        using var scope = _rootServices.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CikeWorkflowDbContext>();
        var entities = await dbContext.WorkflowDefinitions.Where(x => x.DefinitionId == definitionId).ToListAsync();
        foreach (var entity in entities)
            configure(entity);
        await dbContext.SaveChangesAsync();
        await dbContext.Database.CurrentTransaction!.CommitAsync();
    }

    protected static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"请求失败 [{(int)response.StatusCode}]: {await response.Content.ReadAsStringAsync()}");
    }

    /// <summary>
    /// 直库播种工作流实例（门禁/守卫的证据，绕过引擎）。Created/Updated 一律取传入时间，
    /// 传过去时间即可构造"调试证据已过期"场景；审计仅在 CreatedAt == default 时填充，显式值不会被覆盖。
    /// WorkflowState 为影子列存储（实体映射 Ignore），直插不会触发仓储 OnSaveAsync 序列化钩子，
    /// 这里显式写入 SerializedWorkflowState，保证引擎路径（如取消）能读到真实状态。
    /// </summary>
    protected async Task<long> SeedInstanceAsync(string definitionId, long definitionVersionRowId, bool isDebug,
        WorkflowStatus status, DateTime? createdAt = null)
    {
        using var scope = _rootServices.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CikeWorkflowDbContext>();
        var snowflake = scope.ServiceProvider.GetRequiredService<ISnowflakeIdGenerator>();
        var stateSerializer = scope.ServiceProvider.GetRequiredService<IWorkflowStateSerializer>();
        var time = createdAt ?? DateTime.Now;

        var instance = new WorkflowInstance
        {
            Id = snowflake.NextId(),
            DefinitionId = definitionId,
            DefinitionVersionId = definitionVersionRowId,
            Name = "seeded",
            Status = status,
            IsDebug = isDebug,
            CreatedAt = time,
            UpdatedAt = time,
            WorkflowState = new WorkflowState
            {
                DefinitionId = definitionId,
                DefinitionVersionId = definitionVersionRowId,
                Status = status,
                CreatedAt = time,
                UpdatedAt = time,
            },
        };
        instance.WorkflowState.Id = instance.Id;

        dbContext.WorkflowInstances.Add(instance);
        dbContext.Entry(instance).Property("SerializedWorkflowState").CurrentValue = stateSerializer.Serialize(instance.WorkflowState);
        await dbContext.SaveChangesAsync();
        await dbContext.Database.CurrentTransaction!.CommitAsync();
        return instance.Id;
    }

    /// <summary>轮询探针直到返回非空（引擎路径经后台事件异步推进，断言前轮询等待）。</summary>
    protected static async Task<T> WaitUntilAsync<T>(Func<Task<T?>> probe, TimeSpan? timeout = null) where T : class
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            var result = await probe();
            if (result != null)
                return result;
            await Task.Delay(100);
        }

        throw new TimeoutException("条件在超时时间内未满足。");
    }

    /// <summary>轮询实例详情直到状态等于期望值（引擎经后台事件异步执行，返回时状态尚未落库）。</summary>
    protected async Task<JsonDocument> WaitForInstanceStatusAsync(long instanceId, WorkflowStatus expected)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        var lastStatus = "<不存在>";
        while (DateTime.UtcNow < deadline)
        {
            var response = await CreateClient().GetAsync($"/api/v1/WorkflowInstances/{instanceId}");
            if (response.IsSuccessStatusCode)
            {
                var doc = await response.Content.ReadFromJsonAsync<JsonDocument>();
                if (doc != null)
                {
                    lastStatus = GetInstanceStatus(doc.RootElement);
                    if (lastStatus == expected.ToString())
                        return doc;
                }
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"实例 {instanceId} 未在超时时间内转为 {expected}，最后状态：{lastStatus}。");
    }

    /// <summary>播种一条新鲜的调试成功记录（发布门禁证据）：Finished + 创建时间在未来一分钟，保证晚于行内容变更。</summary>
    protected Task<long> SeedDebugSuccessAsync(string definitionId, long definitionVersionRowId, DateTime? createdAt = null)
        => SeedInstanceAsync(definitionId, definitionVersionRowId, isDebug: true, WorkflowStatus.Finished, createdAt ?? DateTime.Now.AddMinutes(1));

    /// <summary>读实例状态字段：兼容字符串与数字两种枚举序列化形态。</summary>
    protected static string GetInstanceStatus(JsonElement element)
    {
        var raw = element.GetProperty("status");
        return raw.ValueKind == JsonValueKind.String ? raw.GetString()! : ((WorkflowStatus)raw.GetInt32()).ToString();
    }

    /// <summary>框架将 long 序列化为字符串，统一按字符串读取再解析。</summary>
    protected static async Task<long> ReadLongAsync(HttpResponseMessage response)
        => long.Parse((await response.Content.ReadAsStringAsync()).Trim('"'));

    protected static string GetString(JsonElement element, string propertyName)
        => element.GetProperty(propertyName).GetString()!;

    protected static long GetLong(JsonElement element, string propertyName)
    {
        var raw = element.GetProperty(propertyName);
        return raw.ValueKind == JsonValueKind.String ? long.Parse(raw.GetString()!) : raw.GetInt64();
    }

    protected static int GetInt(JsonElement element, string propertyName)
    {
        var raw = element.GetProperty(propertyName);
        return raw.ValueKind == JsonValueKind.String ? int.Parse(raw.GetString()!) : raw.GetInt32();
    }

    protected static bool GetBool(JsonElement element, string propertyName)
        => element.GetProperty(propertyName).GetBoolean();

    protected static DateTime GetDateTime(JsonElement element, string propertyName)
    {
        var raw = element.GetProperty(propertyName);
        return raw.ValueKind == JsonValueKind.String ? raw.GetDateTime() : default;
    }
}
