# Changelog

All notable changes are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- Desktop piles: drop a desktop card on another to stack them neatly; dragging any card of a pile carries the whole pile.
- Shake a desktop card while dragging it: a card in a pile shakes loose, a loose card tidies every card on that monitor into rows.
- Screenshots take a note, written in the polaroid's bottom strip.
- Notes take a picture (Ctrl+V, drop a file, or the picture button) and show a preview of the first web address they contain.

## [0.4.0] - 2026-10-20

### Added
- Automatic updates: Jemur downloads a new version, checks its size and SHA-256 against the release manifest, and installs it when you are away or when you quit. The installed build runs the setup; the portable build replaces its own EXE and restarts.
- Settings → Update automatically turns this off. About shows the download and ready status and a Restart to update button.

### Fixed
- A download that fails its checks is deleted and that version is not retried by itself.

### Upgrading
- 0.3.0 and older update by hand once.

## [0.3.0] - 2026-10-20

### Added
- Desktop mode: Alt+Shift+M, the tray menu or Settings → Cards switches every card between the line and free spots on the desktop, above other apps, with no limit.
- Resizable cards on the desktop: drag the corner grip; double-click it to reset. Notes keep their text size and screenshots keep their shape.
- Seven more note colours (Peach, Lilac, Mint, Coral, Lavender, Sand, Cloud), twelve in all.

### Changed
- Spotlight colour swatches wrap into two rows of six.

### Upgrading
- Per-card pins and the 3-pin limit are replaced by desktop mode. Cards you had pinned keep their spot for desktop mode.

## [0.2.0] - 2026-10-19

### Added
- Lines: keep cards on more than one rope. Switch from the name tag, the Control Panel or Alt+Shift+G (next line), and move cards between lines from their menu.
- Groups: gather cards into a named group on a line, move it by its label, and fold it into a stack. Group actions are also under Group in a card's menu, so the Control Panel reaches them with the Apps key or Shift+F10.
- Pin to desktop: up to 3 cards per line float above other apps anywhere on screen, also from an opened card.
- Take everything in: Alt+Shift+J or a left-click on the tray icon lifts every card and pin out of sight, and hangs them back out. The choice is kept across restarts.

### Changed
- A left-click on the tray icon now takes everything in or hangs it back out. Open Jemur from the tray menu or Alt+Shift+P.
- Lifting, hanging out and switching lines stay quick with many cards; every motion is instant when Windows animation effects are off.

### Upgrading
- Older versions can't read lines, groups or pins; downgrading flattens them (a v1 backup is kept as items.v1-backup.json).

## [0.1.4] - 2026-10-18

### Added
- Manual update checks in About and a quiet delayed/24-hour background check.
- Tray notification when a newer verified GitHub release is available.

### Changed
- Update metadata is validated before it can be shown, and downloads remain an explicit user action.

## [0.1.3] - 2026-10-18

### Added
- Spanish, Brazilian Portuguese, Japanese and Malay translations.
- Settings persistence tests for defaults, corrupt and locked files, older settings, normalization and save/load round trips. These also run in Cloudflare's Linux test suite.
- WPF accessibility regression tests for action names, setting labels, search and shortcut recording.

### Fixed
- Expose localized control names, setting descriptions and shortcut values to screen readers.
- Let Tab leave shortcut recording, and let Esc cancel recording without closing the control panel.
- Open library cards with Enter or Space, and show their action buttons during keyboard focus. Shift+F10 opens the item menu.
- Keep the language selector populated when the saved code uses regional casing such as pt-BR.

## [0.1.2] - 2026-10-18

### Changed
- Publish new application versions from Cloudflare with a repository-scoped GitHub release credential.
- Verify release asset sizes and SHA-256 digests before publishing, and resume incomplete draft uploads on retry.
- Preserve published release tags and assets when later builds use the same application version.

## [0.1.1] - 2026-10-18

### Added
- Per-user EXE installers for x64 and ARM64, with Start menu shortcuts and a Windows uninstall entry.
- An optional desktop shortcut. Uninstalling preserves the library and settings.

### Changed
- Application downloads are self-contained portable EXEs and setup EXEs instead of ZIPs.
- Cloudflare builds both installer architectures on Linux and verifies all four public executable downloads.
- Successful Cloudflare builds automatically create a GitHub Release for each new application version, after checking all uploaded asset hashes.
- Download chunks are fetched on demand to keep streaming connections bounded.

## [0.1.0] — 2026-10-17

First public release.

### Added
- A clothesline across the top of the screen that holds screenshots and notes.
- Screenshot capture: drag an area, click a window, or press Enter for the whole screen.
- Notes that open as a sheet of paper in the middle of the screen.
- Drag a card into any app to share it; drop it on empty space to let it go.
- Mark-up editor with pen, highlighter, box, arrow, eraser and undo/redo.
- Tucks away when idle, leaving only the bottom edges of the cards visible.
- Control panel with a library of everything on the line, a discarded pile kept for 1, 7 or 30 days, and settings.
- Customisable global shortcuts.
- Light and dark theme following Windows, with the system accent colour and Mica.
- English and Indonesian, with drop-in JSON translations.
