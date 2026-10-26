# Self-Update Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Jemur downloads, verifies and installs new releases by itself at a quiet moment, then restarts. This works for both the installed app and the portable EXE. Release it as 0.4.0.

**Architecture:**
- **Pure rules (`src/Core/UpdatePackage.cs`, WPF-free):** package choice, hash check, quiet-moment test, the helper script and the portable swap. They compile in the portable test project, so they are tested on Linux CI.
- **`src/App/UpdateInstaller.cs`:** stages a download and waits for a quiet moment.
- **Applying:** a hidden PowerShell helper applies an installed update after Jemur exits.
- **`UpdateCoordinator`:** hands available updates to the installer.

**Tech Stack:** C# / WPF .NET 10, `System.Net.Http`, `System.Security.Cryptography.SHA256`, xUnit. No new packages.

**Spec:** `docs/superpowers/specs/2026-10-20-self-update-design.md`

## Global Constraints

- No new dependencies.
- `src/Core/*.cs` files that are compiled into `tests/portable/Jemur.Portable.Tests.csproj` must stay WPF-free and Win32-free.
  - Add `../../src/Core/UpdatePackage.cs` to that project.
  - The release gate is `dotnet test tests/portable/Jemur.Portable.Tests.csproj -c Release`.
- Never run or move a file whose byte count and SHA-256 don't match the manifest.
  - Only accept `https` URLs on the same host as the manifest URL.
  - `size` must be in 1,000,000..300,000,000.
  - `sha256` must be 64 hex characters.
- Staging folder: `%LOCALAPPDATA%\Jemur\updates`. The partial file is `<name>.partial`. Keep at most one staged file.
- Quiet moment means:
  - no overlay (capture, spotlight, mark-up, menu or prompt);
  - at least 2 minutes since the last input;
  - checked by a 1-minute timer that exists only while an update is staged.
- Single-instance mutex: `Jemur.SingleInstance.v1`. Release it before applying.
- Restart arguments: `--background --updated <version>`. Portable leftover file: `<exe>.old`, deleted at startup.
- `Settings.AutoUpdate` is a bool with default `true`. When it is off, behaviour is exactly as in 0.3.0.
- Strings go in all six locales (en, es, id, ja, ms, pt-BR). The en and id values are in the spec.
- Every task must pass:
  - `dotnet build Jemur.sln -c Release -p:EnableWindowsTargeting=true` with 0 warnings;
  - `dotnet test Jemur.sln`;
  - the portable tests.
- Build environment: `DOTNET_ROOT` and `PATH` point to `C:\Users\revan\.codex\tools\dotnet10`.

## Review Focus

1. **The network drops mid-download, or a proxy returns HTML instead of the EXE.** Expected: no staged file, no install, notify-only, and a retry at the next check. Test: `Verify_TruncatedOrWrongFile_LeavesNothingStaged` (Task 1).
2. **A manifest lists a file on another host or over http, perhaps after a DNS hijack.** Expected: rejected. Test: `Select_RejectsForeignHostAndHttp` (Task 1).
3. **A path with spaces or an apostrophe**, for example `C:\Users\O'Neil\AppData\Local\Programs\Jemur`. Expected: the helper script still targets it correctly. Test: `HelperScript_QuotesPathsWithSpacesAndApostrophes` (Task 1).
4. **A portable EXE in a read-only folder.** Expected: the swap fails cleanly, the original EXE stays in place, and notify-only applies. Test: `PortableSwap_FailedMoveRestoresOriginal` (Task 1).
5. **The installed update fails, so the old version restarts.** Expected: the `update.failed` notification and no update loop for that version. Test: `UpdatedArgument_SameVersion_ReportsFailure` (Task 2, pure decision function).

---

### Task 1: Pure update rules

**Files:**
- Create: `src/Core/UpdatePackage.cs`
- Modify:
  - `src/Core/UpdateChecker.cs`: parse `files[]` into `UpdateManifest.Files`.
  - `src/Core/Models.cs`: add `Settings.AutoUpdate = true`.
  - `tests/portable/Jemur.Portable.Tests.csproj`: include the new file and the tests.
- Test: `tests/portable/UpdatePackageTests.cs`

