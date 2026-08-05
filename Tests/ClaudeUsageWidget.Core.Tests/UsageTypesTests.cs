namespace ClaudeUsageWidget.Core.Tests;

public class UsageTypesTests
{
    [Fact]
    public void SnapshotEqualityComparesBucketContents()
    {
        var a = new UsageSnapshot(new Dictionary<string, UsageBucket>
            { ["five_hour"] = new(42, null) });
        var b = new UsageSnapshot(new Dictionary<string, UsageBucket>
            { ["five_hour"] = new(42, null) });
        Assert.Equal(a, b);
        Assert.NotEqual(a, new UsageSnapshot(new Dictionary<string, UsageBucket>
            { ["five_hour"] = new(43, null) }));
    }

    [Fact]
    public void IndexerReturnsNullForMissingKey()
    {
        var s = new UsageSnapshot(new Dictionary<string, UsageBucket>());
        Assert.Null(s["absent"]);
    }

    [Theory]
    [InlineData(UsageErrorKind.NoCredentials, "No Claude.ai web session was found.")]
    [InlineData(UsageErrorKind.Unauthorized, "The Claude.ai session expired. Sign in again from the widget menu.")]
    [InlineData(UsageErrorKind.MalformedResponse, "The server returned something unexpected.")]
    public void DescriptionsReadAsSentences(UsageErrorKind kind, string expected)
    {
        var error = kind switch
        {
            UsageErrorKind.NoCredentials => UsageError.NoCredentials,
            UsageErrorKind.Unauthorized => UsageError.Unauthorized,
            _ => UsageError.MalformedResponse,
        };
        Assert.Equal(expected, error.Description);
    }

    [Fact]
    public void RateLimitedDescriptionMentionsSelfRetry() =>
        Assert.Equal("The API is rate limited. The widget retries on its own.",
            UsageError.RateLimited(600).Description);

    [Fact]
    public void RateLimitedDescriptionIsSameWithoutRetryAfter() =>
        Assert.Equal("The API is rate limited. The widget retries on its own.",
            UsageError.RateLimited(null).Description);

    [Fact]
    public void NetworkDescriptionIsTheMessage() =>
        Assert.Equal("boom", UsageError.Network("boom").Description);

    [Fact]
    public void RateLimitedErrorsCompareByRetryAfter()
    {
        Assert.Equal(UsageError.RateLimited(600), UsageError.RateLimited(600));
        Assert.NotEqual(UsageError.RateLimited(600), UsageError.RateLimited(null));
    }

    [Fact]
    public void NoDescriptionLeaksCSharpSyntax()
    {
        var descriptions = new[]
        {
            UsageError.NoCredentials.Description,
            UsageError.Unauthorized.Description,
            UsageError.MalformedResponse.Description,
            UsageError.RateLimited(60).Description,
            UsageError.Network("offline").Description,
        };

        foreach (var description in descriptions)
        {
            Assert.DoesNotContain("(", description);
            Assert.DoesNotContain("\"", description);
        }
    }

    [Fact]
    public void UsageExceptionMessageMatchesErrorDescription()
    {
        var exception = new UsageException(UsageError.Unauthorized);
        Assert.Equal(UsageError.Unauthorized.Description, exception.Message);
        Assert.Equal(UsageError.Unauthorized, exception.Error);
    }
}
