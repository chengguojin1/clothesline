# Desktop Mode, Resizable Cards and More Colours Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace per-card pinning with a global line/desktop mode (Alt+Shift+M), make desktop cards resizable, add 12 note colours, and release 0.3.0.

**Architecture:**
- Mode lives in `Settings.Mode`.
- `LineWindow` shows the active line's cards only in line mode.
- `PinBoard`/`PinWindow` show every card of the active line as a desktop card in desktop mode.
- Placement and resize rules are pure functions in `LineLayout` so they can be tested.
- `Library` keeps the desktop spot and size and clears them only on Discard.

**Tech Stack:** C# / WPF, .NET 10, xUnit 2.9, no new packages.

**Spec:** `docs/superpowers/specs/2026-10-20-desktop-mode-design.md`. Read it first; this plan does not repeat its behaviour text.

## Global Constraints

- No new dependencies.
- No timers or render loops while idle.
- Layered shadows, no `DropShadowEffect`.
- Thumbnails are decoded at the shown size × 2.
- Every string lives in all six locale files (en, es, id, ja, ms, pt-BR); `LocalizationTests` enforces this.
- `src/Core/Models.cs` and `src/Core/Library.cs` must stay WPF-free. They are compiled by `tests/portable/Jemur.Portable.Tests.csproj` on Linux in Cloudflare. Verify with `dotnet test tests/portable/Jemur.Portable.Tests.csproj -c Release`.
- Motion is 200–450 ms per element, the stagger is capped (`LineWindow.Stagger`), and everything is instant when `Motion.Enabled` is false.
- Identifiers and values:
  - Mode values `"line"` (default) and `"desktop"`.
  - Action id `"mode"`, default `"Alt+Shift+M"`.
  - Note size clamp: 120×100 to 640×640 DIP.
  - Image width clamp: 120 to 960 DIP.
  - Size fields are stored unscaled and multiplied by `CardView.Scale` when shown.
  - Free-spot cascade: 32 px margin, 16 px gap, 24 px offset on wrap-around.
- Build env: `DOTNET_ROOT` and `PATH` must point to `C:\Users\revan\.codex\tools\dotnet10`. Each task must pass:
  - `dotnet build Jemur.sln -c Release -p:EnableWindowsTargeting=true` with 0 warnings;
  - `dotnet test Jemur.sln`;
  - the portable test command above.

## Review Focus

1. **Switching modes repeatedly** (line → desktop → line → desktop): every card is back at the same rope slot and the same desktop spot and size, with no duplicate or stray windows. Test `ModeRoundTrip_KeepsSpotsSizesAndPositions` (Task 1, at library level), plus a manual check in Task 2.
2. **A desktop card on a monitor that is unplugged:** it is shown clamped, but the saved spot is unchanged. Test `DisplayClamp_DoesNotOverwriteSavedSpot` (Task 2, PinBoard path through Library.MovePin not being called; assert via a Library save round-trip in the manual check), and the pure `ClampPin` is already tested.
3. **Many cards in desktop mode, beyond one cascade screen:** new cards keep appearing, offset, never off-screen. Test `FreeDesktopSpot_WrapsWithOffsetWhenFull` (Task 1).
4. **Resizing a note to the clamp edges, and a screenshot with an extreme aspect ratio:** sizes stay inside the limits and the aspect ratio is kept. Tests `ClampCardSize_*` (Task 1).
5. **A 0.2.0 user with pinned cards and the old `pin.*` labels upgrades:** the app starts in line mode with every card on the rope, and desktop mode later restores their saved spots. Test `Upgrade_PinnedCardsKeepSpotAndPosition` (Task 1).

---

### Task 1: Core: mode, spot rules, layout helpers

**Files:**
- Modify: `src/Core/Models.cs`, `src/Core/Library.cs`, `src/Core/LineLayout.cs`, all `locales/*.json`
- Test: `tests/LibraryGroupPinTests.cs`, `tests/LineLayoutTests.cs`, `tests/SettingsStoreTests.cs`

