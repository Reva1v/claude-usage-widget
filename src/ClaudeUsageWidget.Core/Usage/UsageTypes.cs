namespace ClaudeUsageWidget.Core;

/// One usage bucket from the /api/oauth/usage response.
public sealed record UsageBucket(
    /// Percentage of the limit used, in the server's 0...100 range.
    double Utilization,
    /// When this window resets. Absent for buckets the account doesn't use.
    DateTimeOffset? ResetsAt);

/// The decoded /api/oauth/usage response.
///
/// Intentionally a dictionary rather than a record with fixed properties: the
/// set of bucket keys changes as models come and go, and neither a new nor a
/// disappearing key should require a code change.
public sealed class UsageSnapshot : IEquatable<UsageSnapshot>
{
    public IReadOnlyDictionary<string, UsageBucket> Buckets { get; }

    /// When the source actually observed these numbers. Network responses are
    /// fresh and leave this null; the Claude Code statusline bridge passes the
    /// capture time so stale cached numbers are honestly marked as such.
    public DateTimeOffset? SourceUpdatedAt { get; }

    public UsageSnapshot(IReadOnlyDictionary<string, UsageBucket> buckets, DateTimeOffset? sourceUpdatedAt = null)
    {
        Buckets = buckets;
        SourceUpdatedAt = sourceUpdatedAt;
    }

    public UsageBucket? this[string key] => Buckets.TryGetValue(key, out var bucket) ? bucket : null;

    public bool Equals(UsageSnapshot? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (SourceUpdatedAt != other.SourceUpdatedAt) return false;
        if (Buckets.Count != other.Buckets.Count) return false;

        foreach (var (key, bucket) in Buckets)
        {
            if (!other.Buckets.TryGetValue(key, out var otherBucket) || !bucket.Equals(otherBucket))
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as UsageSnapshot);

    public override int GetHashCode()
    {
        // Dictionary order isn't guaranteed, so XOR over the keys is enough
        // for hash stability — the element-by-element comparison is still
        // done by Equals.
        var hash = Buckets.Count.GetHashCode();
        foreach (var key in Buckets.Keys)
            hash ^= key.GetHashCode();
        return HashCode.Combine(hash, SourceUpdatedAt);
    }
}

public enum UsageErrorKind
{
    /// No Claude Code credentials were found in the Keychain.
    NoCredentials,
    /// The endpoint rejected the token — Claude Code needs a fresh login.
    Unauthorized,
    /// The body wasn't JSON, or didn't contain usage buckets at all.
    MalformedResponse,
    /// The endpoint answered 429; RetryAfterSeconds is the server's Retry-After, if it sent one.
    RateLimited,
    /// Transport failure or an unexpected status code.
    Network,
}

public sealed record UsageError(UsageErrorKind Kind, int? RetryAfterSeconds = null, string? Message = null)
{
    public static readonly UsageError NoCredentials = new(UsageErrorKind.NoCredentials);
    public static readonly UsageError Unauthorized = new(UsageErrorKind.Unauthorized);
    public static readonly UsageError MalformedResponse = new(UsageErrorKind.MalformedResponse);

    public static UsageError RateLimited(int? retryAfterSeconds) =>
        new(UsageErrorKind.RateLimited, RetryAfterSeconds: retryAfterSeconds);

    public static UsageError Network(string message) =>
        new(UsageErrorKind.Network, Message: message);

    /// These messages reach the user directly — the update-check alert shows
    /// them on failure — so they read as sentences, not as C# syntax.
    public string Description => Kind switch
    {
        UsageErrorKind.NoCredentials => "No Claude.ai web session was found.",
        UsageErrorKind.Unauthorized => "The Claude.ai session expired. Sign in again from the widget menu.",
        UsageErrorKind.MalformedResponse => "The server returned something unexpected.",
        UsageErrorKind.RateLimited => "The API is rate limited. The widget retries on its own.",
        UsageErrorKind.Network => Message ?? string.Empty,
        _ => throw new ArgumentOutOfRangeException(),
    };
}

/// Not sealed: the App layer subclasses it to carry a WebView2-specific cause
/// (NavigationFailedException) that Core must not know the type of. Every
/// subclass still reports a UsageError, so `catch (UsageException)` in
/// UsageStore keeps handling all of them the same way.
public class UsageException : Exception
{
    public UsageError Error { get; }

    public UsageException(UsageError error) : base(error.Description)
    {
        Error = error;
    }
}

public abstract record UsageState
{
    public sealed record Loading : UsageState;

    public sealed record Ok(UsageSnapshot Snapshot, DateTimeOffset FetchedAt) : UsageState;

    public sealed record Failed(UsageError Error) : UsageState;
}
