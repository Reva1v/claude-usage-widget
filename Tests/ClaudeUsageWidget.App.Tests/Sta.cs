using System.Runtime.ExceptionServices;

namespace ClaudeUsageWidget.App.Tests;

/// <summary>
/// Runs a test body on a fresh STA thread. WPF refuses to create a Window on
/// anything else, and xunit hands every fact an MTA thread pool thread — so
/// without this the window under test throws before the assertion it exists
/// for. A dedicated thread rather than a shared one: each test gets its own
/// Dispatcher, and a window left behind by one cannot reach the next.
/// </summary>
internal static class Sta
{
    public static void Run(Action body)
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        // Rethrown with its original stack, so a failed Assert still reads as
        // that assertion and not as "something happened on some thread".
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
