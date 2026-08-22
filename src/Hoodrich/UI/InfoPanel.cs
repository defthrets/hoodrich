using System;
using System.Collections.Generic;
using System.Drawing;
using Control = GTA.Control;
using GTA;
using GTA.Native;
using Hoodrich.Core;
using Hud = Hoodrich.UI.Draw;

namespace Hoodrich.UI
{
    /// <summary>How a row is drawn. Row is what every existing caller gets.</summary>
    internal enum InfoKind { Row, Hero, Meter }

    /// <summary>One line of the readout.</summary>
    internal sealed class InfoRow
    {
        public string Label = "";
        public string Value = "";
        public Color Colour = Palette.Text;

        /// <summary>
        /// A plain line under the label, for the facts that were being crushed into the value.
        ///
        /// Declared since the first version of this panel and never actually drawn until now.
        /// It is drawn at ONE line, always, and its length never changes the row's height --
        /// it is fitted rather than wrapped, so a long note is trimmed instead of pushing the
        /// row into a size the scroll arithmetic did not budget for.
        /// </summary>
        public string Note = "";

        public InfoKind Kind = InfoKind.Row;

        /// <summary>Game sprite for the art slot.</summary>
        public Icon Art;

        /// <summary>A PNG in data\icons. Wins over <see cref="Art"/> when both are set.</summary>
        public string ArtFile = "";

        /// <summary>
        /// Override for the art tint.
        ///
        /// Unset means: a PNG is a white mask and takes the row's colour, while a game sprite
        /// carries its own colour and draws neutral. Set it where the icon means IDENTITY
        /// rather than STATE -- a coke sprite tinted with the money colour is a green brick.
        /// </summary>
        public Color? ArtTint;

        /// <summary>A colour bar at the row's left edge. A gang's colour, never its name.</summary>
        public Color? Tab;

        /// <summary>0..1 progress, for a Meter row.</summary>
        public float Meter = -1f;

        /// <summary>
        /// A strip of pips after the label: how many, how many filled, and which one is ringed
        /// as "you are here". PipAt is -1 for none.
        /// </summary>
        public int Pips;
        public int PipsOn;
        public int PipAt = -1;

        /// <summary>A row with no label is a spacer.</summary>
        public bool IsSpacer => string.IsNullOrEmpty(Label) && string.IsNullOrEmpty(Value);

        public bool HasArt => !string.IsNullOrEmpty(ArtFile) || Art.IsSet;

        /// <summary>
        /// How many grid cells this row occupies.
        ///
        /// Every vertical quantity on this panel is an integer number of cells, which is what
        /// makes the measure and the draw incapable of disagreeing about where the bottom is.
        /// </summary>
        public int Cells
        {
            get
            {
                if (Kind == InfoKind.Hero) return 3;
                if (Kind == InfoKind.Meter) return 2;
                return string.IsNullOrEmpty(Note) ? 1 : 2;
            }
        }
    }

    /// <summary>A titled group of rows.</summary>
    internal sealed class InfoSection
    {
        public string Title = "";

        /// <summary>An optional figure on the heading, right-aligned. The section's own sum.</summary>
        public string Total = "";
        public Color TotalColour = Palette.Text;

        public readonly List<InfoRow> Rows = new List<InfoRow>();

        public InfoSection Row(string label, string value, Color? colour = null, string note = "")
        {
            Rows.Add(new InfoRow
            {
                Label = label,
                Value = value,
                Colour = colour ?? Palette.Text,
                Note = note
            });
            return this;
        }

        /// <summary>The same, with a hand on the row for the things a positional call cannot say.</summary>
        public InfoSection Row(string label, string value, Color? colour, Action<InfoRow> with)
        {
            var row = new InfoRow { Label = label, Value = value, Colour = colour ?? Palette.Text };
            if (with != null) with(row);

            Rows.Add(row);
            return this;
        }

        /// <summary>The one number on the section worth finding without reading.</summary>
        public InfoSection Hero(string label, string value, Color colour, string note = "")
        {
            Rows.Add(new InfoRow
            {
                Kind = InfoKind.Hero,
                Label = label,
                Value = value,
                Colour = colour,
                Note = note
            });
            return this;
        }

