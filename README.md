# Hoodrich

A drug-dealing and gang mod for GTA V, built from scratch, driven from a custom radial wheel
that replaces the weapon wheel.

You run with the Families. You buy weight, you take it home and bag it up, you post up on a
corner and let the trade come to you, and you try to be gone before the police or somebody
else's people decide you have been there long enough.

Zero external dependencies. One DLL, some JSON, ScriptHookVDotNet. Nothing else to install.

---

## What is in it

### The wheel

Holding the weapon-wheel button opens Hoodrich instead. **Tapping** it still holsters your
weapon, because that is what that button has always done and the mod should not fight muscle
memory.

Four wedges: **Weapons**, **Dealing**, **Gangs**, **Inventory**. Weapons hands the button back
to the game for a few seconds so you get the real weapon wheel, selection and all.

The wheel is a gateway and nothing more. It says "re-up" and "post up", not "turf price x1.05" —
every number lives on a readout screen you open deliberately.

### Dealing

Buy **weight** off Stretch or, later, the docks. Weight cannot be sold. Take it to the kitchen
counter at Aunt Denise's, pick what you are working and how far you stretch it, and you get
street-ready product at a purity you chose. Cut it hard and you get more units that are worth
less each and get handed back more often.

Then **post up**. You do not pick customers, you pick a **spot**:

- A dead alley has no footfall, so no sales and no heat
- A busy pavement pushes buyers at you and stacks heat with every one of them
- Buyers ask for a gram or an eighth, and prices are roughly what these things really cost
- Too much heat and a patrol comes to ask what you are standing there for

You are never locked in place. Walk the block; go too far and you have stopped working it.

### The police

- A cruiser sets off from a few streets away at a random point in a 50–210 second window,
  drives in on normal traffic rules, pulls in near you and sits for ten seconds
- Serving where a uniform can see you is **one star**, and that patrol drops what it is doing
  and comes for you
- A corner that has got hot enough draws a star on its own
- Stay put while an officer is on you and you get searched, cleaned out and fined

### The other gangs

- Deal on somebody's block and the ones who see you come and put hands on you — unarmed, because
  on foot it is a beating
- Work one pitch long enough and a carload turns up: one pass, three seconds of shooting from
  alongside you, then gone. Only if the car stalls do they get out, and their weapons are taken
  off them first
- Turf follows the vanilla map. Grove Street is in Davis and Davis is Ballas

### Stretch

The Families' leader, on his corner in Chamberlain Hills, marked with a weed leaf. Walk up,
press **D-pad right**, and talk to him properly — a panel with lines and choices, not a menu.

He puts you on, sells you weight, tells you which blocks are safe, and once you have moved
enough of it he tells you where it really comes from. That is what opens the docks.

### The docks

Phone the dock worker and he **drives out to you** in the same blacked-out Astron every time.
Real phone animation, real drive on real roads, spawned far enough out that you never see the
car appear. He pulls up, gets out, sells you anything in weight at the best price in the mod,
then walks back to the car and leaves.

### Lamar

In the courtyard on Chamberlain, marked with a skull. He has **work** — three jobs at a time out
of a bigger pool, one running at once, and he pays you himself when you come back.

- **Ride out** — take homies, go put hands on some Ballas
- **Drive-by** — same corner, everyone armed, from a car

Homies are real: they ride with you in your ped group, they fight, they can die, and losing one
costs rep off the payout.

### The stash house

Aunt Denise's on Forum Drive is yours from the start. No property to buy.

- **Inventory** at the house opens a two-column transfer screen — what is on you, what is at
  home, d-pad left and right to move it
- **The kitchen counter** is the only place product gets worked
- **The bed** sleeps six hours, heals you, saves the game, and a load puts you back there
- **Chop** is in the yard
- Denise is kept quiet while you are in there

### Reputation

Earned four ways, not one:

| | |
|---|---|
| Standing on your own blocks | a slow drip |
| Every sale | a nudge |
| Every purchase | a nudge |
| A rival dropped while working a corner | 12 |
| A job finished for Lamar | 40 |

---

## Installing

**Requirements**

- GTA V (Legacy or Enhanced — one build runs on both)
- [ScriptHookV](http://www.dev-c.com/gtav/scripthookv/)
- [ScriptHookVDotNet 3](https://github.com/scripthookvdotnet/scripthookvdotnet/releases)
- .NET Framework 4.8

**Steps**

1. Install ScriptHookV and ScriptHookVDotNet 3 if you have not already
2. Copy `Hoodrich.dll` and `Hoodrich.ini` into `Grand Theft Auto V\scripts\`
3. Copy the `data` folder in as `Grand Theft Auto V\scripts\Hoodrich\`
4. Launch the game

The mod reads its data from `scripts\Hoodrich\`, not from `scripts\`. Files in the wrong place
are silently ignored.

**Open All Interiors** is recommended — the stash house is only reachable with it.

---

## Configuring

`Hoodrich.ini` holds the settings; the JSON in `scripts\Hoodrich\` holds the content.

| File | What it decides |
|---|---|
| `gangs.json` | who exists, their colours, their turf, who they are at war with |
| `drugs.json` | the catalogue and street prices |
| `dealers.json` | suppliers. **Authoritative** — deleting somebody removes them |
| `leaders.json` | where each leader stands, facing which way, and their hours |
| `missions.json` | the work Lamar hands out |
| `zones.json` | zone geometry, generated from the game's own bounds |

Coordinates in `leaders.json` and `missions.json` take an optional `z`. Leave it at zero and the
ground is found automatically; set it when the spot is on a raised walkway, where "the ground" is
a floor below where you meant.

---

## Building

The build is self-contained. It does not need the .NET SDK — a Roslyn compiler lives in `tools\`.

```powershell
.\build.ps1                                    # build to .\build\Hoodrich.dll
.\build.ps1 -Deploy -Target Both               # build and install to both editions
.\build.ps1 -Deploy -Target Both -FreshData    # also overwrite the installed JSON
```

Without `-FreshData` the installed data files are left alone, and any that differ from source
are called out loudly — a build whose data does not match itself is worse than an overwrite.

`tools\make_map.ps1` draws every zone as a labelled SVG you can paint gang colours onto;
`tools\read_map.ps1` reads it back into `gangs.json`.

---

## Notes

- **One `Script` subclass.** SHVDN ticks scripts in an unspecified order, so Hoodrich has a
  single entry point and defines its own update order. One place to be exception-safe.
- **No external runtime dependencies.** `scripts\` is one shared assembly-resolution namespace,
  so a mod that drags in libraries can break its neighbours. The INI and JSON parsers are
  hand-rolled for that reason.
- **Everything fails soft.** A missing model, a texture that is not in this install, a native
  that behaves differently between editions — each is caught and degraded, never thrown.
- **The world gets put back.** Time scale, timecycle, relationships, every spawned ped and car
  are restored on unload, including after a crash.

## Licence

Personal project. GTA V and its assets belong to Rockstar Games.
