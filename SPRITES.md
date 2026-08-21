# HUD sprites

Reference: <https://wiki.rage.mp/wiki/Textures>

Every icon this mod draws on screen is a game texture, addressed by two strings:
a **dictionary** and a **texture name inside it**. Both are needed and both are
guesses until proven otherwise. `Draw.Sprite` requests the dict with
`REQUEST_STREAMED_TEXTURE_DICT` and then calls `DRAW_SPRITE(dict, texture, ...)`.

The problem this file exists to solve: **a texture name the game does not have
draws nothing at all.** The dict streams fine, the sprite call succeeds, no
error is raised anywhere -- the wedge is just empty. That is indistinguishable
from a rendering bug, a bad layout, or an icon drawn at zero alpha, so a single
wrong name costs an afternoon.

There is no "does this sprite exist" native. `Draw.HasTexture` gets around it by
calling `GET_TEXTURE_RESOLUTION` and treating a zero-sized result as absent,
which is why `Icon` carries a *list* of names instead of one
(`src/Hoodrich/UI/Icons.cs`). The first name the install actually has wins, a bad
guess costs nothing, and the list should end on something already proven so the
item can never come out blank. `Icon.Resolve` logs the winner, so the log is the
evidence for everything below.

---

## Confirmed on this install

These 13 pairs are in the log resolving, repeatedly, up to the newest session.
This is runtime proof from the actual game build on this machine, not a list off
a website. Use these freely.

### `commonmenu`

| texture | reads as | where |
|---|---|---|
| `shop_ammo_icon_a` | a box of rounds | Stash, Crack |
| `shop_gunclub_icon_a` | a pistol | Weapons |
| `shop_mask_icon_a` | a balaclava | Gangs |
| `shop_tattoos_icon_a` | a tattoo gun | Socials |
| `shop_health_icon_a` | a heart (see below) | The numbers, Ecstasy |
| `shop_garage_icon_a` | a garage door | This block |
| `shop_tick_icon` | a tick | Pack up, Leave it |
| `shop_lock` | a padlock | anything locked |
| `mp_alerttriangle` | a warning triangle | Work, the gang rows |

### `mpinventory`

| texture | reads as | where |
|---|---|---|
| `mp_specitem_weed` | a leaf | Dealing, Marijuana |
| `mp_specitem_coke` | a baggie | Cocaine |
| `mp_specitem_meth` | a crystal | Meth |
| `mp_specitem_cash` | a bundle of notes | Post up, Borrow money |

On `shop_health_icon_a`: the name is proven, the *appearance* is not. NativeUI
calls it `BadgeStyle.Heart` and that is the only reason this file says heart. It
could equally be a health cross. Worth looking at once before a screen is built
around it reading as a heart.

## Confirmed absent on this install

A give-up in the log prints the dict with nothing after the slash. These five
were asked for and came back empty, so they are not in this build. Do not put
them back.

| pair | evidence |
|---|---|
| `commonmenu/shop_money_icon_a` | ~9,200 empty lines. `Icons.Money` had one candidate, so empty can only be this |
| `commonmenu/mp_specitem_crack` | Crack resolved to its second candidate |
| `commonmenu/mp_specitem_pills` | Ecstasy resolved to its third |
| `commonmenu/mp_specitem_ecstasy` | same |
| `mpinventory/mp_specitem_crack` | 510 empty lines from an older single-candidate build |

`shop_money_icon_a` was the expensive one -- four wedges permanently blank
(Re-up, Call the plug, Call the docks, Text the plug) and about nine thousand
wasted log lines. The published `commonmenu` lists do not contain it either, so
this is not a streaming problem: **there is no money shop icon.** Use
`mpinventory/mp_specitem_cash`.

The three failed `mp_specitem_*` names under `commonmenu` point at a rule that
the dumps independently agree with, and it is the single most useful thing in
this file:

> `commonmenu` carries exactly five `mp_specitem_*` textures -- `cash`, `coke`,
> `heroin`, `meth`, `weed`. Every other `mp_specitem_*` lives only in
> `mpinventory`. Asking `commonmenu` for one of the others draws nothing.

## Documented, not verified here

From texture dumps and shipping menu libraries. Very likely real; none has been
seen resolving in this mod, so treat any one of them as a candidate rather than
a certainty and always put a proven name last in the list.

### `mpinventory` -- 65 textures, whole dictionary

Three dumps in three different formats agree byte-for-byte, and one of them is a
per-texture image export, so this is a genuine enumeration rather than a copied
wordlist. Every name here also has a `_black` variant *except* `data`,
`partnericon`, `plane2`, `safe` and `steer_wheel` -- do not assume the pairing is
universal.

```
mp_specitem_bike          mp_specitem_boat          mp_specitem_boatpickup
mp_specitem_car           mp_specitem_cash          mp_specitem_coke
mp_specitem_cuffkeys      mp_specitem_data          mp_specitem_heli
mp_specitem_heroin        mp_specitem_keycard       mp_specitem_meth
mp_specitem_package       mp_specitem_partnericon   mp_specitem_ped
mp_specitem_plane         mp_specitem_plane2        mp_specitem_randomobject
mp_specitem_remote        mp_specitem_safe          mp_specitem_steer_wheel
mp_specitem_weapons       mp_specitem_weed          mp_specitem_black
```