        public InfoSection Meter(string label, string value, float fraction, Color colour,
                                 string note = "")
        {
            Rows.Add(new InfoRow
            {
                Kind = InfoKind.Meter,
                Label = label,
                Value = value,
                Colour = colour,
                Meter = fraction,
                Note = note
            });
            return this;
        }

        /// <summary>
        /// The three overloads that were missing, so every row shape can carry art.
        ///
        /// Row already had a hand-on-the-row form; Hero and Meter did not, and Row had no way
        /// to take a note AND a hand at the same time. That is why a wiring pass over the whole
        /// file could give art to a plain row and not to the rank hero sitting above it.
        /// </summary>
        public InfoSection Row(string label, string value, Color? colour, string note,
                               Action<InfoRow> with)
        {
            var row = new InfoRow
            {
                Label = label,
                Value = value,
                Colour = colour ?? Palette.Text,
                Note = note
            };

            if (with != null) with(row);

            Rows.Add(row);
            return this;
        }

        public InfoSection Hero(string label, string value, Color colour, string note,
                                Action<InfoRow> with)
        {
            var row = new InfoRow
            {
                Kind = InfoKind.Hero,
                Label = label,
                Value = value,
                Colour = colour,
                Note = note
            };

            if (with != null) with(row);

            Rows.Add(row);
            return this;
        }

        public InfoSection Meter(string label, string value, float fraction, Color colour,
                                 string note, Action<InfoRow> with)
        {
            var row = new InfoRow
            {
                Kind = InfoKind.Meter,
                Label = label,
                Value = value,
                Colour = colour,
                Meter = fraction,
                Note = note
            };

            if (with != null) with(row);

            Rows.Add(row);
            return this;
        }

        public InfoSection Gap()
        {
            Rows.Add(new InfoRow());
            return this;
        }
    }

    /// <summary>
    /// The numbers, on their own screen.
    ///
    /// The wheel is a gateway: it should say "sell" and "re-up", not "turf price x1.05" and
    /// "heat per sale x1.2". Anyone who wants the multipliers can have all of them at once,
    /// laid out and readable, instead of squinting at a panel beside a spinning wheel.
    ///
    /// Read-only by design. Nothing here changes the game; it is somewhere to look things up.
    /// </summary>
    internal sealed class InfoPanel
    {
        /// <summary>
        /// A card down the left, not a screen.
        ///
        /// The first version was 40% of the screen wide and centred, with labels and values
        /// pushed to opposite edges -- so a two-word label and a one-word value sat half a
        /// monitor apart and read as unrelated. This is a narrow column with the value directly
        /// under its label's eye line, which is how a stat block is meant to work.
        /// </summary>
        private const float PanelWidth = 0.290f;

        /// <summary>
        /// The panel's ONLY vertical unit.
        ///
        /// There used to be a second one, SectionGap, and the two of them are where every
        /// scrolling bug lived: a walk that measured and a walk that drew had to agree about
        /// two quantities instead of one. A section break is a blank cell now, so the body is
        /// exactly cells * RowHeight for any window and divergence is arithmetically
        /// impossible.
        /// </summary>
        private const float RowHeight = 0.0225f;
        private const float Pad = 0.012f;

        private const float HeaderH = 0.048f;
        private const float BodyLead = 0.010f;
        private const float FooterH = 0.026f;

        /// <summary>Cells visible at once before it scrolls.</summary>
        private const int CellBudget = 28;

        /// <summary>Ignore input briefly, or the button that opened this closes it.</summary>
        private const int OpenGraceMs = 220;

        /// <summary>Art height, and the column it needs.</summary>
        private const float IconH = 0.019f;
        private const float IconGap = 0.0065f;

        private static float Gutter { get { return Hud.ToX(IconH) + IconGap; } }

        private const float TabW = 0.0055f;
        private const float PipPitch = 0.017f;

