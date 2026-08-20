# Hoodrich: full audit

70 source files, 29,877 lines of C#. 4,885 lines of data. Audited end to end —
cross-references, the social chain, money, entity lifecycle, per-frame cost, the
mission state machines, save round-trip, dead code.

**Ten things were fixed during the audit and are already pushed. Eleven are
written up below as recommendations, because they are your call rather than
mine.**

---

## What was wrong, and is now fixed

### 1. A war with the Vagos produced Ballas mourning their dead

`GangFor` mapped every rival-side post set — `RivalMourns`, `RivalGloats`,
`WarLiveRival` — to `"ballas"` flat. So whoever actually raided you, the other
half of the argument came out of Balla accounts. Ten minutes of Ballas grieving
over a fight in Rancho they were not in.

The war names who it is now.

### 2. Holding your own block said nothing

`WarHeld` and `WarLost` are events that fire, and neither had any words. `Build`
returned null and the main post silently never happened — only the argument
afterwards landed. Fifteen lines between them.

### 3. The Aztecas could be called out but had nothing to say back

They went into the registry, so "Start beef online" offers them — but with no
`YouDissAztecas` and no `DissedBackAztecas` both ends fell through to the
generic set, while every other gang had their own. Twelve lines.

### 4. `RideThrough` was written and wired to nothing

Six lines for the Rancho end of the torch job, sitting unreachable. It is its
own event now: a ride-through is loud, quick and nobody hit, which the block
describes differently from a drive-by that came to leave somebody on a pavement.

### 5. Loading an old save made peace with the Ballas and the Vagos

Beef is a number now, and those two are seeded below the hostility line. But
`LoadFrom` repopulates standings from the file, and a save written before the
change has them at zero — which reads as *no problem with you*. So every
existing save quietly ended the two feuds Franklin has never not been in.

A flag marks saves from here on; its absence puts them back exactly once.

### 6. The dump stage could skip the burn entirely

`TickDump` recorded the car only while you were sat in it **and** already inside
the dump radius. Park twenty-five metres short and walk the last bit and it held
nothing — so the mission announced there was no car to burn and jumped to the
payout, skipping the whole point of the job.

The car is remembered the whole way there now.

### 7. The stash house swept the street every frame

`Hush` and `ClearHousehold` are both gated on being at the door and both do a
full ped sweep, unthrottled. Standing in your own kitchen ran two world scans
per frame. Four times a second now; neither job is one you can catch happening.

### 8–10. Dead code, and a silent catch

`WheelPages.MaxLoanFor` and `Borrow` (28 lines) left behind when borrowing was
removed; `Pricing.StreetPrice`, which nothing had called since the ladder
replaced it; and the one `catch { }` in the mod that swallowed without a word.

---

## What is healthy

Worth saying, because it is most of it.

**Money is on one system.** Eleven places move the player's cash and every one
of them gets its number from `Pricing`. No hardcoded amounts anywhere outside
`Settings` and the data files. The ladder is literal end to end — a gram of weed
pays twenty dollars in the kitchen readout, the wheel, the corner and the
inventory valuation, because they all ask the same method.

**Every cross-reference resolves.** Every mission's target gang, every gang's
rivals and drugs, every dealer's stock, every leader's gang, every `rollsInto` —
all point at something that exists. Every one of the eight gangs has member
models and can actually turn up.

**The social chain has no holes.** 24 events, 63 post sets, 870 lines, 101
voices, 143 authors. Every event can find words. Every slot resolves. No voice
points at a missing set, no author at a missing voice, and there is not one
duplicated line across the whole catalogue.

**Teardown is thorough.** Every class that creates anything has a
`RestoreWorld`, Main calls all thirty of them, and the relationship groups that
get altered are captured and put back rather than assumed.

**The wheel is inside its budget.** 1,206 rectangles a frame at 1080p, down from
about 3,130 before the ring rework — a 61% cut, which is why the missing
segments stopped.

---

## Recommendations

Ordered by how much they matter.

### A. Five of seven gang leaders now stand on somebody else's turf

The turf pass moved the gangs; nobody moved the men. As it stands:

| Leader | Gang | Stands in | Which is now |
|---|---|---|---|
| OG Reese | Ballas | `RANCHO` | Vagos / Aztecas |
| El Tio | Vagos | `EBURO` | Marabunta |
| Chavo | Marabunta | `CYPRE` | Triads |
| Uncle Wei | Triads | `KOREAT` | Kkangpae (not a gang here) |
| Sarkis | Armenians | `ALTA` | nobody's |

