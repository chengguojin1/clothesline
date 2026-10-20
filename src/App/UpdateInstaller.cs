using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using Jemur.Core;
using Jemur.Interop;
using Microsoft.Win32;

namespace Jemur.App;

internal enum UpdateState { Idle, Downloading, Ready, Failed }

internal enum UpdateOutcome { None, Done, Failed }

/// <summary>What the installer needs from the running app. Tests leave it out: nothing is ever applied then.</summary>
/// <param name="Allowed">Whether this version may install itself (auto update on, not a version that failed before).</param>
/// <param name="Busy">A capture, spotlight, mark-up, menu or prompt is open.</param>
/// <param name="Save">Saves the lines and settings; false when the lines could not be written.</param>
/// <param name="Exit">Shuts the app down once the update has been handed over.</param>
/// <param name="Fallback">Staging or applying failed: tell the user the 0.3.0 way. True asks to stop retrying this version.</param>
internal sealed record UpdateHost(Dispatcher Dispatcher, Func<string, bool> Allowed, Func<bool> Busy, Func<bool> Save, Action Exit, Action<UpdateManifest, bool> Fallback);

/// <summary>
/// Downloads a verified update to <c>%LOCALAPPDATA%\Jemur\updates</c>, then installs it at a quiet moment (or on quit):
/// the installed build hands the setup to a hidden helper that waits for this process to exit; the portable build
/// swaps its own EXE and starts the new one.
/// </summary>
internal sealed class UpdateInstaller : IDisposable
{
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan QuietCheck = TimeSpan.FromMinutes(1);

    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jemur", "updates");

    public static string Architecture => RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64";

    private readonly HttpClient _http;
    private readonly string _folder;
    private readonly Uri _manifestUri;
    private readonly bool _installed;
    private readonly UpdateHost? _host;
    private readonly CancellationTokenSource _life = new();
    private DispatcherTimer? _timer;
    private UpdateManifest? _manifest;
    private UpdateFile? _file;
    private string? _path;

    public UpdateInstaller(HttpClient http, string folder, Uri manifestUri, bool installed, UpdateHost? host)
    {
        _http = http;
        _folder = folder;
        _manifestUri = manifestUri;
        _installed = installed;
        _host = host;
    }

    public UpdateState State { get; private set; }
    public string? ReadyVersion => State == UpdateState.Ready ? _manifest?.Version : null;
    public event Action? Changed;

    /// <summary>True when this EXE runs from the folder the installer recorded, compared as full paths, ignoring case.</summary>
    public static bool IsInstalled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Jemur");
            var dir = Path.GetDirectoryName(Environment.ProcessPath);
            return key?.GetValue("InstallLocation") is string { Length: > 0 } location && dir != null
                && string.Equals(Full(location), Full(dir), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Log.Warn("Could not read the install location", ex);
            return false;
        }

