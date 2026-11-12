namespace ClaudeUsageWidget.Core;

/// Replaces a file whole, through a flushed temp file moved over it, and waits
/// out a reader that holds the target for a moment.
///
/// A reader that opens the file with FileShare.ReadWrite but without Delete (a
/// polling script, an editor, an indexer) makes the move fail while it holds
/// the handle. Such holds are short, so a few retries keep the write instead
/// of losing it. A permanent refusal (a read-only file, or a folder whose ACL
/// denies deleting its files) fails at once: retrying it would only sleep on
/// the caller's thread. A deny ACE on the FILE alone does not stop the replace
/// (the folder's delete-child right still allows it), and the new file takes
/// the folder's inherited ACL, not the old file's. The last failure is
/// rethrown: the caller decides what a lost write means.
public static class FileReplace
{
    /// Waits between attempts: about 0.75 s in all before giving up.
    private static readonly int[] BackoffMs = [50, 100, 200, 400];

    /// ERROR_SHARING_VIOLATION and ERROR_LOCK_VIOLATION: another handle is in
    /// the way, which passes.
    internal static bool IsSharingViolation(Exception ex) =>
        ex is IOException && (ex.HResult & 0xFFFF) is 32 or 33;

    /// Writes <paramref name="path"/> whole: <paramref name="write"/> fills a
    /// sibling temp file, which is flushed to disk and moved over the target.
    /// A crash before the move leaves the old file; a failure deletes the temp.
    ///
    /// A symbolic link is followed and its TARGET replaced, the link kept: a
    /// dotfiles or sync setup links the file from elsewhere, and replacing the
    /// link with a regular file would cut it off without a word. The temp sits
    /// beside the final target, so the move stays a rename on one volume.
    public static void Write(string path, Action<Stream> write)
    {
        var target = ResolveLink(path);
        var directory = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        // A unique name: a stale `.tmp` left read-only by a crash or a sync tool
        // must not block every later write, and two writers never share one.
        var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                write(stream);
                stream.Flush(flushToDisk: true);
            }

            Move(temp, target);
        }
        catch (Exception ex)
        {
            try { File.Delete(temp); }
            catch (Exception cleanup) { throw new AggregateException(ex, cleanup); }
            throw;
        }
    }

    public static void Move(string temp, string target)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(temp, target, overwrite: true);
                return;
            }
            catch (Exception ex) when (attempt < BackoffMs.Length && IsTransient(ex, target))
            {
                Thread.Sleep(BackoffMs[attempt]);
            }
        }
    }

    /// Reads with the same patience: a writer or a backup tool holding the file
    /// exclusively for a moment. Anything else is rethrown at once.
    public static T Read<T>(Func<T> read)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return read();
            }
            catch (Exception ex) when (attempt < BackoffMs.Length && IsSharingViolation(ex))
            {
                Thread.Sleep(BackoffMs[attempt]);
            }
        }
    }

    /// A move over a file another handle holds fails with ACCESS_DENIED, the
    /// same code a read-only file or a denying ACL gives (measured on Windows 11:
    /// 0x80070005 for a held and for a read-only target). Opening the target
    /// for writing tells them apart: a reader that shares ReadWrite lets it
    /// through and a stricter one answers with a sharing violation, both of
    /// which pass; the permanent cases are refused again.
    private static bool IsTransient(Exception ex, string target)
    {
        if (IsSharingViolation(ex)) return true;
        if (ex is not UnauthorizedAccessException) return false;

        try
        {
            if (File.GetAttributes(target).HasFlag(FileAttributes.ReadOnly)) return false;
            using var probe = new FileStream(
                target, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            return true;
        }
        catch (Exception probeEx)
        {
            // No target yet (a first write) means the denial came from the
            // temp side, an antivirus scanning the fresh temp: that passes too.
            return IsSharingViolation(probeEx) || probeEx is FileNotFoundException;
        }
    }

    private static string ResolveLink(string path)
    {
        var info = new FileInfo(path);
        return info.Exists && info.LinkTarget is not null
            ? info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path
            : path;
    }
}
