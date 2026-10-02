namespace ClaudeUsageWidget.Core;

/// How see-through the panel's background is. The dials and the text stay
/// opaque at every value; 0 leaves only them on the wallpaper.
public static class PanelOpacities
{
    /// The alpha the panel has always had (Palette.PanelAlpha): Theme.swift's
    /// 0.35 sat over a desktop blur, without one the digits need more behind them.
    public const double Default = 0.82;

    /// The tray menu's steps, brightest first. Default is one of them so the
    /// menu ticks something on a fresh install.
    public static readonly IReadOnlyList<double> Presets = [1.0, Default, 0.6, 0.4, 0.2, 0.0];

    /// Null is a file from before the setting; anything outside 0..1 — or NaN
    /// from a hand-edited file — is clamped rather than refused, so a typo
    /// cannot make the panel unreadable or vanish.
    public static double Resolve(double? saved) =>
        saved is { } value && !double.IsNaN(value) ? Math.Clamp(value, 0.0, 1.0) : Default;

    /// Whether a saved value is one of the presets — the menu ticks the step
    /// only when the value really is that step, never the nearest one.
    public static bool IsPreset(double value, double preset) => Math.Abs(value - preset) < 0.005;
}
