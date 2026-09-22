# How the feed talks

The style guide for `data/socials.json`. Read it before adding lines, because the feed is
5,700 templates and there is no way to tell by eye whether a new one fits — everything you
write sounds right to you, which is exactly why this exists as a written rule and as a
measurement (`python tools\socials_check.py`) instead of as taste.

Researched 2026-09-22 against the conventions of LA street and rap social posting. It
describes a register, not any real person or set.

---

## The one thing to get right

**Apply the shape rules to every line and the slang to about a third of them.**

Over-lexicalising is the commonest way to get this wrong. A line that is just

> they think im sleep

is more in-register than one with four slang terms in it. The shape — short, unpunctuated,
understated — does the work. The vocabulary is a garnish.

Three properties, in order of how much they matter:

1. **Brevity.** A real post runs 4 to 9 words. Length is the tell, not vocabulary.
2. **Under-marking.** Punctuation, capitals and conjunctions are *absent*, not replaced with
   something cuter. Nobody is performing informality; they are not spending keystrokes.
3. **Understatement.** Flat declaratives. Emphasis comes from repetition, capitals or an oath
   phrase, almost never from adjectives or exclamation marks.

---

## What you do NOT have to write

Orthography is applied at runtime, per account, by `Social.Typing`. Write the line in whatever
case reads clearly and **leave the apostrophes in** — the styler removes them in the mouth of
anybody who types that way, and keeps them in the auntie's.

So do not hand-write `dont`, `im`, `yall`, `talkin` or a missing full stop in order to sound
right. You will get them for free from about three-quarters of the cast, and hard-coding them
takes the choice away from the quarter who would not.

Four bands, set per author by `"types"` in the file, defaulted from the handle:

| band | who | what happens |
|---|---|---|
| `street` | anybody in a set with no written lines | lowercase, no apostrophes, no full stops, `-in`, sometimes stretches or shouts |
| `plain` | tidier phone typists | about half of the above |
| `proper` | the aunties, the elders, the teacher | capitals, apostrophes, full stops, the ellipsis |
| `none` | every written voice and every organisation | untouched |

Two things the styler will **not** do, so they are yours:

- It never adds or removes a word. Length and vocabulary are the writer's job.
- It leaves a word in full capitals alone. So `LONG LIVE BRO` and `FREE THE GNG` survive a
  street typist, which is right — those are shouted out loud too.

---

## Who is allowed to say it

`SocialFeed.BuildOnce` picks the author pool **before** the words, so the set a line lives in
decides whose mouth it can be in. Get this wrong and you produce a sentence nobody could have
typed.

| set | spoken by | register |
|---|---|---|
| `Ambient` | every voice-less person in the city | general. No set markers, no regional tells |
| `AmbientHood` | Families and the neighbours with no set | Chamberlain Hills, full |
| `AmbientOrg`, `OrgEvent` | organisations only | corporate. Leave it corporate |
| `OursGloats`, `OursMourns`, `WarLiveOurs`, `DissTrack` | Families | full, plus the set's own spelling |
| `ProductGood/Bad`, `Hospital`, `HouseRaided`, `WarHeld`, `WarLost`, `RankUp` | Families and the neighbours | written from inside. First person is safe here |
| `BallasTaunt` | Ballas | purple. Blood-coded |
| `VagosTaunt` | Vagos | LA Chicano, **not** Black LA |
| `RivalMourns`, `RivalGloats`, `WarLiveRival`, `Wasted*` | whoever the live rival is | **unmarked.** See below |
| everything else | anybody | general |

**The unmarked sets are the trap.** `GangFor` hands `RivalMourns` and the four `Wasted` sets to
whichever gang is actually fighting you tonight, and that is one of eight — including the
Triads, the Armenians, the Koreans and a biker chapter in Sandy Shores. Nothing set-marked can
go in them: no `on hood`, no `bacc`, no `blood`, no `ese`. A Korean posting "on hood" is the
same class of bug as the Azteca who used to send love to Aunt Denise.

### Two sets, two spellings

Used lightly — a word or two per post, and only in the sets one side alone speaks. This is a
graphemic taboo: a rival's initial is avoided in writing and replaced with your own.

- **Families** (green) avoid `ck`, which reads as an initialism: `back` → `bacc`, `block` →
  `blocc`, `check` → `checc`. Address: `cuh`, `cuz`, `twin`, `gng`, `lil bro`, `big homie`.
- **The purple** avoid `c`: `crazy` → `brazy`, `cool` → `bool`, `what's cracking` → `wus
  brackin`. Address: `blood`, `bloodie`.

It clusters on words that already carry weight — the greeting, the neighbourhood, the address
term — and passes straight through neutral vocabulary. A post where every C is a B is a parody.
Note that `brazy` and `thicc` both escaped into general internet use years ago and no longer
mark anybody; `bacc`, `blocc` and `bool` still do.

Do not write the reciprocal insult forms — appending a letter to a rival's name to mean killer.
That part of the system is purely an attack and the reader-facing signal is already carried by
the spellings, the address terms and the colour.

---

## The formulas

The most conventionalised part of the register, and the place a wrong note is most conspicuous.
Capitals are close to obligatory on these.

