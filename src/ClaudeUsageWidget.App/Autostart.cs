using Microsoft.Win32;

namespace ClaudeUsageWidget.App;

/// <summary>
/// Autostart via <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>
/// — requires no administrator rights and works from a bare exe with no
/// installer, unlike Task Scheduler/a service. Port of LaunchAtLoginToggle
/// (<c>ClaudeUsageWidgetApp.swift:165-188</c>), which on macOS relies
/// on SMAppService; there is no Windows equivalent of SMAppService, hence
/// working with the registry directly.
/// </summary>
public static class Autostart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClaudeUsageWidget";

    /// <summary>The value can be missing (never turned on) or not be a
    /// string (someone manually messed up the registry) — both cases read
    /// as "off".</summary>
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string;
    }

    /// <summary>
    /// Lets any registry exception propagate out (no rights, the key is
    /// locked down by group policy, etc.) — the caller (TrayIcon) catches
    /// it and rolls back the checkbox, a port of LaunchAtLoginToggle's
    /// catch semantics.
    /// </summary>
    public static void SetEnabled(bool enabled)
    {
        // The HKCU Run key almost always already exists (Windows creates
        // it), but CreateSubKey is a safety net for a nonstandard profile.
        // The second "?? throw": both overloads are annotated as nullable
        // in Microsoft.Win32.Registry — the compiler does not know that in
        // practice they never return null, and without an explicit throw
        // this is CS8600 (Nullable build has 0 warnings as a hard
        // requirement).
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath)
            ?? throw new InvalidOperationException($@"Unable to open or create HKCU\{RunKeyPath}.");

        if (enabled)
        {
            var exePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("Environment.ProcessPath is null — cannot register autostart.");
            key.SetValue(ValueName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
