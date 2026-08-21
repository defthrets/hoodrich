# Hoodrich: second full audit

36,828 lines of C# across 72 files, nine data files, 34 icons. Run after the first
audit (`AUDIT.md`, taken at 29,877 lines), so this covers roughly seven thousand
lines of newer work as well as re-checking the old.

Everything below was verified against the source or against the live log from
Michael's own sessions. Nothing here is inferred.

---

## What was wrong, and is now fixed

### 1. The police kept turning up during raids

The one the player actually noticed. `GangWar` calls `HoldTheLaw(true)` on every
tick of a war and says so in its own comment, on the correct reasoning that the
game resets the max wanted level on a cutscene, a mission ending or an area
reload. It was not re-asserting anything. `LawHold.Hold` opened with:

```csharp
if (who == null || Holders.Contains(who)) return;
```

A caller already on the holder list got nothing at all. So the natives went out
once, at the start of the raid, and whatever the game did to them afterwards
stood for the rest of the fight.

Re-asking re-applies now, and clears any star already showing rather than only
capping it. The one thing that must still happen exactly once is reading the
ceiling to restore -- read it again while held and it returns zero, and the
police never come back at all.

`BikeRide` had the identical hole: one call at the start, never again. It
re-asserts in its tick too.

The two stars awarded at the *end* of a raid are also gone. They were the only
wanted level anybody ever saw near a war, so a guaranteed two stars every time
one ended was the same complaint as stars during it.

### 2. Rancho belonged to the wrong gang, and five hints were lying

`GangRegistry` resolves a contested zone silently in favour of whoever is listed
first, logging a warning nobody reads. From the live log, three sessions running:

```
WARN  Zone 'RANCHO' claimed by both aztecas and vagos; keeping aztecas.
WARN  Zone 'CHAMH' claimed by both families and ballas; keeping families.
```

The Aztecas sort earlier in `gangs.json`, so the Vagos held no Rancho at all --
while two of Lamar's briefs are built on Rancho being theirs. "Rancho is yellow
top to bottom" was narrated over turf the mod said belonged to somebody else,
and standing there showed Varrios Los Aztecas as the owner.

Worse, `turfHint` is shown in **eight** player-facing places -- joining a set,
twice in leader dialogue, the diss card, and three wheel pages -- and five of
the nine gangs had a hint naming places they did not hold. The Armenians told
you they run Alta, Burton and Pillbox Hill while sitting on La Puerta. The
Marabunta named Cypress Flats, Elysian Island and Textile City and held El Burro
and Vespucci. The Lost named Sandy Shores and did not have it.

Turf and hint agree for all nine now, with the missions and each gang's own
written voice as the tiebreakers. Verified after the change: no zone claimed
twice, every code resolves against `zones.json`, and every place a hint names is
a zone that gang actually holds -- nine of nine, where it was four.

Mission consistency went from four of six to five of six. The sixth,
`bikeride_courts`, targets Ballas on Families turf and is correct by design:
they come to your block.

### 3. Grimes took your money in silence

`GunScreen.OnBought` has been declared since the rack became a screen, documents
itself as "set by Main", and fires on every sale. Main never assigned it. The
compiler had been reporting it as `CS0649` on every single build.

Wired, with two lists of lines on `ArmourerTalk` beside the voice they are said
in -- handing somebody a weapon and handing them a box of rounds are not the
same transaction and he does not talk about them the same way.

### 4. The ini promised eleven settings that did nothing

`[TurfWars]` documented a war economy in real detail: a reinforcement pool, an
attack cost rising with the square of intensity, health, armour and accuracy per
defender. Not one of its eleven keys was read by anything. A gang war takes its
pool and pacing from constants in `GangWar.cs`, so editing `WarMemberHealth`
changed nothing and never had.

Removed. The shipped template is now seventy keys and every one of them is read.

Michael's own deployed ini carries **21** dead keys rather than eleven, because
`build.ps1` deliberately never overwrites it and old versions have accumulated:
`MaxHideouts`, `StartingRespect`, `BulkSaleDiscountPercent`, `ShowTurfOnMap`,
`TurfBlipAlpha`, `[Wheel] Texture` and `TextureDict`. Left alone pending a
decision, since it is a live config file.

### 5. Dead enum

`Stance` -- four values, declared in `GangDef.cs`, referenced by nothing in the
entire mod. Removed.

---

## Checked, and genuinely sound

Listed so the coverage is legible and this does not read as a complaint list.

- **Callback wiring.** Every `public Action`/`Func` field in the mod is assigned
  somewhere except the one fixed above.
- **Save round-trip.** Every file with a `ToJson` writes and reads the same key
  set. The single mismatch, `inventory` in `PlayerState`, is the deliberate
  0.1.0 migration and is commented as such.
- **Entity lifecycle.** All seventeen files that create peds, vehicles, props or
  blips release them, and each has a teardown method reachable from `Main` on
  unload. `BikeRide` and `TagRun` look unreferenced from `Main` but are owned by
  `MissionRunner`, whose `RestoreWorld()` calls both.
- **Panel scroll arithmetic.** `WindowEnd` and `MaxScroll` were replicated
  offline and stressed over 20,000 random panel states: the last row is always
  reachable at maximum scroll, and no row can exceed the 28-cell budget.
- **Icons.** All 34 PNGs referenced by the code exist on disk; none is unused.
- **House rules.** No emoji anywhere in code or data. No colour hashtags left --
  every gang tag names the gang.
- **Data referential integrity.** Every drug a gang or dealer sells exists;
  every gang named by a mission, leader or dealer exists; every turf code
  resolves. `joints` looks orphaned but is `madeOnly`, produced from weed.
- **Mission ladder.** `minRank` runs 0, 0, 1, 1, 2, 3 with rep and pay both
  ascending monotonically.
- **Payback flavours.** `Flavour.Guns` has no explicit `case` in `Warning()` but
  is covered by the `default:` arm.

---

## Method note

The first attempt at this ran thirteen parallel auditing agents with adversarial
verification. It died twice on the session usage limit having produced nothing,
after burning about 1.46M tokens on retries. The findings above came from
working the codebase directly: deterministic whole-repo checks in Python for
anything mechanical (wiring, round-trips, referential integrity, icon coverage),
targeted reads for anything that needed judgement, and the live log as evidence
wherever behaviour was in question.

That is worth recording. For a codebase this size the scripted checks found more
real defects per token than the agent fan-out did, because most of what was
wrong was a mismatch between two files rather than something requiring a whole
subsystem to be read and understood.

---

## Still open

- **The grow room.** The streaming fix is deployed and unverified; it needs one
  walk through that door. It now logs four numbers on entry that separate the
  three failure modes -- IPL never loaded, interior not at that coordinate,
  coordinate inside a wall.
- **`@ortega_vla`** reads as Varrios Los Aztecas and he is Vagos. Renaming him
  changes his generated avatar, which is derived from the handle, so it was left
  alone.
- **The deployed ini's 21 dead keys**, above.
- **Runtime behaviour generally.** Nothing in this audit can see whether a marker
  renders where you are standing, whether an NPC clips a doorway, or whether an
  animation reads right. That still needs playing.
