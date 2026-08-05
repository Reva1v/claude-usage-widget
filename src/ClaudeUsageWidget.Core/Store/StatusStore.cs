namespace ClaudeUsageWidget.Core;

/// Owns the service status and its refresh cycle. Separate from
/// <see cref="UsageStore"/> because it reads a different, unauthenticated
/// endpoint and a failure in one must not blank the other.
///
/// This deliberately differs from the Swift original
/// (`Sources/ClaudeUsageWidgetCore/Store/StatusStore.swift`): there is no
/// timer and no wake handler here — the App layer schedules refreshes and
/// calls <see cref="LoadAsync"/> — mirroring the split already made in
/// <see cref="UsageStore"/>.
public sealed class StatusStore
{
    public const int RefreshIntervalSeconds = 300;

    private readonly Func<CancellationToken, Task<ServiceStatus>> _fetch;

    /// Запрос, который выполняется прямо сейчас, если есть. App-слой может
    /// дёрнуть LoadAsync() из таймера, ручного обновления и обработчика
    /// пробуждения почти одновременно; без коалесценции они гонятся друг с
    /// другом, и побеждает тот, кто закончил последним — на экране может
    /// оказаться статус старее уже показанного.
    private Task? _inFlight;

    /// Синхронизирует проверку-и-публикацию `_inFlight`. В отличие от
    /// Swift-оригинала, где @MainActor сериализовал всех вызывающих,
    /// LoadAsync() здесь дёргают и с UI-потока (таймер, пункт меню), и с
    /// worker-потока SystemEvents.PowerModeChanged — без блокировки два
    /// потока могут одновременно увидеть `_inFlight == null` и оба запустить
    /// свой fetch, нарушая контракт "один fetch на все параллельные вызовы".
    private readonly object _inFlightGate = new();

    /// The last status successfully read. A failed refresh leaves it
    /// standing — a transient network blip should not claim the service is
    /// down.
    public ServiceStatus Status { get; private set; } = ServiceStatus.Unknown;

    /// Fires after a successful load sets a new <see cref="Status"/>. A
    /// failed load never fires it: nothing changed for anyone to react to.
    public event Action? Changed;

    public StatusStore(Func<CancellationToken, Task<ServiceStatus>> fetch)
    {
        _fetch = fetch;
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
        ServiceStatus fetched;
        try
        {
            fetched = await _fetch(CancellationToken.None);
        }
        catch
        {
            // Сбой фетча оставляет прежний статус как есть — временный
            // сетевой сбой не должен объявлять сервис недоступным. Порт
            // Swift-эквивалента `guard let fetched = try? await fetch() else { return }`.
            return;
        }

        Status = fetched;
        Changed?.Invoke();
    }
}
