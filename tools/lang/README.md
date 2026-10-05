# Translations

`data/lang/<code>.json` is what ships, in `scripts\Hoodrich\lang\`. Each is
`{"strings": {"English": "Translated"}}` -- the English literal in the code is the key, looked
up when the string is drawn, and a key that is missing shows in English. Keep the `~y~` colour
codes, the `~INPUT_CONTEXT~` button names and the leading/trailing spaces exactly: the mod glues
fragments together on them, and a key that starts or ends with a space is treated as glue.

To edit a language, edit the json. To regenerate all of them from the authoring dictionaries
here (`tr_*.py`), run `python make_langs.py` from this folder -- it refuses a colour code that
went missing, edge whitespace that changed, a key written twice, and reports coverage against
`uikeys.json`, which `uikeys.py` rebuilds from the source.

Glue and dialogue share a lookup, so `make_langs.py` also writes each file's `"tails"`: the glue
keys (" now.", " a week.", " of ") that also turn up inside a line of dialogue or the feed. A tail
is only swapped inside a string where some other glue matched too, or in a bare counter that is
nothing else but numbers ("12 down"), so a notice comes out wholly translated and a line somebody
says is never touched. Translate the ordinary words freely; the tails list keeps them out of the
dialogue. A fixed phrase in the MIDDLE of a notice (a ternary like `"the block"`) cannot be reached
by glue -- add the whole line, or a longer piece that spans it, by hand.

Languages: en-US (a spelling overlay), pt-BR, es, fr, de, ru, pl, zh-CN and hi. Chinese only
draws when GTA V itself is set to Chinese -- its glyphs are not in the font otherwise. Hindi is
in Latin letters, the way it is typed, because the game's fonts have no Devanagari at all.

What is NOT translated, on purpose: the dialogue (voiced in English, and data rather than
code), the social feed's posts (likewise), and the log.
