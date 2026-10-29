using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Controls;
using Clothesline.Core;
using Clothesline.Interop;
using Clothesline.UI;

namespace Clothesline.Windows;

/// <summary>
/// Watches global mouse drags: when the user drags a window by its title bar toward the
/// top of the screen, the clothesline is summoned and a "hangable" slot appears on the right
/// side of the line. Releasing the mouse in the hot zone hangs the live window thumbnail.
/// </summary>
internal sealed class DragAttachWatcher : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202;
    private const int WM_NCHITTEST = 0x0084, HTCAPTION = 2;
    private const int HotZoneHeightPx = 90;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public Native.POINT pt;
        public int mouseData;
        public int flags;
        public int time;
        public IntPtr dwExtraInfo;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookExW(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private readonly LowLevelMouseProc _proc;
    private IntPtr _hook;
    private bool _tracking;
    private IntPtr _dragHwnd;
    private bool _inHotZone;
    private readonly HangSlot _slot;
    private readonly Action<IntPtr> _onAttach;
    private readonly Action? _onEnterHotZone;
    private readonly Action? _onLeaveHotZone;

    /// <param name="onAttach">Called with the dragged window's HWND when the mouse is released in the hot zone.</param>
    /// <param name="onEnterHotZone">Called when a drag enters the top hot zone (to summon the clothesline).</param>
    /// <param name="onLeaveHotZone">Called when a drag leaves the hot zone without releasing.</param>
    public DragAttachWatcher(Action<IntPtr> onAttach, Action? onEnterHotZone = null, Action? onLeaveHotZone = null)
    {
        _onAttach = onAttach;
        _onEnterHotZone = onEnterHotZone;
        _onLeaveHotZone = onLeaveHotZone;
        _proc = HookCallback;
        _slot = new HangSlot();
    }

    public void Start()
    {
        if (_hook != IntPtr.Zero) return;
        _hook = SetWindowsHookExW(WH_MOUSE_LL, _proc, IntPtr.Zero, 0);
    }

    public void Stop()
    {
        if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
        _slot.Close();
        if (_inHotZone) { _inHotZone = false; _onLeaveHotZone?.Invoke(); }
        _tracking = false; _dragHwnd = IntPtr.Zero;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var msg = (int)wParam;
            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            try
            {
                if (msg == WM_LBUTTONDOWN)
                {
                    var under = Native.WindowFromPoint(data.pt);
                    var hwnd = Native.GetAncestor(under, Native.GA_ROOT);
                    if (hwnd != IntPtr.Zero && hwnd != Native.Handle(_slot) && Native.IsWindowVisible(hwnd))
                    {
                        // Windows 11 apps (Notepad, Explorer, Settings, anything WinUI) put a separate
                        // child window over the title bar and answer HTCAPTION themselves, while the top
                        // level window answers HTCLIENT for the same point. Ask the window under the
                        // cursor first, then fall back to the top level one.
                        IntPtr lparam = (IntPtr)((data.pt.Y << 16) | (data.pt.X & 0xFFFF));
                        if (IsCaption(under, lparam) || (under != hwnd && IsCaption(hwnd, lparam)))
                        {
                            _tracking = true;
                            _dragHwnd = hwnd;
                            Log.Info($"DragAttach: started tracking hwnd={hwnd}");
                        }
                    }
                }
                else if (msg == WM_MOUSEMOVE && _tracking)
                {
                    var work = Native.MonitorAt(data.pt, workArea: true);
                    bool hot = data.pt.Y <= work.Top + HotZoneHeightPx;
                    if (hot != _inHotZone)
                    {
                        _inHotZone = hot;
                        Log.Info($"DragAttach: hot zone {hot} at y={data.pt.Y}");
                        if (hot)
                        {
                            _onEnterHotZone?.Invoke();
                            _slot.ShowFor(_dragHwnd, work);
                        }
                        else
                        {
                            _slot.Hide();
                            _onLeaveHotZone?.Invoke();
                        }
                    }
                }
                else if (msg == WM_LBUTTONUP)
                {
                    bool wasHot = _inHotZone;
                    if (_tracking && wasHot && _dragHwnd != IntPtr.Zero && Native.IsWindow(_dragHwnd))
                    {
                        _slot.Hide();
                        var h = _dragHwnd;
                        _ = System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => _onAttach(h)));
                    }
                    if (wasHot) _onLeaveHotZone?.Invoke();
                    _tracking = false; _inHotZone = false; _dragHwnd = IntPtr.Zero;
                    _slot.Hide();
                }
            }
            catch (Exception ex) { Log.Warn("DragAttach hook error", ex); }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static bool IsCaption(IntPtr hwnd, IntPtr screenPoint) =>
        hwnd != IntPtr.Zero && SendMessage(hwnd, WM_NCHITTEST, IntPtr.Zero, screenPoint).ToInt32() == HTCAPTION;

    public void Dispose() => Stop();

    /// <summary>
    /// A "hangable" slot that appears on the right side of the line when a window is dragged into the
    /// hot zone. It looks like an empty polaroid card with a clothespin and a dashed drop target — the
    /// visual cue that says "drag here and let go to hang this window".
    /// </summary>
    private sealed class HangSlot : Window
    {
        private const double CardW = 168, CardH = 116, PinH = 30;
        private readonly TextBlock _title;
        private readonly Border _card;

        public HangSlot()
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            IsHitTestVisible = false;
            Width = CardW + 24; Height = CardH + PinH + 24;

            // clothespin (matches CardView's pin look)
            var pin = new Grid { Width = 12, Height = PinH, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 0, 0) };
            var wood = Theme.Freeze(new LinearGradientBrush(Color.FromRgb(0xE4, 0xB4, 0x80), Color.FromRgb(0xB5, 0x7A, 0x42), 0));
            var woodEdge = Theme.Freeze(new SolidColorBrush(Color.FromArgb(0x55, 0x4A, 0x2C, 0x10)));
            pin.Children.Add(new Border { Background = wood, CornerRadius = new CornerRadius(3, 3, 2, 2), BorderBrush = woodEdge, BorderThickness = new Thickness(0.75) });
            pin.Children.Add(new Border { Width = 1, Background = woodEdge, Margin = new Thickness(0, 3, 0, 3) });

            // the empty polaroid with a dashed drop hint
            _title = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x6B)), FontSize = 11, FontWeight = FontWeights.SemiBold,
                FontFamily = Theme.Font, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(6), Text = "松开鼠标\n挂起此窗口",
            };
            _card = new Border
            {
                Width = CardW, Height = CardH, Background = Brushes.White, CornerRadius = new CornerRadius(2),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2B, 0x7A, 0xD2)), BorderThickness = new Thickness(2),
                Margin = new Thickness(0, PinH - 12, 0, 0), VerticalAlignment = VerticalAlignment.Top,
                Child = _title,
            };

            var root = new Grid();
            root.Children.Add(_card);
            root.Children.Add(pin);
            Content = root;
        }

        public void ShowFor(IntPtr hwnd, Native.RECT work)
        {
            try
            {
                int len = Native.GetWindowTextLength(hwnd);
                var buf = new char[Math.Max(1, len + 1)];
                Native.GetWindowText(hwnd, buf, buf.Length);
                string title = new string(buf).TrimEnd('\0', ' ');
                // The work area arrives in physical pixels while WPF positions windows in device
                // independent pixels: divide by the monitor scale or the slot lands off screen.
                double scale = Native.ScaleAt(new Point(work.Left + 1, work.Top + 1));
                Left = work.Right / scale - Width - 24;
                Top = work.Top / scale + 8;
                _title.Text = string.IsNullOrWhiteSpace(title)
                    ? L.T("dragAttach.hint")
                    : L.F("dragAttach.title", title.Length > 60 ? title[..59] + "…" : title);
                Show();
                Log.Info($"DragAttach: slot {Left:0},{Top:0} {Width:0}x{Height:0} work={work.Left},{work.Top},{work.Right},{work.Bottom}");
            }
            catch (Exception ex) { Log.Warn("DragAttach: slot failed", ex); }
        }
    }
}
