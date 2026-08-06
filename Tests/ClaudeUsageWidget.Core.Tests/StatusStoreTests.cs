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
        // A Task.Delay(50) fence is flaky: LoadAsync() runs synchronously up
        // to and including `calls++` before the fetch's first await, so
        // `first`'s in-flight task is already recorded by the time the
        // statement below returns — but if the test runner stalls for more
        // than 50ms between the two LoadAsync() statements, the delay can
        // elapse and its continuation can clear `_inFlight` before `second`
        // is even issued, letting it start a real second fetch. A manually
        // released gate makes the fetch stay pending until both LoadAsync()
        // calls have definitely been issued, no matter how slow the runner is.
        var calls = 0;
        var gate = new TaskCompletionSource();
        var store = MakeStore(async _ =>
        {
            calls++;
            await gate.Task;
            return ServiceStatus.Operational;
        });

        var first = store.LoadAsync();
        var second = store.LoadAsync();
        gate.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, calls);
    }

    // Mirrors UsageStoreTests.CoalescesConcurrentLoadAsyncAcrossThreads: the App
    // layer can call LoadAsync() from more than one thread at once (UI thread
    // and a background wake handler), so the check-then-set on `_inFlight`
    // must be exercised across real OS threads, not just interleavings on one.
    // The fetch is gated for the same reason as there: an instantly-completing
    // fetch lets the first thread clear `_inFlight` before the second thread
    // even checks it, which makes a second fetch correct and the assertion
    // flaky. Releasing the gate only after both calls have been issued keeps
    // the two loads genuinely overlapping.
    [Fact]
    public async Task CoalescesConcurrentLoadAsyncAcrossThreads()
    {
        var calls = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = MakeStore(async _ =>
        {
            Interlocked.Increment(ref calls);
            await gate.Task;
            return ServiceStatus.Operational;
        });

        var barrier = new Barrier(2);
        using var entered = new CountdownEvent(2);
        Task Racer() => Task.Run(async () =>
        {
            barrier.SignalAndWait();
            var load = store.LoadAsync();
            entered.Signal();
            await load;
        });

        var racers = new[] { Racer(), Racer() };
        entered.Wait();
        gate.SetResult();
        await Task.WhenAll(racers);

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
