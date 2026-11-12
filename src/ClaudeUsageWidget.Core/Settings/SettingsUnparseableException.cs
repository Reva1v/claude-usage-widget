namespace ClaudeUsageWidget.Core;

/// settings.json was read but does not parse, and defaults are not safe to
/// hand out: a good read came before, or the bad bytes could not be kept. An
/// IOException, so every caller that already waits out a held file (a
/// read-modify-write aborting, a view drawing the last good settings, the
/// startup retry) treats it the same way.
public sealed class SettingsUnparseableException : IOException
{
    public SettingsUnparseableException(string message, Exception inner)
        : base(message, inner)
    {
    }

    public SettingsUnparseableException(string message)
        : base(message)
    {
    }
}
