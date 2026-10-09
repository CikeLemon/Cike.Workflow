using Cike.Workflow.Domain.Shared.ValueObjects;

namespace Cike.Workflow.EntityFrameworkCore.Tests.Repositories;

/// <summary>
/// WorkflowDefinition 仓储：影子属性（SerializedOptions）序列化 round-trip——
/// 插入写 JSON、更新写新值 JSON（迁移修复项）、单查还原、损坏 JSON 容错。
/// </summary>
public class WorkflowDefinitionRepositoryTest : RepositoryTestBase
{
    private static WorkflowDefinition MakeDefinition(string marker) => new()
    {
        WorkspaceId = 1,
        DefinitionId = $"WF_{marker}",
        Name = $"def-{marker}",
        Description = "test",
    };

    /// <summary>在新 Scope 中读取影子属性原始值（跟踪查询，影子属性值随实体条目携带）。</summary>
    private async Task<string?> GetRawSerializedOptionsAsync(long id)
    {
        using var scope = CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CikeWorkflowDbContext>();
        var entity = await dbContext.Set<WorkflowDefinition>().FirstOrDefaultAsync(e => e.Id == id);
        return entity == null ? null : (string?)dbContext.Entry(entity).Property("SerializedOptions").CurrentValue;
    }

    [Test]
    public async Task InsertAsync_WithOptions_WritesJsonToShadowProperty()
    {
        long id;
        using (var scope = CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionRepository>();
            var entity = MakeDefinition("insert");
            entity.Options = new WorkflowDefinitionOptionsValueObject { Outcomes = ["done", "retry"] };

            await repository.InsertAsync(entity);
            id = entity.Id;
        }

        var raw = await GetRawSerializedOptionsAsync(id);
        Assert.That(raw, Is.Not.Null);
        Assert.That(raw, Does.Contain("done"));
    }

    [Test]
    public async Task UpdateAsync_WithChangedOptions_WritesNewJsonToShadowProperty()
    {
        long id;
        using (var scope = CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionRepository>();
            var entity = MakeDefinition("update");
            entity.Options = new WorkflowDefinitionOptionsValueObject { Outcomes = ["v1"] };

            await repository.InsertAsync(entity);
            id = entity.Id;
        }

        using (var scope = CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionRepository>();
            var entity = await repository.FindAsync(id);

            entity!.Options = new WorkflowDefinitionOptionsValueObject { Outcomes = ["v1", "v2"] };

            await repository.UpdateAsync(entity);
        }

        // 迁移修复项：Update 路径同样执行序列化，影子属性写入新值 JSON
        var raw = await GetRawSerializedOptionsAsync(id);
        Assert.That(raw, Does.Contain("v2"));
    }

    [Test]
    public async Task FindAsync_WithPersistedOptions_RestoresOptions()
    {
        long id;
        using (var scope = CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionRepository>();
            var entity = MakeDefinition("restore");
            entity.Options = new WorkflowDefinitionOptionsValueObject { Outcomes = ["restored"] };
            await repository.InsertAsync(entity);
            id = entity.Id;
        }

        var restored = await WithScopeAsync(sp => sp.GetRequiredService<IWorkflowDefinitionRepository>().FindAsync(id));

        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.Options.Outcomes, Does.Contain("restored"));
    }

    [Test]
    public async Task FindAsync_WithCorruptedJson_LogsAndReturnsDefault()
    {
        // 绕过仓储直接写损坏 JSON（模拟历史脏数据）
        await Host.SeedAsync<WorkflowDefinition>(e =>
        {
            e.WorkspaceId = 1;
            e.DefinitionId = "WF_corrupt";
            e.Name = "def-corrupt";
            e.Description = "test";
        });
        long id;
        using (var scope = CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CikeWorkflowDbContext>();
            var tracked = await dbContext.Set<WorkflowDefinition>().FirstAsync(e => e.DefinitionId == "WF_corrupt");
            dbContext.Entry(tracked).Property("SerializedOptions").CurrentValue = "{corrupted-json";
            await dbContext.SaveChangesAsync();
            id = tracked.Id;
        }

        // 单查不抛异常，Options 回退默认值
        var restored = await WithScopeAsync(sp => sp.GetRequiredService<IWorkflowDefinitionRepository>().FindAsync(id));

        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.Options, Is.Not.Null);
    }

    [Test]
    public async Task GetPublishedVersionMapAsync_WithMultiplePublishedRows_ReturnsLatestPublishedRowPerDefinition()
    {
        long publishedRowId;
        using (var scope = CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionRepository>();
            // 同一定义两个已发布版本行：最高版本（v2）应胜出
            var v1 = MakeDefinition("pub-multi");
            v1.Version = 1;
            v1.IsLatest = false;
            v1.IsPublished = true;
            var v2 = MakeDefinition("pub-multi");
            v2.Version = 2;
            v2.IsPublished = true;
            // 另一定义仅有草稿，不应出现在 map 中
            var draft = MakeDefinition("pub-draft");

            await repository.InsertManyAsync([v1, v2, draft]);
            publishedRowId = v2.Id;
        }

        var map = await WithScopeAsync(sp => sp.GetRequiredService<IWorkflowDefinitionRepository>()
            .GetPublishedVersionMapAsync(["WF_pub-multi", "WF_pub-draft"]));

        Assert.That(map, Has.Count.EqualTo(1));
        Assert.That(map.ContainsKey("WF_pub-multi"), Is.True);
        Assert.That(map["WF_pub-multi"].Id, Is.EqualTo(publishedRowId));
        Assert.That(map["WF_pub-multi"].Version, Is.EqualTo(2));
    }

    [Test]
    public async Task GetPublishedVersionMapAsync_WhenNoDefinitionPublished_ReturnsEmptyMap()
    {
        using (var scope = CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionRepository>();
            var draft = MakeDefinition("pub-none");
            await repository.InsertAsync(draft);
        }

        var map = await WithScopeAsync(sp => sp.GetRequiredService<IWorkflowDefinitionRepository>()
            .GetPublishedVersionMapAsync(["WF_pub-none"]));

        Assert.That(map, Is.Empty);
    }
}