**Interfaces (produces):**
- `Settings.Mode` (string, `"line"`). `Normalize()` turns anything else into `"line"`. Constants `Settings.LineMode = "line"`, `Settings.DesktopMode = "desktop"`.
- `Actions.Mode = "mode"` in `Actions.All`, with `DefaultHotkeys()` `"Alt+Shift+M"`. Locale key `shortcut.mode`: en "Switch between line and desktop", id "Ganti mode jemuran/jepit".
- `HangItem.PinWidth`, `HangItem.PinHeight` (double, unscaled DIP, 0 means default). `Pinned` means "has a saved desktop spot".
- `Library`:
  - Remove `MaxPinsPerRope`, `PinCount`, `Unpin`.
  - `bool Pin(...)` becomes `void Place(HangItem item, double left, double top)`. It sets the spot, sets `Pinned = true`, raises nothing, and saves.
  - `MovePin(item, left, top, width, height)` saves the spot and size.
  - `Discard` clears `Pinned`, `PinWidth` and `PinHeight`.
  - `MoveToRope`, `CreateGroup`, `JoinGroup` and group exits no longer touch `Pinned` or leave groups because of pins.
  - `DiscardAll(ropeId)` discards every live card on that rope.
- `LineLayout`:
  - `Point FreeDesktopSpot(Rect workArea, Size card, IReadOnlyList<Rect> occupied)`. Cascade from top-left with 32 px margin and 16 px gap, row by row. A spot is free if no occupied rect covers more than half of it. When every spot is taken, restart offset by 24 px (repeat until free or offset ≥ card size, then return the first spot).
  - `Size ClampCardSize(bool image, Size wanted, double aspect)`. Note: clamp width to 120..640 and height to 100..640. Image: clamp width to 120..960 and set height to width / aspect.

- [ ] **Step 1: Write failing tests.**
  - `Normalize_RepairsModeAndAddsModeShortcut`: `Mode = "x"` gives `"line"`; missing `mode` gives `"Alt+Shift+M"`; if another action already uses Alt+Shift+M, the result is `""`.
  - `ModeRoundTrip_KeepsSpotsSizesAndPositions`: `Place` plus `MovePin(…, 300, 200)` with Position 0.4, then save and reload; the spot, size, `Pinned` and Position all survive.
  - `MoveToRopeAndGrouping_KeepSpotAndSize`.
  - `Discard_ClearsSpotAndSize`.
  - `Upgrade_PinnedCardsKeepSpotAndPosition`: a v2 JSON with `Pinned: true`, `PinLeft: 50`, `PinTop: 60`, `Position: 0.3` loads unchanged.
  - `DiscardAll_DiscardsEveryLiveCardOnTheRope`.
  - `FreeDesktopSpot_FirstSpotIsTopLeftWithMargin`: work area (0,0,1920,1040), card 160×140, no occupied rects, gives (32,32).
  - `FreeDesktopSpot_SkipsOccupiedAndWrapsRows`: occupying (32,32) gives (208,32); a full first row gives the next row at y = 32 + 140 + 16.
  - `FreeDesktopSpot_WrapsWithOffsetWhenFull`: every cascade spot is occupied, so the result is (56,56).
  - `ClampCardSize_NoteLimits`: (50,50) gives (120,100); (900,900) gives (640,640).
  - `ClampCardSize_ImageKeepsAspect`: aspect 2 with width 2000 gives (960,480); width 10 gives (120,60).
  - Delete the tests for the removed pin limit and Unpin, and update callers in other tests.
- [ ] **Step 2:** Run `dotnet test Jemur.sln`. Expected: compile errors or FAIL.
- [ ] **Step 3:** Implement the above. Fix compile errors in `src/App`, `src/Windows` and `ControlPanel` minimally so the build passes: remove `CanPin` and the pin menu, tile and spotlight entries, and the `pin.toDesktop`, `pin.back` and `pin.limit` keys from every locale; keep `pin.name`. Task 2 rebuilds the desktop behaviour, so a temporarily simplified `PinBoard` (it shows cards with `Pinned` as today) is fine.
- [ ] **Step 4:** Run the build, both test commands and the portable tests. Expected: PASS, 0 warnings.
- [ ] **Step 5: Commit** `feat: core rules for desktop mode`.

