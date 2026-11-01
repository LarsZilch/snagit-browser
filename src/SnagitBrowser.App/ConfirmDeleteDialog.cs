using System.Windows;
using System.Windows.Controls;

namespace SnagitBrowser.App;

/// <summary>Rückfrage vor dem Löschen mit Option „Nicht mehr fragen“.</summary>
public static class ConfirmDeleteDialog
{
    public static bool Ask(Window? owner, string message, out bool dontAskAgain)
    {
        var check = new CheckBox { Content = Loc.T("Del_DontAsk"), Margin = new Thickness(0, 14, 0, 0) };
        var yes = new Button { Content = Loc.T("Del_Yes"), IsDefault = true, Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 8, 0) };
        var no = new Button { Content = Loc.T("Common_Cancel"), IsCancel = true, Padding = new Thickness(14, 4, 14, 4) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(yes);
        buttons.Children.Add(no);

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 460 });
        panel.Children.Add(new TextBlock
        {
            Text = Loc.T("Del_Restore"),
            Foreground = System.Windows.Media.Brushes.Gray, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 460,
        });
        panel.Children.Add(check);
        panel.Children.Add(buttons);

        var win = new Window
        {
            Title = Loc.T("Common_Delete"), Content = panel, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Owner = owner, ShowInTaskbar = false, FontSize = 13,
        };
        yes.Click += (_, _) => win.DialogResult = true;
        win.Loaded += (_, _) => yes.Focus();
        bool ok = win.ShowDialog() == true;
        dontAskAgain = ok && check.IsChecked == true;
        return ok;
    }
}
