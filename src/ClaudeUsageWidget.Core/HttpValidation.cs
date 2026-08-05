namespace ClaudeUsageWidget.Core;

/// Maps an HTTP status code onto the widget's error vocabulary. Shared by
/// every API client so a 401 from the usage endpoint and a 401 from the
/// status endpoint mean the same thing to callers.
public static class HttpValidation
{
    public static void Validate(int statusCode, int? retryAfterSeconds = null)
    {
        switch (statusCode)
        {
            case >= 200 and < 300:
                return;
            case 401 or 403:
                throw new UsageException(UsageError.Unauthorized);
            case 429:
                throw new UsageException(UsageError.RateLimited(retryAfterSeconds));
            default:
                throw new UsageException(UsageError.Network($"HTTP {statusCode}"));
        }
    }
}
