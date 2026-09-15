## 0.9.3

Mostly your reports this one.

**bought ammo read zero after a reload** — two things wrong. the count was being
written down before the mod had handed your guns back, so it saved what the base
game had, which is nothing. and a zero deleted the whole row from the save instead
of saving a zero, so one autosave in the first few seconds and the rounds were gone
for good. both sorted. cheers gaspin12.

**both phones opening at once** — I was only hiding the game's cellphone, which
isn't the same as closing it. if the real one was already up you got two. properly
ended now. cheers Vorx4643.

**standing at the fridge doing nothing** — the house only told you where product
goes on your first visit and never again, which is no help on your third night with
full pockets. it says it again now when you walk in carrying something. cheers
NordCyborg.

**crashes and frame drops on busy blocks** — this one was mine. the mod has always
had a world-is-full check and exactly one system was actually asking it. thirty
others just kept spawning people and cars. they all ask now, and cars get counted
as well as people, which is the pool that was actually running out. missions and
anything you called for yourself are untouched. cheers kocabac and Felony83.

**performance** — loading a model was stopping the whole script for up to a second
at a time with the HUD off, every time a crate or a crew loaded in. and the graffiti
bystander check was doing a ped sweep and six raycasts in a single frame, 284 times
a session. both fixed.

**vernon** texts you after the buy and sells powder to the house now — cheaper than
the port, two kilos a call. his messages have his face on them. and if he dies on
his own job it fails, because the hand-in is walking up to him.

**drugs** — the comedown is one washed out grey for two minutes instead of four
pictures and a shaking camera, and it's a lot less brutal. acid actually warps.
bars and heroin walk like coke on the first one and meth on the double.

**also** — die with the bag on and you drop it with everything in it. die without it
and your pockets are just gone. and the inventory's laid out like Bare Minimum's
pocket now, one grid, bigger tiles.

---

# replies

**gaspin12**

that's done in 0.9.3. two things were wrong — the count was being written down
before the mod had handed your guns back, so it saved what the base game had, and a
zero deleted the row from the save instead of saving a zero. one autosave in the
first few seconds and the rounds were gone. both sorted. cheers for the report.

**Vorx4643**

fixed in 0.9.3. I was only hiding the game's phone, not closing it, so if the real
one was already up you got both. properly ended now. cheers for flagging it.

**NordCyborg**

you had it right, it's Phone > Inventory while you're inside the house. problem is
the mod only told you that on your first visit, which is no use later on. it says it
again now whenever you walk in carrying something. cheers for coming back with the
answer, that's what made it obvious.

**Felony83**

should be a lot better in 0.9.3. two causes — loading a model was stopping the whole
script for up to a second at a time, worst exactly where crews spawn like Gerald's,
and the spawners never checked whether the world was already full. both fixed. if
it still drops there send me the last lines of scripts\Hoodrich\Hoodrich.log and
I'll have another look.

**kocabac**

the ERR_MEM_EMBEDDEDALLOC and the gang war crash were the same thing and you found
it yourself turning the rollers off. I had a world-is-full check that only one
system was asking — everything else just kept spawning. they all ask now and cars
get counted too. sorry it took me a while to join the dots.

**vultures**

still on this one. your log ends on a normal line with no exception, and
Franklin-only is odd given the whole mod is built round him. 0.9.3 has real crash
fixes in it so try that first, and if it still goes send me the last 30 lines of
scripts\Hoodrich\Hoodrich.log from just before it happens.

**coltsbell87**

appreciated, seriously. keeps me building it.
