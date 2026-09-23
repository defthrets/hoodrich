# How the feed talks

The style guide for `data/socials.json`. Read it before adding lines, because the feed is
6,000 templates and there is no way to tell by eye whether a new one fits — everything you
write sounds right to you, which is why this exists as a written rule and as a measurement
(`python tools\socials_check.py`) instead of as taste.

Researched in two passes, both against conventions rather than against any real person: the
street and rap register, and the ordinary-working-person register for everybody in this city
who is not in a gang.

---

## The one thing to get right

**Apply the shape rules to every line and the slang to about a third of them.**

Over-lexicalising is the commonest way to get this wrong. A line that is just

> they think im sleep

is more in-register than one with four slang terms in it. The shape — short, unpunctuated,
understated — does the work. Vocabulary is a garnish.

Three properties, in order of how much they matter:

1. **Brevity.** A real post runs 4 to 9 words on the street, 6 to 16 for everybody else.
   Length is the tell, not vocabulary.
2. **Under-marking.** Punctuation, capitals and conjunctions are *absent*, not replaced with
   something cuter. Nobody is performing informality; they are not spending keystrokes.
3. **Understatement.** Flat declaratives. Emphasis comes from repetition, capitals or an oath
   phrase, almost never from adjectives or exclamation marks.

And a fourth that applies only to the ordinary cohort: **a quarter of the lines should be
actively dull.** A feed reads as real in proportion to how much of it is not worth reading. If
every line has a punchline it reads as a writers' room.

---

## What you do NOT have to write

Orthography is applied at runtime, per account, by `Social.Typing`. Write the line in whatever
case reads clearly, **leave the apostrophes in**, and **leave the terminal punctuation off**.
The styler strips for the people who type that way and adds for the people who don't.

Do not hand-write `dont`, `im`, `yall` or `talkin` to sound right. You get them free from
about three-quarters of the cast, and hard-coding them takes the choice away from the quarter
who would not.

Five bands, set per author by `"types"` in the file, defaulted from the handle:

| band | who | what happens |
|---|---|---|
| `street` | anybody in a set with no written lines | lowercase, no apostrophes, no full stops, `-in`, sometimes stretches or shouts |
| `young` | an ordinary 20-something with a job and no set | lowercase and unterminated, and **keeps its g's** |
| `plain` | tidier phone typists, late 20s to mid 30s | about half of the above, and sometimes punctuates |
| `proper` | the aunties, the elders, the teacher, late 30s | **adds** capitals, full stops, exclamation marks and the ellipsis |
| `none` | every written voice and every organisation | untouched |

`young` deliberately does not drop its g's. `-in` belongs to the street register, and putting
it in a barista's mouth in Vespucci puts the wrong city in it.

Three things the styler will **not** do, so they are yours:

- It never adds or removes a word. Length and vocabulary are the writer's job.
- It leaves a word in full capitals alone, so `LONG LIVE BRO` and `FREE THE GNG` survive a
  street typist — right, because those are shouted out loud too.
- It never restores a name's capital. `vespucci` stays lowercase in a proper author's mouth.
  That was tried and cut: this file is half lowercase by design, so Franklin appears in lower
  case six times for every capital, and guessing capitalises ordinary words mid-sentence.

---

## How loud each person is

Almost nobody posts. It is the best-measured thing about social media and the thing fiction
gets most wrong: the top quarter of users on a measured platform produced 97% of the posts,
and the bottom three quarters a median of zero a month.

So the author is drawn by weight, not evenly. Four bands off the handle, and the loudest tenth
write about half the feed. Override with `"volume"` on an author when it is characterisation —
a new parent is the highest-volume account in any real feed, and one or two people should be
pinned to the bottom band so that their turning up means something.

Comments stay on a flat roll on purpose. The people who never post are exactly the people who
comment.

---

## Who is allowed to say it

`SocialFeed.BuildOnce` picks the author pool **before** the words, so the set a line lives in
decides whose mouth it can be in. Get this wrong and you produce a sentence nobody could have
typed.