        static string Full(string p) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p));
    }

    /// <summary>
    /// What a <c>--updated &lt;v&gt;</c> restart means: still running a version older than v is a failed install.
    /// The helper ignores the installer's exit code, so this is the only success check.
    /// </summary>
    public static UpdateOutcome UpdatedOutcome(string? updatedArg, string runningVersion)
    {
        if (!SemanticVersion.TryParse(updatedArg, out var wanted) || !SemanticVersion.TryParse(runningVersion, out var running)) return UpdateOutcome.None;
        return running.CompareTo(wanted) < 0 ? UpdateOutcome.Failed : UpdateOutcome.Done;
    }

    /// <summary>
    /// Startup: partial downloads are always stray. A staged file may stay (it is checked again against the next
    /// manifest before it is reused), unless this start follows an update or auto update is off.
    /// </summary>
    public void CleanUp(bool keepStaged)
    {
        try
        {
            if (!Directory.Exists(_folder)) return;
            foreach (var f in Directory.EnumerateFiles(_folder))
                if (!keepStaged || f.EndsWith(".partial", StringComparison.OrdinalIgnoreCase)) TryDelete(f);
        }
        catch (Exception ex) { Log.Warn("Could not clean the updates folder", ex); }
    }

    /// <summary>
    /// Downloads the package for this build to <c>&lt;name&gt;.partial</c>, checks its size and SHA-256, then renames it.
    /// Any failure leaves the folder empty. True when the update is (or is being) staged.
    /// </summary>
    public async Task<bool> StageAsync(UpdateManifest m, CancellationToken ct)
    {
        if (State == UpdateState.Downloading || (State == UpdateState.Ready && ReadyVersion == m.Version)) return true;
        var file = UpdatePackage.Select(m, _manifestUri, Architecture, _installed);
        if (file == null || !IsPlainName(file.Name))
        {
            Log.Warn($"No usable {(_installed ? "installer" : "portable")} package for {Architecture} in the {m.Version} manifest");
            return Fail(m, skip: false);
        }
        StopTimer();
        _manifest = m;
        _file = null;
        _path = null;
        SetState(UpdateState.Downloading);
        var target = Path.Combine(_folder, file.Name);
        bool ok;
        try { ok = await Task.Run(() => DownloadAsync(file, target, ct)); }
        catch (Exception ex) { Log.Warn($"Downloading Jemur {m.Version} failed", ex); ok = false; }
        if (!ok)
        {
            CleanUp(keepStaged: false);
            return Fail(m, skip: false);
        }
        _file = file;
        _path = target;
        Log.Info($"Jemur {m.Version} is downloaded and verified");
        SetState(UpdateState.Ready);
        return true;
    }

    private async Task<bool> DownloadAsync(UpdateFile f, string target, CancellationToken ct)
    {
        Directory.CreateDirectory(_folder);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, _life.Token);
        timeout.CancelAfter(DownloadTimeout);
        if (File.Exists(target) && await UpdatePackage.VerifyAsync(target, f.Size, f.Sha256, timeout.Token))
        {
            foreach (var other in Directory.EnumerateFiles(_folder)) if (!other.Equals(target, StringComparison.OrdinalIgnoreCase)) TryDelete(other);
            return true; // staged before a restart, and still intact
        }
        CleanUp(keepStaged: false); // at most one staged update
        var partial = target + ".partial";
        using (var response = await _http.GetAsync(f.Url, HttpCompletionOption.ResponseHeadersRead, timeout.Token))
        {
            if (!response.IsSuccessStatusCode) { Log.Warn($"Update download: HTTP {(int)response.StatusCode}"); return false; }
            if (response.Content.Headers.ContentLength is long length && length != f.Size) { Log.Warn("Update download has the wrong length"); return false; }
            await using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
            await using var sink = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, timeout.Token)) > 0)
            {
                total += read;
                if (total > f.Size) { Log.Warn("Update download is longer than the manifest says"); return false; }
                await sink.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
            }
        }
        if (!await UpdatePackage.VerifyAsync(partial, f.Size, f.Sha256, timeout.Token)) { Log.Warn("Update download does not match its SHA-256"); return false; }
        File.Move(partial, target, true);
        return true;
    }

    /// <summary>
    /// Saves, releases the single-instance mutex, hands over the update and shuts down. False (and still running,
    /// notify-only) when nothing is staged, it is not allowed, or anything fails before the hand-over.
    /// </summary>
    public bool ApplyNow()
    {
        if (State != UpdateState.Ready || _host == null || _manifest is not { } m || _file is not { } f || _path is not { } path || !_host.Allowed(m.Version)) return false;
        StopTimer();
        // The file has sat on disk since it was checked: check it again right before it runs. Task.Run keeps the
        // hash off this thread's synchronization context, so blocking on it cannot deadlock the UI.
        if (!Task.Run(() => UpdatePackage.VerifyAsync(path, f.Size, f.Sha256, CancellationToken.None)).GetAwaiter().GetResult())
        {
            Log.Warn("The staged update no longer matches its manifest; deleting it");
            CleanUp(keepStaged: false);
            return Fail(m, skip: false);
        }
        try
        {
            if (!_host.Save()) throw new IOException("Could not save the lines before updating");
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Unknown EXE path");
            if (_installed)
            {
                Program.ReleaseSingleInstance(); // the installer refuses to run while it is held
                StartHelper(UpdatePackage.HelperScript(Environment.ProcessId, path, exe, m.Version));
            }
            else
            {
                if (!UpdatePackage.PortableSwap(exe, path)) throw new IOException("Could not replace " + exe);
                Program.ReleaseSingleInstance();
                Start(exe, "--background", "--updated", m.Version);
            }
        }
        catch (Exception ex)
        {
            // The mutex may already be released: a second Jemur could start meanwhile, which beats losing this one.
            Log.Error($"Installing Jemur {m.Version} failed", ex);
            CleanUp(keepStaged: false);
            return Fail(m, skip: true);
        }
        Log.Info($"Handed over to Jemur {m.Version}; exiting");
        _host.Exit();
        return true;
    }

    /// <summary>Starts or stops the quiet-moment timer; it exists only while an allowed update is staged.</summary>
    public void Refresh()
    {
        bool want = State == UpdateState.Ready && _host != null && _manifest != null && _host.Allowed(_manifest.Version);
        if (!want) { StopTimer(); return; }
        _timer ??= new DispatcherTimer(QuietCheck, DispatcherPriority.Background, (_, _) => OnQuietCheck(), _host!.Dispatcher);
    }

    private void OnQuietCheck()
    {
        Refresh();
        if (_timer != null && UpdatePackage.IsQuietMoment(_host!.Busy(), Native.IdleTime())) ApplyNow();
    }

    private void StopTimer()
    {
        _timer?.Stop();
        _timer = null;
    }

    private bool Fail(UpdateManifest m, bool skip)
    {
        _file = null;
        _path = null;
        SetState(UpdateState.Failed);
        _host?.Fallback(m, skip);
        return false;
    }

    private void SetState(UpdateState state)
    {
        if (_host != null && !_host.Dispatcher.CheckAccess()) { _host.Dispatcher.Invoke(() => SetState(state)); return; }
        State = state;
        Refresh();
        Changed?.Invoke();
    }

    /// <summary>Manifest names become file names here: no folders, no tricks, an EXE.</summary>
    private static bool IsPlainName(string name) =>
        name.Length > 4 && name == Path.GetFileName(name) && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    private static void StartHelper(string script)
    {
        // Full path: never whatever powershell.exe happens to be first on PATH.
        var powershell = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
        Start(powershell, "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
    }

    private static void Start(string file, params string[] args)
    {
        var psi = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException(file + " did not start");
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (Exception ex) { Log.Warn("Could not delete " + path, ex); }
    }

    public void Dispose()
    {
        StopTimer();
        _life.Cancel(); // not disposed: a download still finishing may link to it
    }
}
