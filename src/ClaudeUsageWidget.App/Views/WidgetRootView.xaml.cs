using System.Windows;
using System.Windows.Controls;
using ClaudeUsageWidget.Core;
// UseWindowsForms делает System.Windows.Forms глобально видимым —
// UserControl существует и там под тем же именем.
using UserControl = System.Windows.Controls.UserControl;

namespace ClaudeUsageWidget.App.Views;

/// <summary>
/// Плашка «цифрам нельзя доверять» — App-слойный аналог BlockingNotice.swift.
/// Core её не портирует (см. task-14-brief.md), поэтому текстовые правила
/// живут здесь же, рядом с тем, что их показывает.
/// </summary>
public sealed record WidgetNotice(string Title, string Detail, bool ShowSignIn);

/// <summary>
/// Панель виджета: сетка 2x2 циферблатов + строка статуса + плашка
/// BlockingNotice + hover-хедер (глаз/замок). Порт
/// <c>Sources/ClaudeUsageWidgetCore/Views/WidgetRootView.swift</c>.
/// </summary>
public partial class WidgetRootView : UserControl
{
    /// Кнопка-глаз в hover-хедере.
    public event Action? HideRequested;

    /// Кнопка Sign in в плашке NoCredentials — в оригинальном
    /// BlockingNotice.swift такой кнопки нет вовсе (там только текст со
    /// ссылкой на меню), но бриф Task 14 явно просит именно кнопку.
    public event Action? SignInRequested;

    /// Замок в hover-хедере. Здесь только уведомление о клике — источник
    /// истины для PositionLocked живёт в DesktopWidgetWindow (публичное
    /// свойство из интерфейса задачи), эта view лишь отображает то, что ей
    /// сказали через <see cref="PositionLocked"/>.
    public event Action? LockToggleRequested;

    private bool _positionLocked;

    public bool PositionLocked
    {
        get => _positionLocked;
        set
        {
            _positionLocked = value;
            UpdateLockIcon();
        }
    }

    public WidgetRootView()
    {
        InitializeComponent();

        RootGrid.MouseEnter += (_, _) => SetHovering(true);
        RootGrid.MouseLeave += (_, _) => SetHovering(false);

        UpdateLockIcon();
    }

    /// <summary>
    /// Пересчитывает всю геометрию под новую сторону панели. В Swift это
    /// происходит реактивно на каждый рендер через вычисляемые свойства
    /// scale/pad/gap/dialSize; здесь вызывается явно из
    /// DesktopWidgetWindow — при создании окна и на каждом изменении
    /// размера при resize.
    /// </summary>
    public void ApplyLayout(double side)
    {
        var scale = side / WidgetSettings.DefaultSide;
        var pad = Theme.Padding(scale);
        var gap = Theme.Gap(scale);
        var corner = Theme.CornerRadius(scale);
        var dialSize = Math.Max(0, (side - pad * 2 - gap) / 2);

        Width = side;
        Height = side;
        RootGrid.Width = side;
        RootGrid.Height = side;

        PanelBorder.CornerRadius = new CornerRadius(corner);
        NoticeBorder.CornerRadius = new CornerRadius(corner);
        // Только верхние углы — хедер сидит поверх верхней кромки панели,
        // WidgetRootView.swift:151-159.
        HeaderBorder.CornerRadius = new CornerRadius(corner, corner, 0, 0);

        DialGrid.Margin = new Thickness(pad);
        SessionDial.Margin = new Thickness(0);
        WeekDial.Margin = new Thickness(gap, 0, 0, 0);
        ModelDial.Margin = new Thickness(0, gap, 0, 0);
        StatusDial.Margin = new Thickness(gap, gap, 0, 0);

        SessionDial.Width = SessionDial.Height = dialSize;
        WeekDial.Width = WeekDial.Height = dialSize;
        ModelDial.Width = ModelDial.Height = dialSize;
        StatusDial.Width = StatusDial.Height = dialSize;

        // WidgetRootView.swift:144-148: горизонталь 10*scale, верх 10*scale, низ 5*scale.
        HeaderGrid.Margin = new Thickness(10 * scale, 10 * scale, 10 * scale, 5 * scale);
        EyeButton.Width = EyeButton.Height = 16 * scale;
        LockButton.Width = LockButton.Height = 16 * scale;
        EyeButton.FontSize = 9 * scale;
        LockButton.FontSize = 9 * scale;

        // .padding(.top, 31*scale) — освобождает место под хедер, который
        // при наведении рисуется поверх плашки. WidgetRootView.swift:199.
        NoticeStack.Margin = new Thickness(pad, 31 * scale, pad, pad);
        NoticeTitleText.FontSize = Theme.ValueFontSize(scale);
        NoticeDetailText.FontSize = Theme.CaptionFontSize(scale);
        NoticeDetailText.Margin = new Thickness(0, 5 * scale, 0, 0);
        SignInButton.Margin = new Thickness(0, 8 * scale, 0, 0);
        SignInButton.FontSize = Theme.CaptionFontSize(scale);

        StatusLineText.FontSize = Theme.CaptionFontSize(scale);
        StatusLineText.Margin = new Thickness(0, 0, 0, 2 * scale);
    }

