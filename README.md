# Archon Soul Mods

Community mods for [Archon Soul](https://store.steampowered.com/) by PolyGryph.

| Mod | Description |
|---|---|
| [ArchonSoulGamepad](ArchonSoulGamepad/) | Full controller support — cursor-free focus navigation for a mouse-only game |

Built and tested against **Archon Soul 1.0.00** (Unity 6000.0.36, Mono backend, Windows x64).

---

## Controller Support — Installation

The mod adds native gamepad navigation. There is no on-screen cursor: a focus
outline moves between real interactive elements.

### 1. Install BepInEx

Download **BepInEx 5.4.23.5, Windows x64** (the Mono build, *not* IL2CPP):

<https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5>
→ `BepInEx_win_x64_5.4.23.5.zip`

Extract it directly into your Archon Soul folder — the one containing
`Archon Soul.exe`. By default that is:

```
C:\Program Files (x86)\Steam\steamapps\common\Archon Soul
```

> Find it via Steam: right-click **Archon Soul → Manage → Browse local files**.

Afterwards the folder should contain `winhttp.dll`, `doorstop_config.ini` and a
`BepInEx` folder alongside `Archon Soul.exe`.

### 2. Generate the BepInEx folders

Launch the game once, then quit. BepInEx creates `BepInEx\plugins` and
`BepInEx\config`. If `BepInEx\LogOutput.log` appears, it is working.

### 3. Install the mod

Download `ArchonSoulGamepad.dll` from the
[latest release](../../releases/latest) and place it here:

```
Archon Soul\BepInEx\plugins\ArchonSoulGamepad\ArchonSoulGamepad.dll
```

### 4. Turn off Steam Input

**This step matters.** Steam can capture the controller and present it to the game
as a mouse, in which case the game sees no gamepad and the mod stays dormant.

In Steam: right-click **Archon Soul → Properties → Controller** →
set *Override for Archon Soul* to **Disable Steam Input**.

### 5. Play

Launch the game and press any button on the controller. A focus outline appears
and the mouse cursor hides. Moving the mouse hands control back instantly;
pressing anything on the pad switches back.

---

## Controls

| Input | Action |
|---|---|
| Left stick / D-pad | Move focus |
| A | Activate — button, pick up a die, drop a carried die into the focused slot, or engage a setting |
| B | Cancel — release a setting, return a carried die, cancel spell targeting, press the screen's Back button, otherwise pause |
| X | Apply a held item (same as A), or right-click the focused element |
| Y | Enter or leave the run's top bar |
| LB / RB | Jump between groups of controls (dice pool, spell slots, settings columns) |
| Start | Pause menu |

### The run's top bar

The bar across the top of a run is excluded from normal navigation, so the d-pad
only reaches the screen you are on. Press **Y** to enter it, then cycle across it
to read tooltips or press **A** to open a panel. **Y**, **B** or **down** leaves.

### Dragging dice, spells and modifications

While something is held, focus is limited to the places it can actually go, taken
from the game's own validation rather than hard-coded per screen.

- **A** or **X** places the held item
- **B** returns it to where it came from, changing nothing

On the Modify Dice screen, **A** on a bag die puts it straight into the edit slot
(swapping out whatever is there), and **A** on the die in the edit slot returns it
to the bag. A die's faces are reordered by picking one up and moving it; it holds
position until you press a direction.
### Settings screens

Each setting is one focus target rather than a row of separate arrows.

1. **Up / Down** moves between settings
2. **A** engages the focused setting — the outline turns cyan
3. **Left / Right** changes the value, one step per press
4. **B** releases it, and Up/Down navigates again

Controls that quit the game need **two** A presses; the outline turns red while
armed, and automatic focus never lands on them.

---

## Verifying it loaded

Open `Archon Soul\BepInEx\LogOutput.log`. You should see:

```
[Info   :Archon Soul Gamepad Support] initialising
[Info   :Archon Soul Gamepad Support] game bridge ready
[Info   :Archon Soul Gamepad Support] patched Input.mousePosition
[Info   :Archon Soul Gamepad Support] ready - connect a controller and press any button
```

Then, once a controller is active: `gamepad mode enabled`.

## Troubleshooting

**Controller acts like a mouse / nothing is highlighted.** The game cannot see a
gamepad. The log will say `no gamepad detected`. Disable Steam Input (step 4), and
make sure the controller is awake before launching.

**Nothing in the log at all.** BepInEx is not loading. Confirm `winhttp.dll` sits
next to `Archon Soul.exe`, and that you used the **x64 Mono** build.

**A screen navigates badly.** Set `NavigationSelfTest = true` and
`DebugHarness = true` in
`BepInEx\config\com.community.archonsoul.gamepad.cfg`, reproduce, and attach
`LogOutput.log` to an issue.

## Configuration

`BepInEx\config\com.community.archonsoul.gamepad.cfg`

| Setting | Default | Purpose |
|---|---|---|
| `AutoEnableWithGamepad` | `true` | Enter controller mode as soon as a pad is present |
| `StickDeadzone` | `0.5` | How far the stick must move to count as a direction |
| `RepeatDelay` / `RepeatRate` | `0.4` / `0.12` | Hold-to-repeat timing |
| `HighlightColor` | `FFD940` | Focus outline colour (RRGGBB) |
| `HighlightThickness` | `3` | Outline thickness in pixels |
| `Diagnostics` | `true` | Periodic focus summary in the log |
| `NavigationSelfTest` | `false` | One-off focus walk of the current screen |
| `DebugHarness` | `false` | Developer only — drives a scripted controller sequence |

## Uninstall

Delete `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version` and the `BepInEx`
folder from the game directory. Nothing else is modified, and no game files are
patched on disk.

## Building from source

Requires the [.NET SDK](https://dotnet.microsoft.com/download) (8.0 or newer) and
BepInEx already installed into the game folder (steps 1–2 above).

```
cd ArchonSoulGamepad
dotnet build -c Release
```

Override the game location if it is not the default Steam path:

```
dotnet build -c Release -p:GameDir="D:\Games\Archon Soul"
```

Copy `bin/Release/netstandard2.1/ArchonSoulGamepad.dll` into
`BepInEx\plugins\ArchonSoulGamepad\`.

## License

[MIT](LICENSE).

## Disclaimer

Unofficial and not affiliated with PolyGryph. It changes nothing on disk beyond
the BepInEx files you install, and the game's own settings file is left untouched
apart from options you change in-game yourself.
