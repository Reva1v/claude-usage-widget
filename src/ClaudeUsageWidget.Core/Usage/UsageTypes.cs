namespace ClaudeUsageWidget.Core;

/// Один usage bucket из ответа /api/oauth/usage.
public sealed record UsageBucket(
    /// Процент от лимита, использованный в диапазоне сервера 0...100.
    double Utilization,
    /// Когда это окно обнулится. Отсутствует для bucket'ов, которые аккаунт не использует.
    DateTimeOffset? ResetsAt);

/// Декодированный ответ /api/oauth/usage.
///
/// Намеренно словарь, а не запись с фиксированными свойствами: набор ключей
/// bucket'ов меняется по мере появления и исчезновения моделей, и ни новый,
/// ни пропавший ключ не должны требовать изменения кода.
public sealed class UsageSnapshot : IEquatable<UsageSnapshot>
{
    public IReadOnlyDictionary<string, UsageBucket> Buckets { get; }

    /// Когда источник реально наблюдал эти цифры. Сетевые ответы актуальны и
    /// оставляют это null; statusline-мост Claude Code передаёт время
    /// снятия, чтобы устаревшие закэшированные цифры были честно помечены.
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
        // Порядок словаря не гарантирован, поэтому XOR по ключам — этого
        // достаточно для стабильности хэша, поэлементное сравнение всё равно
        // делает Equals.
        var hash = Buckets.Count.GetHashCode();
        foreach (var key in Buckets.Keys)
            hash ^= key.GetHashCode();
        return HashCode.Combine(hash, SourceUpdatedAt);
    }
}

public enum UsageErrorKind
{
    /// В Keychain не найдены credentials Claude Code.
    NoCredentials,
    /// Эндпоинт отклонил токен — Claude Code нужен новый логин.
    Unauthorized,
    /// Тело не было JSON, либо вообще не содержало usage buckets.
    MalformedResponse,
    /// Эндпоинт ответил 429; RetryAfterSeconds — Retry-After сервера, если он его прислал.
    RateLimited,
    /// Сбой транспорта или неожиданный код статуса.
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

    /// Эти сообщения доходят до пользователя напрямую — алерт проверки
    /// обновлений показывает их при сбое — поэтому они читаются как
    /// предложения, а не как C#-синтаксис.
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

public sealed class UsageException : Exception
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
