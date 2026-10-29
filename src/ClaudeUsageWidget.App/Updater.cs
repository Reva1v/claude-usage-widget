using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using ClaudeUsageWidget.Core;
// UseWindowsForms exposes System.Windows.Forms globally, and its MessageBox
// family collides with the WPF one.
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;
using MessageBoxOptions = System.Windows.MessageBoxOptions;

namespace ClaudeUsageWidget.App;

/// <summary>
/// The tray's "Check for updates and install": asks GitHub for the latest
/// release, downloads its win-x64 zip and swaps the install folder for it.
/// </summary>
///
/// The running exe cannot overwrite itself, so the swap is handed to a small
/// PowerShell script that waits for this process to exit, copies the new files
/// over the folder and starts the exe again. The app quits right after
/// launching it.
internal static class Updater
{
    private const string Caption = "Claude Usage Widget";

    private static readonly HttpClient Client = CreateClient();

    /// One check at a time: a second click while the zip is downloading
    /// would start a second script racing the first over the same folder.
    private static bool _busy;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        // GitHub's API refuses requests without a User-Agent.
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ClaudeUsageWidget", CoreInfo.Version));
        return client;
    }

    /// <param name="quit">Called once the swap script is running — the
    /// script waits for this process to be gone before it touches a file.</param>
    public static async Task CheckAndInstallAsync(Action quit)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            await RunAsync(quit);
        }
        catch (Exception ex)
        {
            WidgetLog.Write("-", "update", $"failed {ex.GetType().Name}: {ex.Message}");
            Show($"Update failed: {ex.Message}", MessageBoxImage.Error);
        }
        finally
        {
            _busy = false;
        }
    }

    private static async Task RunAsync(Action quit)
    {
        WidgetLog.Write("-", "update", $"check current={CoreInfo.Version}");

        using var request = new HttpRequestMessage(HttpMethod.Get, LatestRelease.Endpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await Client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var release = LatestRelease.Parse(await response.Content.ReadAsStringAsync())
                      ?? throw new InvalidDataException("GitHub returned no release.");

        if (!LatestRelease.IsNewer(release.Version, CoreInfo.Version))
        {
            WidgetLog.Write("-", "update", $"up-to-date latest={release.Version}");
            Show($"You have the latest version (v{CoreInfo.Version}).", MessageBoxImage.Information);
            return;
        }

        var exePath = Environment.ProcessPath ?? throw new InvalidOperationException("No process path.");
        var installDir = Path.GetDirectoryName(exePath)!;

        // A build from source (dotnet run, an IDE) keeps the app in a DLL
        // beside the exe; the release is a single-file exe. Copying it over a
        // bin\Debug folder would leave a build nobody updates the source of,
        // so this case gets the page instead.
        var fromSource = File.Exists(Path.Combine(installDir, "ClaudeUsageWidget.App.dll"));
        if (release.AssetUrl is null || fromSource || !CanWrite(installDir))
        {
            var why = release.AssetUrl is null ? "The release has no Windows build attached."
                : fromSource ? "This copy runs from a source build, so it can't replace itself."
                : $"The app folder is not writable:\n{installDir}";
            if (Ask($"Version v{release.Version} is available.\n\n{why}\n\nOpen the release page?"))
                OpenUrl(release.PageUrl);
            return;
        }

        if (!Ask($"Version v{release.Version} is available (you have v{CoreInfo.Version}).\n\n" +
                 "Download and install it now? The widget will restart."))
            return;

        var work = Path.Combine(Path.GetTempPath(), $"ClaudeUsageWidget-update-{release.Version}");
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
        Directory.CreateDirectory(work);

        var zip = Path.Combine(work, "release.zip");
        WidgetLog.Write("-", "update", $"download {release.AssetUrl}");
        await using (var source = await Client.GetStreamAsync(release.AssetUrl))
        await using (var target = File.Create(zip))
            await source.CopyToAsync(target);

        var extracted = Path.Combine(work, "files");
        ZipFile.ExtractToDirectory(zip, extracted);

        // The zip's own layout is the release skill's business — flat today,
        // possibly a folder tomorrow. The exe is the anchor either way.
        var exeName = Path.GetFileName(exePath);
        var newExe = Directory.EnumerateFiles(extracted, exeName, SearchOption.AllDirectories).FirstOrDefault()
                     ?? throw new InvalidDataException($"{exeName} is not in the release zip.");

        var script = Path.Combine(work, "install.ps1");
        await File.WriteAllTextAsync(script, InstallScript);

        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in new[]
                 {
                     "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script,
                     "-ProcId", Environment.ProcessId.ToString(),
                     "-Source", Path.GetDirectoryName(newExe)!,
                     "-Target", installDir,
                     "-Exe", exePath,
                     "-Work", work,
                 })
            start.ArgumentList.Add(arg);
        Process.Start(start);

        WidgetLog.Write("-", "update", $"install v{release.Version} into {installDir}");
        quit();
    }

    /// Waits for the old process, copies with retries (an exiting process can
    /// hold its files for a moment after it is gone), relaunches, cleans up.
    /// The exe is started even when the copy failed, so a failed update
    /// leaves the old version running rather than nothing at all.
    private const string InstallScript = """
        param([int]$ProcId, [string]$Source, [string]$Target, [string]$Exe, [string]$Work)
        Wait-Process -Id $ProcId -Timeout 60 -ErrorAction SilentlyContinue
        for ($i = 0; $i -lt 20; $i++) {
            try {
                Copy-Item -Path (Join-Path $Source '*') -Destination $Target -Recurse -Force -ErrorAction Stop
                break
            } catch {
                Start-Sleep -Milliseconds 500
            }
        }
        Start-Process -FilePath $Exe
        Remove-Item -LiteralPath $Work -Recurse -Force -ErrorAction SilentlyContinue
        """;

    private static bool CanWrite(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, $".update-probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // DefaultDesktopOnly: the app has no window to own the box, and an
    // unowned box opened from the tray otherwise lands behind whatever has focus.
    private static void Show(string text, MessageBoxImage icon) =>
        MessageBox.Show(text, Caption, MessageBoxButton.OK, icon, MessageBoxResult.OK, MessageBoxOptions.DefaultDesktopOnly);

    private static bool Ask(string text) =>
        MessageBox.Show(text, Caption, MessageBoxButton.YesNo, MessageBoxImage.Question,
            MessageBoxResult.Yes, MessageBoxOptions.DefaultDesktopOnly) == MessageBoxResult.Yes;

    private static void OpenUrl(Uri url)
    {
        WidgetLog.Write("-", "browser-open", $"site=update url={url}");
        Process.Start(new ProcessStartInfo(url.ToString()) { UseShellExecute = true });
    }
}
