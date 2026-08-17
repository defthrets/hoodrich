# Hoodrich

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

That drops `Hoodrich.dll` into `scripts\`, `Hoodrich.ini` next to it, and the data files
into `scripts\Hoodrich\`. Existing config and data are never overwritten.

To uninstall: delete `scripts\Hoodrich.dll`. Your save in `scripts\Hoodrich\save.json`
survives, so reinstalling picks up where you left off.

## Controls

By default Hoodrich **takes over the weapon wheel button** (`Mode=Replace` in the ini).
The vanilla weapon wheel is suppressed while the mod is loaded.

| Input | Action |
|---|---|
| Hold weapon-wheel button | Open the Hoodrich wheel |
| Mouse / right stick | Point at a segment |
| Release | Pick the highlighted segment |
| LMB / RT | Pick without releasing (the only way into a submenu) |
| RMB / LT | Back out one page, or close at the root |

Prefer to keep your weapon wheel? Set `Mode=Separate` in `Hoodrich.ini` and the wheel
moves to its own key (`Key=B` by default).

## Menu structure

Four wedges at the root — 90° each. Every tab owns its own sub-tabs rather than spilling
onto the root, and each one has a single job: **Gangs is where you do things, Reputation
is where you read the numbers those things produced.**

```
Weapons ─── Melee · Handguns · SMGs · Shotguns · Rifles · Sniper · Heavy · Thrown
                 └── each weapon, with the game's own icon and its ammo

Drugs ───── Supply ── your contacts ── their stock
        ├── Cut ───── product ─────── purity (100 / 75 / 50 / 33%)
        ├── Sell ──── what you have bagged
        ├── Status ── the books: every product's bulk and street-ready weight,
        │             plus where every supply line stands right now
        └── Stash     (next build)

Gangs ───── Turf ──── this block: who claims it, what it pays, what it costs
        └── one wedge per gang
              └── Join or Leave · Their plug · Their turf · Dossier

Reputation  Rank ──── the ladder: what you have passed, where you are,
        │            and what the next rung actually unlocks
        └── one wedge per gang: your rep, kills, deals and earnings with each
