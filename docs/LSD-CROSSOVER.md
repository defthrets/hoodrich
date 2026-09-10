# LSD — crossover notes

Everything another mod needs in order to sell, hold, take or react to acid, without
referencing either assembly at compile time.

Written 2026-09-10. Two mods are involved and they do **not** reference each other:

| | repo | what it owns |
|---|---|---|
| **Posted Up** (Hoodrich) | `github.com/defthrets/hoodrich` | the product: price, weight, icon, inventory, dealing, the feed |
| **Bare Minimum** | `github.com/defthrets/bare-minimum` | what it does to the player: the meters, the trip, the comedown |

If you only want one of those, you only need one of the sections below.

---

## 1. The product (Posted Up)

`data/drugs.json`:

```json
{
  "id": "lsd",
  "name": "LSD",
  "tag": "LSD",
  "tier": 2,
  "basePrice": 20,
  "bulkPrice": 4,
  "lotGrams": 10000,
  "heatFactor": 0.6,
  "splitVerb": "Cut",
  "unitName": "tabs",
  "workVerb": "Cutting sheets",
  "counted": true,
  "deals": [
    { "quantity": 1, "label": "a tab",    "price": 20 },
    { "quantity": 2, "label": "two tabs", "price": 35 },
    { "quantity": 5, "label": "five tabs","price": 80 }
  ],
  "codeWord": "paper"
}
```

`counted: true` is the important one — it is sold by the **tab**, not by weight, like the
pills and unlike everything else. A gram of acid is a phrase nobody has ever said.

There is a matching fallback registration in `Economy/Drugs.cs` → `AddDefaults()`, so the
drug still exists if `drugs.json` is missing or older than the dll.

**Icon**: `data/icons/acid.png` — a torn stamp with a smiley on it, 128×128, white on
transparent, tinted at runtime. `Api/Drugs.IconOf` answers to both `"lsd"` and `"acid"`.

### Reading it from another mod

`Hoodrich.Api.Drugs` is the public surface. It is **late-bound on purpose** — see the long
comment at the top of `Api/Drugs.cs`: a GTA `scripts\` folder is one assembly-resolution
namespace, so two mods that both hard-reference a third must agree about its version
forever, and the day they stop the failure is a `TypeLoadException` with no log, because the
thing that writes the log is the thing that did not load.

Copy the resolver pattern out of Bare Minimum's `Food/Dope.cs`. In outline:

```csharp
foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
{
    if (asm.GetName().Name != "Hoodrich") continue;

    var type = asm.GetType("Hoodrich.Api.Drugs");
    if (type == null) continue;

    // ALWAYS read ApiVersion FIRST and bail on a number you do not know.
    var api = type.GetProperty("ApiVersion", BindingFlags.Public | BindingFlags.Static);
    if ((int)api.GetValue(null, null) != 1) return;   // quietly use your own settings

    // ...then bind the members you want.
}
```

**Retry, do not resolve once.** SHVDN builds scripts in whatever order it finds them, so on
about half of all launches your script is constructed before Hoodrich exists in the
AppDomain at all. A bridge that looks once at start-up is permanently absent on those
launches only — intermittent, and not reproducible by whoever wrote it. Retry every couple
of seconds and give up after thirty.

Members worth knowing:

| member | gives you |
|---|---|
| `Ready` | Posted Up is here **and** has finished starting up |
| `Ids()` | every drug id, `lsd` among them |
| `NameOf(id)` / `IconOf(id)` | the label and an absolute png path |
| `CountedOf(id)` | true for `lsd` — show tabs, not grams |
| `AmountOf(id, qty)` | "three tabs", already worded |
| `Use(id)` | take one. Returns `null` on success, or **that mod's own sentence** for why not |

`Use` returning a written refusal rather than a bool is deliberate: the wording already
exists for a screen, so there is nothing to invent on your side.

---

## 2. What it does to the player (Bare Minimum)

`src/BareMinimum/Food/Dope.cs`, in the `Doses` table:

```csharp
{ "lsd", new Dose {
    Hunger = -0.14f, Wake = 0.45f, Trip = 12f,
    Tint = Color.FromArgb(255, 206, 130, 232),
    Desc = "The road is breathing. Give it a minute." } },