Only Stretch and Bull are where they should be. This is the single largest
inconsistency in the mod. It needs coordinates rather than a guess from me —
same as everything else you have placed.

### B. `driveby_lamesa` has no site coordinate

It is `0, 0, 0`, so targets fall back to the zone centre. And it targets the
Vagos in La Mesa, which after the turf pass is not Vagos turf at all — they are
Rancho only. Either move the mission or move the gang.

### C. The Aztecas have no leader

Every other gang has one, so they cannot be talked to or joined. They were added
for a mission that then moved to the Vagos, so they currently exist only as
somebody to shoot at and post about.

### D. The coke ladder is upside down

An 8-ball is $850 for 3.5g — $243/g, against $200/g for a single. Every other
drug gets cheaper per unit as the bag gets bigger; coke gets dearer. It is your
number, so it stays, but be aware `SaleValue` values weight largest-deal-first,
so a stash of coke is worth *more* than the same weight in singles. Backwards
from every other product.

### E. Heroin's `basePrice` disagrees with its own ladder

`basePrice` 80, but the smallest deal is a point at $10 — $100/g. Only a
fallback for a drug with no ladder, so nothing reads it today. Worth making them
agree before something does.

### F. Mission pay does not climb

| | rank | pay |
|---|---|---|
| bikeride_courts | 0 | $400–800 |
| **torch_rancho** | 0 | **$1,600–2,600** |
| tags_run | 0 | $600–1,100 |
| hit_rancho | 0 | $900–1,600 |
| hit_grove | 0 | $1,100–1,900 |
| driveby_lamesa | 2 | $1,400–2,400 |

The second job pays the most in the chain, and five of six are rank 0 — so rank
gates almost nothing. Either torch_rancho comes down to about $900–1,400, or it
moves later.

### G. The Kkangpae exist in the feed but not the world

There is an author tagged `koreans` and a hashtag set for them, but no such
gang. gta-xtreme puts them on Ginger Street in Little Seoul, which is also where
Uncle Wei is standing. Adding them fixes that and gives the Triads a neighbour.

### H. Borrowing is unreachable but still loads and saves

`GangLoan` is still constructed, serialised and restored — the wheel entry is
just gone. Either delete it properly or give it a way back in.

### I. `MissionKind.RideOut` is declared and unused

No mission uses it. Harmless, but it is a kind the code branches on.

### J. The arcs cost more than they look

288 of the wheel's 1,206 rectangles are hairlines. Dropping the arc step cap
from 96 to 72 buys about 70 back with no visible difference.

### K. The turf map still has open questions

`TURF.md` records them: the Madrazo Cartel are Vagos and should stay out; the
Marabunta are in more places than the data says; and Merryweather is the only
faction on that map with a real claim to its own entry.

---

## Features worth building

Not fixes — things the mod is shaped to support and does not have yet.

**1. Beef you can cool down.** Standing only ever falls. Dissing costs 12,
killing costs 5, and nothing puts it back — so every gang trends toward
hostility and the world only gets worse. A way down from beef, through a
leader or through time, would make the standing system two-directional and
make dissing a decision rather than a ratchet.

**2. Turf that changes hands.** `TurfWatch` knows whose block you are on and
`GangWar` knows who won. Nothing connects them. Winning enough raids in a zone
should move it, and that is most of an endgame with the pieces already built.

**3. The stash house is the only place work happens.** One kitchen, one
cupboard. A second safe house would give the map somewhere else to be, and the
code takes a position as an argument already.

**4. Buyers who come back.** `PostUp` invents a customer per sale. Regulars —
somebody who asks for you by name and pays over for it — would give dealing a
memory, and the standing model is already there to hang it on.

**5. The plug can be robbed.** Ruban turns up with weight in a car and drives
away with your money. Nothing can go wrong with that, ever. It is the most
obvious missing risk in the economy.

**6. Weather and the hour matter to demand but not to anything else.** Rain on a
corner, a Sunday, a police helicopter overhead — the demand multiplier is
already the right place to hang all of it.

**7. The feed cannot be replied to.** You can post and you can be answered.
Answering back would close the loop, and `Argue` is nearly the whole mechanism.

---

*Audited at commit `5d570fe`. Everything under "fixed" is pushed.*
