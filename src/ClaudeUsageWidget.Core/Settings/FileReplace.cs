namespace ClaudeUsageWidget.Core;

/// Moves a finished temp file over its target, waiting out a reader that holds
/// the target for a moment.
///
/// A reader that opens the file with FileShare.ReadWrite but without Delete (a
/// polling script, an editor, an indexer) makes the move fail while it holds
/// the handle. Such holds are short, so a few retries keep the write instead
/// of losing it. The last failure is rethrown: the caller decides what a lost
/// write means.
public static class FileReplace
{
    /// Waits between attempts: about 0.75 s in all before giving up.
    private static readonly int[] BackoffMs = [50, 100, 200, 400];

    public static void Move(string temp, string target)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(temp, target, overwrite: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < BackoffMs.Length)
            {
                Thread.Sleep(BackoffMs[attempt]);
            }
        }
    }
}
