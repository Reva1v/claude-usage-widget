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
    /// Enums as their NAMES, not their ordinals: this file is meant to be
    /// hand-edited, and `"Flow": 2` is not something anyone can read or write
    /// on purpose. Used for reading too, so a name written by hand round-trips.
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly Lock _saveLock = new();

    public SettingsStore(string path)
    {
        _path = path;
    }

    /// A missing file (first run) or one that fails to parse (hand-edited,
    /// truncated by a crash mid-write) is not fatal — either falls back to
    /// defaults rather than throwing, so a corrupt settings file cannot take
    /// down the whole app.
    ///
    /// A file that is there but cannot be read right now is NOT defaults: an
    /// editor or a backup tool holding it, or an ACL refusing it. A short hold
    /// is waited out; past that the error is rethrown. Every save here is
    /// `Save(Load() with …)`, so defaults read off a held file would be written
    /// back over it, every account gone, once the hold ends.
    public WidgetSettingsData Load()
    {
        string json;
        try
        {
            json = FileReplace.Read(() => File.ReadAllText(_path));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // A first run has no file at all, and an absent file must stay an
            // empty account list rather than become a phantom `default`
            // account with no cookies behind it.
            return new WidgetSettingsData();
        }

        try
        {
            var raw = JsonSerializer.Deserialize<WidgetSettingsData>(json, SerializerOptions)
                      ?? new WidgetSettingsData();
            return SettingsMigration.Apply(raw);
        }
        catch (Exception)
        {
            // Unparseable: the bytes were read, so this is the file's content
            // and not a passing IO state. Defaults, as the summary says.
            return new WidgetSettingsData();
        }
    }

    /// Replaced whole through <see cref="FileReplace.Write"/>: an in-place
    /// rewrite cut short by a crash leaves a truncated file, which Load reads
    /// as defaults, every account gone.
    public void Save(WidgetSettingsData data)
    {
        // One temp name per store, so two saves at once must not share it.
        lock (_saveLock)
        {
            FileReplace.Write(_path, stream => JsonSerializer.Serialize(stream, data, SerializerOptions));
        }
    }
}
