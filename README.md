# Hoodrich

A drug-dealing and gang mod for GTA V, built from scratch, driven from a custom phone that
replaces the in-game one. Your weapon wheel is left alone.

You run with the Families. You buy weight off a plug, you take it home and cut it, you post up
on a corner and let the trade come to you, and you try to be gone before the police or somebody
else's people decide you have been there long enough.

**Zero external dependencies.** One DLL, some JSON, ScriptHookVDotNet. No Lua, no NativeUI, no
config framework, no installer. The INI parser and the JSON parser are both hand-rolled and
live in `src/Hoodrich/Core`.

```
37,156 lines of C# across 72 files   ·   9 data files   ·   66 icons drawn from scratch
```

---

## Install

1. **ScriptHookV** and **ScriptHookVDotNet 3** — the only prerequisites.
2. Drop `Hoodrich.dll`, `Hoodrich.ini` and the `Hoodrich/` data folder into `scripts/`.
3. Load a save. Hoodrich is on the phone button.

Works on both Legacy and Enhanced. `build.ps1 -Deploy -Target Both` puts it in both.

---

## The phone

Press the **phone button**. Apps on the home screen, arrows to move, Enter to open one,
Backspace to go back and to put it away.

It used to be a radial wheel on the weapon-wheel button, and that was the wrong button. A mod
about dealing was charging the player the one control a GTA player uses most — every time you
wanted a gun you got a business menu, and the menu's own first entry was a **Weapons** wedge
whose only job was apologising for that. The phone costs nothing by comparison: the vanilla
phone is a contacts list and an email client for missions that are over by the time this mod is
interesting. `SelectWeapon` is now untouched — un-disabled, un-hidden, un-read.

- **Apps on the home screen, text menus inside them.** The top level is a grid you point at;
  everything below it is a list with the icon, the name, what it does and its value on one line.
- **The content layer did not change.** Every page is still built by `WheelPages`, still a
  `WheelPage` of `WheelItem`s. That was always a list — it was only ever *drawn* as a ring — so
  the phone is a second presentation of the same tree and no business rule moved.
- **The HUD stands down while it is up.** The handset stands on the right, which is where the
  posted-up readout and the licence strip live, and everything in this mod draws in call order.
- **Two keys, one button.** `INPUT_PHONE` and `INPUT_CELLPHONE_UP` are the same key on PC by
  default, so a plain toggle reads "scroll up" as "put the phone away". Closing on the phone
  button is conditional on the up direction not also being down.

Apps: **Dealing · Contacts · Gangs · Inventory · Socials**, and below those the supply, dealer,
stock, sell, gang, turf and start-over pages.

---

## Dealing

### Weight, and what you do to it

Bulk **weight** cannot be sold. It has to be worked first.

- **Buy** it off a gang leader, or off Tao Cheng at the port once you have moved enough to be
  told he exists.
- **Cut it** in the kitchen. You pick how far you step on it; you do not pick the bag size,
  because the stash is measured by weight and an ounce and twenty-eight singles are the same
  entry in it.
- **Post up** on a corner and let buyers come to you.

### Purity is the whole economy

Everything you cut is stored at a purity, and purity does two jobs at once:

- **It multiplies.** Cutting 100g at 50% gives you 200g to sell.
- **It gets noticed.** Stepped-on product gets handed back, and a refusal is not just a lost
  sale — the block remembers. Your product reputation moves every time somebody buys *or*
  refuses, and the feed starts talking about it either way.

A bag fronted to you by your own set arrives at **75%** — already stepped on once before it
reached you, so the kitchen is worth walking into.

### The catalogue

| Product | Tier | Street | Bulk | Notes |
|---|---|---|---|---|
| Marijuana | 1 | $20 | $4 | rolls into pre-rolls |
| Oxycodone | 1 | $25 | $7 | counted in pills |
| Alprazolam | 2 | $40 | $11 | counted in bars |
| Crack | 2 | $100 | $26 | |
| Meth | 3 | $200 | $48 | |
| Cocaine | 4 | $250 | $55 | |
| Heroin | 5 | $100 | $21 | |
| Pre-rolls | 1 | $25 | — | made only, never bought |

