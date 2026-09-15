# Posted Up 0.9.3 — for the mod page

Paste-ready. The long version lives in `CHANGELOG-0.9.3.txt` and in `CHANGES.txt`
inside the -full zip; this is the short one for the update box.

---

## 0.9.3

**Your reports**

- **Bought ammo read zero after a reload.** Two faults together — the count was
  written before the game had handed your guns back, and a zero deleted the whole
  row from the save instead of storing a zero. One autosave in the first six
  seconds wiped it for good. Fixed. *(gaspin12)*
- **Both phones opening at once.** Hiding the cellphone HUD isn't closing a phone —
  the game's own script was still running underneath. It's properly ended now.
  *(Vorx4643)*
- **Standing at the fridge did nothing.** The house told you where product goes on
  your first visit and never again. It says it again when you walk in carrying.
  *(NordCyborg)*
- **Crashes and frame drops on busy blocks.** The mod has always had a
  world-is-full check and exactly one system consulted it — thirty others spawned
  people and cars without ever asking. Every ambient spawner asks now, and vehicles
  are counted as well as people, which is the pool that was actually running out.
  Missions and anything you called for yourself are untouched.
  *(kocabac, Felony83)*

**Performance**

- Asking the game for a model used to **stop the script** — up to 1.2 seconds per
  model with the HUD dark, every time a crate or a crew loaded in. They ask and
  come back now.
- The graffiti bystander scan did a ped sweep and six raycasts inside one frame,
  284 times a session. Spread out.

**Vernon** texts you after the buy and sells powder to the house, cheaper than the
port, two kilos a call. His messages have his face on them. Kill him on his own job
and the job ends.

**Drugs** — the comedown is one washed-out grey for two minutes with no camera
shake, and much milder. Acid warps properly. Bars and heroin walk like cocaine on
the first and meth on the double.

**Also** — die with the bag on and you drop it; die without it and your pockets are
gone. The inventory is laid out like Bare Minimum's pocket.

---

# Replies

## gaspin12

That's in 0.9.3 now. Two things were wrong: the count was being written down before
the mod had handed your guns back, so it recorded what the base game had — nothing —
and a zero deleted the row from the save instead of saving a zero. One autosave in
the first few seconds and the rounds were gone. Both sorted. Cheers for the report.

## Vorx4643

Fixed in 0.9.3. My phone was only *hiding* the game's one, which isn't the same as
closing it — so if the real phone was already up, you got both. It properly ends it
now. Thanks for flagging.

## NordCyborg

You had it right — it's Phone → Inventory while you're inside the house. The mod
only told you that on your very first visit, which is no use on your third night
with full pockets. As of 0.9.3 it says it again whenever you walk in carrying
something. Thanks for coming back with the answer, that's what made it obvious.

## Felony83

Should be a lot better in 0.9.3. Two causes: loading a model was stopping the whole
script for up to a second at a time (worst exactly where crews spawn, like
Gerald's), and the ambient spawners never checked whether the world was already
full. Both fixed. If it still drops for you there, send me the last lines of
`scripts\Hoodrich\Hoodrich.log` and I'll take another look.

## kocabac

The `ERR_MEM_EMBEDDEDALLOC` and the gang-war crash were the same thing, and you
found it yourself by turning the rollers off. The mod had a world-is-full check that
only *one* system consulted — every other spawner just kept adding. In 0.9.3 they
all ask, and cars are counted as well as people. Sorry it took a while to join the
dots.

## vultures

Still chasing this one. Your log ends on a normal line with no exception, and
Franklin-only is odd since the whole mod is built around him. 0.9.3 has some real
crash fixes in it, so try that first — and if it still goes, send me the last 30
lines of `scripts\Hoodrich\Hoodrich.log` from right before it happens.

## coltsbell87

Appreciated, seriously. Keeps me building it.
