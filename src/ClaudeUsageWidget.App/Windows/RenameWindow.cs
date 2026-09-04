using System.Windows;
// UseWindowsForms makes System.Windows.Forms visible globally, and all these
// names exist there too — without aliases each of them is ambiguous.
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;
using StackPanel = System.Windows.Controls.StackPanel;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace ClaudeUsageWidget.App.Windows;

/// A single input field for renaming an account.
///
/// A window of its own, not a WinForms InputBox or MessageBox: WPF has
/// neither, and pulling in Microsoft.VisualBasic for one string is worse than
/// the thirty lines here.
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

    /// Null — cancelled, or an empty string was entered.
    public static string? Ask(string current)
    {
        var window = new RenameWindow(current);
        return window.ShowDialog() == true ? window._input.Text : null;
    }
}
