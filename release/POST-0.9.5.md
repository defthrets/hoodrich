## 0.9.5 — everything since 0.8.1

**vernon's got a job now, and a car.** he finally asks you for something: walks you
down into the basement he keeps calling square footage, then a kilo off an armenian
at rogers scrap for twenty-two five, and he's coming with you because it's his money.
goes about how you'd expect. his dorado's parked down the side of the shop — chrome,
green tubes, OGVEE plate, locked every hour of the save except the one he needs you
for.

**and he sells now.** finish the buy, drive off, and he texts you. powder to the
house, cheaper than the port, two kilos a call. he's a man with a basement, not a
dock.

**a bag you can lose.** twenty slots, worn on your back, dropped with a key. die
wearing it and it comes off where you fell with everything still in it — die without
it and your pockets are just gone.

**the inventory's laid out like bare minimum's pocket** — one grid, five across, tiles
twice the size, in a panel a third of the screen instead of over half.

**drugs.** everything lasts twice as long. meth is speed, not a stagger. heroin
doesn't put you on the floor any more. acid actually warps. bars and heroin walk like
coke on the first one and meth on the double. and the comedown is one washed-out grey
for two minutes with no camera shake, instead of four pictures and a shaking screen.

**the hunt got a compass**, the fam app got three pages, and every voice in the pack
is levelled to the same loudness.

### your reports

- **bought ammo read zero after a reload** — fixed. two faults: the count was written
  before the mod handed your guns back, and a zero deleted the row from the save
  instead of saving a zero. *(gaspin12)*
- **both phones opening at once** — I was hiding the game's phone, not closing it.
  *(Vorx4643)*
- **standing at the fridge did nothing** — the house only told you where product goes
  on your first visit. it says it again now when you walk in carrying. *(NordCyborg)*
- **crashes and frame drops on busy blocks** — the mod had a world-is-full check and
  exactly one system was asking it. thirty others just kept spawning. they all ask
  now, and cars get counted as well as people. *(kocabac, Felony83)*

### performance

loading a model used to stop the whole script for up to a second with the HUD off,
every time a crate or a crew loaded in. and the graffiti bystander check did a ped
sweep and six raycasts in a single frame, 284 times a session. both fixed.

### and the fixes to the fixes

0.9.3's spawn budget had the vehicle ceiling set below what normal play actually
holds, which would have emptied the block more than half the time. the car meet would
have given up on itself. the phone fix took the real phone off you when you weren't
even using ours. and dogs stopped appearing. all found by reading it back rather than
by anyone having to play it.

vernon also stopped re-offering a job you'd already done — the offer was reading the
same flag the basement door is locked behind — he stopped getting stuck in the
scrapyard after the shooting, and he and ardo stopped talking over each other with
both mouths going at once.
