namespace ClaudeUsageWidget.Core.Tests;

public class FetchIdleLeaseTests
{
    [Fact]
    public void TheLastUserLeavingHandsOutAReleaseToken()
    {
        var lease = new FetchIdleLease();
        lease.Enter();

        var token = lease.Exit();

        Assert.NotNull(token);
        Assert.True(lease.ShouldRelease(token.Value));
    }

    [Fact]
    public void NoTokenWhileAnotherUserStillHoldsTheWebView()
    {
        var lease = new FetchIdleLease();
        lease.Enter();
        lease.Enter();

        Assert.Null(lease.Exit());
        Assert.True(lease.InUse);
        Assert.NotNull(lease.Exit());
        Assert.False(lease.InUse);
    }

    [Fact]
    public void AUseInsideTheIdleWindowCancelsThatRelease()
    {
        // One refresh is several back-to-back fetches: the cookie check, the
        // organizations page, the usage page. The webview they share must not
        // be closed and re-created between them.
        var lease = new FetchIdleLease();
        lease.Enter();
        var first = lease.Exit()!.Value;

        lease.Enter();
        Assert.False(lease.ShouldRelease(first));

        var second = lease.Exit()!.Value;
        Assert.False(lease.ShouldRelease(first));
        Assert.True(lease.ShouldRelease(second));
    }

    [Fact]
    public void ATokenIsNotHonouredWhileTheWebViewIsInUse()
    {
        var lease = new FetchIdleLease();
        lease.Enter();
        var token = lease.Exit()!.Value;
        lease.Enter();

        Assert.False(lease.ShouldRelease(token));
    }

    [Fact]
    public void LeavingWithoutEnteringIsABug() =>
        Assert.Throws<InvalidOperationException>(() => new FetchIdleLease().Exit());

    [Fact]
    public void TheIdleDelayEndsWellInsideOneRefreshCycle()
    {
        // Longer than the gap between the fetches of one refresh, short enough
        // that the browser is gone for most of the five-minute cycle.
        Assert.True(FetchIdleLease.Delay > TimeSpan.Zero);
        Assert.True(FetchIdleLease.Delay <= TimeSpan.FromSeconds(UsageStore.RefreshIntervalSeconds) / 10);
    }
}