| set | spoken by | register |
|---|---|---|
| `Ambient` | every voice-less person in the city | general. No set markers, no regional tells |
| `AmbientHood` | Families and the neighbours with no set | Chamberlain Hills, full |
| `Ambient` + region | everybody, when you are standing there | see below |
| `Ambient` + time of day | everybody, at that hour | see below |
| `AmbientOrg`, `OrgEvent` | organisations only | corporate. Leave it corporate |
| `OursGloats`, `OursMourns`, `WarLiveOurs`, `DissTrack` | Families | full, plus the set's own spelling |
| `ProductGood/Bad`, `Hospital`, `HouseRaided`, `WarHeld`, `WarLost`, `RankUp` | Families and the neighbours | written from inside. First person is safe |
| `BallasTaunt` | Ballas | purple. Blood-coded |
| `VagosTaunt` | Vagos | LA Chicano, **not** Black LA |
| `RivalMourns`, `RivalGloats`, `WarLiveRival`, `Wasted*` | whoever the live rival is | **unmarked.** See below |
| everything else | anybody | general |

**The unmarked sets are the trap.** `GangFor` hands `RivalMourns` and the four `Wasted` sets to
whichever gang is fighting you tonight, and that is one of eight — including the Triads, the
Armenians, the Koreans and a biker chapter in Sandy Shores. Nothing set-marked can go in them:
no `on hood`, no `bacc`, no `blood`, no `ese`. A Korean posting "on hood" is the same class of
bug as the Azteca who used to send love to Aunt Denise.

### Two sets, two spellings

Used lightly — a word or two per post, and only in the sets one side alone speaks. A rival's
initial is avoided in writing and replaced with your own.

- **Families** (green) avoid `ck`: `back` → `bacc`, `block` → `blocc`. Address: `cuh`, `cuz`,
  `twin`, `gng`, `lil bro`, `big homie`.
- **The purple** avoid `c`: `crazy` → `brazy`, `cool` → `bool`, `what's cracking` → `wus
  brackin`. Address: `blood`, `bloodie`.

It clusters on words that already carry weight and passes through neutral vocabulary. A post
where every C is a B is a parody. Note `brazy` and `thicc` escaped into general internet use
years ago and no longer mark anybody; `bacc`, `blocc` and `bool` still do. Do not write the
reciprocal insult forms — that part is purely an attack, and the signal is already carried.

---

## Where you are, and when it is

The feed knows four things about the world at the moment it writes a post, and all four are
wired.

| it knows | how | slot or set |
|---|---|---|
| the zone under the player | `Main.ZoneNameHere` | `{here}` |
| the road under the player | `Main.StreetNameHere` | `{street}` |
| which part of the map that zone is in | `regions` in this file | `Ambient<Region>` |
| the hour and the weather | `Social.Moment` | `{timeofday}`, `{weathertalk}`, `Ambient<Part>` |

**Regions** are `south`, `docks`, `city`, `vinewood`, `beach`, `blaine`, mapping every zone
code the game can report except North Yankton and the island. A region set takes a slice of
the ambient roll, not the whole thing — people travel, and a feed that never mentions anywhere
else is as wrong as one that never mentions here. Add a region's chatter by creating
`AmbientSouth`, `AmbientBeach` and so on; if the set does not exist, the roll silently falls
back to `Ambient`.

**Times of day** are `latenight`, `earlymorning`, `morning`, `midday`, `afternoon`, `evening`,
`night`, from `Moment.Part`. Same deal: `AmbientLatenight`, `AmbientMidday`. These exist
because a slot can only change a phrase inside a line — it cannot stop somebody posting about
the school run at two in the morning, because the subject lives in the line.

`{timeofday}` and `{weathertalk}` now read the game before they read a list. Their word lists
live under `moments` and `weather`, keyed by the same bucket names. Both fall back to the flat
`slots` entry when a bucket is missing, so an older file still works.

---

## The four tells that make a line Los Angeles

A line with none of these is probably generic-urban. With two, it is probably here.

1. **A freeway with `the` in front of the number.** "took the 110 north at 4." Never "took
   110", never "I-110".
2. **Distance as time, and usually two times.** "twenty minutes, or an hour if you leave now."
3. **A cross street instead of a block count.** Not "two blocks over."
4. **An exact dollar amount.** Rent, the ticket, the fare, the fill, the tow.

