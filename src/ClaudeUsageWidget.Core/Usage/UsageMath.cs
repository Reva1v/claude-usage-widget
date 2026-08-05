namespace ClaudeUsageWidget.Core;

/// Чистая арифметика за циферблатами. Никакого I/O, никаких чтений часов —
/// `now` всегда передаётся снаружи, так что каждый случай воспроизводим в тесте.
public static class UsageMath
{
    /// Оставшееся время до окна: "45s", "10m", "1h 0m", "1d 1h".
    /// Null, если времени сброса нет или оно уже прошло.
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

    /// Сервер сообщает utilization в шкале 0...100; внутри виджета всё работает
    /// в шкале 0...1.
    public static double Fraction(double utilization) =>
        Math.Min(Math.Max(utilization / 100, 0), 1);

    /// Доля 0...1 в виде строки с целым процентом, например "57%".
    ///
    /// Сдвигается на крошечный эпсилон перед округлением: доля, полученная
    /// делением точного серверного процента на 100, может оказаться чуть
    /// меньше границы .5 из-за двоичного представления — 0.575 * 100 равно
    /// 57.49999999999999 — и округлилась бы вниз вопреки всем ожиданиям.
    /// Сдвиг намного меньше любой реальной разницы в данных.
    public static string PercentText(double fraction) =>
        $"{(int)Math.Round(fraction * 100 + 1e-9, MidpointRounding.AwayFromZero)}%";
}
