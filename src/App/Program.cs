using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Clothesline.Core;

namespace Clothesline.App;

public static class Program
{
    private const string MutexName = "Clothesline.SingleInstance.v1";
    internal const string ShowPanelEvent = "Clothesline.ShowPanel.v1";
    private static Mutex? _instance;

    [STAThread]
    public static int Main(string[] args)
    {
        bool tool = args.Contains("--render-docs");
        int u = Array.IndexOf(args, "--updated");
        var updated = u >= 0 && u + 1 < args.Length ? args[u + 1] : null;
        if (updated != null) WaitForOldCopy();
        using var mutex = new Mutex(true, tool ? MutexName + ".tool" : MutexName, out bool first);
        if (!first)
        {
            // Already running: bring up its control panel instead of starting a second copy.
            try { using var ev = EventWaitHandle.OpenExisting(ShowPanelEvent); ev.Set(); } catch { }
            return 0;
        }
        if (!tool) _instance = mutex;

        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unhandled exception", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Error("Unobserved task exception", e.Exception); e.SetObserved(); };

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += OnDispatcherException;
        int r = Array.IndexOf(args, "--render-docs");
        var host = new ClotheslineApp(app, background: args.Contains("--background"), renderDocs: r >= 0 && r + 1 < args.Length ? args[r + 1] : null, updated: updated);
        app.Startup += (_, _) => host.Start();
        app.Exit += (_, _) => host.Stop();
        return app.Run();
    }

    /// <summary>
    /// Lets go of the single-instance mutex so an installer or the new copy can run. Mutexes belong to a thread:
    /// call this on the UI thread, which is the thread that took it in <see cref="Main"/>.
    /// </summary>
    internal static void ReleaseSingleInstance()
    {
        if (_instance == null) return;
        _instance.ReleaseMutex();
        _instance = null;
    }

    /// <summary>
    /// After an update the old copy is still shutting down when the new one starts; it holds the shortcuts and may be
    /// writing items.json. Give copies of Clothesline in this session up to 10 seconds to exit.
    /// </summary>
    private static void WaitForOldCopy()
    {
        using var self = Process.GetCurrentProcess();
        foreach (var p in Process.GetProcessesByName(self.ProcessName))
        {
            using (p)
            {
                try { if (p.Id != self.Id && p.SessionId == self.SessionId) p.WaitForExit(10_000); }
                catch (Exception ex) { Log.Warn("Could not wait for the old copy of Clothesline", ex); }
            }
        }
    }

    private static int _crashes;

    /// <summary>Log and keep going: a tray utility disappearing silently is worse than one glitchy click.</summary>
    private static void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("UI exception", e.Exception);
        e.Handled = ++_crashes < 20;
    }
}