```

**Drugs → Status** is the import board. The panel lists every contact and its live state —
`inbound 340m`, `HERE NOW`, `at war with the families`, `works 5:00-21:00`, `ready to
call` — then totals for bulk, ready-to-sell, free space, street value, price context and
heat. The wedges are the products themselves: flick round the ring and each one reads out
its own bulk, its cut weight and purity, and what that's worth right now.

`Weapons` sits at index 0, straight up: Hoodrich took the weapon-wheel button, so getting
a gun back has to be the fastest thing on the wheel.

## What works right now (0.2.0)

Six wheel pages, and a supply chain you have to actually work.

**Supply → Cut → Sell.** You cannot buy street-ready product. You buy **bulk** from a
contact, and bulk is worthless until you cut and bag it.

- **Supply** — the **dock worker** is your one-stop shop: he can get you anything in the
  catalogue, at full price, in daylight hours only. Everyone else is a **gang contact** who
  carries one product and sells it 30% under the odds:

  | Contact | Product | Price | Rank |
  |---|---|---|---|
  | Dock Worker | everything | x1.00 | — |
  | Families Plug | Weed | x0.70 | — |
  | Wei Cheng Contact | Ecstasy | x0.70 | Soldier |
  | Lost MC Cook | Meth | x0.70 | Soldier |
  | Vagos Connection | Cocaine | x0.70 | Enforcer |
  | Armenian Connection | Heroin | x0.70 | Enforcer |

  Standing matters: run with a gang and their contact takes another **10%** off; reach 100
  rep with them without joining and it is **5%**. A gang at war with your crew **will not
  deal with you at any price** — which is the real cost of picking a side.

  Calling a contact arranges a **meet**: a blip appears, you drive there, the contact spawns
  when you get close, and you trade face to face. No map coordinates are hardcoded anywhere.
- **Cut** — turn bulk into street units at a purity you choose (100 / 75 / 50 / 33%).
  This is the greed dial: cutting to 50% doubles your units, but each is worth less and
  buyers get a chance to clock it, refuse the sale, and occasionally swing at you.
  Cutting takes real time and pins you in place, so *where* you do it matters.
- **Sell** — hand-to-hand to any ped on foot in front of you, with a handshake animation
  and a deal state machine that aborts if you walk off.

**Gangs.** The `Gang` page shows a live dossier — affiliation, rank, respect, gang rep,
kills for them, money made for them, deals closed, lookouts nearby — and lets you join or
walk out. Affiliating makes that gang respect you globally, so their people **back you up
in a fight**, and each one standing near you while you deal adds a lookout bonus to the
price. Walking out costs you rep with the crew you left.

**Turf.** Territory keys off GTA's own zone codes, so every neighbourhood in the map is
already a claimable block. Where you post up decides everything:

| Block | Price | Heat | What happens |
|---|---|---|---|
| Your gang's | x1.00 | x0.4 | Quiet. Your people back you up. |
| Unclaimed | x1.05 | x1.0 | Occasional stick-up if you linger |
| Rival's | **x1.35** | **x2.0** | They clock you dealing and come for you |

Rival aggression is targeted — specific peds who can actually *see* you get tasked onto
you, gated on line of sight and scaled by your heat. Nothing flips a whole faction to
hate-on-sight, so the rest of the world stays playable.

Turf **capture** is the next milestone; the `Claim` item is present but disabled.

### Fixing the turf map

The shipped zone codes are best-effort. To correct them, stand anywhere in game and pick
**Turf → Log zone** on the wheel: the exact `GET_NAME_OF_ZONE` code is printed on screen
and written to `scripts\Hoodrich.log`. Paste it into the right gang's `turf` list in
`scripts\Hoodrich\gangs.json`. **Turf → Dossier** dumps the whole current map to the log.

## Layout

```
src/Hoodrich/
  Main.cs              script entry point; single owner of the tick loop
  Core/                Paths, Log, IniFile, Json (hand-rolled), JsonFile, Settings
  UI/                  Draw (native primitives), Palette, RadialMenu, WheelController, Notify
  Wheel/WheelPages.cs  builds all six wheel pages from live game state
  Economy/             Drugs catalogue, Stash (bulk + packaged), Cutting, Pricing
  Supply/              supply contacts and the meet system
  Gangs/               gang definitions, registry, affiliation and backup
  Territory/TurfWatch  zone ownership, spotting, rival aggression
  Dealing/StreetDeal   buyer selection and the hand-to-hand deal
  State/               PlayerState, SaveGame
data/                  shipped data files, copied to scripts\Hoodrich\ on deploy
tools/                 self-contained Roslyn compiler + net48 reference assemblies
```

Data files, all live-editable in `scripts\Hoodrich\`:

| File | What it controls |
|---|---|
| `drugs.json` | product catalogue: prices, tiers, heat |
| `gangs.json` | gangs, colours, rivalries, **turf zone codes** |
| `suppliers.json` | contacts, prices, models, rank gates, hours |
| `save.json` | your progression (written by the mod) |

## Building

`build.ps1` deliberately does **not** use `dotnet build`. The .NET 8 SDK on this machine
is broken — `Microsoft.NETCore.App\8.0.28` is a partial install (3 files against 8.0.19's
184), so every `dotnet` invocation dies on a missing `hostpolicy.dll`. The build instead
drives a Roslyn `csc.exe` pulled into `tools\`, which needs no SDK, no Visual Studio and
no admin rights.

`Hoodrich.csproj` is kept in sync for whenever the SDK gets repaired, but `build.ps1` is
the authoritative build.

```powershell
.\build.ps1                    # -> build\Hoodrich.dll
.\build.ps1 -Configuration Debug
.\build.ps1 -Deploy            # build + install (refuses while GTA V is running)
```

## Tuning

Everything in `Hoodrich.ini` is live-reloaded at script start (SHVDN reloads scripts with
Insert). Two knobs worth knowing:

- `RenderMode` — `Wedge` draws true arc segments from a streamed texture; `Node` uses
  plain rectangles and needs no textures at all. `Auto` picks Wedge and falls back.
- `TimeScale` — how far time slows while the wheel is open. `1.0` turns it off.

Product prices, tiers and heat live in `scripts\Hoodrich\drugs.json`.
