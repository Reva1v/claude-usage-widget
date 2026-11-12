using System.Text.Json;
using ClaudeUsageWidget.Core;

namespace ClaudeUsageWidget.App;

/// Writes one account's figures where another tool can read them.
///
/// Never throws: an unwritable export path is a broken side channel, not a
/// reason for the widget to stop showing usage.
public static class UsageExportWriter
{
    public static void Write(string path, UsageExportPayload payload)
    {
        try
        {
            // Replaced whole: the reader is a separate process polling this
            // path, and a plain write lets it catch a half-written file and
            // decide the account has no figures at all.
            // UsageExport.JsonOptions, not a local copy: the key names and the
            // null policy are the reader's contract, and Core.Tests pin them
            // through that same object.
            FileReplace.Write(path, stream => JsonSerializer.Serialize(stream, payload, UsageExport.JsonOptions));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Usage export to '{path}' failed: {ex}");
        }
    }
}
