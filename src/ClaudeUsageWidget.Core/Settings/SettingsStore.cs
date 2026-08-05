using System.Text.Json;

namespace ClaudeUsageWidget.Core;

/// Persists <see cref="WidgetSettingsData"/> as indented JSON at a fixed
/// path — the App layer passes `%APPDATA%\ClaudeUsageWidget\settings.json`.
///
/// No Swift counterpart: the mac build kept everything in UserDefaults, which
/// has no Windows equivalent worth emulating, so this is a plain JSON file
/// instead.
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _path;

    public SettingsStore(string path)
    {
        _path = path;
    }

    /// A missing file (first run) or one that fails to parse (hand-edited,
    /// truncated by a crash mid-write) is not fatal — either falls back to
    /// defaults rather than throwing, so a corrupt settings file cannot take
    /// down the whole app.
    public WidgetSettingsData Load()
    {
        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<WidgetSettingsData>(json) ?? new WidgetSettingsData();
        }
        catch
        {
            return new WidgetSettingsData();
        }
    }

    public void Save(WidgetSettingsData data)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(_path, JsonSerializer.Serialize(data, SerializerOptions));
    }
}
