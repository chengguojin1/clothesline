# Jemur: updates itself

## Intent

The owner wants Jemur to update itself. Since 0.1.4 Jemur has only checked for a
new release and opened the release page. The user has to download and run the
installer by hand.

This design replaces that with an automatic update. It keeps the safety
properties that made the original design refuse to update itself: an update is
verified before it runs, and the user never loses work.

## Goals

- When a newer release exists, Jemur downloads the right package in the
  background, verifies it, and installs it on its own.
- The update happens at a quiet moment, never in the middle of a capture, a note
  being written, mark-up, or active use. Jemur comes back by itself afterwards,
  with every card, pin, size, mode and line exactly as before. A tray
  notification says "Jemur updated to X".
- Both packages update themselves: the installed app (Setup) and the portable EXE.
- Settings gets an **Update automatically** switch, on by default. Switched off,
  Jemur behaves as in 0.3.0 (it notifies and links to the release page).
- About keeps "Check for updates". When an update is downloaded and waiting, the
  About button becomes **Restart to update** (`Mulai ulang untuk update`).

## Non-goals

- Code signing, delta or partial updates, update channels, or rolling back to an
  older version.
- Updating while another Jemur window or dialog is open.

## Anti-goals

- Running a file that does not match the published SHA-256.
- Downloading from anywhere other than the Jemur download host.
- Losing user data, or leaving the user without a working Jemur: no running
  copy, no EXE.
- Interrupting the user, stealing focus, or showing an installer window.
- Idle cost: a single timer may exist only while an update is downloaded and
  waiting for a quiet moment.

## How it works

### 1. Find the package

- The update checker already fetches `manifest.json`. It also reads
  `files[]`: `name`, `variant`, `kind`, `architecture`, `size`, `sha256`, `url`.
- Pick the file whose architecture matches the running process (x64 or arm64) and
  whose kind matches how Jemur is running:
  - **Installed:** the EXE runs from the folder recorded in
    `HKCU\Software\Jemur\InstallLocation`, compared case-insensitively as a full
    path. Use the `installer` file.
  - **Portable:** anything else. Use the `portable` file.
- Reject the update (fall back to notify-only) when:
  - the file is missing;
  - the URL is not `https` on the same host as the manifest URL;
  - `size` is not between 1 MB and 300 MB;
  - `sha256` is not 64 hexadecimal characters.

### 2. Download and verify

- Download to `%LOCALAPPDATA%\Jemur\updates\<name>.partial` over `HttpClient`,
  streaming to disk with a timeout of 10 minutes. This runs off the UI thread at
  normal priority and starts after the existing check finds the update.
- Check the byte count and SHA-256 against the manifest. On a match, rename the
  file to `<name>`. On a mismatch or error, delete it, log it, fall back to
  notify-only for this version, and try again at the next 24-hour check.
- Keep at most one staged update. Delete other files in `updates\` at startup and
  when staging a new one.

### 3. Wait for a quiet moment

An update is applied when all of these are true:

- no capture, spotlight, mark-up, menu or prompt is open;
- the user has not touched the mouse or keyboard for 2 minutes
  (`GetLastInputInfo`);
- the lines are saved (`Library.SaveNow` succeeds right before applying).

While an update is staged, a 1-minute timer checks these conditions. It stops
once the update is applied or the app quits. **Restart to update** in About (or
in the tray notification) applies it at once, skipping the idle wait but still
saving first.

If Jemur quits normally with an update staged, the update is applied on quit
instead of at the next quiet moment.

### 4. Apply

**Installed:**

1. Save everything and release the single-instance mutex
   `Jemur.SingleInstance.v1` (the installer refuses to run while it is held).
2. Start a hidden `cmd.exe` that does three things in order:
   - waits for this process to exit;
   - runs the installer silently: `"<setup>" /S`;
   - starts `"<InstallLocation>\Jemur.exe" --background --updated <version>`.
3. Exit.

The installer is the existing NSIS one, which is per-user and needs no admin. It
keeps the user's data, shortcuts and startup entry.

**Portable:**

1. Save everything and release the mutex.
2. Rename the running `<exe>` to `<exe>.old`. Windows allows renaming a running
   EXE.
3. Move the verified new EXE to `<exe>`.
4. Start `<exe> --background --updated <version>`, then exit.

If step 2 or 3 fails (for example the folder is read-only), undo the rename,
fall back to notify-only and log it. At startup, a portable Jemur deletes
`<exe>.old` if it exists.

**After restart:** `--updated <version>` makes Jemur show a tray notification,
`update.done` "Jemur updated to {0}" / "Jemur diperbarui ke {0}". If the
restarted Jemur is still the old version (the install failed), it shows
`update.failed` "Couldn't update Jemur. Download it from the release page." /
"Gagal memperbarui Jemur. Unduh dari halaman rilis." Jemur stays notify-only
for that version until the next release.

### 5. Settings and About

- Add `Settings.AutoUpdate` (bool, default true) and a Settings card
  `settings.autoUpdate` "Update automatically" / "Update otomatis", with
  description `settings.autoUpdateDesc` "Download, check and install new versions
  when you're not using Jemur" / "Unduh, periksa dan pasang versi baru saat
  Jemur tidak dipakai".
- About shows the status:
  - "Downloading 0.3.1…" / "Mengunduh 0.3.1…";
  - "0.3.1 is ready and installs when you're away" / "0.3.1 siap, dipasang saat
    Anda tidak memakai Jemur";
  - plus the **Restart to update** button (`about.restartToUpdate`).
- With auto update off, everything stays as in 0.3.0.

## Testing

Pure logic gets tests, and some of them run in the portable test project:

- Package selection: arch × kind × manifest shapes, the host check, and the size
  and hash format checks.
- Verification: a correct file passes; a wrong hash, a wrong size or a truncated
  file fails and leaves no staged file.
- Quiet-moment rule: the decision function given overlay state and idle time.
- Building the `cmd.exe` argument string: paths with spaces and quotes are
  quoted correctly.
- Portable swap, using a temporary folder: success; a failed move restores the
  original file name; and the `.old` file is deleted at startup.
- Settings normalization: `AutoUpdate` stays true for old settings files.

Manual checks (in the report): installed 0.4.0 updating to a test build;
portable updating; auto update off; an unplugged network mid-download.

## Release

- Version 0.4.0, with a README section on updates and a CHANGELOG entry.
- Users of 0.3.0 and older still update by hand once. Self-update starts working
  from 0.4.0 onward.

## Left to the builder

- Class layout (for example `UpdateInstaller` next to `UpdateChecker`).
- Exact wording of es, ja, ms and pt-BR.
- How the About status row is laid out.