```

- **`Hunger = -0.14`** — nobody eats on acid, and the meter says so.
- **`Wake = +0.45`** — it keeps you up, about half as hard as a stimulant. A night on it is a
  night you did not sleep, not a night you were awake for.
- **`Trip = 12`** — real minutes the picture is bent.
- **No `Wired`** — it does not pin the energy bar. It gives you nothing to run on.
- **No `Crash`** — deliberate, not an oversight. The way off acid is a long flat glide; the
  tired walk the stimulants get would say the wrong thing about it.

`Trip` is a separate field from `Wired` because they are separate clocks, and a drug could
have both. The comedown timer arms off **whichever lasts longer** — crashing a man whose
screen is still bending is two states arguing about one body.

### The trip

`src/BareMinimum/Needs/Trip.cs`. Everything it does is to the screen:

- **`drug_flying_base`**, the timecycle the game's own peyote trips run on. Nothing is drawn
  by the mod, so there is nothing to keep in step with a graphics setting or a time of day.
- The strength **swells** on two periods that do not divide into each other (17 s and 6.7 s),
  so it never settles into a filter and never repeats. A timecycle held at one strength is
  something you stop seeing after a minute, which is the opposite of the point.
- Never on a countable beat: **a trip that pulsed to a rhythm is a strobe**, which is a
  different and much worse thing to do to somebody.
- Up over 25 s, down over 45 s. The one thing a trip is not is sudden.
- `[Effects] TripStrength` 0–1 scales the lot. **0 still runs the drug** — twelve minutes,
  the stomach, the sleep meter — it just stops being something you look at.

### The two collisions, if you are writing something similar

1. **There is ONE timecycle slot on the game.** `Effects.Wobble` wants it when the sleep
   meter bottoms out. It *stands down* for the trip rather than being overwritten, so neither
   clears the other's modifier out from under it. If your mod also sets a timecycle, expect
   to lose to whichever wrote last — decide the priority explicitly.
2. **There is ONE movement clipset slot on a ped.** Bare Minimum's queue, highest first:
   drink → comedown → hunger (`move_m@injured`) → thirst (`move_m@tired`) → sleep
   (`move_m@drunk@moderatedrunk`). LSD takes none of it.

### Reading the state from outside

`BareMinimum.Food.Dope` exposes statics you can late-bind exactly as above:

| member | gives you |
|---|---|
| `TripLeft` | seconds of trip remaining, or 0 |
| `TripTotal` | how long the whole trip is, so a curve knows where it is |
| `Crashing` | coming down off a stimulant right now |
| `Blacking` | two-plus doses of sedative in him — the xanax blackouts |

A second tab while one is running **extends** it rather than starting a second. There is one
screen and one man.

---

## 3. The feed (Posted Up)

`data/socials.json` gained `SaleLSD` and `BigSaleLSD`, plus ambient lines.

The mechanism is general and worth knowing about even if you never touch acid: a post set
named **`<Event><Subject>`** is used instead of `<Event>` when one exists. `Sale` becomes
`SaleLSD` when LSD is what moved. See `Social/SocialFeed.cs` → `SetFor`.

- Letters and digits only, so `Cocaine` gives `SaleCocaine`.
- It only ever **narrows**: an unknown combination falls back to the plain set, so a set named
  after a product that does not exist is dead weight rather than a silently missing post.
- Any drug, gang or job can have its own words by adding a set to the json and touching no
  code at all.

---

## 4. Known loose ends

- The `codeWord` on the **ecstasy** entry is still `"blues"`, which is oxycodone slang left
  over from when that entry was named Oxycodone. Not LSD's problem, but it is in the same
  file and reads oddly in dealer dialogue.
- `Rack` API (the HUD row Fumes stands in) is at **v2** and the version check is exact
  equality — **ship Fumes and Bare Minimum together** or the fuel gauge silently falls back
  to its own ini position.
- Nothing in Posted Up knows how long a high lasts. The minutes in Bare Minimum's table are
  that mod's own reckoning, not something to ask for.
