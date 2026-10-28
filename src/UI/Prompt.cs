using System.Windows;
using System.Windows.Controls;
using Clothesline.Core;

namespace Clothesline.UI;

/// <summary>A small Fluent dialog that asks for one line of text.</summary>
internal static class Prompt
{
    /// <summary>Returns the trimmed text, or null when cancelled or left empty.</summary>
    public static string? Ask(string title, string label, string initial, Window? owner)
    {
        var w = new Window
        {
            Title = title, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
            FontFamily = Theme.Font, UseLayoutRounding = true,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Owner = owner, Topmost = owner == null, // above the line when there is no panel to sit on
        };
        w.SetResourceReference(Control.ForegroundProperty, "J.Text");
        Theme.StyleWindow(w);

        var box = Ui.TextBox(label, initial);
        box.Width = 320;
        box.MaxLength = 60;
        // The label is already shown above the box and is its accessible name; as help text too, Narrator reads it twice.
        System.Windows.Automation.AutomationProperties.SetHelpText(box, "");
        box.Margin = new Thickness(0, 8, 0, 20);
        string? result = null;
        var save = Ui.Button(L.T("common.save"), () => { result = box.Text.Trim(); w.DialogResult = true; }, Ui.Kind.Accent);
        save.IsDefault = true;
        save.MinWidth = 96;
        save.Margin = new Thickness(0, 0, 8, 0);
        var cancel = Ui.Button(L.T("common.cancel"), () => { }); // IsCancel closes the dialog
        cancel.IsCancel = true;
        cancel.MinWidth = 96;
        void sync() => save.IsEnabled = box.Text.Trim().Length > 0;
        box.TextChanged += (_, _) => sync();
        sync();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        var root = new StackPanel { Margin = new Thickness(24, 20, 24, 24) };
        root.Children.Add(Ui.Text(label));
        root.Children.Add(box);
        root.Children.Add(buttons);
        w.Content = root;
        w.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        w.ShowDialog();
        return string.IsNullOrEmpty(result) ? null : result;
    }
}
