# Turf: everything we know, in one place

Assembled so the next turf pass starts from a single picture instead of four
half-remembered ones. Sources, in descending order of trust:

1. **gtabase** — the character and gang pages read in full during the lore pass.
   The most reliable thing we have used so far, and where the current turf lists
   came from.
2. **The Reddit turf map** Michael supplied. Useful for shape and adjacency. Its
   own author says it was made for fun, may be wrong, and that the Marabunta
   territories are incomplete.
3. **The Reddit comments under it.** Several corrections, listed below, and they
   are the reason the map is a starting point rather than a source.
4. **gta-xtreme's gang locations page** — not read yet. To be worked through
   next: https://www.gta-xtreme.com/en/gang-locations-gta-5

Zone codes are the game's own. **Check one in game with `Turf > Log zone` before
committing it** — a code that does not match costs a gang its turf silently, with
no error to notice.

---

## Where things stand now

| Gang | Turf (zone codes) | Rivals |
|---|---|---|
| Varrios Los Aztecas | `RANCHO` | vagos, ballas, families |
| The Families | `STRAW` `CHAMH` `DAVIS` | ballas, vagos, lost |
| Ballas | `DAVIS` `CHAMH` `RANCHO` | families, marabunta |
| Los Santos Vagos | `RANCHO` `CYPRE` | families, ballas, marabunta |
| Marabunta Grande | `ELBURRO` `VESP` `DESRT` `EVINE` | vagos, ballas |
| The Lost MC | `SLAB` `DESRT` `CHU` `GRAPES` `EVINE` | families |
| Wei Cheng Triads | `KOREAT` | *(none)* |
| Armenian Mob | `LAPUER` | *(none)* |

Four gangs share the Davis/Chamberlain/Rancho block, which is correct and is the
whole engine of the mod — Families, Ballas, Vagos and now the Aztecas are on top
of each other, and that is why raids happen there and nowhere else.

---

## What the map and thread change

**Confirmed by the map, already applied**

- The Aztecas are a pocket in **Rancho**, between Ballas to the west and the
  Vagos strip to the south and east. Small — a couple of streets, not a
  territory. Applied.
- Grove Street is Ballas, not Families. Applied in an earlier pass.

**Corrections from the thread, not yet applied**

- **Kkangpae and the Triads share Little Seoul.** Sometimes one spawns, sometimes
  the other, reliably in front of the apartment block by the petrol station. This
  is one territory with two names, not two territories — so it does not need a
  second gang, it needs the Triads' entry to say so.
- **Marabunta Grande are in more places than either the map or our file shows.**
  Named in the thread: **Vespucci Beach** near Floyd's flat, **El Burro Heights**,
  and a street two blocks behind Lester's. Our `VESP` may already cover the
  first. The map's Marabunta pockets are the part its author admits getting
  wrong.
- **The Lost also hold Hookies and the farm by Grapeseed.** We have `GRAPES`
  already; Hookies is the gap.
- One commenter reckons **the yellow on the right of the map is Marabunta, not
  Vagos.** Worth resolving against gta-xtreme before moving anything.

**Deliberately not gangs**

- **Madrazo Cartel.** The game treats them as Vagos — same assets, and the Vagos
  will fight alongside them; somebody tested it by luring the cartel into the
  city. Giving a reskin its own turf, rivals and standing would stage wars
  between two groups the engine considers the same people.
- **Rednecks (Sandy Shores), Fooliganz (marina, afternoons), The Professionals.**
  Real enough, none of them Franklin's problem.

**Might be worth adding**

- **Merryweather Security.** An actual separate outfit rather than a reskin, and
  the only faction on that map with a claim to its own entry.

---

## The two open gaps

1. **The Triads and the Armenians have no rivals.** They can never take part in a
   turf war, so they are names in a list rather than gangs. Either give them
   rivals or accept they are scenery and say so in the file.
2. **`DESRT` and `EVINE` belong to both the Lost and the Marabunta**, which is
   either correct — two outfits in the same desert — or a leftover. Resolve it
   against gta-xtreme.

---

## Where the codes live

Both of these have to agree, or the built-in fallback contradicts the file the
moment the JSON fails to load:

- `data/gangs.json` — the `turf` array on each gang
- `src/Hoodrich/Gangs/GangRegistry.cs`, `AddDefaults()` — the same lists again