### Task 2: Mode switching and desktop rendering

**Files:**
- Modify: `src/Windows/LineWindow.cs`, `src/Windows/PinBoard.cs`, `src/Windows/PinWindow.cs`, `src/App/JemurApp.cs`, `src/Windows/ControlPanel.cs`, `src/Windows/SpotlightWindow.cs`, all `locales/*.json`

**Interfaces:**
- Consumes: everything from Task 1.
- Produces:
  - `JemurApp.Mode` (string).
  - `JemurApp.SetMode(string mode)`: saves the mode, un-lifts, then animates as the spec's Switching section says.
  - `JemurApp.ToggleMode()`, bound to `Actions.Mode`.
  - `JemurApp.InDesktopMode` (bool).
  - `LineWindow`: cards are filtered by `item.RopeId == _ropeId && !app.InDesktopMode`. In desktop mode the window stays hidden; `SetLifted`, `HoldOpen` and `AddCard` must not show it.
  - `LineWindow.SlotOf(HangItem) -> Point` returns the screen DIP of a card's rope slot. It works while hidden, for the flights.
  - `PinBoard`: `Belongs` = desktop mode, the active rope, and a live item.
    - On add, a card with `!Pinned` gets `LineLayout.FreeDesktopSpot` on the work area of the monitor under the cursor. The occupied list is the current windows' rects. Save it with `Library.Place`.
    - Clamping is display-only: never call `MovePin` from `Add`.
    - `PinBoard.Rebuild()` is used on mode switch.
- UI:
  - The tray menu gets one item after lift, labelled `mode.toDesktop` "Desktop mode" / "Mode jepit" in line mode and `mode.toLine` "Line mode" / "Mode jemuran" in desktop mode, with the hotkey gesture.
  - Settings gets a card `settings.mode` "Cards" / "Letak card" with a dropdown of `mode.line` "On the line" / "Di jemuran" and `mode.desktop` "Free on the desktop" / "Bebas di desktop".
  - Settings → Keyboard shortcuts gets an `(Actions.Mode, glyph)` row.
  - In desktop mode, switching lines shows a tray notification with the line name (`toast.lineName` "{0}" is fine to reuse; otherwise add `mode.lineSwitched` "Line: {0}" / "Tali: {0}").
- Switch animation:
  - Line to desktop: each card's ghost flies from `SlotOf` to its desktop spot (380 ms arc, staggered with `LineWindow.Stagger`), then its `PinWindow` appears with `DropIn`. The line fades and hides.
  - Desktop to line: the reverse; the line shows first.
  - With `Motion.Enabled` false, the switch is instant.