And a fifth that is not a tell but a posture: **complaint is the primary mode of sociability
here.** A complaint post is a friendly post.

### Do not use

| wrong | it reads as | write instead |
|---|---|---|
| the subway | New York | the Metro, the train, the bus, the B line |
| bodega, corner deli | New York | the liquor store, the market, the mini mart |
| the stoop | New York | the driveway, the carport, the front steps |
| brownstone, walk-up, fire escape, the super, radiator, basement | wrong housing stock | the dingbat, the courtyard building, the back house, the manager in unit 1, the wall heater |
| the projects | not the local term | the courts, or the name of the place |
| snow, winter coats, shovelling | does not happen | a beanie at 58 degrees, a day trip up the mountain |
| walking everywhere | nobody does | driving, parking, the walk from the far space |
| Cali | never a resident | LA, SoCal, out here |
| hella | Bay Area | so, bomb, tough, stupid, crazy |
| deadass | New York | fr, ong, no cap |
| highway, expressway, the interstate | not the word | freeway, surface streets, the onramp |
| counting blocks | rarely used | cross streets |
| lol, tbh, atm, smfh | outside the register | see the vocabulary below |

Spanish in an ordinary Angeleno's mouth is fine and constant at the **food and place-name**
level — taco, torta, pan dulce, carniceria, Sepulveda, La Cienega — with no italics and no
explanation. `órale`, `ese`, `vato`, `carnal`, `firme` and the rest of Caló are **in-group and
generationally marked**: they belong to a Chicano character quoting an uncle, not to a barista
as default voice. A young Mexican-American character in LA today says "bet", "fasho", "no
manches" and "bro" long before they say "órale, ese".

---

## The formulas

The most conventionalised part of the street register, and where a wrong note is most
conspicuous. Capitals are close to obligatory.

**The dead.** `LONG LIVE [NAME]`, `LL` plus initials, `RIP` (lowercase `rip` is fine),
`til i see you again`, `on the dead homies`. Anniversaries are a bare month/day with no year —
`6/12` — or a count. The deceased is a nickname or initials, never a full legal name.

Register is flat and short. The grief is carried by the formula and the capitals, not by
adjectives. **A long, eloquent, correctly punctuated grief post is the single most inauthentic
thing in this file.**

**The locked up.** `FREE [NAME]`, `FREE THE GNG`, `free my twin`. Countdowns: `90 days left`,
`he home in november`. Homecoming: `HE HOME`, `bro touched down`.

---

## Antagonism

**Rivals are never named.** Reference is oblique and usually locational: the opps, certain
people, some of yall, whoever it apply to, the block over, that side.

The subtweet is the whole convention: a post *about* somebody, addressed to the public, in the
third person, with no trigger. The target knows, nobody else does, nothing actionable was said.

### Two registers, and the gang sets use both

**Cold** is the older and more established speaker: contempt as indifference, `i aint worried`,
`they know what it is`, nothing actionable said.

**Hot** is the young one, and it is what the gang sets now mostly sound like. It is loud,
profane and violent, and it has its own fixed moves:

- **The scoreboard.** The tally is posted: `we up`, `score updated`, `they counting`.
- **Disrespecting the dead opp.** `smoking on a green pack`, `rip bozo`, laughing at their RIP.
- **Loss turns into aggression.** Research on gang-involved youth on social media found grief
  posts reliably come *before* aggressive ones, so a mourning line pivots to the get-back:
  `candle lit. clip full`, `crying today. sliding tomorrow`.
- **Threat by movement.** `slide`, `spin the block`, `pull up`, `on sight`, `caught lacking`.
- **Insults.** bozo, goofy, clown, bitch made, pussy, lame, scared, folded, ran, dickriding.
- **Praise for your own.** stepper, hitter, demon, solid, certified, never folded.
- **US weapon words only.** stick, blicky, glizzy, drum, switch, draco.

Profanity is in the gang sets now and nowhere else. Some authors dodge it (`f*ck`, `b*tch`,
`goofy ahh`) and some don't — that is a per-author habit in `Typing`, so write the word in full
and let the styler decide.

