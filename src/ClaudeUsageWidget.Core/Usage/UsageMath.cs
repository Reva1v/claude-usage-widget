namespace ClaudeUsageWidget.Core;

/// Pure arithmetic behind the dials. No I/O, no clock reads — `now` is
/// always passed in from outside, so every case is reproducible in a test.
public static class UsageMath
{
    /// Time remaining until the window resets: "45s", "10m", "1h 0m", "1d 1h".
    /// Null if there's no reset time or it has already passed.
    public static string? RemainingText(DateTimeOffset? resetsAt, DateTimeOffset now)
    {
        if (resetsAt is not { } resets) return null;
        var seconds = (int)Math.Floor((resets - now).TotalSeconds);
        if (seconds <= 0) return null;

        var days = seconds / 86_400;
        var hours = (seconds % 86_400) / 3600;
        var minutes = (seconds % 3600) / 60;

        if (days > 0) return $"{days}d {hours}h";
        if (hours > 0) return $"{hours}h {minutes}m";
        if (minutes > 0) return $"{minutes}m";
        return $"{seconds}s";
    }

    /// The server reports utilization on a 0...100 scale; inside the widget
    /// everything works on a 0...1 scale.
    public static double Fraction(double utilization) =>
        Math.Min(Math.Max(utilization / 100, 0), 1);

    /// A 0...1 fraction as a string with a whole-number percent, e.g. "57%".
    ///
    /// Shifted by a tiny epsilon before rounding: a fraction obtained by
    /// dividing the exact server percentage by 100 can end up slightly below
    /// the .5 boundary due to binary representation — 0.575 * 100 equals
    /// 57.49999999999999 — and would round down against all expectations.
    /// The shift is far smaller than any real difference in the data.
    public static string PercentText(double fraction) =>
        $"{(int)Math.Round(fraction * 100 + 1e-9, MidpointRounding.AwayFromZero)}%";
}