- [ ] **Step 1:** Implement the mode filter in LineWindow, the PinBoard membership and first placement, and `SetMode`/`ToggleMode` with the hotkey.
- [ ] **Step 2:** Implement the switch animation and the in-flight bookkeeping (no window shown twice; an interrupted toggle ends in the target mode's correct state).
- [ ] **Step 3:** Add the tray item, the Settings card and the shortcut row, then the locale keys in all six files.
- [ ] **Step 4:** Run the build, the tests and the portable tests. Expected: PASS, 0 warnings.
- [ ] **Step 5: Manual check list** (written in the report):
  - Toggle with Alt+Shift+M four times; positions are stable.
  - Take a screenshot in desktop mode; the new card lands at a free spot.
  - Lift and switch lines in desktop mode.
  - Restart in desktop mode.
  - Unplug a monitor; the card shows clamped and the saved spot returns after replugging.
- [ ] **Step 6: Commit** `feat: switch between line and desktop mode`.

### Task 3: Resizable desktop cards

**Files:**
- Modify: `src/Windows/PinWindow.cs`, `src/Windows/CardView.cs`; Create: `src/Windows/ResizeGrip.cs`; all `locales/*.json`

**Interfaces:**
- Consumes: `LineLayout.ClampCardSize`, `Library.MovePin(item, l, t, w, h)`.
- Produces:
  - `CardView.SetPaperSize(Size dip)` sets the paper area. A note keeps its font size and shows more or less text. An image scales; the thumbnail is re-decoded only through `CardView.ReloadImage(int decodeWidthPx)` when the resize ends.
  - The default size is used when `PinWidth == 0`. The shown size is `stored × CardView.Scale`.
- Grip:
  - A bottom-right 14×14 DIP element, visible on hover and while resizing.
  - Its AutomationPeer is a Thumb-like control named `L.T("card.resize")` (en "Resize" / id "Ubah ukuran").
  - Dragging resizes live (window and paper only, no re-decode) and does not swing the card.
  - Double-click resets the size to default (PinWidth/Height = 0).
  - On release, call `MovePin` with the spot and the unscaled size.
- Pure helpers belong in `LineLayout` if more are needed; add tests for them.

- [ ] **Step 1:** Write tests for any new pure helper (unscaled ↔ shown size at scale 0.85, 1.0 and 1.22).
- [ ] **Step 2:** Implement the grip, live resize, decode on release, reset and save.
- [ ] **Step 3:** Add the locale keys in all six files. Run the build, the tests and the portable tests. Expected: PASS, 0 warnings.
- [ ] **Step 4: Manual check list:**
  - Resize a note: the text stays the same size and more lines show.
  - Resize a screenshot: the aspect ratio holds and the image is sharp after release.
  - Double-click the grip to reset.
  - Restart: the size is kept.
  - Change the card size setting: everything scales.
- [ ] **Step 5: Commit** `feat: resize cards on the desktop`.

### Task 4: Twelve note colours

**Files:**
- Modify: `src/Windows/CardView.cs` (`NoteColors`), `src/Windows/SpotlightWindow.cs`, all `locales/*.json`
- Test: `tests/AccessibilityTests.cs` or a new `tests/NoteColorTests.cs`

- [ ] **Step 1: Write failing tests:**
  - `NoteColors_HasTwelveAndKeepsTheFirstFive`: Count is 12, and indices 0–4 equal FBEB9A, FAD4D0, CFE5F5, D6ECCC and F3EEE4.
  - Every key in `NoteColors.Keys` exists in en.json.
- [ ] **Step 2:** Add peach, lilac, mint, coral, lavender, sand and cloud grey (calm light papers, readable with `Theme.Ink`; the builder picks the values). Keys are `color.peach`, `color.lilac`, `color.mint`, `color.coral`, `color.lavender`, `color.sand`, `color.grey`. Indonesian names: Persik, Ungu muda, Mint, Koral, Lavender, Pasir, Abu-abu.
- [ ] **Step 3:** In the spotlight, the swatch row wraps to two rows of six when needed. The card menu lists all twelve.
- [ ] **Step 4:** Run the build, the tests and the portable tests. Expected: PASS.
- [ ] **Step 5: Commit** `feat: twelve note colours`.

### Task 5: Docs and release 0.3.0

**Files:** `README.md`, `CHANGELOG.md`, `src/Jemur.csproj`

- [ ] **Step 1:** Update the README Features and Shortcuts sections:
  - desktop mode and Alt+Shift+M;
  - resizing;
  - twelve colours;
  - remove the "up to 3 pins" wording.
- [ ] **Step 2:** Add a CHANGELOG `## [0.3.0] - <today>` entry with Added, Changed and Upgrading. The Upgrading note says per-card pins and the 3-pin limit are replaced by desktop mode, and that pinned cards keep their spot for desktop mode.
- [ ] **Step 3:** Set `<Version>0.3.0</Version>`.
- [ ] **Step 4:** Run the build, the tests and the portable tests. Expected: PASS.
- [ ] **Step 5: Commit** `Release 0.3.0 with desktop mode, resizable cards and more colours`.
