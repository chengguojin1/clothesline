# Jemur: desktop mode, resizable cards and more note colours

## Intent

In 0.2.0 a card can be pinned to the desktop, up to three per line, while the
rest stay on the rope. The owner asked for one or the other instead:

- **Line mode (mode Jemuran):** every card hangs on the rope, as before 0.2.0.
- **Desktop mode (mode Jepit):** every card floats freely on the desktop, above
  other apps, anywhere on any monitor, and the rope is not shown.

They also asked for a shortcut to switch modes, resizable cards on the desktop,
and more note colours.

## Goals

- One global mode setting: line or desktop. It applies to every card on every
  line and is remembered across restarts.
- Switch with a new shortcut, **Alt+Shift+M**, from the tray menu, and from
  Settings. Switching animates every card between its rope slot and its desktop spot.
- In desktop mode, each card's position **and size** are remembered, so leaving
  and re-entering desktop mode puts everything back exactly.
- Desktop cards can be resized by dragging a corner grip.
- Note paper colours go from 5 to 12.

## Non-goals

- Mixing modes (some cards on the rope, some on the desktop). The 0.2.0
  per-card "Pin to desktop" / "Back on the line" actions and the 3-pin limit are removed.
- Resizing cards on the rope.
- Groups on the desktop. Groups stay a rope feature; in desktop mode grouped
  cards are free cards, and their group membership is kept for when the user
  returns to line mode.
- Custom colours or a colour picker.

## Anti-goals

- Losing a card's desktop spot or size, or a rope position, when switching modes.
- Cards opening off-screen (unplugged monitor, resolution change).
- Idle cost: no timers or render loops while nothing moves. With many desktop
  cards the cost must stay one small layered window per card, using thumbnails only.
- Breaking 0.2.0 data.

## Behaviour

### Modes

- `Settings.Mode`: `"line"` (default) or `"desktop"`.
- **Line mode:** as in 0.2.0, but without the per-card pin actions. A 0.2.0
  card that was pinned shows on the rope.
