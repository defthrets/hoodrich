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

Their turf also has to be picked. Varrios Los Aztecas were El Corona in San
Andreas; the nearest equivalents here are the Latino blocks — Cypress Flats,
El Burro Heights, La Mesa. Worth confirming with Michael rather than guessing,
since he has been specific about turf every other time.

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