**Interfaces (produces):**
- `record UpdateFile(string Name, string Kind, string Architecture, long Size, string Sha256, Uri Url)`
- `UpdateManifest` gains `IReadOnlyList<UpdateFile> Files`, which may be empty.
- `static class UpdatePackage`:
  - `UpdateFile? Select(UpdateManifest m, Uri manifestUri, string architecture /* "x64"|"arm64" */, bool installed)`
    - kind is `"installer"` when installed, otherwise `"portable"`;
    - the URL must be https on the same host as `manifestUri`, the size must be in range, and the hash must be hex64;
    - returns null otherwise.
  - `Task<bool> VerifyAsync(string path, long size, string sha256, CancellationToken ct)` compares hex case-insensitively.
  - `bool IsQuietMoment(bool overlayOpen, TimeSpan sinceLastInput)` returns true when there is no overlay and the time since input is at least 2 minutes.
  - `string HelperScript(int pid, string setupPath, string exePath, string version)` returns a PowerShell script with three steps:
    1. `Wait-Process -Id <pid> -ErrorAction SilentlyContinue`;
    2. `Start-Process -FilePath '<setup>' -ArgumentList '/S' -Wait`;
    3. `Start-Process -FilePath '<exe>' -ArgumentList '--background','--updated','<version>'`.

    Single quotes inside paths are doubled. Jemur runs it as `powershell.exe -NoProfile -WindowStyle Hidden -EncodedCommand <base64 UTF-16LE>`.
  - `bool PortableSwap(string exePath, string newExePath)`:
    1. rename `exe` to `exe.old` (deleting any existing `.old` first);
    2. move `newExePath` to `exe`;
    3. on failure, put the original back and return false.
  - `void CleanupOld(string exePath)` deletes `exePath + ".old"` and ignores errors.
- `Settings.AutoUpdate`: a missing value in an old `settings.json` stays `true`, which is the property default.

- [ ] **Step 1: Write the failing tests** in `tests/portable/UpdatePackageTests.cs`:
  - `Select_PicksInstallerOrPortableForArch`: the 0.3.0 manifest shape (four files) with x64 installed gives `*-win-x64-setup.exe`, and arm64 portable gives `*-win-arm64.exe`.
  - `Select_RejectsForeignHostAndHttp`.
  - `Select_RejectsBadSizeOrHash`.
  - `Select_ReturnsNullWhenNoFiles`.
  - `Verify_GoodFilePasses`.
  - `Verify_TruncatedOrWrongFile_LeavesNothingStaged`: test the verify result itself; the staging cleanup is in Task 2.
  - `IsQuietMoment_RequiresNoOverlayAndTwoMinutes`: checks 1:59 and 2:00.
  - `HelperScript_QuotesPathsWithSpacesAndApostrophes`: the path `C:\Users\O'Neil\Jemur App\Jemur.exe` appears as `'C:\Users\O''Neil\Jemur App\Jemur.exe'`, and the pid and version appear.
  - `PortableSwap_Succeeds`: in a temp folder, `exe` gets the new bytes and `exe.old` gets the old bytes.
  - `PortableSwap_FailedMoveRestoresOriginal`: the new file is missing, so the method returns false and `exe` still has the old bytes.
  - `CleanupOld_DeletesLeftover`.
  - `Parse_ManifestFiles`: extend `UpdateCheckerTests` so the parsed files match the JSON.
  - `Settings_AutoUpdateDefaultsTrue`.
- [ ] **Step 2:** Run `dotnet test tests/portable/Jemur.Portable.Tests.csproj -c Release`. Expected: FAIL (compile).
- [ ] **Step 3:** Implement the interfaces above.
- [ ] **Step 4:** Run all three gates. Expected: PASS, 0 warnings.
- [ ] **Step 5: Commit** `feat: rules for verified self-updates`.

### Task 2: Staging, quiet-moment apply, restart

**Files:**
- Create: `src/App/UpdateInstaller.cs`
- Modify:
  - `src/App/UpdateCoordinator.cs`: when an update is available and AutoUpdate is on, hand the manifest to the installer and skip the "open Jemur to download" notification.
  - `src/App/Program.cs`: expose a static `ReleaseSingleInstance()` that runs on the UI/main thread and releases the mutex; parse `--updated <v>`.
  - `src/App/JemurApp.cs`: wire everything, show the post-update notification, apply on quit.
  - `src/Interop/Native.cs`: add `GetLastInputInfo`.
  - all `locales/*.json`.

