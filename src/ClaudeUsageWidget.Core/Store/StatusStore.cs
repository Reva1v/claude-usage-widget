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

    /// The request that's in flight right now, if any. The App layer can
    /// kick LoadAsync() from a timer, a manual refresh, and a wake handler
    /// almost simultaneously; without coalescing they'd race each other, and
    /// whichever finishes last wins — the screen could end up showing a
    /// status older than the one already displayed.
    private Task? _inFlight;

    /// Synchronizes the check-and-publish of `_inFlight`. Unlike the Swift
    /// original, where @MainActor serialized all callers, LoadAsync() here
    /// gets called both from the UI thread (timer, menu item) and from the
    /// SystemEvents.PowerModeChanged worker thread — without a lock, two
    /// threads could simultaneously see `_inFlight == null` and both start
    /// their own fetch, breaking the "one fetch for all concurrent calls"
    /// contract.
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
            // A fetch failure leaves the previous status as is — a transient
            // network glitch shouldn't declare the service unavailable. Port
            // of the Swift equivalent `guard let fetched = try? await fetch() else { return }`.
            return;
        }

        Status = fetched;
        Changed?.Invoke();
    }
}