        private static readonly Color Ground = Color.FromArgb(238, 12, 13, 15);
        private static readonly Color Groove = Color.FromArgb(70, 200, 205, 200);
        private static readonly Color PipOff = Color.FromArgb(90, 200, 205, 200);
        private static readonly Color HeroWash = Color.FromArgb(46, 255, 255, 255);
        private static readonly Color Hairline = Color.FromArgb(60, 200, 205, 200);
        private static readonly Color TrackBg = Color.FromArgb(50, 200, 205, 200);
        private static readonly Color SpriteNeutral = Color.FromArgb(235, 255, 255, 255);

        private enum ItemKind { Heading, Spacer, Row }

        private sealed class Item
        {
            public ItemKind Kind;
            public InfoSection Section;
            public InfoRow Row;
            public int Cells;
        }

        private string _title = "";
        private string _subtitle = "";
        private List<Item> _items;
        private int _totalCells;
        private float _bodyHeight;
        private int _openedAt;
        private int _scroll;

        public bool IsOpen => _items != null;

        public void Open(string title, string subtitle, List<InfoSection> sections)
        {
            if (sections == null || sections.Count == 0) return;

            _title = title ?? "";
            _subtitle = subtitle ?? "";

            // Flattened once, into one list of cells. Everything downstream walks this and only
            // this, so there is no second structure to keep in step with it.
            _items = new List<Item>();

            foreach (var s in sections)
            {
                if (!string.IsNullOrEmpty(s.Title))
                {
                    if (_items.Count > 0) _items.Add(new Item { Kind = ItemKind.Spacer, Cells = 1 });
                    _items.Add(new Item { Kind = ItemKind.Heading, Section = s, Cells = 1 });
                }

                foreach (var r in s.Rows)
                {
                    _items.Add(new Item
                    {
                        Kind = ItemKind.Row,
                        Row = r,
                        Cells = Math.Min(CellBudget, r.Cells)
                    });
                }
            }

            _totalCells = 0;
            foreach (var it in _items) _totalCells += it.Cells;

            _bodyHeight = Math.Min(_totalCells, CellBudget) * RowHeight;

            _scroll = 0;
            _openedAt = Game.GameTime;

            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        public void Close()
        {
            _items = null;
            _scroll = 0;
        }

        public void Update()
        {
            if (_items == null) return;

            LockControls();

            if (Game.GameTime - _openedAt < OpenGraceMs) return;

            if (Pressed(Control.PhoneCancel) || Pressed(Control.PhoneSelect) ||
                Pressed(Control.Attack) || Pressed(Control.Aim))
            {
                Hud.PlaySound("BACK", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                Close();
                return;
            }

            // Recomputed every frame rather than remembered, so it is self-correcting.
            var maxScroll = MaxScroll();
            if (_scroll > maxScroll) _scroll = maxScroll;
            if (maxScroll == 0) return;

            if (Pressed(Control.PhoneUp) && _scroll > 0) _scroll--;
            else if (Pressed(Control.PhoneDown) && _scroll < maxScroll) _scroll++;
        }

        private static bool Pressed(Control control)
        {
            return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, (int)control);
        }

        private static void LockControls()
        {
            Game.DisableControlThisFrame(Control.Attack);
            Game.DisableControlThisFrame(Control.Attack2);
            Game.DisableControlThisFrame(Control.Aim);
            Game.DisableControlThisFrame(Control.MeleeAttack1);
            Game.DisableControlThisFrame(Control.Phone);
            Game.DisableControlThisFrame(Control.SelectWeapon);
            Game.DisableControlThisFrame(Control.PhoneUp);
            Game.DisableControlThisFrame(Control.PhoneDown);
            Game.DisableControlThisFrame(Control.PhoneSelect);
            Game.DisableControlThisFrame(Control.PhoneCancel);
        }

        /// <summary>
        /// The last item that fits the budget from the current scroll.
        ///
        /// The draw loop has NO stop condition of its own -- it runs _scroll to here,
        /// inclusive -- so there is no second walk that could disagree with this one.
        /// </summary>
        private int WindowEnd()
        {
            var cells = 0;
            var last = _scroll - 1;

            for (var i = _scroll; i < _items.Count; i++)
            {
                if (cells + _items[i].Cells > CellBudget) break;

                cells += _items[i].Cells;
                last = i;
            }

            return last;
        }

        /// <summary>The smallest offset whose tail still fits the budget.</summary>
        private int MaxScroll()
        {
            var cells = 0;
            var i = _items.Count - 1;

            while (i >= 0 && cells + _items[i].Cells <= CellBudget)
            {
                cells += _items[i].Cells;
                i--;
            }

            return i + 1;
        }

        /// <summary>
        /// The art column, if this section has any art in it.
        ///
        /// Per section rather than per panel, so a section whose rows declare no art lays out
        /// exactly as it did before any of this existed -- which is what keeps the three other
        /// callers of this panel looking untouched.
        /// </summary>
        private static float GutterFor(InfoSection s)
        {
            if (s == null) return 0f;

            foreach (var r in s.Rows)
            {
                if (r.HasArt) return Gutter;
            }

            return 0f;
        }

        // ---- drawing -----------------------------------------------------------

        public void Draw()
        {
            if (_items == null) return;

            var last = WindowEnd();
            var height = HeaderH + BodyLead + _bodyHeight + FooterH;

            // Centred. A readout in the corner competes with the minimap and the wanted stars
            // and reads as a notification; in the middle it reads as a screen you opened.
            var x = 0.5f - PanelWidth * 0.5f;
            var top = Math.Max(0.06f, 0.5f - height * 0.5f);
            var right = x + PanelWidth - Pad;

            // The house frame: one ground, one bar. There used to be three framing devices on
            // this panel -- an outlined border, a filled header strip and a rule under it --
            // where the gun hub and the feed both prove that one works.
            Hud.RectFrom(x, top, PanelWidth, height, Ground);
            Hud.RectFrom(x, top, PanelWidth, 0.0028f, Palette.Accent);

            Hud.Text(_title.ToUpperInvariant(), x + Pad, top + 0.008f, 0.74f, Palette.Text,
                     Hud.FontCursive, centre: false);

            if (!string.IsNullOrEmpty(_subtitle))
            {
                Hud.TextRight(_subtitle, right, top + 0.022f, 0.34f, Palette.Cash,
                              Hud.FontChaletLondon);
            }

            var y = top + HeaderH + BodyLead;
            var gutter = 0f;
            var first = true;

            for (var i = _scroll; i <= last; i++)
            {
                var it = _items[i];

                switch (it.Kind)
                {
                    case ItemKind.Spacer:
                        break;

                    case ItemKind.Heading:
                        gutter = GutterFor(it.Section);
                        DrawHeading(it.Section, x, y, right, first);
                        break;

                    case ItemKind.Row:
                        if (!it.Row.IsSpacer) DrawRow(it.Row, x, y, right, gutter);
                        break;
                }

                first = false;
                y += it.Cells * RowHeight;
            }

            Hud.RectFrom(x + Pad, top + height - 0.026f, PanelWidth - Pad * 2f, 0.0012f, Hairline);

            var maxScroll = MaxScroll();

            Hud.Text(maxScroll > 0 ? "UP/DOWN  SCROLL      BACKSPACE  OUT" : "BACKSPACE  OUT",
                     x + Pad, top + height - 0.018f, 0.24f, Palette.TextDim, Hud.FontLabel,
                     centre: false);

            if (maxScroll > 0)
            {
                // With rows of two and three cells, "four from the bottom" is no longer
                // inferable from the text, so where you are has to be shown.
                var trackTop = top + HeaderH + BodyLead;
                var trackH = _bodyHeight;
                var thumbH = Math.Max(0.010f, trackH * Math.Min(1f, CellBudget / (float)_totalCells));
                var posF = _scroll / (float)maxScroll;

                Hud.RectFrom(x + PanelWidth - 0.0034f, trackTop, 0.0014f, trackH, TrackBg);

                Hud.RectFrom(x + PanelWidth - 0.0034f, trackTop + (trackH - thumbH) * posF,
                             0.0014f, thumbH, Palette.Alpha(Palette.Accent, 170));
            }
        }

        private void DrawHeading(InfoSection s, float x, float y, float right, bool first)
        {
            // Full width, and ABOVE the words, which is what a divider is. The old one ran from
            // the end of the title to the right edge, which is a line that starts wherever the
            // heading happened to stop.
            if (!first)
            {
                Hud.RectFrom(x + Pad, y - 0.008f, PanelWidth - Pad * 2f, 0.0022f,
                             Palette.Alpha(Palette.Accent, 60));
            }

            // Dim, not accent. Headings were the brightest text on the panel, labelling the
            // least important thing on it.
            Hud.Text(s.Title.ToUpperInvariant(), x + Pad, y + 0.002f, 0.26f, Palette.TextDim,
                     Hud.FontLabel, centre: false);

            if (!string.IsNullOrEmpty(s.Total))
            {
                Hud.TextRight(s.Total, right, y + 0.001f, 0.28f, s.TotalColour,
                              Hud.FontChaletLondon);
            }
        }

        /// <summary>
        /// Art in the gutter, and whether anything landed.
        ///
        /// Hud.File and Hud.Sprite both place by the CENTRE while Hud.Text places by the TOP,
        /// so the icon on a 0.28 body line whose top is at y centres a little below it.
        ///
        /// A PNG of ours wins over a game sprite: our masks are authored for this size, and the
        /// shop_* fallbacks are shared between three different drugs and therefore lie about
        /// which one is being looked at.
        /// </summary>
        private static bool DrawArt(InfoRow row, float x, float y)
        {
            var cy = y + 0.0092f;

            if (!string.IsNullOrEmpty(row.ArtFile))
            {
                var tint = row.ArtTint ?? row.Colour;
                if (Hud.File(row.ArtFile, x + Hud.ToX(IconH) * 0.5f, cy, IconH, 0f, tint)) return true;
            }

            if (row.Art.IsSet)
            {
                float aspect;
                var tex = row.Art.Resolve(out aspect);

                if (!string.IsNullOrEmpty(tex))
                {
                    if (aspect < 0.25f || aspect > 4f) aspect = 1f;

                    // Shrunk uniformly, never squashed: these dictionaries mix square shop icons
                    // with wide banner art, and a 3:1 sprite at full height runs under the label.
                    var w = Hud.ToX(IconH) * aspect;
                    var h = IconH;
                    var maxW = Gutter - 0.0045f;

                    if (w > maxW) { h *= maxW / w; w = maxW; }

                    Hud.Sprite(row.Art.Dict, tex, x + w * 0.5f, cy, w, h, 0f,
                               row.ArtTint ?? SpriteNeutral);
                    return true;
                }
            }

            // Art was declared and this install has not got it. A missing sprite draws literally
            // nothing, which is indistinguishable from a broken row and leaves a hole in an
            // indented column, so something has to occupy the slot.
            if (row.HasArt)
            {
                Hud.Disc(x + Hud.ToX(IconH) * 0.5f, cy, 0.0042f, Palette.Alpha(row.Colour, 150));
                return true;
            }

            return false;
        }

        private void DrawRow(InfoRow row, float x, float y, float right, float gutter)
        {
            if (row.Kind == InfoKind.Hero) { DrawHero(row, x, y, right); return; }

            if (row.Tab.HasValue)
            {
                Hud.RectFrom(x + 0.004f, y + 0.0015f, Hud.ToX(TabW), 0.019f, row.Tab.Value);
            }

            if (row.HasArt) DrawArt(row, x + Pad, y);

            var tx = x + Pad + gutter;

            // Nothing was clamping these against each other, which is how a long name came to
            // be printed straight through its own value. The value goes first because the value
            // is the content; the label gets whatever is left.
            var valueW = string.IsNullOrEmpty(row.Value)
                ? 0f
                : Hud.MeasureText(row.Value, 0.28f, Hud.FontBody);

            var pipsW = row.Pips > 0 ? 0.012f + row.Pips * Hud.ToX(PipPitch) : 0f;
            var labelMax = right - tx - valueW - pipsW - 0.010f;

            Hud.Text(Hud.Fit(row.Label, labelMax, 0.28f, Hud.FontBody), tx, y, 0.28f,
                     Palette.TextDim, Hud.FontBody, centre: false);

            if (row.Pips > 0)
            {
                // Three states as three SHAPES rather than three colours, which survives a
                // colourblind player and survives 720p, where a thirteen-pixel disc has no hue
                // worth reading.
                var px = tx + Hud.MeasureText(row.Label, 0.28f, Hud.FontBody) + 0.012f;
                var cy = y + 0.0095f;

                for (var i = 0; i < row.Pips; i++, px += Hud.ToX(PipPitch))
                {
                    if (i == row.PipAt)
                    {
                        Hud.Disc(px, cy, 0.0062f, Palette.Accent);
                        Hud.Disc(px, cy, 0.0036f, Color.FromArgb(255, 12, 13, 15));
                    }
                    else if (i < row.PipsOn) Hud.Disc(px, cy, 0.0046f, row.Colour);
                    else Hud.Disc(px, cy, 0.0030f, PipOff);
                }
            }

            Hud.TextRight(row.Value, right, y, 0.28f, row.Colour, Hud.FontBody);

            var noteY = y + 0.0165f;

            if (row.Kind == InfoKind.Meter)
            {
                var trackW = right - tx;
                var f = row.Meter < 0f ? 0f : row.Meter > 1f ? 1f : row.Meter;

                Hud.RectFrom(tx, y + 0.0190f, trackW, 0.0055f, Groove);
                Hud.RectFrom(tx, y + 0.0190f, trackW * f, 0.0055f, row.Colour);

                noteY = y + 0.0290f;
            }

            if (!string.IsNullOrEmpty(row.Note))
            {
                Hud.Text(Hud.Fit(row.Note, right - tx, 0.235f, Hud.FontLabel), tx, noteY, 0.235f,
                         Palette.Alpha(Palette.TextDim, 170), Hud.FontLabel, centre: false);
            }
        }

        /// <summary>
        /// The one number worth finding without reading.
        ///
        /// Double the size of a body row, because size is the only ranking channel this panel
        /// was not using -- everything on it was the same weight, so nothing on it was more
        /// important than anything else.
        ///
        /// A hero ignores the section's art gutter and starts at the pad: it is a block rather
        /// than a list item, and an empty indent under a number this big reads as a mistake.
        /// </summary>
        private void DrawHero(InfoRow row, float x, float y, float right)
        {
            Hud.RectFrom(x + 0.004f, y - 0.004f, PanelWidth - 0.008f, 0.0655f, HeroWash);
            Hud.RectFrom(x + 0.004f, y + 0.0625f, PanelWidth - 0.008f, 0.0022f,
                         Palette.Alpha(row.Colour, 200));

            var tx = x + Pad;

            // Art on a hero row, which it could not have before.
            //
            // The draw returns here rather than falling through to the shared row path, so the
            // HasArt check further up was never reached for a hero -- a Hero could be given an
            // ArtFile and would silently ignore it. It is drawn beside the number rather than
            // beside the caption, because the number is the thing the row exists for and the
            // caption is three-quarters the height of the art itself.
            var heroArt = 0.030f;

            if (row.HasArt &&
                Hud.File(row.ArtFile, tx + Hud.ToX(heroArt) * 0.5f, y + 0.034f, heroArt, 0f,
                         row.ArtTint ?? row.Colour))
            {
                tx += Hud.ToX(heroArt) + 0.008f;
            }

            // Caption above the number, small and dim. The number is what is being looked for;
            // the label is only needed once it has been found.
            Hud.Text(row.Label.ToUpperInvariant(), tx, y + 0.004f, 0.24f, Palette.TextDim,
                     Hud.FontLabel, centre: false);

            Hud.Text(Hud.Fit(row.Value, right - tx, 0.56f, Hud.FontChaletLondon), tx, y + 0.018f,
                     0.56f, row.Colour, Hud.FontChaletLondon, centre: false);

            if (!string.IsNullOrEmpty(row.Note))
            {
                Hud.Text(Hud.Fit(row.Note, right - tx, 0.26f, Hud.FontLabel), tx, y + 0.048f,
                         0.26f, Palette.Alpha(Palette.TextDim, 170), Hud.FontLabel, centre: false);
            }
        }
    }
}
