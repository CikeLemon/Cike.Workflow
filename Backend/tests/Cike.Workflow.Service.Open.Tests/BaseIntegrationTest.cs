using Cike.Caching;
using Cike.Data.EFCore;
using Cike.Locks.Abstracts;
using Cike.Workflow.Caching;
using Cike.Workflow.Service.Open.Tests.Infrastructure;
using Cike.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cike.Workflow.Service.Open.Tests;

/// <summary>
/// HTTP 集成测试基类：WebApplicationFactory 起真实 host，经 HttpClient 走完整请求管道。
/// 数据库为共享单连接的 SQLite in-memory（覆盖 MySQL 方言，连接存活期间库不丢失）；
/// ICacheService 以内存替身接管（不依赖 Redis）；测试数据需显式传 Code / DefinitionId，
/// 避免触发分布式缓存的序列号生成路径。每个测试类独享一个宿主（独立 in-memory 库）。
/// </summary>
public abstract class BaseIntegrationTest : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private WebApplicationFactory<Program> _app = default!;
    private IServiceScope _scope = default!;

    protected IServiceProvider serviceProvider = default!;
    protected IServiceProvider _rootServices = default!;

    protected BaseIntegrationTest()
    {
        _connection.Open();

        _app = new WebApplicationFactory<Program>()
          .WithWebHostBuilder(builder =>
          {
              builder.ConfigureServices((context, services) =>
              {
                  // 端点默认 RequireAuthorization，测试环境注册一个自动通过的认证方案
                  services.AddAuthorization();
                  services.AddAuthentication(TestAuthHandler.SchemeName)
                      .AddScheme<TestAuthOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
              });

              // 在应用自身的服务注册完成之后覆盖：SQLite 连接 + 缓存替身
              builder.ConfigureTestServices(services =>
              {
                  services.Configure<CikeDbContextOptions>(options =>
                      options.Configure(context => context.DbContextOptionsBuilder.UseSqlite(_connection)));
                  services.Replace(ServiceDescriptor.Singleton(typeof(ICacheService<>), typeof(InMemoryCacheService<>)));
                  // 定义运行时缓存的真实实现跑在此内存介质上（不依赖 Redis），键/索引/选取逻辑被真实执行
                  services.Replace(ServiceDescriptor.Singleton<IMultilevelCacheClient, InMemoryMultilevelCacheClient>());
                  // 分布式锁以进程内信号量替身接管（不依赖 Redis），锁的获取/互斥/释放语义被真实执行
                  services.Replace(ServiceDescriptor.Singleton<ILock, TestLocalLock>());
              });
          });

        // 建表（模型驱动，含最新索引定义）
        using (var scope = _app.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CikeWorkflowDbContext>();
            dbContext.Database.EnsureCreated();
        }

        _scope = _app.Services.CreateScope();
        serviceProvider = _scope.ServiceProvider;
        _rootServices = _app.Services;
    }

    protected HttpClient CreateClient() => _app.CreateClient();

    public void Dispose()
    {
        try
        {
            _scope?.Dispose();
            _app?.Dispose();
            _connection.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Ignore disposed exception during teardown
        }
    }
}