**The dead.** `LONG LIVE [NAME]`, `LL` plus initials (`LLT`, `LLB`), `RIP` (lowercase `rip` is
fine and not disrespectful), `til i see you again`, `on the dead homies`. Anniversaries are a
bare month/day with no year — `6/12` — or a count: `2 years today`. The deceased is a nickname
or initials, never a full name. Birthday posts for the dead are their own genre: `you would be
24 today`.

Register is flat and short. The grief is carried by the formula and the capitals, not by
adjectives. **A long, eloquent, correctly punctuated grief post is the single most inauthentic
thing in this file.**

**The locked up.** `FREE [NAME]`, `FREE THE GNG`, `free my twin`, `free em all`. Countdowns:
`90 days left`, `he home in november`, `one more winter`. Homecoming: `HE HOME`, `bro touched
down`, `welcome home twin`.

---

## Antagonism

**Rivals are never named.** Naming an individual is both an exposure and a status concession.
Reference is oblique, and usually locational:

> the opps · the op · certain people · some of yall · whoever it apply to · them people ·
> the block over · that side · whoever did that

The subtweet is the whole convention: a post *about* somebody, addressed to the public, in the
third person, with no trigger. The target knows, nobody else does, and nothing actionable was
said.

> heard some of yall talkin. its cool

**Stance is dismissive, not hot.** Contempt reads as indifference — `i aint worried`, `they
know what it is`, `nothin to talk about`. Overt threat-writing reads as young and unserious,
which makes it useful for exactly that characterisation and wrong as a default.

The denial that confirms is also live: posting `this aint about nobody` to an accusation nobody
made publicly.

---

## Vocabulary

Everything here is on the research's **nationally current** list unless marked, so it is safe
in the shared word lists and in any mouth.

`fr` / `frfr` (the highest-frequency token in the whole register) · `ong` · `icl` (prefer over
`ngl`, which reads internet-generic) · `rn` · `idc` · `wyd` · `otw` · `fs` · `wtm` · `ard` ·
`nun` · `gng` · `twin` · `op` / `opps` · `tryna` · `finna` · `imma` / `ima` · `prolly` · `bet` ·
`no cap` · `tap in` · `locked in` · `we outside` · `say less` · `crashout`

**`bro` is a pronoun, not just an address.** The register narrates third parties with it —
`bro said he otw`, `bro really did that`. This is under-imitated and worth using.

**LA-marked** (the Families sets and `AmbientHood` only): `cuh` / `cuzz` / `cuddie` · `on hood`
· `on the dead homies` · `the homies` · `big homie` / `lil homie` · `unc` · `mobbin` · `slide` ·
`the shop` (barbershop) · `the swap meet` · `the courts` (never "the projects") · **`the` before
a freeway number — `the 110`, `the 405`** which is the single most reliable LA marker there is ·
area codes as place names: `310`, `323`, `213`, `562`.

**Grammar to preserve.** These are AAVE, not errors, and correcting them is the fastest way to
sound like a novelist: zero copula (`he outside`), habitual `be` (`he be at the shop every
saturday` = habitually), habitual `stay` (`the op stay watchin`), negative concord (`i aint told
nobody nothin`), zero third-person `-s` (`he own it`), possessive `they` (`they car`), adjectival
`sleep` (`they think im sleep`).

**Numbers are digits.** `19`, `at 4`, `til 11`, `2 years`, `10 deep`. Never spelled out.

### Do not use

| token | reads as |
|---|---|
| `hella` | Bay Area. LA says `stupid`, `crazy`, `tough`, `dumb` |
| `deadass` | New York |
| `my g`, `son`, `b`, `shorty`, `bodega`, `the projects` | New York |
| `slatt`, `slime` as a primary address | Atlanta |
| `jawn` | Philadelphia |
| `folks`, `joe`, `on foenem` | Chicago (`opps` itself is fine — it went national) |
| `lol`, `tbh`, `atm`, `smfh`, `af`, `brb` | outside the register entirely |
| `hmu` | 2012. Displaced by `tap in` |
| `smh` | still live but reads 30+. Fine on an elder |
| `lls` | ambiguous — it reads as "laughing like shit" (dated). Spell out `long live` |
| `otp` | reads as fandom, not "on the phone" |
| `ts` meaning "this" | TikTok. `ts` is "this shit" and is a noun phrase |
| `!`, `!!!`, `...` | the first two almost never; the ellipsis is a `proper` band marker |

Snow, subways, stoops and winter coats are the same class of error at the level of world
building. This city has heat, cars and outdoor food.

---

## No emoji

Standing rule for every mod in this repo, and it costs nothing here: the register carries
itself on capitalisation, elision, letter-stretching, punctuation and length. Text symbols
(`▶ ● ▲ ★`) only.

---

## Checking your work

```bash
python tools\socials_check.py
```

Errors first — an unknown slot prints its own braces on the phone, and a duplicate line just
makes the block repeat itself. Then a table of how the file reads.

The `words`, `abbrev` and `emoji` columns are about the screen. **`Cap` and `apost` are about
the source only**, because `Typing` restyles both at runtime. To measure the screen, compile
`src\Hoodrich\Social\Typing.cs` into a throwaway console app and run the real thing over the
file — a mirror of it written in Python would only ever agree with itself.

Targets, over a set: 8 to 9 words, a third of lines carrying a token, nothing in the "do not
use" table. The long observational lines in `Ambient`, `AmbientHood` and `Local` are a
deliberate exception — that voice is the mod's, the phone wraps and grows the card so nothing
truncates, and flattening it to six words would remove the thing it is good at.