and 23 activity icons in the same dict:

```
arm_wrestling  basejump  custom_mission  darts  deathmatch  drug_trafficking
gang_attack  golf  inworld_ringpointer  inworld_ringpointer_blue
in_world_circle  mp_arrow  race_air  race_bike  race_boat  race_foot
race_land  race_offroad  shooting_range  survival  team_deathmatch  tennis
vehicle_deathmatch
```

`mp_specitem_weapons`, `_package`, `_ped`, `_car` and `_cuffkeys` are the ones
this mod is most likely to want.

### `commonmenu` -- the parts worth trusting

There is no public dump of the real `commonmenu.ytd`. Every list on the web
traces back to one scrape of 61 names, reformatted six ways -- treat their
agreement as worth nothing. What is below survived a check against Rockstar's
decompiled scripts and against menu libraries that would visibly break if the
names were wrong.

```
mp_alerttriangle    mp_hostcrown       mp_medal_bronze   mp_medal_gold
mp_medal_silver     shop_lock          shop_lock_arena   shop_new_star
shop_tick_icon      shop_box_tick      shop_box_tickb    shop_box_cross
shop_box_crossb     shop_box_blank     shop_box_blankb   arrowleft
arrowright          gradient_bgd       gradient_nav      interaction_bgd
shop_arrows_upanddown
```

plus these, each in an `_a` / `_b` pair -- `_a` is the unselected art and `_b`
the selected one, two tints of the same shape rather than two icons:

```
shop_ammo   shop_armour   shop_art      shop_barber    shop_chips (a/b only)
shop_clothing  shop_franklin  shop_garage  shop_garage_bike
shop_garage_podium  shop_gunclub  shop_health  shop_makeup  shop_mask
shop_michael  shop_tattoos  shop_trevor
```

(so `shop_armour_icon_a`, `shop_armour_icon_b`, and so on; `shop_chips_a` /
`shop_chips_b` breaks the `_icon_` pattern.)

Also documented but from the single 2014 scrape only, so weaker:
`bettingbox_centre`, `bettingbox_left`, `bettingbox_right`, `common_medal`,
`header_gradient_script`, `medal_bronze`, `medal_gold`, `medal_silver`.

Names that circulate and are **not** in any dump or library:
`shop_police_icon_a`, `shop_skull_icon`, `mp_specitem_crown`, `mp_specitem_bomb`,
`mp_specitem_health`, `mp_hostoff`, `blackhexagon`. They look plausible, which is
exactly what makes them expensive. The crown is `mp_hostcrown`.

### Other dictionaries

Not used by this mod yet, listed because they cover gaps `commonmenu` does not.

| dict | textures worth knowing |
|---|---|
| `timerbars` | `all_black_bg`, `all_white_bg` -- flat bar backgrounds, tint with the colour args rather than hunting a coloured variant |
| `shared` | `emptydot_32`, `info_icon_32`, `medaldot_32`, `bggradient_16x512`, `bggradient_32x1024` |
| `mphud` | `mp_anim_cash`, `mp_anim_rp`, `mp_anim_ammo`, `ammo_pickup` |
| `mpleaderboard` | `leaderboard_cops_icon`, `leaderboard_deaths_icon`, `leaderboard_cash_icon`, `leaderboard_star_icon`, `leaderboard_kills_icon`, `leaderboard_rankshield_icon` |
| `mprankbadge` | `globe` -- one sprite, recoloured per rank via the RGBA args |
| `mpshopsale` | `saleicon` |

The `mpleaderboard` names are the only cop-ish and death-ish sprites that appear
in a dump at all. The names are solid; nobody has described the art, so what
`leaderboard_cops_icon` actually looks like is unknown until it is drawn.

---

## What to use for

Best first. Anything marked confirmed is in the log; anything else is a
candidate and needs a confirmed name after it in the list.

