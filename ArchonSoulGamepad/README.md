# Archon Soul — Controller Support

Native gamepad navigation for Archon Soul (Unity 6000.0.36, Mono) via BepInEx 5.4.23.5.

There is **no on-screen cursor**. A focus outline moves between real interactive
elements, and the mod synthesises the exact pointer events the game already
listens for.

## Controls

| Input | Action |
|---|---|
| Left stick / D-pad | Move focus between elements |
| A | Activate focused element — click a button, pick up a die, drop a carried die into the focused slot, or engage a setting |
| B | Cancel: release an engaged setting, return a carried die, cancel spell targeting, press the screen's Back button, otherwise pause menu |
| X | Right-click the focused element |
| LB / RB | Jump between groups of controls (dice pool, spell slots, action buttons) |
| Start | Pause menu |

### Settings screens

Each setting is a single focus target rather than a row of separate arrows.

1. **Up / Down** moves between settings
2. **A** engages the focused setting — the outline turns cyan
3. **Left / Right** changes the value, one step per press
4. **B** releases the setting, and Up/Down navigates again

While a setting is engaged, vertical input is ignored so the row cannot change
under you mid-adjustment. Volume sliders move in ~20 steps across their range.
**LB / RB** jumps between the layout columns (Display / Audio / Other).

Moving the physical mouse hands control back to mouse mode instantly; pressing
anything on the pad switches back.

## How it works

- **Discovery** — each screen is scanned for components the game already uses for
  interaction (`IPointerClickHandler`, `IPointerDownHandler`, `Selectable`, and
  `DiceInput` slots while carrying a die). Every candidate is confirmed with a
  real UI raycast, so anything covered by a modal, faded out, or with raycasts
  disabled automatically stops being focusable. No per-screen lists to maintain.
- **Navigation** — direction scoring uses rectangle geometry: distance along the
  travel axis plus the perpendicular *gap* between rects. Elements that overlap on
  the cross axis count as the same column, which is what makes staggered menus
  step correctly instead of skipping entries.
- **Activation** — `ExecuteEvents` dispatch of pointerEnter/Exit/Down/Up/Click.
  This is independent of the input module and of the OS cursor.
- **Dice drag** — the game positions a held die from `Mouse.current.position` and
  resolves drops against the slot it believes is hovered. The mod drives both: the
  synthetic pointer is pinned to the focused element, and hover follows focus.

## Safety

Controls that quit the game (`QuitToDesktop`) are treated as destructive:

- automatic focus never lands on them — focus only reaches them if you deliberately
  navigate there
- activating one requires **two presses** of A; the outline turns red while armed
- activation is ignored for a moment after any screen change, so a fast double tap
  cannot hit something that just slid into place

## Troubleshooting

**"My controller behaves like a mouse."** The mod is dormant because the game sees
no gamepad — almost always Steam Input capturing the pad and emulating a mouse.
In Steam: right-click **Archon Soul → Properties → Controller**, set
*Override for Archon Soul* to **Disable Steam Input**. The log will say
`no gamepad detected` and list the devices it can see.

## Notes

- While a controller is active the mod forces the game's **Click to Drag** setting
  on, because it turns pick-up/put-down into two discrete presses. The saved
  settings file is patched so your original preference is never overwritten.
- `Input.GetMouseButton*` are native methods; HarmonyX patches them successfully
  on this build, which is what allows B to cancel spell targeting.
- Screens slide in and out rather than activating/deactivating, so an element is
  only considered interactive once its centre is on screen and it was present in
  the previous scan. Controls are judged by centre position rather than full
  containment, because real buttons often overhang a screen edge.
- Many screens are popups with no Escape handler, so B looks for the screen's own
  Back/Close control and presses that before falling back to Escape.
- Directional navigation resolves in two passes: first only targets that genuinely
  overlap the current one on the cross axis, then a wider cone, then wrapping.
  Without the first pass a near-but-diagonal control beats a far-but-aligned one —
  which is how pressing left from the SFX slider used to land on a row below, and
  how the bottom settings rows used to jump to Close.
- A settings row (`SettingsShuffler` / `SettingsSlider`) collapses to one focus
  target; its arrows are driven through the button's own `onClick` rather than a
  synthetic raycast click, so a value never needs a second press to register.
- The game's `InputSystemUIInputModule` carries Unity's **default gamepad
  bindings**, so pressing A also fired uGUI's Submit on whatever was still
  selected — and a synthetic pointer click leaves its target selected. That
  invisible second path re-activated earlier buttons (reopening screens, flipping
  settings, and in some cases quitting). The mod now clears the EventSystem
  selection each frame, leaving text fields alone so typing still works.

## Config

`BepInEx/config/com.community.archonsoul.gamepad.cfg`

- `AutoEnableWithGamepad` — enter controller mode as soon as a pad is present (default true)
- `StickDeadzone`, `RepeatDelay`, `RepeatRate` — navigation feel
- `HighlightColor` (RRGGBB), `HighlightThickness` — focus outline
- `Diagnostics` — periodic focus summary in `BepInEx/LogOutput.log`
- `NavigationSelfTest` — logs a one-off focus walk of the current screen; useful
  when reporting a screen that navigates badly

## Uninstall

Delete `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version` and the `BepInEx`
folder from the game directory.

## Rebuilding

Requires the .NET SDK. From `src/`:

```
dotnet build -c Release
```

Copy `bin/Release/netstandard2.1/ArchonSoulGamepad.dll` into
`<game>/BepInEx/plugins/ArchonSoulGamepad/`.