Every one is a line in `data/drugs.json` with its own deal sizes, verbs and heat factor. Adding
one is a JSON entry and an icon.

### Posting up

You pick a **spot**, not a customer. The footfall there decides both how fast product moves and
how hot the corner gets — a dead alley sells nothing and draws nothing; a busy pavement does
both. Stand somewhere long enough and you get clocked, and then it is a police matter.

---

## The sets

Nine gangs, each with its own turf, product, rivals, colour and hand-drawn emblem.

| Gang | Turf | Moves |
|---|---|---|
| The Families | Chamberlain Hills, Strawberry | weed, oxy, xanax |
| Ballas | Davis, Vespucci | crack, weed |
| Los Santos Vagos | Rancho, La Mesa, Murrieta, El Burro | coke, meth |
| Marabunta Grande | Cypress Flats, Elysian Island, Textile City | meth, coke |
| Varrios Los Aztecas | Banning | weed, coke |
| The Lost MC | Stab City, Grapeseed, Sandy Shores | meth, heroin |
| Wei Cheng Triads | Mirror Park, Hawick | oxy, heroin, xanax |
| Armenian Mob | Alta, Burton, Pillbox Hill | heroin |
| Kkangpae | Little Seoul | meth, coke |

Every gang's `turfHint` names exactly the zones it actually holds, and no zone is claimed twice
— both of which are checked, because the registry resolves a contested zone silently and a
wrong claim costs a gang its turf with nothing on screen to say so.

### Standing with each of them

Kept **per gang**, not as one global number: rep, kills, deals, money earned and time affiliated.
Fall far enough with a set and you are **beefing** with them, at which point their people go for
you on sight.

### Leaders

Nine of them, each holding court on his own set's block: **Gerald** (Families, Chamberlain),
**OG Reese** (Ballas, Davis), **El Tio** (Vagos, Rancho), **Chavo** (Marabunta, Cypress Flats),
**Bull** (Lost, Stab City), **Uncle Wei** (Triads, Mirror Park), **Sarkis** (Armenians, Alta),
**Chuy** (Aztecas, Banning), **Mr Kim** (Kkangpae, Little Seoul).

Walk up and you get a conversation: join, buy weight, ask about work, ask who they are beefing
with. Joining fronts you a starter bag.

### Wars

Somebody raids your block and it becomes a fight. Defenders **hold a perimeter** rather than
chasing the attacker down the street — posted on a ring around the spot, spread evenly, with a
radius that closes up as the group gets smaller so four men do not leave a gap.

The police stay out of it. Nobody calls in a gang war while it is happening and nobody calls it
in afterwards either.

---

## Missions

Six, from Lamar, gated on rank.

| Job | Rank | Pay |
|---|---|---|
| Get on a bike with me | Pee-Wee | $400–800 |
| Spin Jamestown, burn the whip | Pee-Wee | $900–1,500 |
| Cross this fool out | Soldier | $1,100–1,700 |
| The cut house in Rancho | Soldier | $1,400–2,100 |
| Get 'em off Grove | Enforcer | $1,700–2,500 |
| Spin they route in La Mesa | Shotcaller | $2,000–3,000 |

They include a bike ride that turns into a straightener on the courts, a drive-by where the
homies stay in the car until it is time to burn it, and a tag run where you cross out somebody
else's wall. Homies follow as bodyguards and get back in the car afterwards.

**Ranks:** Pee-Wee → Soldier → Enforcer → Shotcaller → OG.

---

## Socials

A working in-game feed, and the largest single thing in the mod by content.

```
174 accounts   ·   101 written voices   ·   70 post sets   ·   2,462 lines
```

