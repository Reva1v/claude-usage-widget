namespace ClaudeUsageWidget.Core.Tests;

public class UsageStoreTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_785_348_000);

    /// Mutable clock for tests that need `now` to move between calls.
    private sealed class Clock
    {
        public DateTimeOffset Now;
    }

    private static UsageSnapshot Snapshot(double utilization) =>
        new(new Dictionary<string, UsageBucket> { ["five_hour"] = new(utilization, null) });

    private static UsageStore MakeStore(
        bool hasCredentials = true,
        Func<CancellationToken, Task<UsageSnapshot>>? fetch = null) =>
        new(
            fetch ?? (_ => Task.FromResult(Snapshot(1))),
            () => hasCredentials,
            () => Now,
            () => null,
            _ => { });

    [Fact]
    public void StartsLoading()
    {
        var store = MakeStore();
        Assert.Equal(new UsageState.Loading(), store.CurrentState);
    }

    [Fact]
    public async Task PublishesSnapshot()
    {
        var store = MakeStore(fetch: _ => Task.FromResult(Snapshot(42)));
        await store.LoadAsync();
        Assert.Equal(new UsageState.Ok(Snapshot(42), Now), store.CurrentState);
        Assert.Equal(Snapshot(42), store.LastSnapshot);
    }

    // Replaces Swift's failsWithoutToken/passesToken: there is no token to pass
    // through any more (the transport is always the web session), only a
    // boolean gate on whether a session exists at all.
    [Fact]
    public async Task NoCredentialsFailsWithoutFetch()
    {
        var called = false;
        var store = MakeStore(hasCredentials: false, fetch: _ =>
        {
            called = true;
            return Task.FromResult(Snapshot(1));
        });

        await store.LoadAsync();

        Assert.Equal(new UsageState.Failed(UsageError.NoCredentials), store.CurrentState);
        Assert.False(called);
    }

    [Fact]
    public async Task SurfacesUnauthorized()
    {
        var store = MakeStore(fetch: _ => throw new UsageException(UsageError.Unauthorized));
        await store.LoadAsync();
        Assert.Equal(new UsageState.Failed(UsageError.Unauthorized), store.CurrentState);
    }

    [Fact]
    public async Task WrapsUnknownErrors()
    {
        var store = MakeStore(fetch: _ => throw new InvalidOperationException("boom"));
        await store.LoadAsync();
        var failed = Assert.IsType<UsageState.Failed>(store.CurrentState);
        Assert.Equal(UsageErrorKind.Network, failed.Error.Kind);
    }

    [Fact]
    public async Task KeepsLastSnapshotOnFailure()
    {
        var shouldFail = false;
        var store = MakeStore(fetch: _ => shouldFail
            ? throw new UsageException(UsageError.Unauthorized)
            : Task.FromResult(Snapshot(42)));

        await store.LoadAsync();
        shouldFail = true;
        await store.LoadAsync();

        Assert.Equal(new UsageState.Failed(UsageError.Unauthorized), store.CurrentState);
        Assert.Equal(Snapshot(42), store.LastSnapshot);
    }

    [Fact]
    public async Task PausesOnRateLimit()
    {
        var calls = 0;
        var store = MakeStore(fetch: _ =>
        {
            calls++;
            throw new UsageException(UsageError.RateLimited(600));
        });

        await store.LoadAsync();
        Assert.Equal(new UsageState.Failed(UsageError.RateLimited(600)), store.CurrentState);
        Assert.Equal(Now + TimeSpan.FromSeconds(3600 + UsageStore.RetryMarginSeconds), store.RetryPausedUntil);

        await store.LoadAsync();
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ResumesAfterRateLimitDeadline()
    {
        var clock = new Clock { Now = Now };
        var calls = 0;
        var store = new UsageStore(
            fetch: _ =>
            {
                calls++;
                if (calls == 1) throw new UsageException(UsageError.RateLimited(600));
                return Task.FromResult(Snapshot(7));
            },
            hasCredentials: () => true,
            now: () => clock.Now,
            loadRetryState: () => null,
            saveRetryState: _ => { });

        await store.LoadAsync();
        clock.Now = Now + TimeSpan.FromSeconds(3600 + UsageStore.RetryMarginSeconds + 1);
        await store.LoadAsync();

        Assert.Equal(2, calls);
        Assert.Equal(new UsageState.Ok(Snapshot(7), clock.Now), store.CurrentState);
        Assert.Null(store.RetryPausedUntil);
    }

    [Fact]
    public async Task RateLimitWithoutRetryAfterBacksOff()
    {
        var store = MakeStore(fetch: _ => throw new UsageException(UsageError.RateLimited(null)));
        await store.LoadAsync();
        Assert.Equal(Now + TimeSpan.FromSeconds(3600 + UsageStore.RetryMarginSeconds), store.RetryPausedUntil);
    }

    [Fact]
    public async Task ConsecutiveRateLimitsEscalate()
    {
        var clock = new Clock { Now = Now };
        var store = new UsageStore(
            fetch: _ => throw new UsageException(UsageError.RateLimited(60)),
            hasCredentials: () => true,
            now: () => clock.Now,
            loadRetryState: () => null,
            saveRetryState: _ => { });

        await store.LoadAsync();
        Assert.Equal(clock.Now + TimeSpan.FromSeconds(3600 + UsageStore.RetryMarginSeconds), store.RetryPausedUntil);

        clock.Now += TimeSpan.FromSeconds(4000);
        await store.LoadAsync();
        Assert.Equal(clock.Now + TimeSpan.FromSeconds(6 * 3600 + UsageStore.RetryMarginSeconds), store.RetryPausedUntil);

        clock.Now += TimeSpan.FromSeconds(22_000);
        await store.LoadAsync();
        Assert.Equal(clock.Now + TimeSpan.FromSeconds(24 * 3600 + UsageStore.RetryMarginSeconds), store.RetryPausedUntil);
    }

    [Fact]
    public async Task SuccessResetsEscalation()
    {
        var clock = new Clock { Now = Now };
        var fail = true;
        var store = new UsageStore(
            fetch: _ => fail
                ? throw new UsageException(UsageError.RateLimited(null))
                : Task.FromResult(Snapshot(1)),
            hasCredentials: () => true,
            now: () => clock.Now,
            loadRetryState: () => null,
            saveRetryState: _ => { });

        await store.LoadAsync();                           // 429 #1
        clock.Now += TimeSpan.FromSeconds(4000);
        fail = false;
        await store.LoadAsync();                            // success
        fail = true;
        clock.Now += TimeSpan.FromSeconds(1000);
        await store.LoadAsync();                            // 429 again

        // Back to the first step of the ladder, not the third.
        Assert.Equal(clock.Now + TimeSpan.FromSeconds(3600 + UsageStore.RetryMarginSeconds), store.RetryPausedUntil);
    }

    [Fact]
    public async Task PersistsRateLimitPause()
    {
        UsageRetryState? saved = null;
        var calls = 0;

        var first = new UsageStore(
            fetch: _ => throw new UsageException(UsageError.RateLimited(60)),
            hasCredentials: () => true,
            now: () => Now,
            loadRetryState: () => saved,
            saveRetryState: state => saved = state);

        await first.LoadAsync();
        Assert.NotNull(saved);

        var relaunched = new UsageStore(
            fetch: _ =>
            {
                calls++;
                return Task.FromResult(Snapshot(1));
            },
            hasCredentials: () => true,
            now: () => Now + TimeSpan.FromSeconds(30),
            loadRetryState: () => saved,
            saveRetryState: state => saved = state);

        await relaunched.LoadAsync();

        Assert.Equal(saved!.Until, relaunched.RetryPausedUntil);
        Assert.Equal(new UsageState.Failed(UsageError.RateLimited(null)), relaunched.CurrentState);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task CoalescesOverlappingRefreshes()
    {
        var calls = 0;
        var store = MakeStore(fetch: async _ =>
        {
            calls++;
            await Task.Delay(50);
            return Snapshot(1);
        });

        var first = store.LoadAsync();
        var second = store.LoadAsync();
        await Task.WhenAll(first, second);

        Assert.Equal(1, calls);
    }

    // Changed has no Swift equivalent (Swift observation is implicit via
    // @Observable); these pin down the C#-specific event contract.
    [Fact]
    public async Task ChangedFiresOnSuccessfulLoad()
    {
        var store = MakeStore(fetch: _ => Task.FromResult(Snapshot(1)));
        var fireCount = 0;
        store.Changed += () => fireCount++;

        await store.LoadAsync();

        Assert.Equal(1, fireCount);
    }

    [Fact]
    public async Task ChangedDoesNotFireWhilePaused()
    {
        var store = MakeStore(fetch: _ => throw new UsageException(UsageError.RateLimited(600)));

        await store.LoadAsync(); // first failure: pauses and fires once
        var fireCount = 0;
        store.Changed += () => fireCount++;
        await store.LoadAsync(); // paused: returns without a state change

        Assert.Equal(0, fireCount);
    }
}
