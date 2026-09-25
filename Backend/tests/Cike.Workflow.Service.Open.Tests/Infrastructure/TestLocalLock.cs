using System.Collections.Concurrent;
using Cike.Core;
using Cike.Locks.Abstracts;

namespace Cike.Workflow.Service.Open.Tests.Infrastructure;

/// <summary>
/// 测试用进程内 ILock 替身：key → SemaphoreSlim(1,1)，释放用 Release 而非 Dispose，
/// 支持同 key 反复获取（框架 LocalLock 有一次性缺陷，Redis 实现依赖外部服务，均不适合测试）。
/// </summary>
internal sealed class TestLocalLock : ILock
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new();

    public IDisposable? TryGet(string key, TimeSpan timeout = default)
    {
        var semaphore = _semaphores.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        return semaphore.Wait(timeout) ? new DisposeAction(() => semaphore.Release()) : null;
    }

    public async Task<IAsyncDisposable?> TryGetAsync(string key, TimeSpan timeout = default, CancellationToken cancellationToken = default)
    {
        var semaphore = _semaphores.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        return await semaphore.WaitAsync(timeout, cancellationToken) ? new DisposeAction(() => semaphore.Release()) : null;
    }
}