- **Desktop mode:**
  - The line window is hidden. Each card of the active line is its own
    desktop card window (today's `PinWindow`), topmost and not activating.
  - There is no limit on the number of cards.
  - Click opens the card in the spotlight. Hold marks up an image. Drag moves
    the card with the clothespin swing. Right-click opens the item menu.
  - There is no drag-off-to-share and no drop-to-discard; share, copy and
    discard come from the menu.
  - New screenshots, notes and pastes appear on the desktop with the drop and
    swing, at the next free spot (below).
  - Line switching, lift, capture hiding, and the Control Panel all work as in
    0.2.0, acting on the desktop cards instead of the rope.
- **Free spot for a card that has never been on the desktop:** cascade from the top-left of the
  primary monitor's work area, 32 px from the edges, card width + 16 px apart,
  wrapping to the next row, skipping spots another card of the same line already
  covers by more than half. If the cascade is full, start again with a 24 px
  offset.
- A 0.2.0 pinned card keeps its saved desktop spot.

### Switching

- Alt+Shift+M (action id `"mode"`, rebindable, shown under Settings → Keyboard
  shortcuts), the tray menu item (label follows state: "Desktop mode" / "Line
  mode"), and a Settings card with a two-choice dropdown.
- Switching while lifted un-lifts.
- **Line to desktop:** cards fly from their rope slots to their desktop spots,
  staggered, using the existing 380 ms ghost arc, then land with the swing.
  The rope fades out.
- **Desktop to line:** the reverse. Cards fly to their rope slots; the rope fades in.
- Stagger is capped as in 0.2.0, and with Windows animation effects off the
  switch is instant.

### Resizing (desktop mode only)

- A small grip appears at the card's bottom-right corner on hover and is also
  reachable by Narrator.
- **Notes:** width and height change independently. The text keeps its size
  and the paper shows more or less of it. Minimum 120 × 100 DIP, maximum
  640 × 640 DIP.
- **Screenshots:** the aspect ratio is kept, and the image scales. Minimum
  120 DIP wide, maximum 960 DIP wide. The thumbnail is decoded at the shown
  size × 2 for high DPI, and re-decoded only when the resize ends, never per mouse move.
- The clothespin and shadow stay the same size.
- Double-clicking the grip resets to the default size.
- Size is saved when the resize ends, together with the position.
- The spotlight, mark-up and line mode are unaffected by a card's desktop size.

### More note colours

- Keep the existing five, in the same order and with the same indices, so
  saved notes keep their colour. Add seven more, all readable with the dark ink:
  peach, lilac, mint, coral, lavender, sand, cloud grey.
- The note colour row in the spotlight and the card menu show all twelve. In
  the spotlight they wrap to two rows of six if they don't fit.
- Every new colour name is localised in all six locale files.

## Data

- `Settings.Mode` (string, default `"line"`; `Normalize` repairs unknown values to `"line"`).
- `HangItem.PinWidth`, `HangItem.PinHeight` (double, DIP at card scale 1;
  0 means default size).
- `HangItem.Pinned` keeps its meaning of "has a saved desktop spot". In
  desktop mode every card gets one when it is first placed. Line mode ignores it.
- `Library.MaxPinsPerRope`, `PinCount`, the `Pin` limit and `JemurApp.CanPin` are removed.
- `Library.MovePin` also takes the size: `MovePin(item, left, top, width, height)`.
- `items.json` stays version 2; the new fields are additive.

## Removed UI

- Item menu, Control Panel tile and spotlight toolbar: *Pin to desktop* and
  *Back on the line*. The spotlight toolbar's pin button is replaced by nothing.
- Locale keys `pin.toDesktop`, `pin.back` and `pin.limit` are removed.
  `pin.name` stays, as the desktop card's Narrator name.

## Tests

- `Normalize` repairs Mode and adds the `mode` shortcut without stealing a taken combination.
- Free-spot cascade is a pure function: first spot, wrap to the next row,
  skipping occupied spots, and full-cascade offset.
- Resize clamping is a pure function: note min and max, and image aspect kept with min and max.
- A 0.2.0 file with pinned cards loads; positions and sizes round-trip.
- `NoteColors.Count == 12`, the first five unchanged, and every colour key
  present in `en.json` (`LocalizationTests` covers the other locales).

## Docs and release

README Features and Shortcuts sections, and a CHANGELOG `0.3.0` entry. The
upgrade note says the 3-pin limit and per-card pin actions are replaced by
desktop mode. Version 0.3.0.

## Decisions made by the designer (not discussed with the owner)

1. **Keeping spot, size and group.** Only Discard clears a card's desktop
   spot and size. Restore treats it as never placed. Everything else keeps
   them: moving to another line, joining or creating a group, and switching
   modes. Grouping no longer touches `Pinned`. Pinning no longer leaves the
   group, because there is no pin action any more. `DiscardAll` and
   `LineWindow`'s card filter depend on the mode, not on `Pinned`.
2. **Card size setting.** `PinWidth` and `PinHeight` are stored unscaled and
   multiplied by the Small/Medium/Large scale when shown. The clamps are
   unscaled DIP. Reset clears them to 0 (default).
3. **0.2.0 pinned cards in line mode** keep their stored `Position` when it is
   in 0..1. Otherwise they go to a free spot, as today.
4. **Desktop mode hides the whole line window,** including the tag and group
   labels. Lines are switched from the tray, the Control Panel or Alt+Shift+G.
   Switching shows a toast with the line's name, using the existing tray
   notification. Flights back to the rope take their slots from the line
   layout, computed while the window is hidden; it is shown at the start of
   the desktop-to-line switch.
5. **Clamping is display-only.** An off-screen saved spot is shown clamped but
   not overwritten, so replugging a monitor restores it. The spot is saved
   only when the user moves or resizes the card.
6. **Free-spot cascade** uses the actual on-screen rectangles of the line's
   desktop cards, including resized ones, and the default size of the new card.

## Left to the builder

- Grip artwork and hover behaviour.
- The exact paper colour values (they must read well with the existing ink and stay calm).
- Whether the mode switch reuses `GhostWindow` flights or animates the card windows directly.
- Translations for es, ja, ms and pt-BR.
- How to split the work into commits.