**Insults are this city's, not the real world's.** Families call the Ballas grapes, purple dinos
and lavender boys; the Ballas call the Families green goblins, leprechauns and lime-green lames,
and say CGF stands for can't get fed. Real-world set insults and kill-tags aimed at real gangs
do not go in. Racial slurs do not go in either.

**The Ballas speak as themselves when they are the rival.** `RivalGloats`, `RivalMourns`,
`WarLiveRival` and the four `Wasted` sets are shared by eight gangs and stay unmarked, but
`SocialFeed.Theirs` prefers `<Set><Gang>` when that exists — `RivalGloatsBallas`,
`WastedShotBallas` and so on — three times in four. Another gang gets its own voice the same way:
add the set.

**Do not use London drill.** skeng, ching, mandem, wagwan, peng, duppy, splash, wet. Most drill
glossaries online are UK and read as the wrong country.

---

## Vocabulary

**Street, nationally current, safe in the shared word lists:** `fr` / `frfr` (the highest
frequency token in the register) · `ong` · `icl` (prefer over `ngl`) · `rn` · `idc` · `wyd` ·
`otw` · `nun` · `gng` · `twin` · `op` / `opps` · `tryna` · `finna` · `imma` · `bet` · `no cap`
· `tap in` · `locked in` · `we outside`.

**`bro` is a pronoun, not just an address.** The register narrates third parties with it —
`bro said he otw`. Under-imitated and worth using.

**LA-marked** (Families sets and `AmbientHood` only): `cuh` · `on hood` · `on the dead homies`
· `the homies` · `big homie` / `lil homie` · `unc` · `mobbin` · `slide` · `the shop`
(barbershop) · `the swap meet` · `the courts`.

**Ordinary cohort, and this is a different code.** Lowercase, unterminated, ironic by default,
and built on stock constructions: `no because`, `tell me why`, `the way I`, `not me [verb]ing`,
`it's giving`, `the fact that`, `this man`, `im cooked`, `anyway` as a closer. Age is carried
almost entirely by punctuation, not by words — which is why the bands do most of this work and
you should not reach for slang to signal it.

**Grammar to preserve** in the street sets. These are AAVE, not errors, and correcting them is
the fastest way to sound like a novelist: zero copula (`he outside`), habitual `be` (`he be at
the shop every saturday`), habitual `stay`, negative concord (`i aint told nobody nothin`),
zero third-person `-s`, possessive `they`, adjectival `sleep`.

**Numbers are digits.** `19`, `at 4`, `til 11`, `10 deep`.

**Never `{money}` or `{count}` in an ambient set.** Ambient posts are built with no amount, so
both fill with a zero. Write the figure into the line.

---

## No emoji

Standing rule for every mod in this repo, and it costs nothing: the register carries itself on
capitalisation, elision, letter-stretching, punctuation and length. Text symbols only.

The substitutions that do the most work: **the lone full stop** after a short word is the
eye-roll and the cold shoulder; **`anyway` as a closer** is the shrug; **letter repetition**
is warmth and intensity; **ALL CAPS on one word** is the emphasis; **`lmao` sentence-final** is
the softener, not a laugh; **`im dead`** is the skull; **`<3`** is the heart and is plain
ASCII.

---

## Checking your work

```bash
python tools\socials_check.py
```

Errors first — an unknown slot prints its own braces on the phone, and a duplicate line makes
the block repeat itself. Then a table of how the file reads.

The `words`, `abbrev` and `emoji` columns are about the screen. **`Cap` and `apost` are about
the source only**, because `Typing` restyles both at runtime. To measure the screen, compile
`src\Hoodrich\Social\Typing.cs` into a throwaway console app and run the real thing over the
file — a mirror written in Python would only ever agree with itself. That harness has now
caught four bugs that review missed, including the styler rewriting slot names.

Targets, over a set: 8 to 9 words for the street, 9 to 12 for everybody else, a third of lines
carrying a token, nothing in the do-not-use tables.

The long observational lines in `Ambient`, `AmbientHood` and `Local` are a deliberate
exception. That voice is the mod's, the phone wraps and grows the card so nothing truncates,
and flattening it to six words would remove the thing it is good at.
