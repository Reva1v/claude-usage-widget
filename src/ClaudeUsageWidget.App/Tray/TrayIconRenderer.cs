using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace ClaudeUsageWidget.App.Tray;

/// <summary>
/// Рисует живую цифру трея — значение выбранной метрики без знака «%» (на
/// иконке размером с <see cref="SystemInformation.SmallIconSize"/> знак
/// процента сжимает сами цифры до нечитаемости), белым по прозрачному,
/// GDI+ → <see cref="Icon.FromHandle"/>.
/// </summary>
///
/// Пока данных нет, рисуется кольцо-заглушка — тот же глиф, что раньше жил в
/// TrayIcon.CreateRingIcon (порт monochrome-ring из
/// <c>ClaudeUsageWidgetApp.swift:17-30</c>; там template-изображение, которое
/// tint-ит сама macOS под светлый/тёмный менюбар — у Win32-трея такого
/// автотонирования нет, а подавляющее большинство трей-панелей тёмные,
/// поэтому белый зашит напрямую, не чёрный).
public static class TrayIconRenderer
{
    /// <summary>
    /// <paramref name="valueText"/> — значение вида "42%" (как в
    /// <c>TrayText.Metrics</c>) или null/пусто/"—" (доли нет). Возвращённый
    /// <see cref="Icon"/> оборачивает новый HICON, которым владеет вызывающая
    /// сторона: она обязана вызвать DestroyIcon при замене/освобождении (тот
    /// же контракт, что раньше был у TrayIcon.CreateRingIcon — см.
    /// TrayIcon.SetIcon/Dispose).
    /// </summary>
    public static Icon Render(string? valueText)
    {
        var size = SystemInformation.SmallIconSize;

        using var bitmap = new Bitmap(size.Width, size.Height);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // AntiAlias/AntiAliasGridFit — обычное серошкальное сглаживание,
            // не ClearType: на прозрачном фоне субпиксельный ClearType дал бы
            // цветные ореолы по краям глифов, а эти два режима — нет.
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            var digits = DigitsOnly(valueText);
            if (digits is null)
            {
                DrawRing(g, size);
            }
            else
            {
                DrawDigits(g, size, digits);
            }
        }

        // Bitmap.GetHicon() выделяет новый HICON, переживающий bitmap —
        // владение переходит вызывающей стороне (см. doc-комментарий выше).
        return Icon.FromHandle(bitmap.GetHicon());
    }

    /// <summary>"42%" → "42"; строка вообще без цифр — null/пусто, "—"
    /// (TrayText.Metrics для отсутствующей доли) или что угодно ещё
    /// нечисловое — → null, то есть кольцо-заглушка. Проверка на "хотя бы
    /// одна цифра", а не точечное сравнение с "—": RefreshTrayIcon на самом
    /// первом старте (LastSnapshot ещё null) как раз и подаёт сюда "—", и
    /// проверять нужно именно это, а не только явные null/пусто.</summary>
    private static string? DigitsOnly(string? valueText)
    {
        if (string.IsNullOrEmpty(valueText)) return null;

        var digits = valueText.TrimEnd('%');
        return digits.Any(char.IsDigit) ? digits : null;
    }

    private static void DrawRing(Graphics g, Size size)
    {
        using var pen = new Pen(Color.White, 2f);
        const float inset = 2f;
        g.DrawEllipse(pen, inset, inset, size.Width - inset * 2, size.Height - inset * 2);
    }

    private static void DrawDigits(Graphics g, Size size, string digits)
    {
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };

        using var font = FitFont(g, digits, size);
        var rect = new RectangleF(0, 0, size.Width, size.Height);
        g.DrawString(digits, font, Brushes.White, rect, format);
    }

    /// <summary>
    /// Подбирает наибольший кегль Segoe UI Bold, при котором строка ещё
    /// умещается по ширине иконки — у "8" и "100" при одном кегле совсем
    /// разная ширина, а трей слишком мал, чтобы фиксировать шрифт под
    /// худший случай (три цифры).
    /// </summary>
    private static Font FitFont(Graphics g, string digits, Size size)
    {
        const float minEm = 6f;
        const float margin = 1f;

        for (var em = size.Height * 0.95f; em >= minEm; em -= 0.5f)
        {
            var font = new Font("Segoe UI", em, FontStyle.Bold, GraphicsUnit.Pixel);
            var measured = g.MeasureString(digits, font);
            if (measured.Width <= size.Width - margin) return font;
            font.Dispose();
        }

        return new Font("Segoe UI", minEm, FontStyle.Bold, GraphicsUnit.Pixel);
    }
}
