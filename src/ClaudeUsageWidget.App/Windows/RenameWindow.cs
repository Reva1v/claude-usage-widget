using System.Windows;
// UseWindowsForms делает System.Windows.Forms видимым глобально, и все эти
// имена существуют там же — без алиасов каждое из них неоднозначно.
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;
using StackPanel = System.Windows.Controls.StackPanel;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace ClaudeUsageWidget.App.Windows;

/// Одно поле ввода для переименования аккаунта.
///
/// Своё окно, а не WinForms InputBox или MessageBox: тех в WPF нет, а тянуть
/// Microsoft.VisualBasic ради одной строки — хуже, чем тридцать строк здесь.
public sealed class RenameWindow : Window
{
    private readonly TextBox _input;

    private RenameWindow(string current)
    {
        Title = "Rename account";
        Width = 320;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;

        _input = new TextBox { Text = current, Margin = new Thickness(12, 12, 12, 6) };
        _input.SelectAll();

        var ok = new Button { Content = "Rename", IsDefault = true, Width = 90, Margin = new Thickness(6) };
        ok.Click += (_, _) => { DialogResult = true; };

        var cancel = new Button { Content = "Cancel", IsCancel = true, Width = 90, Margin = new Thickness(6) };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(6),
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var stack = new StackPanel();
        stack.Children.Add(_input);
        stack.Children.Add(buttons);
        Content = stack;

        Loaded += (_, _) => { _input.Focus(); _input.SelectAll(); };
    }

    /// Null — отменено или введена пустая строка.
    public static string? Ask(string current)
    {
        var window = new RenameWindow(current);
        return window.ShowDialog() == true ? window._input.Text : null;
    }
}
