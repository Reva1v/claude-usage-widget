using System.Globalization;
using System.Security.Cryptography;
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
    /// A trailing comma and an unknown enum name are tolerated: either one
    /// failing the parse would read as defaults, and the next save would
    /// write every account away.
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
        Converters = { new TolerantEnumConverter() },
    };

    private readonly string _path;
    private readonly Lock _saveLock = new();
    private readonly Lock _lastGoodLock = new();

    /// The last settings read or written successfully: what the views draw
    /// while the file cannot be read, and what a vanished file still holds.
    private WidgetSettingsData? _lastGood;

    public SettingsStore(string path)
    {
        _path = path;
    }

    public string Path => _path;

    /// A missing file (first run) or one that fails to parse (hand-edited,
    /// truncated by a crash mid-write) is not fatal — either falls back to
    /// defaults rather than throwing, so a corrupt settings file cannot take
    /// down the whole app. An unparseable file is copied aside first
    /// (`settings.json.bad-<utc stamp>`), because the next save replaces it.
    ///
    /// A file that is there but cannot be read right now is NOT defaults: an
    /// editor or a backup tool holding it, or an ACL refusing it. A short hold
    /// is waited out; past that the error is rethrown. This is the read for
    /// `Save(Load() with …)`: defaults read off a held file would be written
    /// back over it, every account gone. A view that only draws uses
    /// <see cref="Current"/>.
    ///
    /// A file missing after a good read is not a first run: a sync tool
    /// deletes and recreates it. The last good settings stand in.
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
            lock (_lastGoodLock) return _lastGood ?? new WidgetSettingsData();
        }

        WidgetSettingsData data;
        try
        {
            var raw = JsonSerializer.Deserialize<WidgetSettingsData>(json, SerializerOptions)
                      ?? new WidgetSettingsData();
            data = SettingsMigration.Apply(raw);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            // Unparseable: the bytes were read, so this is the file's content
            // and not a passing IO state. Defaults, as the summary says.
            KeepBadCopy(json);
            return new WidgetSettingsData();
        }

        Remember(data);
        return data;
    }

    /// The settings to draw from: a fresh read, or the last good one while
    /// the file cannot be read. Never throws once one read has succeeded, so a
    /// held file cannot break a render, a tooltip or a fetch. Never use it for
    /// a read-modify-write: that must see the failure and not save.
    public WidgetSettingsData Current()
    {
        try
        {
            return Load();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            lock (_lastGoodLock)
            {
                if (_lastGood is { } last) return last;
            }
            throw;
        }
    }

    /// Replaced whole through <see cref="FileReplace.Write"/>: an in-place
    /// rewrite cut short by a crash leaves a truncated file, which Load reads
    /// as defaults, every account gone.
    public void Save(WidgetSettingsData data)
    {
        lock (_saveLock)
        {
            FileReplace.Write(_path, stream => JsonSerializer.Serialize(stream, data, SerializerOptions));
        }

        Remember(data);
    }

    private void Remember(WidgetSettingsData data)
    {
        lock (_lastGoodLock) _lastGood = data;
    }

    /// Best effort and once per distinct content: a hand-edit gone wrong is
    /// worth a copy, but a widget polling every five minutes must not fill the
    /// folder with the same bad file.
    private void KeepBadCopy(string json)
    {
        try
        {
            var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json)));
            var directory = System.IO.Path.GetDirectoryName(_path) ?? ".";
            var prefix = System.IO.Path.GetFileName(_path) + ".bad-";
            foreach (var existing in Directory.EnumerateFiles(directory, prefix + "*"))
            {
                var bytes = File.ReadAllBytes(existing);
                if (Convert.ToHexString(SHA256.HashData(bytes)) == hash) return;
            }

            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            File.WriteAllText(_path + ".bad-" + stamp, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: the copy is a courtesy, the load must still answer.
        }
    }
}