**Interfaces:**
- Consumes: everything from Task 1.
- Produces, on `UpdateInstaller`:
  - `UpdateState State` (`Idle | Downloading | Ready | Failed`), `string? ReadyVersion`, `event Action? Changed`;
  - `Task StageAsync(UpdateManifest m, CancellationToken ct)`: downloads to `updates\<name>.partial`, verifies, then renames the file. Cleanup on failure leaves nothing staged.
  - `void ApplyNow()`: saves the library and settings, releases the mutex, then applies the update. Installed builds use the helper script; portable builds use `PortableSwap` followed by `Process.Start(exe, "--background --updated v")`. Afterwards it shuts the WPF app down. If anything fails before exiting, it logs, sets `Failed`, re-acquires nothing, and keeps running in notify-only mode.
  - The quiet-moment `DispatcherTimer` (1 minute) runs only while `State == Ready` and AutoUpdate is on. It calls `ApplyNow` when `UpdatePackage.IsQuietMoment(app.HasOverlay || menuOpen, idle)`.
- Install detection: `bool IsInstalled()` checks whether `HKCU\Software\Jemur\InstallLocation` equals `Path.GetDirectoryName(Environment.ProcessPath)`, compared as full paths, case-insensitively.
- Architecture is `RuntimeInformation.ProcessArchitecture`: `Arm64` maps to `"arm64"`, anything else to `"x64"`.
- Pure decision `static UpdateOutcome UpdatedOutcome(string? updatedArg, string runningVersion)` returns `None`, `Done` or `Failed`. `Failed` happens when the argument equals a version newer than the running one. After a failure, JemurApp remembers `Settings.SkipAutoUpdateVersion = v` so it does not loop.
- `Settings.SkipAutoUpdateVersion` is a string defaulting to `""`.
- Startup: in portable builds, `CleanupOld`; delete stray files in `updates\`, except a verified staged file for a version newer than the running one.
- Quit: if `Ready`, apply instead of plain exit.
- Locale keys:
  - `update.done` "Jemur updated to {0}" / "Jemur diperbarui ke {0}"
  - `update.failed` "Couldn't update Jemur. Download it from the release page." / "Gagal memperbarui Jemur. Unduh dari halaman rilis."

- [ ] **Step 1:** Write failing tests in `tests/UpdateInstallerTests.cs` (Windows test project):
  - `UpdatedArgument_SameVersion_ReportsFailure`;
  - `UpdatedArgument_Newer_ReportsDone`;
  - `StageAsync_BadHash_LeavesNothingStaged`, using a fake `HttpMessageHandler` and a temp `updates` folder;
  - `StageAsync_GoodFile_IsReady`.
- [ ] **Step 2:** Implement. Make the HttpClient and the folder injectable for the tests.
- [ ] **Step 3:** Run the three gates. Expected: PASS, 0 warnings.
- [ ] **Step 4: Commit** `feat: install verified updates when Jemur is idle`.

### Task 3: Settings and About

**Files:** `src/Windows/ControlPanel.cs`, `src/App/JemurApp.cs`, all `locales/*.json`

- [ ] **Step 1:** Settings card with switch `settings.autoUpdate` and `settings.autoUpdateDesc`, bound to `Settings.AutoUpdate`. Turning it off stops the timer and keeps the staged file. Turning it on with a staged file restarts the timer.
- [ ] **Step 2:** About status row follows `UpdateInstaller.State`:
  - `about.downloading` "Downloading {0}…" / "Mengunduh {0}…"
  - `about.readyToInstall` "{0} is ready and installs when you're away" / "{0} siap, dipasang saat Anda tidak memakai Jemur"
  - button `about.restartToUpdate` "Restart to update" / "Mulai ulang untuk update", which calls `ApplyNow`.

  With AutoUpdate off, or in the `Failed` state, keep the 0.3.0 "Download installer" button.
- [ ] **Step 3:** The page refreshes on `UpdateInstaller.Changed`, through the dispatcher.
- [ ] **Step 4:** Run the three gates. Expected: PASS.
- [ ] **Step 5: Commit** `feat: update settings and status in Jemur`.

### Task 4: Docs and release 0.4.0

- [ ] **Step 1:** README: add a short "Updates" section (automatic, verified, installs when idle; can be turned off in Settings) and correct any "never updates itself" wording, including in `docs/CI-CD.md`.
- [ ] **Step 2:** CHANGELOG `## [0.4.0] - 2026-10-20`, with the upgrade note "0.3.0 and older update by hand once."
- [ ] **Step 3:** Set `<Version>0.4.0</Version>`.
- [ ] **Step 4:** Run the three gates. Expected: PASS.
- [ ] **Step 5: Commit** `Release 0.4.0 with automatic updates`.
