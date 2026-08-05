namespace ClaudeUsageWidget.Core.Tests;

public class StatusStoreTests
{
    private static StatusStore MakeStore(Func<CancellationToken, Task<ServiceStatus>>? fetch = null) =>
        new(fetch ?? (_ => Task.FromResult(ServiceStatus.Operational)));

    [Fact]
    public void StartsUnknown()
    {
        var store = MakeStore();
        Assert.Equal(ServiceStatus.Unknown, store.Status);
    }

    [Fact]
    public async Task PublishesStatusOnSuccess()
    {
        var store = MakeStore(_ => Task.FromResult(ServiceStatus.Degraded));
        await store.LoadAsync();
        Assert.Equal(ServiceStatus.Degraded, store.Status);
    }

    [Fact]
    public async Task KeepsLastStatusOnFailure()
    {
        var shouldFail = false;
        var store = MakeStore(_ => shouldFail
            ? throw new UsageException(UsageError.Network("offline"))
            : Task.FromResult(ServiceStatus.Operational));

        await store.LoadAsync();
        shouldFail = true;
        await store.LoadAsync();

        Assert.Equal(ServiceStatus.Operational, store.Status);
    }

    [Fact]
    public async Task FailedLoadDoesNotThrow()
    {
        var store = MakeStore(_ => throw new UsageException(UsageError.Network("offline")));
        await store.LoadAsync(); // must complete without throwing
        Assert.Equal(ServiceStatus.Unknown, store.Status);
    }

    [Fact]
    public async Task CoalescesOverlappingLoads()
    {
        var calls = 0;
        var store = MakeStore(async _ =>
        {
            calls++;
            await Task.Delay(50);
            return ServiceStatus.Operational;
        });

        var first = store.LoadAsync();
        var second = store.LoadAsync();
        await Task.WhenAll(first, second);

        Assert.Equal(1, calls);
    }

    // Mirrors UsageStoreTests.CoalescesConcurrentLoadAsyncAcrossThreads: the App
    // layer can call LoadAsync() from more than one thread at once (UI thread
    // and a background wake handler), so the check-then-set on `_inFlight`
    // must be exercised across real OS threads, not just interleavings on one.
    [Fact]
    public async Task CoalescesConcurrentLoadAsyncAcrossThreads()
    {
        var calls = 0;
        var store = MakeStore(_ =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(ServiceStatus.Operational);
        });

        var barrier = new Barrier(2);
        Task Racer() => Task.Run(() =>
        {
            barrier.SignalAndWait();
            return store.LoadAsync();
        });

        await Task.WhenAll(Racer(), Racer());

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ChangedFiresOnSuccessfulLoad()
    {
        var store = MakeStore(_ => Task.FromResult(ServiceStatus.Operational));
        var fireCount = 0;
        store.Changed += () => fireCount++;

        await store.LoadAsync();

        Assert.Equal(1, fireCount);
    }

    [Fact]
    public async Task ChangedDoesNotFireOnFailure()
    {
        var store = MakeStore(_ => throw new UsageException(UsageError.Network("offline")));
        var fireCount = 0;
        store.Changed += () => fireCount++;

        await store.LoadAsync();

        Assert.Equal(0, fireCount);
    }
}
