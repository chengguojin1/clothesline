<p align="center">
  <img src="docs/images/logo.png" width="112" alt="Jemur logo">
</p>

<h1 align="center">Jemur</h1>

<p align="center">
  Hang screenshots and notes on a clothesline across the top of your screen.<br>
  A small, native Windows utility. Free and open source.
</p>

<p align="center">
  <a href="https://github.com/revanapriyandi/jemur/releases/latest"><b>Download</b></a> ·
  <a href="#features">Features</a> ·
  <a href="#screenshots">Screenshots</a> ·
  <a href="#shortcuts">Shortcuts</a> ·
  <a href="#why-i-built-this">Why</a>
</p>

<p align="center">
  <a href="docs/CI-CD.md"><img src="https://img.shields.io/badge/build-Cloudflare-F38020" alt="Built on Cloudflare"></a>
  <img src="https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D4" alt="Windows 10 and 11">
  <a href="LICENSE"><img src="https://img.shields.io/github/license/revanapriyandi/jemur" alt="MIT license"></a>
</p>

<p align="center">
  <img src="docs/images/line.png" alt="Screenshots and notes hanging on a line across the top of the desktop">
</p>

*Jemur* is Indonesian for hanging things out to dry. The reference you need for the next few minutes — part of a design, an error message, a phone number — hangs where you can see it, instead of getting buried in a folder or under a pile of windows.

## Why I built this

During a normal workday I keep a lot of small things around: a screenshot of a bug, a colour from a design, a client's address, a checklist for a deploy. I used to keep them in a notes app or leave them as files and open windows on my desktop. Before long there were too many, they were scattered everywhere, and I kept losing track of what was where.

Then a simple idea came to me: what if they just hung along the top of the screen, like clothes on a line? Always there when I glance up, out of the way when I don't need them, and easy to take down when I'm done. That idea became Jemur. It has made my own work noticeably tidier, so I'm sharing it in case it helps you too.

## Features

<p align="center">
  <img src="docs/images/drop.gif" width="760" alt="A note dropping onto the line and swinging into place">
</p>

- **Screenshot straight to the line.** <kbd>Alt</kbd> <kbd>Shift</kbd> <kbd>S</kbd>, then drag an area, click a window, or press <kbd>Enter</kbd> for the whole screen. The capture drops onto the line with a little swing.
- **Notes on paper.** <kbd>Alt</kbd> <kbd>Shift</kbd> <kbd>N</kbd> brings a sheet to the middle of the screen. Type, pick one of twelve colours, click outside — it hangs itself back up.
- **Bring anything closer.** Click a card and it comes to the middle of the screen, where you can copy, mark up, open or save it.
- **Share by dragging.** Pull a card down off the line and drop it into WhatsApp, Slack, an email or a folder — anything that accepts files.
- **Let it go.** Drop a card on empty space and it falls away. It waits in *Discarded* for a week (configurable) in case you change your mind.
- **Mark up.** Press and hold a screenshot to draw on it with a pen, highlighter, box or arrow.
- **Out of sight.** When you're not using it, the line tucks itself away and only the bottom edges of the cards peek out. Touch the top of the screen to bring it back.
- **Take everything in.** <kbd>Alt</kbd> <kbd>Shift</kbd> <kbd>J</kbd> or a left-click on the tray icon lifts every card out of sight. Do it again and they hang back out.
- **More than one line.** Keep a line (*Tali*) per project or task. Click the name tag at the left end of the rope to switch, or press <kbd>Alt</kbd> <kbd>Shift</kbd> <kbd>G</kbd> for the next line. Cards move between lines from their right-click menu.
- **Groups.** Gather related cards into a named group (*Kelompok*) on the line. Drag a card into a group to join it, drag the label to move the whole group, and click the label to fold the group into a stack.
- **Desktop mode.** <kbd>Alt</kbd> <kbd>Shift</kbd> <kbd>M</kbd> (or the tray menu) lets every card go free on the desktop, above your other apps, with no limit. Press it again and they hang back on the line.
- **Resize cards.** On the desktop, drag a card's corner to make it bigger or smaller. Notes keep their text size and show more or less of it; screenshots keep their shape. Double-click the corner to reset.
- **Feels like Windows.** Light and dark mode, your accent colour, Mica, keyboard shortcuts you can change, and a tray icon that matches your taskbar.

## Screenshots

<table>
  <tr>
    <td width="50%"><img src="docs/images/note.png" alt="A note opened in the middle of the screen"></td>
    <td width="50%"><img src="docs/images/preview.png" alt="A screenshot opened in the middle of the screen with a command bar"></td>
  </tr>
  <tr>
    <td align="center"><sub>A note, taken off the line to write on</sub></td>
    <td align="center"><sub>A screenshot up close, with quick actions</sub></td>
  </tr>
  <tr>
    <td><img src="docs/images/panel-light.png" alt="Control panel listing everything on the line, light theme"></td>
    <td><img src="docs/images/settings-dark.png" alt="Settings page, dark theme"></td>
  </tr>
  <tr>
    <td align="center"><sub>Everything on the line, in one place</sub></td>
    <td align="center"><sub>Settings, following the Windows 11 style</sub></td>
  </tr>
  <tr>
    <td><img src="docs/images/markup-dark.png" alt="Mark-up editor with pen, highlighter, box and arrow tools"></td>
    <td><img src="docs/images/menu.png" alt="Right-click menu on a card"></td>
  </tr>
  <tr>
    <td align="center"><sub>Mark up a screenshot before sharing it</sub></td>
    <td align="center"><sub>Every action is a right-click away</sub></td>
  </tr>
</table>

## Install

Download an installer or a portable EXE. Every package includes the .NET runtime.

