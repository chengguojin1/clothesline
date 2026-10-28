using System.Runtime;
using System.Windows.Threading;

namespace Clothesline.Interop;

/// <summary>
/// Gives memory back after something big (a full-screen capture, the control panel) goes away.
/// A tray app spends almost all of its life idle, so it should look small in Task Manager while it does.
/// </summary>
internal static class Memory
{
    private static DispatcherTimer? _timer;

    /// <summary>Trim a moment after the UI settles, so animations aren't interrupted.</summary>
    public static void TrimSoon(int delayMs = 1500)
    {
        _timer ??= new DispatcherTimer(DispatcherPriority.ApplicationIdle);
        _timer.Stop();
        _timer.Interval = TimeSpan.FromMilliseconds(delayMs);
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private static void OnTick(object? sender, EventArgs e)
    {
        _timer?.Stop();
        TrimNow();
    }

    /// <summary>Writes heap vs. process numbers to the log when JEMUR_DIAG=1 (see docs/MEMORY.md).</summary>
    public static void Report(string when)
    {
        if (Environment.GetEnvironmentVariable("JEMUR_DIAG") != "1") return;
        var info = GC.GetGCMemoryInfo();
        using var p = System.Diagnostics.Process.GetCurrentProcess();
        Core.Log.Info($"[mem] {when}: managed heap {info.HeapSizeBytes / 1048576.0:F1} MB, committed {info.TotalCommittedBytes / 1048576.0:F1} MB, " +
                      $"private {p.PrivateMemorySize64 / 1048576.0:F1} MB, working set {p.WorkingSet64 / 1048576.0:F1} MB");
    }

    public static void TrimNow()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
        GC.WaitForPendingFinalizers();
        // Ask Windows to page out what we are not using right now; it comes back on demand.
        Native.SetProcessWorkingSetSizeEx(Native.GetCurrentProcess(), -1, -1, 0);
    }
}
