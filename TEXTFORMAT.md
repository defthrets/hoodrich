# Rockstar text formatting

Every string this mod draws goes through the game's own text system, so it takes
Rockstar's `~tag~` codes. They work in `DRAW_TEXT`, help messages, subtitles and
notifications.

## Colour

| tag | is |
|---|---|
| `~r~` | red — enemies |
| `~g~` | green — pickups, objectives |
| `~b~` | blue — friendlies |
| `~f~` | friendly (the rarer variant) |
| `~y~` | yellow — destinations |
| `~o~` | orange |
| `~p~` | purple |
| `~q~` | pink |
| `~m~` | mid grey — de-emphasis, silver |
| `~c~` | menu grey — a speaker out of view |
| `~t~` | menu grey — speech in another language |
| `~l~` | black |
| `~d~` | dark blue |
| `~w~` | white |
| `~s~` | **reset to the context default** |
| `~v~` | the colour set by `SET_SCRIPT_VARIABLE_HUD_COLOUR` |
| `~u~` | the second script variable colour |
| `~HUD_COLOUR_...~` | any named HUD colour |
| `~HC_...~` | alias of the above |
| `~HC_13~` | a HUD colour by index |

`~s~` resets rather than sets white. Ending a coloured run with `~w~` leaves
everything after it white; ending with `~s~` puts it back to whatever the
surrounding context wanted. Use `~s~`.

## Layout

| tag | is |
|---|---|
| `~n~` | line break |
| `~h~` / `~bold~` | bold, and again to turn it off |
| `~italic~` | italic, and again to turn it off |
| `~nrt~` | line break with no top padding |
| `<C>...</C>` | condensed |
| `~ws~` / `~wanted_star~` | a wanted star glyph |
| `~EX_R*~` | the Rockstar logo, where the font has it |

## Art inside text

| tag | is |
|---|---|
| `~BLIP_...~` | **the blip with that name, drawn inline** |
| `~INPUT_...~` | the player's current key for a control |
| `~INPUTGROUP_...~` | a control group's hint |
| `~ACCEPT~`, `~CANCEL~` | prompt buttons |
| `~PAD_A~`, `~PAD_DPAD_LEFT~`, `~PAD_LSTICK_ALL~`, ... | gamepad glyphs |

`~BLIP_...~` is the useful one and it is easy to miss. Blip sprite IDs address
the MAP and cannot be handed to `DRAW_SPRITE` — but the blip ART can be put in a
string. The name is the sprite's name with the `radar_` prefix dropped and
upper-cased:

| sprite | id | tag |
|---|---|---|
| `radar_police_chase` | 42 | `~BLIP_POLICE_CHASE~` |
| `radar_community_series` | 835 | `~BLIP_COMMUNITY_SERIES~` |
| `radar_production_weed` | 496 | `~BLIP_PRODUCTION_WEED~` |
| `radar_gun_shop` | 110 | `~BLIP_GUN_SHOP~` |

See [BLIPS.md](BLIPS.md) for the sprite list itself.

The docs say "help messages and other supported contexts", which is not a
promise about every context. An unsupported one renders the tag as literal text
rather than failing quietly, so it is obvious on screen the first time you look.

## Placeholders

| tag | is |
|---|---|
| `~a~` | a string component |
| `~1~` | a number component |
| `~a_0~`, `~a_1~` | string components out of order, for translation |
| `~1_0~`, `~1_1~` | number components out of order |
| `~z~` | at the start of a string, hides it when subtitles are off |

---

Two things worth remembering:

* The mod's own `Draw.Text` pushes strings in 96-character chunks because
  `"STRING"` honours only one substring component. A `~tag~` split across a
  chunk boundary will not render, so keep tags away from the ends of very long
  lines.
* `~INPUT_...~` is why the prompts say "Press ~INPUT_CONTEXT~" rather than
  "Press E" — it follows the player's own bindings and their controller.