| Package | Intel / AMD 64-bit | Windows on ARM64 |
|---|---|---|
| Installer | [x64 Setup.exe](https://jemur-downloads.syeana-co.workers.dev/latest/win-x64-setup) | [ARM64 Setup.exe](https://jemur-downloads.syeana-co.workers.dev/latest/win-arm64-setup) |
| Portable | [x64 EXE](https://jemur-downloads.syeana-co.workers.dev/latest/win-x64) | [ARM64 EXE](https://jemur-downloads.syeana-co.workers.dev/latest/win-arm64) |

The installer adds Jemur to your Start menu and Windows' installed-apps list. It
installs for your current user in `%LOCALAPPDATA%\Programs\Jemur`. A desktop
shortcut is optional. Close Jemur from its tray menu before upgrading or
uninstalling. Uninstalling keeps your screenshots, notes and settings.

The portable EXE runs directly without installation. Both versions store your
library in `%APPDATA%\Jemur` and live in the notification area. Turn on
**Settings → Start with Windows** if you want Jemur to start when you sign in.

[GitHub Releases](https://github.com/revanapriyandi/jemur/releases) preserve
versioned downloads. The Cloudflare links above track the latest successful
`main` build. [Checksums](https://jemur-downloads.syeana-co.workers.dev/SHA256SUMS.txt)
and the [build manifest](https://jemur-downloads.syeana-co.workers.dev/manifest.json)
identify its source commit and packages.

## Updates

Jemur updates itself. When a newer version is published, it downloads the
package for your build in the background, checks its size and SHA-256 against the
public manifest, and installs it when you are away (no capture, spotlight,
mark-up, menu or prompt open, and no input for 2 minutes) or when you quit.
Turn this off in **Settings → Update automatically**; Jemur then behaves as
before and only tells you that a version is available.

Open **About** and choose **Check for updates** to check right away. Jemur also
checks about 30 seconds after startup and at most once every 24 hours. While an
update is downloading or waiting, About shows its status and a **Restart to
update** button.

The checker sends no account or library data. It only reads the public
manifest. Jemur never runs a file whose size and SHA-256 do not match the
manifest, and only downloads from the Jemur download host. If an automatic
update fails, choose **Download installer** in About to open the verified GitHub
release page and start the installer yourself. Versions 0.3.0 and older update
by hand once.

Runs on Windows 10 (1809 or later) and Windows 11. Mica and rounded corners appear on Windows 11.

## Shortcuts

| Action | Default |
|---|---|
| Take a screenshot | <kbd>Alt</kbd> <kbd>Shift</kbd> <kbd>S</kbd> |
| New note | <kbd>Alt</kbd> <kbd>Shift</kbd> <kbd>N</kbd> |
| Hang whatever is on the clipboard (image, image file or text) | <kbd>Alt</kbd> <kbd>Shift</kbd> <kbd>V</kbd> |
| Take everything in / hang it back out | <kbd>Alt</kbd> <kbd>Shift</kbd> <kbd>J</kbd> |
| Switch between line and desktop mode | <kbd>Alt</kbd> <kbd>Shift</kbd> <kbd>M</kbd> |
| Next line | <kbd>Alt</kbd> <kbd>Shift</kbd> <kbd>G</kbd> |
| Open Jemur | <kbd>Alt</kbd> <kbd>Shift</kbd> <kbd>P</kbd> |

Change any of them under **Settings → Keyboard shortcuts**. A left-click on the tray icon also takes everything in or hangs it back out; Jemur itself opens from the tray menu or <kbd>Alt</kbd> <kbd>Shift</kbd> <kbd>P</kbd>. In the mark-up editor: <kbd>P</kbd> pen, <kbd>H</kbd> highlighter, <kbd>R</kbd> box, <kbd>A</kbd> arrow, <kbd>E</kbd> eraser, <kbd>Ctrl</kbd>+<kbd>Z</kbd> / <kbd>Ctrl</kbd>+<kbd>Y</kbd> undo and redo.

## Light on resources

Jemur sits in the background all day, so it's built to stay small:

- no WinForms or System.Drawing — the tray icon, shortcuts and screen capture talk to Win32 directly;
- thumbnails are decoded at the size they're shown and shared between views, never at full resolution;
- nothing animates or polls while the line is idle, and the line window shrinks to a thin strip when tucked away;
- memory is handed back to Windows after the heavy moments — a capture, closing the control panel.

Tucked away, Jemur shows about **3 MB** in Task Manager's *Memory* column, with or without items on the line. While the line or the control panel is on screen it uses more (roughly 30–80 MB, mostly WPF's renderer) and gives it back once it's tucked away again. Measurements and how to reproduce them: [docs/MEMORY.md](docs/MEMORY.md).

## Privacy

Everything stays on your PC. Screenshots and notes live in `%APPDATA%\Jemur`. There is no telemetry and no account. Jemur only goes online to check for updates and, when a note contains a web address, to fetch that one page's title and picture for its preview.

## Languages

English, Bahasa Indonesia, Spanish, Portuguese (Brazil), Japanese and Bahasa Melayu are included. Adding a language is a single JSON file and no code — see [docs/TRANSLATING.md](docs/TRANSLATING.md). Native speakers are welcome to suggest wording improvements.

## Build from source

```powershell
git clone https://github.com/revanapriyandi/jemur
cd jemur
dotnet run --project src
```

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). [CONTRIBUTING.md](CONTRIBUTING.md) covers the project layout, tests and guidelines.

## Credits

Inspired by Tendedero, a macOS app by [@alejandr0bujan](https://x.com/alejandr0bujan). Jemur is an independent Windows implementation and shares no code with it.

## License

[MIT](LICENSE) © 2026 M. Revan Apriyandi
