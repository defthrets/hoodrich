# Posted Up

**A drug-dealing and gang mod for GTA V, built from scratch. You pick the corner, not the customer.**

---

You run with the Chamberlain Gangster Families. You get hold of some product, take it home
and cut it, stand somewhere busy and let the trade come to you — and try to be gone before
the police or somebody else's people decide you have been there long enough.

Everything runs off a phone that replaces the in-game one. Your weapon wheel is left exactly
as it was.

---

## The loop

**Get hold of it.** Gerald fronts you the first two packages for nothing — you do not need
money to start. After that you buy: off him by the gram, or off the man at the port by the
brick, once you have proved you come back.

**Cut it.** Weight bought by the brick will not sell to anybody. It goes to the sink in
Denise's kitchen first, and how hard you step on it is the only real decision in the mod.
100g at half strength is 200g to sell. It is also obviously weak, and the block can tell.

**Stand somewhere.** You do not pick customers, you pick a **spot**. A busy pavement sells
fast and gets you noticed fast; an empty street does neither. Buyers walk up and ask for an
amount, and what one block pays is not what the next one pays.

**Do not still be there.** Heat builds where you stand and sticks to the *ground*, not to
you. Come back to the same corner and you pick up where you left it. A different block is a
clean start, and a quiet corner cools off on its own.

---

## What is in it

**Nine gangs**, each with a named leader standing on their own turf, their own colours, their
own zones and their own standing with you — Families, Ballas, Vagos, Aztecas, Marabunta
Grande, The Lost MC, the Wei Cheng Triads, the Armenian Mob and the Kkangpae. Rep, bodies,
money and beef are tracked per gang, not as one number.

**A reputation that is separate from your rank.** Sell decent product and your name climbs.
Step on everything and it does not — and a refusal is never just one lost sale. They
remember, they tell people, and some of them want to do something about it.

**Six jobs from Lamar**, gated on your standing and written as one continuous argument he is
having about the block: a straightener at the Chamberlain courts, a drive-by through
Jamestown, going over a rival's tags, a cut house in Rancho, getting the Ballas off Grove,
and a route through La Mesa. He rides out with you, keeps up, and does not shut up.

**A police system that is not a wanted level.** Patrols roll the blocks you work whether or
not anybody is after you. Work one spot long enough and they stop driving past and start
stopping. Getting searched holding product costs you the product; holding nothing costs you
a fine — which is why dropping a bag before they reach you works. And stood near a car with
no stars, you can put a finger up at them, which they cannot do you for and they know it.

**A social feed that reacts to what you actually do.** 173 people with fixed handles and
avatars, 1,600 template lines across 93 event sets, filled in with the block you are on, the
set you run with and the money involved. Post up and the word goes out in a language that
does not say anything outright.

**Seven products** from weed to heroin, each with its own price, heat and street name. Eleven
cars from Hao, all on competition suspension, permanent and persistent across saves. Guns
from Stretch with extended mags on everything. Bodyguards who lock the doors once you are
rolling. Turf war, dead drops, a stash house, graffiti.

---

## Built the awkward way, on purpose

**Zero external dependencies.** No LemonUI, no NativeUI, no Newtonsoft, no Lua, no OpenIV, no
asset replacement, no installer. One DLL and some JSON. Nothing in your scripts folder can
lose a version fight with it, and no game file is modified — uninstalling is deleting three
things.

**Everything is data.** 10 JSON files you can edit: drugs, prices, gangs, turf, dealers,
missions, cars, weapons, and every line of the social feed. Break one and the mod says so in
the log and falls back to its built-in copy rather than failing to load. 70 settings in a
commented INI; delete any line to go back to its default.

**105 icons, drawn from scratch** for this mod. No Rockstar assets ship with it.

```
62,582 lines of C# across 94 files · 10 data files · 105 original icons
Works on GTA V Legacy and GTA V Enhanced from the same build
```

---

## Requirements

- **ScriptHookV** — not bundled by anybody, ever; its package grants no permission to pass it
  on. Thirty seconds from dev-c.com.
- **ScriptHookVDotNet 3** — included in the full download (zlib licensed, notices intact), or
  use your own if it is newer.
- .NET Framework 4.8 — already on Windows 10 and 11.

**Install:** drag the contents of the zip into your GTA V folder and say yes to merging.

Single player only.

---

## Honest notes

- The Wei Cheng business at the port is built but **switched off**, and shows as *Coming
  soon*. It is not finished and is not meant to be played yet.
- The mod holds the front doors of Denise's house and Franklin's Vinewood Hills house
  unlocked so you can reach the kitchen sink without Open All Interiors. If you already run
  that, nothing conflicts.
- Not compatible with GTA Online. Do not take it anywhere near it.

Source: https://github.com/defthrets/hoodrich