- **Everybody has their own voice.** A hundred and one named characters — Wei Cheng, Tao Cheng,
  Lamar, Stretch, the Weazel newsdesk, a bail bondsman, a bird-watcher, the barber on Grove —
  say their own lines and nobody else's. An account with a written voice is never given somebody
  else's words, which is why the LSPD does not post a shop's opening hours.
- **Rank-and-file accounts** for all nine sets speak the shared templates, so a diss aimed at
  the Triads gets answered by Triads.
- **It reacts.** Sales, busts, bodies, wars, missions, tags, joining and leaving a set, and a
  drive-by all reach the feed.
- **Your product gets talked about.** Good work earns praise, weak work earns complaints — from
  civilians and your own set only, never from rivals, because a Balla telling you your work is
  good is not a compliment. And it *hints* rather than naming you: not one of the forty-eight
  product posts says Franklin.
- **You can post back.** Say something, or name a set and take the consequences — they answer
  on the feed within the minute, and then somebody comes to find you.

Tweets arrive as toasts down the right-hand side, or you open the full screen and read the
timeline with tabs for what is about you.

---

## Around the map

- **The stash house** — Aunt Denise's. Holds 300kg. Sleep there to pass time.
- **The kitchen** — where weight becomes product.
- **The grow room** and **the pill press** — real interiors, streamed in and waited on properly.
- **Grimes** — sells guns and ammo out of the courtyard on Forum Drive, and says something when
  money changes hands.
- **Tao Cheng** — call him and he drives over from the port, parks at the kerb, opens the boot,
  walks the box through the house and puts it down in the store room.
- **Boombox, parked car, corner life** — the block has people on it who talk to each other.

Franklin's own block never spawns hostiles. It is his hood; he does not get jumped outside his
own house.

---

## The HUD

Every surface is on the same art: the phone, the dialogue panel, the info panels, the kitchen,
the stash, the feed and the toasts. **66 icons**, all drawn from scratch as white masks and
tinted at draw time, so the same file is the set's colour on a gang row and the money colour on
a price.

They are generated by `tools/make_icons.py` and checked against each other at the size the game
actually draws them — every pair of the 2,145 is at least 0.100 apart, because two icons that
measure the same *are* the same icon.

Drugs look like paraphernalia rather than pill bottles: weed is a bong, meth is a shard cluster,
coke is a razor blade, crack is a cut stone, heroin is a syringe. Sixteen more products are
drawn and waiting for the catalogue to grow.

---

## Under it

- **Saves** alongside your GTA save, round-tripped field for field.
- **Everything is data.** Gangs, drugs, dealers, leaders, missions, zones, tags, weapons and the
  whole social library are JSON. New gang, new drug, new mission — no rebuild.
- **`Hoodrich.ini`** — 70 keys, every one of them read by something. Settings that do nothing
  are worse than no settings.
- **Deploy never overwrites your ini.**

### Build

```powershell
powershell -NoProfile -File ./build.ps1 -Deploy -Target Both -FreshData
```

Compiles with a self-contained Roslyn in `tools/`, so it does not need a .NET SDK installed.

---

## Documents

| File | What it is |
|---|---|
| `AUDIT.md` | first full audit, at 29,877 lines |
| `AUDIT2.md` | second full audit — what was wrong, what was checked and found sound |
| `TURF.md` | every source the turf table was built from, and how much each is trusted |
| `COMBAT.md` | the combat attribute and defensive-area findings, verified against the assembly |
| `BLIPS.md` | blip sprite reference |
| `SPRITES.md` | the usable in-game sprite dictionaries |
| `TEXTFORMAT.md` | Rockstar's `~tag~` codes |
| `NEXT.md` | what is agreed and not yet built |
| `preview/` | the icon sheet, and every wheel redesign rendered as a real image (the wheel is gone; kept as the record of how it was designed) |

---

Built with [Claude Code](https://claude.com/claude-code).
