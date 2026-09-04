using System.IO;
using System.Text.Json;
using ClaudeUsageWidget.Core;

namespace ClaudeUsageWidget.App;

/// Writes one account's figures where another tool can read them.
///
/// Never throws: an unwritable export path is a broken side channel, not a
/// reason for the widget to stop showing usage.
public static class UsageExportWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void Write(string path, UsageExportPayload payload)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            // Temp file plus move: the reader is a separate process polling this
            // path, and a plain write lets it catch a half-written file and
            // decide the account has no figures at all.
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(payload, Options));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Usage export to '{path}' failed: {ex}");
        }
    }
}
