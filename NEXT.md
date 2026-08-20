# Next: rework `driveby_grove` into the Aztecas job

Agreed with Michael, not yet built. Written down so nothing is lost between sessions.

## The mission, end to end

Lamar's drive-by job stops being about Grove and the Ballas and becomes a run at
**Varrios Los Aztecas on their own turf**. Five stages:

1. **Ride out.** The car that spawns for it is an **old rusty beater** — not the
   current pick. It is a car you are going to burn, and it should look like one
   from the moment you get in.
2. **The drive-by.** Getting the Aztecas **aggro is the whole objective**. No kill
   count, no survive-timer. They notice you, the stage is done.
3. **Escape.** Completing stage 2 gives you **one star**. Lose it.
4. **Dump it.** Drive the car to **(-284.604, -1652.626, 31.849)** — the park in
   La Puerta. On getting out you are **automatically handed a full jerry can**,
   already in your hands, with the objective to torch the car.
5. **Torch it.** **Pour petrol over the car and ignite it** — the real thing, not a
   button press. Once it goes up, **walk back to Lamar** to finish.

Lamar briefs all of it himself, and the brief has to be **lore accurate** — his
words, his reasons, and a real reason for going at the Aztecas rather than a
line that could be about anybody.

## The one thing to decide first

**The Aztecas are not in the registry, and GTA V has no ambient Azteca gang.**
The game ships the `g_m_y_azteca_01` ped model, but there is no
`AMBIENT_GANG_AZTECAS` relationship group to hang them off — the Latino ambient
groups belong to the Vagos and the Marabunta.

So they need adding to `data/gangs.json` as a gang in their own right, with a
relationship group **created at runtime** via `ADD_RELATIONSHIP_GROUP` rather
than a vanilla group name that does not exist. `Payback` already does exactly
this for its attackers and is the pattern to copy.

Their turf is settled, off the map Michael supplied: a **small pocket in
Rancho**, wedged between Ballas turf to the west and the Vagos strip to the
south and east. Not a territory -- a couple of streets. Which is the point of
them: they are the smallest thing on that map with a name, and going at them is
a different kind of job from going at the Ballas.

So the drive-by happens in **RANCHO**, and the gang gets that one zone rather
than a list. No OpenIV lookup needed after all -- the map answers it.

### About that map

Its own author says it was made for fun, may be wrong, and that the Marabunta
territories are incomplete. The comments bear that out, so it is a starting
point for a turf pass and not a source. What the thread adds, worth keeping:

- **Kkangpae and the Triads share Little Seoul.** Sometimes one spawns, sometimes
  the other. Two names on one territory rather than two territories.
- **Marabunta Grande are in far more places than the map shows** — Vespucci Beach
  near Floyd's flat, El Burro Heights, and a street a couple of blocks behind
  Lester's. The map's own Marabunta pockets are the part it admits to getting
  wrong, and one commenter reckons the yellow on the right is Marabunta rather
  than Vagos.
- **The Lost also hold Hookies and the farm by Grapeseed**, not only Stab City.
- **The Madrazo Cartel are Vagos.** Same assets, and the Vagos will fight
  alongside them. That is a reason NOT to add them as a gang of their own -- a
  faction the game treats as a reskin should not get its own turf, rivals and
  standing in here.
- Sandy Shores has the rednecks, and the Fooliganz turn up at the marina in the
  afternoons. Neither is Franklin's problem.

Merryweather is still worth a look on its own terms, being an actual separate
outfit rather than a reskin.

### The source to work from

**https://www.gta-xtreme.com/en/gang-locations-gta-5** — Michael's reference for
the turf pass. Read it and set every gang's `turf` zone list from it, in
`data/gangs.json` AND in the built-in defaults in `GangRegistry.AddDefaults`,
which have to agree or the fallback disagrees with the file.

Two things to hold on to while doing it:

- Zone codes are checked, not guessed. `Turf > Log zone` in game confirms one.
  A code that does not match costs a gang its turf silently.
- The Madrazo Cartel still do not get an entry, however the page lists them.
  The game treats them as Vagos.

## Also outstanding

**Lamar's chain gets reordered so the Aztecas drive-by is his second job.**
Touches `data/missions.json`, the unlock chain in `MissionRunner`, and the
wheel's job list -- all three, or a save ends up half-renumbered. Best done in
the same pass as building the mission, since both edit the same entry.

None of the thread disputes the Aztecas sitting in Rancho, which is the only bit
needed to build the mission.

Adding them properly also closes an older gap: the Triads and the Armenians
currently have no rivals, so they can never take part in a turf war.

## Files it touches

- `data/missions.json` — the `driveby_grove` entry
- `data/gangs.json` — the Aztecas
- `src/Hoodrich/Missions/MissionDef.cs` — stages the current `DriveBy` kind has no
  concept of
- `src/Hoodrich/Missions/MissionRunner.cs` — the five-stage flow, the star, the
  jerry can, the fire, the walk back
- `data/socials.json` — per the standing rule, a new mission ships with its own
  post templates
