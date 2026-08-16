# Trapline

A drug-dealing and gang mod for GTA V, driven entirely from a custom radial wheel that
replaces the vanilla weapon wheel.

Written from scratch. No decompiled code from any other mod, no licence server, no
Patreon check, and **zero external runtime dependencies** — the built assembly references
only the .NET Framework BCL and `ScriptHookVDotNet3.dll`. Nothing else in `scripts\` can
lose a version fight with it.

---

## Requirements

| Component | Version here |
|---|---|
| GTA V | Legacy, `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V` |
| ScriptHookV | installed |
| ScriptHookVDotNet | 3.9.0.x (`ScriptHookVDotNet3.dll` in the game root) |
| .NET Framework | 4.8 (ships with Windows 11) |

## Install

```powershell
.\build.ps1 -Deploy
```

That drops `Trapline.dll` into `scripts\`, `Trapline.ini` next to it, and the data files
into `scripts\Trapline\`. Existing config and data are never overwritten.

To uninstall: delete `scripts\Trapline.dll`. Your save in `scripts\Trapline\save.json`
survives, so reinstalling picks up where you left off.

## Controls

By default Trapline **takes over the weapon wheel button** (`Mode=Replace` in the ini).
The vanilla weapon wheel is suppressed while the mod is loaded.

| Input | Action |
|---|---|
| Hold weapon-wheel button | Open the Trapline wheel |
| Mouse / right stick | Point at a segment |
| Release | Pick the highlighted segment |
| LMB / RT | Pick without releasing (the only way into a submenu) |
| RMB / LT | Back out one page, or close at the root |

Prefer to keep your weapon wheel? Set `Mode=Separate` in `Trapline.ini` and the wheel
moves to its own key (`Key=B` by default).

## What works right now (0.1.0)

A complete economic loop:

- **Resupply** — buy wholesale from the plug. Lot size scales with rank; tier-2 and
  tier-3 product is rank-gated.
- **Sell** — hand-to-hand to any ambient ped on foot in front of you, with a handshake
  animation and a proper deal state machine (walk away mid-deal and it aborts).
- **Pricing** — base price walked through named multipliers: night rates ramp to 3x at
  2am, rank improves your take, heat cuts into it.
- **Progression** — respect, five ranks (Pee-Wee to OG), notoriety that decays over time.
- **Persistence** — atomic JSON saves in `scripts\Trapline\save.json`.

Wheel segments for **Crew**, **Turf** and **Stash** are present but disabled — they are
the next milestone, and they hold their positions now so the muscle memory does not move.

## Layout

```
src/Trapline/
  Main.cs              script entry point; single owner of the tick loop
  Core/                Paths, Log, IniFile, Json (hand-rolled), JsonFile, Settings
  UI/                  Draw (native primitives), Palette, RadialMenu, WheelController, Notify
  Wheel/WheelPages.cs  builds wheel pages from live game state
  Economy/             Drugs catalogue, Inventory, Pricing
  Dealing/StreetDeal   buyer selection and the hand-to-hand deal
  State/PlayerState    respect, rank, notoriety, save/load
data/                  shipped data files, copied to scripts\Trapline\ on deploy
tools/                 self-contained Roslyn compiler + net48 reference assemblies
```

## Building

`build.ps1` deliberately does **not** use `dotnet build`. The .NET 8 SDK on this machine
is broken — `Microsoft.NETCore.App\8.0.28` is a partial install (3 files against 8.0.19's
184), so every `dotnet` invocation dies on a missing `hostpolicy.dll`. The build instead
drives a Roslyn `csc.exe` pulled into `tools\`, which needs no SDK, no Visual Studio and
no admin rights.

`Trapline.csproj` is kept in sync for whenever the SDK gets repaired, but `build.ps1` is
the authoritative build.

```powershell
.\build.ps1                    # -> build\Trapline.dll
.\build.ps1 -Configuration Debug
.\build.ps1 -Deploy            # build + install (refuses while GTA V is running)
```

## Tuning

Everything in `Trapline.ini` is live-reloaded at script start (SHVDN reloads scripts with
Insert). Two knobs worth knowing:

- `RenderMode` — `Wedge` draws true arc segments from a streamed texture; `Node` uses
  plain rectangles and needs no textures at all. `Auto` picks Wedge and falls back.
- `TimeScale` — how far time slows while the wheel is open. `1.0` turns it off.

Product prices, tiers and heat live in `scripts\Trapline\drugs.json`.
