namespace ClaudeUsageWidget.Core.Tests;

public class HttpValidationTests
{
    [Fact]
    public void AcceptsSuccess()
    {
        HttpValidation.Validate(200);
        HttpValidation.Validate(204);
    }

    [Fact]
    public void RejectsUnauthorized()
    {
        var ex401 = Assert.Throws<UsageException>(() => HttpValidation.Validate(401));
        Assert.Equal(UsageError.Unauthorized, ex401.Error);

        var ex403 = Assert.Throws<UsageException>(() => HttpValidation.Validate(403));
        Assert.Equal(UsageError.Unauthorized, ex403.Error);
    }

    [Fact]
    public void RejectsRateLimitedWithRetryAfter()
    {
        var ex = Assert.Throws<UsageException>(() => HttpValidation.Validate(429, retryAfterSeconds: 600));
        Assert.Equal(UsageError.RateLimited(600), ex.Error);
    }

    [Fact]
    public void RejectsRateLimitedWithoutRetryAfter()
    {
        var ex = Assert.Throws<UsageException>(() => HttpValidation.Validate(429));
        Assert.Equal(UsageError.RateLimited(null), ex.Error);
    }

    [Fact]
    public void RejectsOtherStatusesAsNetworkErrorNamingTheCode()
    {
        var ex = Assert.Throws<UsageException>(() => HttpValidation.Validate(500));
        Assert.Equal(UsageError.Network("HTTP 500"), ex.Error);
    }
}
