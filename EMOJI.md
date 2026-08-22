# Emoji glyphs

Eleven small pictures with a text token each, drawn from `data/icons/` and tinted
at draw time. `UI/Emoji.cs` renders them; `UI/Icons.cs` reaches the same PNGs by
filename.

They exist because GTA's fonts have **no emoji glyphs**. A real emoji character
in a `DRAW_TEXT` call comes out as a box or as nothing at all, so anything that
wants a picture inline with words has to draw a sprite next to the text and
measure the gap itself. That is the whole of `Emoji.cs`.

## They are no longer in the feed

Every post template carried one or two of these and they have all been taken back
out -- 413 lines across `data/socials.json`. Nothing about the machinery has been
removed: `Emoji.Split`, `Measure` and `Draw` still work, the PNGs are still on
disk, and the moment a `:token:` appears in any string that goes through the feed
it will draw again.

Kept for **icons**, which is what they are good for. A 64px white shape that
tints to any colour is a wedge icon, a row marker, a panel bullet or a stat glyph
without drawing anything new.

## The set

| token | file | tint | reads as |
|---|---|---|---|
| `:fire:` | `fire.png` | `240,140,40` | heat, a good night, something selling |
| `:skull:` | `skull.png` | `240,240,238` | a body, a joke about one, the heat bar |
| `:money:` | `money.png` | `126,190,79` | cash, a price, a payout |
| `:leaf:` | `leaf.png` | `110,200,90` | weed -- also the Dealing wedge's alternative |
| `:gun:` | `guns.png` | `226,226,224` | the Weapons wedge's own icon |
| `:pill:` | `pills.png` | `150,195,240` | pills, oxy, xanax |
| `:police:` | `police.png` | `110,160,235` | a badge, a patrol, a raid |
| `:car:` | `car.png` | `226,226,224` | a car, a drive, a stop |
| `:eyes:` | `eyes.png` | `240,240,238` | somebody watching, somebody nosy |
| `:cap:` | `cap.png` | `235,90,80` | a lie, disbelief |
| `:crown:` | `crown.png` | `240,190,60` | a leader, a set, a win |

The tints are the defaults in the `Set` table in `Emoji.cs`. Drawing one as an
icon overrides the colour anyway -- `Icons.FromFile("skull.png")` takes whatever
tint the wedge or row gives it.

## Using one as an icon

```csharp
page.WithIcon(Icons.FromFile("crown.png"));
page.Row("Heat", heat.ToString("0"), Palette.Danger, "skull.png");
```

## Putting one back in text

Nothing needs turning on. Any string that reaches `Emoji.Draw` -- feed posts,
toasts -- renders `:token:` as the picture and everything else as text:

```json
"Block dry all week :skull:"
```

Sizing is tied to the line's own scale (`HeightPerScale`), so a feed drawn at one
size and a toast drawn at another both get pictures sitting on the same line as
their words. Measuring is the part that matters: `Measure` counts a token as one
glyph width rather than as its six characters, otherwise a line wraps against a
length that is not what will be drawn.
