namespace ClaudeUsageWidget.Core;

/// A rate-limit pause, persisted so a restart does not undo it.
public sealed record UsageRetryState(DateTimeOffset Until, int ConsecutiveRateLimits);

/// Owns the usage snapshot and the rate-limit backoff ladder.
///
/// The fetch, the credentials check and the clock are all injected so the
/// whole state machine is testable without a network or real time.
///
/// This deliberately differs from the Swift original (`Sources/ClaudeUsageWidgetCore/Store/UsageStore.swift`):
/// there is no timer and no wake handler here — the App layer schedules
/// refreshes and calls <see cref="LoadAsync"/> — and there is no statusline
/// cache fallback. The transport is always an authenticated claude.ai web
/// session rather than a Keychain token, so a boolean "is there a session"
/// gate (<c>hasCredentials</c>) replaces the token provider.
public sealed class UsageStore
{
    /// The server's figures move slowly; a faster poll would only add requests.
    public const int RefreshIntervalSeconds = 300;

    /// Запас сверх Retry-After сервера. Из наблюдений в проде: заголовок
    /// занижает длительность бана на несколько минут, а счётчик нарушений
    /// бана переживает собственное истечение — повтор, пришедшийся на
    /// несколько секунд раньше срока, снова триггерит свежий часовой бан, и
    /// так до бесконечности. Ожидание чуть дольше запрошенного разрывает
    /// этот цикл.
    public const int RetryMarginSeconds = 300;

    /// Дедлайн Retry-After на практике неоднократно оказывался
    /// недостаточным. Первый повтор разрешён через час; дальнейшие сбои
    /// размыкают цепь на шесть часов, а затем на сутки. Состояние переживает
    /// перезапуск приложения — см. loadRetryState/saveRetryState в конструкторе.
    private static readonly int[] RateLimitBackoffSeconds = [3600, 6 * 3600, 24 * 3600];

    private readonly Func<CancellationToken, Task<UsageSnapshot>> _fetch;
    private readonly Func<bool> _hasCredentials;
    private readonly Func<DateTimeOffset> _now;
    private readonly Action<UsageRetryState?> _saveRetryState;

    private int _consecutiveRateLimits;

    /// Запрос, который выполняется прямо сейчас, если есть. App-слой может
    /// дёрнуть LoadAsync() из таймера, ручного обновления и обработчика
    /// пробуждения почти одновременно; без коалесценции они гонятся друг с
    /// другом, и побеждает тот, кто закончил последним — на экране может
    /// оказаться снимок старее уже показанного.
    private Task? _inFlight;

    /// Синхронизирует проверку-и-публикацию `_inFlight`. В отличие от
    /// Swift-оригинала, где @MainActor сериализовал всех вызывающих,
    /// LoadAsync() здесь дёргают и с UI-потока (таймер, пункт меню), и с
    /// worker-потока SystemEvents.PowerModeChanged — без блокировки два
    /// потока могут одновременно увидеть `_inFlight == null` и оба запустить
    /// свой fetch, нарушая контракт "один fetch на все параллельные вызовы".
    private readonly object _inFlightGate = new();

    public UsageState CurrentState { get; private set; } = new UsageState.Loading();

    /// The most recent successful snapshot. Kept so a failing refresh dims the
    /// dials instead of blanking them.
    public UsageSnapshot? LastSnapshot { get; private set; }

    /// While set, refreshes are skipped: the server answered 429, and polling
    /// through the ban only prolongs it.
    public DateTimeOffset? RetryPausedUntil { get; private set; }

    /// Fires after every change to <see cref="CurrentState"/>.
    public event Action? Changed;

    public UsageStore(
        Func<CancellationToken, Task<UsageSnapshot>> fetch,
        Func<bool> hasCredentials,
        Func<DateTimeOffset> now,
        Func<UsageRetryState?> loadRetryState,
        Action<UsageRetryState?> saveRetryState)
    {
        _fetch = fetch;
        _hasCredentials = hasCredentials;
        _now = now;
        _saveRetryState = saveRetryState;

        var saved = loadRetryState();
        if (saved is not null && saved.Until > now())
        {
            RetryPausedUntil = saved.Until;
            _consecutiveRateLimits = saved.ConsecutiveRateLimits;
            CurrentState = new UsageState.Failed(UsageError.RateLimited(null));
        }
        else
        {
            saveRetryState(null);
        }
    }

    /// Coalesces overlapping calls: a load already in flight is awaited
    /// rather than duplicated, so a timer tick, a manual refresh and a wake
    /// handler firing together still make exactly one request.
    public Task LoadAsync()
    {
        TaskCompletionSource tcs;
        lock (_inFlightGate)
        {
            if (_inFlight is { } inFlight) return inFlight;

            // A synchronously-resolving fetch would let RunLoadAsync race to
            // completion (finally included) before this method could record it,
            // clobbering the very field the finally just cleared. Publishing the
            // completion source's task up front closes that window.
            tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _inFlight = tcs.Task;
        }

        // Deliberately outside the lock: the fetch itself, and everything it
        // awaits, must never run while holding a lock a second caller needs
        // just to check whether a fetch is already running.
        _ = RunLoadAsync(tcs);
        return tcs.Task;
    }

    private async Task RunLoadAsync(TaskCompletionSource tcs)
    {
        try
        {
            await PerformLoadAsync();
            tcs.SetResult();
        }
        catch (Exception ex)
        {
            tcs.SetException(ex);
        }
        finally
        {
            lock (_inFlightGate) { _inFlight = null; }
        }
    }

    private async Task PerformLoadAsync()
    {
        // Everything below — including `_now()` and `_hasCredentials()` — is
        // caller-injected and can throw. That must land in Failed(Network)
        // like any other unexpected error, not skip SetState entirely and
        // leave CurrentState stale (which is what happened when only the
        // fetch call itself was guarded). A nested try/catch's handler body
        // is still inside this outer try, so an exception raised while
        // computing the rate-limit escalation below is covered too.
        try
        {
            if (RetryPausedUntil is { } pausedUntil && _now() < pausedUntil) return;
            RetryPausedUntil = null;

            if (!_hasCredentials())
            {
                SetState(new UsageState.Failed(UsageError.NoCredentials));
                return;
            }

            try
            {
                var snapshot = await _fetch(CancellationToken.None);
                Publish(snapshot);
            }
            catch (UsageException ex)
            {
                var error = ex.Error;
                if (error.Kind == UsageErrorKind.RateLimited)
                {
                    _consecutiveRateLimits++;
                    var index = Math.Min(_consecutiveRateLimits - 1, RateLimitBackoffSeconds.Length - 1);
                    var wait = Math.Max(error.RetryAfterSeconds ?? 0, RateLimitBackoffSeconds[index]);
                    var deadline = _now() + TimeSpan.FromSeconds(wait + RetryMarginSeconds);
                    RetryPausedUntil = deadline;
                    _saveRetryState(new UsageRetryState(deadline, _consecutiveRateLimits));
                }
                SetState(new UsageState.Failed(error));
            }
        }
        catch (Exception ex)
        {
            SetState(new UsageState.Failed(UsageError.Network(ex.Message)));
        }
    }

    private void Publish(UsageSnapshot snapshot)
    {
        LastSnapshot = snapshot;
        _consecutiveRateLimits = 0;
        RetryPausedUntil = null;
        _saveRetryState(null);
        SetState(new UsageState.Ok(snapshot, snapshot.SourceUpdatedAt ?? _now()));
    }

    private void SetState(UsageState state)
    {
        CurrentState = state;
        Changed?.Invoke();
    }
}
