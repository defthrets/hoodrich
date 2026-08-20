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

Their turf is settled: **wherever the game itself spawns Azteca peds**. Not an
invented block and not a San Andreas memory — the drive-by happens where you
would actually run into them.

That has to be looked up rather than guessed. The ped model is
`g_m_y_azteca_01`, and which zones it appears in lives in `popgroups.ymt` and
`popcycle.dat` inside the game's own RPFs — which means OpenIV to read, since
nothing outside it can open an RPF. Michael handles the OpenIV side.

Worth being straight about the likely answer before anyone goes looking: the
Aztecas may well have **no ambient spawn at all** in single player. The model
ships with the game, but plenty of gang models do without ever being placed in
a population group — they exist for missions and for the online creator. If
that turns out to be the case, the honest fallback is the Latino blocks next to
Vagos turf, Cypress Flats or El Burro Heights, and it should be called what it
is: a choice, not a lookup.

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
