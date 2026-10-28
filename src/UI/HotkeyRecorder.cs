using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Clothesline.Core;

namespace Clothesline.UI;

/// <summary>
/// Click, press a combination, done. Esc cancels, Backspace/Delete clears.
/// The owner decides whether a combination can be registered (another app may already own it).
/// </summary>
internal sealed class HotkeyRecorder : StackPanel
{
    private readonly Button _button;
    private readonly TextBlock _error;
    private readonly Func<Hotkey?, bool> _apply;
    private readonly Action<bool> _recording;
    private Hotkey? _value;
    private bool _listening;
    internal Button InputButton => _button;
    internal bool IsRecording => _listening;
    internal void RefreshLabel() => Render();

    /// <param name="apply">Called with the new shortcut (null = cleared); return false to reject it.</param>
    /// <param name="recording">Lets the owner release global shortcuts while recording, so they can be pressed here.</param>
    public HotkeyRecorder(Hotkey? value, Func<Hotkey?, bool> apply, Action<bool> recording)
    {
        _value = value;
        _apply = apply;
        _recording = recording;
        Orientation = Orientation.Vertical;
        HorizontalAlignment = HorizontalAlignment.Right;

        _button = new Button { MinWidth = 150, HorizontalContentAlignment = HorizontalAlignment.Center };
        _button.SetResourceReference(StyleProperty, "J.Button");
        _button.Click += (_, _) => Start();
        _button.PreviewKeyDown += OnKey;
        _button.LostKeyboardFocus += (_, _) => Stop();
        Children.Add(_button);

        _error = Ui.Text("", Ui.Type.Caption, "J.Critical");
        _error.HorizontalAlignment = HorizontalAlignment.Right;
        _error.Margin = new Thickness(0, 4, 0, 0);
        _error.Visibility = Visibility.Collapsed;
        Children.Add(_error);
        Render();
    }

    private void Start()
    {
        if (_listening) return;
        _listening = true;
        _error.Visibility = Visibility.Collapsed;
        _recording(true);
        _button.Focus();
        Render();
    }

    private void Stop()
    {
        if (!_listening) return;
        _listening = false;
        _recording(false);
        Render();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (!_listening) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Tab && (Keyboard.Modifiers & ~ModifierKeys.Shift) == ModifierKeys.None)
        {
            Stop();
            e.Handled = false;
            return;
        }
        if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None) { Stop(); return; }
        if ((key == Key.Back || key == Key.Delete) && Keyboard.Modifiers == ModifierKeys.None)
        {
            Stop();
            if (_apply(null)) _value = null;
            Render();
            return;
        }
        if (Hotkey.IsModifierKey(key)) return; // wait for the real key
        var hk = new Hotkey(Keyboard.Modifiers, key);
        if (!hk.IsValid)
        {
            Fail(L.T("hotkey.needsModifier"));
            return;
        }
        Stop();
        if (_apply(hk)) _value = hk;
        else Fail(L.T("hotkey.taken"));
        Render();
    }

    private void Fail(string message)
    {
        _error.Text = message;
        _error.Visibility = Visibility.Visible;
    }

    private void Render()
    {
        var label = (System.Windows.Automation.AutomationProperties.GetLabeledBy(_button) as TextBlock)?.Text;
        var value = _listening ? L.T("hotkey.listening") : _value?.ToString() ?? L.T("hotkey.none");
        Ui.AutomationName(_button, label == null ? value : $"{label}: {value}");
        if (_listening) _button.Content = Ui.Text(L.T("hotkey.listening"), Ui.Type.Body, "J.TextSecondary");
        else if (_value is { } v) _button.Content = Ui.KeyCaps(v);
        else _button.Content = Ui.Text(L.T("hotkey.none"), Ui.Type.Body, "J.TextSecondary");
    }
}
