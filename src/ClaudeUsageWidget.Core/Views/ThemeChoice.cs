namespace ClaudeUsageWidget.Core;

/// What the user picked in the tray's Theme submenu. System — the default,
/// and what a null setting means — follows Windows' app theme.
public enum ThemeChoice
{
    System,
    Dark,
    Light,
}

/// The two palettes the app can actually draw.
public enum ThemeKind
{
    Dark,
    Light,
}

public static class ThemeChoices
{
    /// The palette a choice means, given what Windows currently says. Null is
    /// System: a file written before the setting existed follows the OS,
    /// which is also the default for a fresh install.
    public static ThemeKind Resolve(ThemeChoice? saved, ThemeKind system) =>
        (saved ?? ThemeChoice.System) switch
        {
            ThemeChoice.Dark => ThemeKind.Dark,
            ThemeChoice.Light => ThemeKind.Light,
            _ => system,
        };
}
