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
    public WidgetSettingsData Load()
    {
        try
        {
            var json = File.ReadAllText(_path);
            var raw = JsonSerializer.Deserialize<WidgetSettingsData>(json, SerializerOptions)
                      ?? new WidgetSettingsData();
            // Deliberately inside the try: a first run has no file at all, and
            // an absent file must stay an empty account list rather than
            // become a phantom `default` account with no cookies behind it.
            return SettingsMigration.Apply(raw);
        }
        catch
        {
            return new WidgetSettingsData();
        }
    }

    /// Writes a sibling temp file, flushes it to disk, then moves it over the
    /// real one: an in-place rewrite cut short by a crash leaves a truncated
    /// file, which Load reads as defaults, every account gone. Same directory,
    /// so the move is a rename on one volume. A crash before the move leaves
    /// at most one `.tmp`, which the next save overwrites.
    public void Save(WidgetSettingsData data)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var temp = _path + ".tmp";
        // One temp name per store, so two saves at once must not share it.
        lock (_saveLock)
        {
            try
            {
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, data, SerializerOptions);
                    stream.Flush(flushToDisk: true);
                }

                File.Move(temp, _path, overwrite: true);
            }
            catch (Exception ex)
            {
                try { File.Delete(temp); }
                catch (Exception cleanup) { throw new AggregateException(ex, cleanup); }
                throw;
            }
        }
    }
}