    /// Заполняет циферблаты, статус-циферблат, строку статуса и плашку —
    /// порт тела WidgetRootView.swift:46-96.
    public void SetContent(IReadOnlyList<DialModel> models, ServiceStatus status, bool dimmed, string? statusLine, WidgetNotice? notice)
    {
        ApplyDial(SessionDial, models[0], dimmed);
        ApplyDial(WeekDial, models[1], dimmed);
        ApplyDial(ModelDial, models[2], dimmed);

        StatusDial.Status = status;
        StatusDial.Dimmed = dimmed;

        StatusLineText.Text = statusLine ?? string.Empty;
        StatusLineText.Visibility = string.IsNullOrEmpty(statusLine) ? Visibility.Collapsed : Visibility.Visible;

        if (notice is null)
        {
            NoticeBorder.Visibility = Visibility.Collapsed;
            return;
        }

        NoticeTitleText.Text = notice.Title;
        NoticeDetailText.Text = notice.Detail;
        SignInButton.Visibility = notice.ShowSignIn ? Visibility.Visible : Visibility.Collapsed;
        NoticeBorder.Visibility = Visibility.Visible;
    }

    private static void ApplyDial(DialControl dial, DialModel model, bool dimmed)
    {
        dial.Title = model.Title;
        dial.Fraction = model.Fraction;
        dial.Remaining = model.Remaining;
        dial.Dimmed = dimmed;
    }

    private void SetHovering(bool hovering)
    {
        HeaderBorder.Opacity = hovering ? 1 : 0;
        // Прозрачность одна не убирает кликабельность кнопок: невидимый
        // EyeButton/LockButton перехватывал бы клик, предназначенный для
        // drag, и тихо прятал бы виджет без видимой причины. Порт
        // .allowsHitTesting(hovering) — WidgetRootView.swift:164.
        HeaderBorder.IsHitTestVisible = hovering;
    }

    private void UpdateLockIcon()
    {
        // Segoe MDL2 Assets: E72E "Lock", E785 "Unlock" — порт lock.fill/lock.open
        // (SF Symbols). Подобранный по официальной таблице глифов MDL2, т.к. в
        // этом наборе нет отдельного "eye.slash" для кнопки-глаза (см. EyeButton
        // в WidgetRootView.xaml — там используется View/E890 как ближайший аналог).
        LockButton.Content = _positionLocked ? "" : "";
        LockButton.Foreground = _positionLocked ? Theme.WarningBrush : Theme.DimBrush;
        LockButton.ToolTip = _positionLocked
            ? "Position and size are locked — click to unlock"
            : "Click to lock the widget position and size";
    }

    private void EyeButton_Click(object sender, RoutedEventArgs e) => HideRequested?.Invoke();

    private void LockButton_Click(object sender, RoutedEventArgs e) => LockToggleRequested?.Invoke();

    private void SignInButton_Click(object sender, RoutedEventArgs e) => SignInRequested?.Invoke();
}
