using System.Windows;
using System.Windows.Threading;
using ClaudeUsageWidget.Core;
using Microsoft.Win32;

namespace ClaudeUsageWidget.App.Windows;

/// Windows' two light/dark switches, read from the registry, and a Changed
/// event when either flips.
///
/// AppsUseLightTheme is "Default app mode" — what the panel and the menu
/// follow under Theme = System. SystemUsesLightTheme is the taskbar's own
/// colour — what the tray icon's digits and the band's text follow ALWAYS,
/// because they are drawn onto the taskbar and the theme setting has no say
/// over its colour. A missing value is dark: Windows 10 before 1903 had no
/// light mode at all.
public static class SystemTheme
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static ThemeKind AppsKind { get; private set; } = Read("AppsUseLightTheme");
    public static ThemeKind TaskbarKind { get; private set; } = Read("SystemUsesLightTheme");

    /// Raised on the dispatcher after either kind changed.
    public static event Action? Changed;

    private static Dispatcher? _dispatcher;

    public static void Start(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public static void Stop() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

    /// UserPreferenceChanged arrives on a thread-pool thread. Everything that
    /// listens to Changed touches WPF, so the hop happens once, here.
    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;

        var apps = Read("AppsUseLightTheme");
        var taskbar = Read("SystemUsesLightTheme");
        if (apps == AppsKind && taskbar == TaskbarKind) return;

        _dispatcher?.BeginInvoke(() =>
        {
            AppsKind = apps;
            TaskbarKind = taskbar;
            Changed?.Invoke();
        });
    }

    private static ThemeKind Read(string valueName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Key);
            return key?.GetValue(valueName) is int value && value != 0 ? ThemeKind.Light : ThemeKind.Dark;
        }
        catch
        {
            return ThemeKind.Dark;
        }
    }
}
