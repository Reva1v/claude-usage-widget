using System.IO;
using System.Text;
using ClaudeUsageWidget.Core;

namespace ClaudeUsageWidget.App;

/// The widget's event log: always on, one line per event, and small.
///
/// It exists because the panel can only ever show the LAST error, in six
/// words, and the errors worth diagnosing here are the ones that came and
/// went while nobody was looking. Nothing is written at the 5-minute success
/// cadence — a file that grows on every healthy refresh is a file nobody
/// reads and Windows never reclaims.
///
/// Never throws. A log that can take the widget down is worse than no log.
public static class WidgetLog
{
    /// Rotate at 1 MB, keeping exactly one previous file. Two generations is
    /// enough to survive a burst of failures without becoming a disk-space
    /// question of its own.
    private const long MaxBytes = 1024 * 1024;

    // Every account's session logs into the same file, and refreshes overlap
    // across accounts.
    private static readonly object Gate = new();

    // No BOM: this file is read by tail-style tools and by eye, and a BOM at
    // the head of the first line shows up as garbage in both.
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// %LOCALAPPDATA%\ClaudeUsageWidget\widget.log — beside the WebView2
    /// profiles, not beside settings.json in %APPDATA%: this is machine-local
    /// diagnostic noise and has no business following a roaming profile.
    public static string Path { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClaudeUsageWidget", "widget.log");

    public static void Write(string account, string evt, string details)
    {
        try
        {
            lock (Gate)
            {
                var directory = System.IO.Path.GetDirectoryName(Path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                // Rotation gets its own catch so that failing to rotate does
                // not also stop the append. A tailer holding the file open
                // past 1 MB makes File.Move throw on every subsequent write,
                // which would silence the log exactly when it is longest and
                // busiest — an oversized file is far better than no events.
                try
                {
                    Rotate();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Widget log rotation failed: {ex}");
                }

                File.AppendAllText(
                    Path,
                    LogLine.Format(DateTimeOffset.Now, account, evt, details) + Environment.NewLine,
                    Utf8NoBom);
            }
        }
        catch (Exception ex)
        {
            // A locked or unwritable log is a broken side channel, exactly
            // like the usage export — never a reason to fail the caller.
            System.Diagnostics.Debug.WriteLine($"Widget log write failed: {ex}");
        }
    }

    /// Checked before the append, so the file overshoots 1 MB by one line at
    /// most rather than being rotated a whole cycle late.
    private static void Rotate()
    {
        var info = new FileInfo(Path);
        if (!info.Exists || info.Length <= MaxBytes) return;

        // Overwrite: the previous generation is the one thing here we are
        // willing to lose, and refusing to rotate would let the file grow
        // without bound instead.
        File.Move(Path, Path + ".1", overwrite: true);
    }
}