| want | use |
|---|---|
| money | `mpinventory/mp_specitem_cash` -- confirmed. Never `commonmenu/shop_money_icon_a` |
| gun | `commonmenu/shop_gunclub_icon_a` -- confirmed. Then `mpinventory/mp_specitem_weapons` |
| ammo | `commonmenu/shop_ammo_icon_a` -- confirmed |
| warning | `commonmenu/mp_alerttriangle` -- confirmed |
| tick | `commonmenu/shop_tick_icon` -- confirmed. Then `shop_box_tick` |
| cross | `commonmenu/shop_box_cross` -- documented, no confirmed alternative |
| lock | `commonmenu/shop_lock` -- confirmed |
| heart | `commonmenu/shop_health_icon_a` -- confirmed to exist, appearance unverified. Not `mp_specitem_health`, which is in no dump |
| star | `commonmenu/shop_new_star` -- documented only. `~ws~` in text is a real wanted star and is proven; see TEXTFORMAT.md |
| crown | `commonmenu/mp_hostcrown` -- documented only |
| weed / coke / meth | `mpinventory/mp_specitem_weed`, `_coke`, `_meth` -- all confirmed |
| heroin | `mpinventory/mp_specitem_heroin` -- documented, and this is the right dict. The mod currently asks `commonmenu` for it, which by the five-name rule above cannot work |
| crack | nothing exists. No rock or pipe sprite anywhere. `mp_specitem_coke` is the only coke art; the mod settles for `shop_ammo_icon_a` |
| pills / ecstasy | nothing exists. No `pills`, no `ecstasy`, in any dict. Closest stand-ins are `mpinventory/mp_specitem_package` or `_randomobject`; the mod settles for `shop_health_icon_a` |
| cuffs | `mpinventory/mp_specitem_cuffkeys` -- documented in all three dumps. **`mp_specitem_cuffs` does not exist in either dict.** The art is cuff keys, possibly with cuffs |
| police | no confirmed sprite. `mpleaderboard/leaderboard_cops_icon` is the only documented candidate, art unseen. `shop_police_icon_a` is a guess and is in no dump |
| skull | no sprite exists. "skull" returns nothing across every dump and every dict. `mpleaderboard/leaderboard_deaths_icon` might be one -- name confirmed, art unseen. Otherwise fall back to `mp_alerttriangle` or a text glyph |
| package | `mpinventory/mp_specitem_package` -- documented |
| person | `mpinventory/mp_specitem_ped` -- documented |
| car / bike / boat / heli | `mpinventory/mp_specitem_car`, `_bike`, `_boat`, `_heli` -- documented |

Skull, a pill, a real handcuff icon and a police badge are the four a gang mod
most wants and none of them can be had as a stock sprite. Anything claiming
otherwise is a guessed name.

### The bar icons are untested

`PostUp.cs` (`PoliceArt`, `SkullArt`, `HeartArt`) was written after the last game
session, so **the log proves nothing about any of those ten pairs.** Six of the
names -- `shop_police_icon_a`, `mp_specitem_cuffs` in both dicts,
`mp_specitem_skull` in both dicts, `shop_franklin_icon_a` -- appear in no dump
either. Expect the police and skull bars to both fall through to
`mp_alerttriangle`, which will make them identical to each other and to
`Icons.Warning`. The heart will win on its first candidate, since
`shop_health_icon_a` is confirmed.

Weapon art (115 model dicts, dict name == texture name) and the 48 `CHAR_*`
contact avatars are equally unproven, because their only failure path is
`Log.Debug` and debug logging is off. Nothing is recorded either way.

---

## What does not work

Two things that look like they should and do not. Both were tried in this mod
and both cost real time, so they are written down rather than rediscovered.

**Blip sprite IDs cannot be drawn as sprites.** `SET_BLIP_SPRITE(handle, 496)`
addresses the minimap's own art through an entirely separate system. There is no
dict and texture for it, so there is nothing to hand `DRAW_SPRITE`. Passing the
number anywhere near a sprite call does nothing. See BLIPS.md for what the ids
are actually for.

**The `~BLIP_...~` text tag draws nothing in a plain `DRAW_TEXT`.** The tag is
real and the docs describe it as putting blip art inline in a string, which
would neatly solve the skull and police problems above -- `~BLIP_DEAD~`,
`~BLIP_POLICE~`. It works in help messages. In a dialogue row it rendered
*nothing at all* -- not even the tag as literal text, which would at least have
been visible. Tested with `~BLIP_CRIM_WANTED~` for Oxycodone; the row came out
blank and looked exactly like a broken texture. It is back on a texture now.
Full detail in TEXTFORMAT.md.

---

## Adding a new one

1. Put the new name **first** in the `Icon` candidate list and a confirmed name
   from the table above **last**. The wedge then cannot come out blank while the
   guess is being tested.
2. Run the game, open the wedge, and read `Hoodrich.log`. `Icon.Resolve` prints
   the winner: `Icon Meth: mpinventory/mp_specitem_meth aspect 1.00`. A name
   after the slash is proof the texture exists. A slash with nothing after it
   means every candidate failed.
3. Watch the aspect. These dictionaries mix square shop icons with wide banner
   art, and `Resolve` hands back the winner's real ratio for exactly that reason
   -- an icon drawn at 1:1 that reports 2.00 will come out squashed.
4. If the answer matters enough, stop guessing and open `commonmenu.ytd` in
   OpenIV or CodeWalker (`update/update.rpf` → `x64/textures`) and read the
   table. Ten minutes converts every "documented" row in this file into ground
   truth, and it is the only way to cover the DLC dictionaries no public list
   touches.

Casing does not matter -- lookups are case-insensitive in practice, and every
library passes lowercase. Rockstar's own scripts use `MP_AlertTriangle` and
`Shop_GunClub_Icon_A`; this file uses lowercase throughout and so does the code.
