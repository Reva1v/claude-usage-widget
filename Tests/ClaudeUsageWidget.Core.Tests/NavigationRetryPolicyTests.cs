namespace ClaudeUsageWidget.Core.Tests;

public class NavigationRetryPolicyTests
{
    [Theory]
    [InlineData("Unknown")]
    [InlineData("ConnectionAborted")]
    [InlineData("ConnectionReset")]
    [InlineData("OperationCanceled")]
    [InlineData("Timeout")]
    public void TheseStatusesAreWorthOneMoreAttempt(string status) =>
        Assert.True(NavigationRetryPolicy.IsTransient(status));

    [Theory]
    [InlineData("HostNameNotResolved")]  // no DNS: a second try fails the same way
    [InlineData("Unknown ")]             // exact match, not "starts with"
    [InlineData("unknown")]              // ordinal, so case matters
    [InlineData("")]
    [InlineData("CertificateCommonNameIsIncorrect")]
    public void AnythingElseIsNotRetried(string status) =>
        Assert.False(NavigationRetryPolicy.IsTransient(status));

    [Fact]
    public void WaitsThreeSecondsBeforeTheSecondAttempt() =>
        Assert.Equal(TimeSpan.FromSeconds(3), NavigationRetryPolicy.RetryDelay);
}
