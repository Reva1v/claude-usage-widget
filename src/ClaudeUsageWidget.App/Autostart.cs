using Microsoft.Win32;

namespace ClaudeUsageWidget.App;

/// <summary>
/// Автозапуск через <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>
/// — не требует прав администратора и работает из голого exe без
/// установщика, в отличие от Task Scheduler/службы. Порт LaunchAtLoginToggle
/// (<c>ClaudeUsageWidgetApp.swift:165-188</c>), которая на macOS полагается
/// на SMAppService; здесь Windows-эквивалента SMAppService нет, поэтому
/// прямая работа с реестром.
/// </summary>
public static class Autostart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClaudeUsageWidget";

    /// <summary>Значение может отсутствовать (никогда не включали) или быть
    /// не строкой (кто-то вручную испортил реестр) — оба случая read as
    /// "выключено".</summary>
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string;
    }

    /// <summary>
    /// Бросает наружу любое исключение реестра (нет прав, ключ заблокирован
    /// групповой политикой и т.п.) — вызывающая сторона (TrayIcon) ловит его
    /// и откатывает чекбокс, порт catch-семантики LaunchAtLoginToggle.
    /// </summary>
    public static void SetEnabled(bool enabled)
    {
        // Run-ключ у HKCU почти всегда уже существует (создаётся Windows), но
        // CreateSubKey — подстраховка на случай нестандартного профиля.
        // Второй "?? throw": обе перегрузки аннотированы как nullable в
        // Microsoft.Win32.Registry — компилятор не знает, что на практике
        // они не возвращают null, и без явного throw это CS8600 (Nullable
        // build has 0 warnings as a hard requirement).
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
