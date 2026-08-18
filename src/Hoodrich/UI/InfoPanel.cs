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
    /// <summary>One line of the readout.</summary>
    internal sealed class InfoRow
    {
        public string Label = "";
        public string Value = "";
        public Color Colour = Palette.Text;

        /// <summary>Optional plain-English line under the value, for anything cryptic.</summary>
        public string Note = "";

        /// <summary>A row with no label is a spacer.</summary>
        public bool IsSpacer => string.IsNullOrEmpty(Label) && string.IsNullOrEmpty(Value);
    }

    /// <summary>A titled group of rows.</summary>
    internal sealed class InfoSection
    {
        public string Title = "";
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

        private const float RowHeight = 0.0225f;
        private const float SectionGap = 0.014f;
        private const float Pad = 0.012f;

        /// <summary>Ignore input briefly, or the button that opened this closes it.</summary>
        private const int OpenGraceMs = 220;

        /// <summary>Rows visible at once before it scrolls.</summary>
        private const int MaxVisibleRows = 26;

        private string _title = "";
        private string _subtitle = "";
        private List<InfoSection> _sections;
        private int _openedAt;
        private int _scroll;

        public bool IsOpen => _sections != null;

        public void Open(string title, string subtitle, List<InfoSection> sections)
        {
            if (sections == null || sections.Count == 0) return;

            _title = title ?? "";
            _subtitle = subtitle ?? "";
            _sections = sections;
            _scroll = 0;
            _openedAt = Game.GameTime;

            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        public void Close()
        {
            _sections = null;
            _scroll = 0;
        }

        public void Update()
        {
            if (_sections == null) return;

            LockControls();

            if (Game.GameTime - _openedAt < OpenGraceMs) return;

            if (Pressed(Control.PhoneCancel) || Pressed(Control.PhoneSelect) ||
                Pressed(Control.Attack) || Pressed(Control.Aim))
            {
                Hud.PlaySound("BACK", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                Close();
                return;
            }

            var total = TotalRows();
            if (total <= MaxVisibleRows) return;

            if (Pressed(Control.PhoneUp) && _scroll > 0) _scroll--;
            else if (Pressed(Control.PhoneDown) && _scroll < total - MaxVisibleRows) _scroll++;
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

        private int TotalRows()
        {
            var n = 0;
            foreach (var s in _sections)
            {
                if (!string.IsNullOrEmpty(s.Title)) n++;
                n += s.Rows.Count;
            }
            return n;
        }

        // ---- drawing -----------------------------------------------------------

        public void Draw()
        {
            if (_sections == null) return;

            var total = TotalRows();
            var visible = Math.Min(total, MaxVisibleRows);

            var bodyHeight = visible * RowHeight + _sections.Count * SectionGap;
            var height = 0.052f + bodyHeight + 0.024f;

            // Centred. A readout in the corner competes with the minimap and the wanted stars
            // and reads as a notification; in the middle it reads as a screen you opened.
            var x = 0.5f - PanelWidth * 0.5f;
            var top = Math.Max(0.06f, 0.5f - height * 0.5f);

            // A hairline border around a slightly darker ground, and a header strip the title
            // sits in rather than floating above the list. The old panel was a black rectangle
            // with words in it; the difference between that and something you would call a
            // screen is almost entirely edges.
            Hud.RectFrom(x - 0.0016f, top - 0.0016f, PanelWidth + 0.0032f, height + 0.0032f,
                         Color.FromArgb(150, 90, 96, 92));

            Hud.RectFrom(x, top, PanelWidth, height, Color.FromArgb(238, 10, 11, 13));

            const float header = 0.046f;

            Hud.RectFrom(x, top, PanelWidth, header, Color.FromArgb(235, 20, 22, 24));
            Hud.RectFrom(x, top + header - 0.0022f, PanelWidth, 0.0022f, Palette.Accent);

            var y = top + 0.006f;

            // The name of the screen in the house script face, and what it is showing under it
            // in plain type -- the same split the wheel readout uses.
            Hud.Text(_title.ToUpperInvariant(), x + Pad, y, 0.68f, Palette.Text,
                     Hud.FontCursive, centre: false);

            if (!string.IsNullOrEmpty(_subtitle))
            {
                Hud.TextRight(_subtitle, x + PanelWidth - Pad, y + 0.014f, 0.30f,
                              Palette.Cash, Hud.FontChaletLondon);
            }

            y = top + header + 0.012f;

            // Flattened once, so scrolling is a plain row offset rather than section bookkeeping.
            var index = 0;
            var drawn = 0;

            foreach (var section in _sections)
            {
                if (!string.IsNullOrEmpty(section.Title))
                {
                    if (index++ >= _scroll && drawn < MaxVisibleRows)
                    {
                        // A rule that runs to the right edge, so a section heading reads as a
                        // divider rather than as another row that happens to be a different
                        // colour.
                        Hud.Text(section.Title.ToUpperInvariant(), x + Pad, y, 0.24f,
                                 Palette.Accent, Hud.FontLabel, centre: false);

                        var ruleFrom = x + Pad + Hud.MeasureText(section.Title.ToUpperInvariant(),
                                                                 0.24f, Hud.FontLabel) + 0.008f;

                        var ruleWidth = x + PanelWidth - Pad - ruleFrom;

                        if (ruleWidth > 0.01f)
                        {
                            Hud.RectFrom(ruleFrom, y + 0.0105f, ruleWidth, 0.0012f,
                                         Color.FromArgb(70, 200, 205, 200));
                        }

                        y += RowHeight * 0.95f;
                        drawn++;
                    }
                }

                foreach (var row in section.Rows)
                {
                    if (index++ < _scroll) continue;
                    if (drawn >= MaxVisibleRows) break;

                    if (!row.IsSpacer)
                    {
                        // Banded, faintly. Reading a value back to its label across a gap is
                        // what a stripe is for, and at this alpha it is felt rather than seen.
                        if ((drawn & 1) == 1)
                        {
                            Hud.RectFrom(x + 0.004f, y - 0.0035f, PanelWidth - 0.008f, RowHeight,
                                         Color.FromArgb(26, 210, 215, 210));
                        }

                        Hud.Text(row.Label, x + Pad, y, 0.28f, Palette.TextDim, Hud.FontBody, centre: false);
                        Hud.TextRight(row.Value, x + PanelWidth - Pad, y, 0.28f, row.Colour, Hud.FontBody);
                    }

                    y += RowHeight;
                    drawn++;
                }

                y += SectionGap;
            }

            Hud.RectFrom(x, top + height - 0.026f, PanelWidth, 0.0012f,
                         Color.FromArgb(60, 200, 205, 200));

            var hint = total > MaxVisibleRows ? "UP / DOWN SCROLL      ENTER CLOSE" : "ENTER CLOSE";
            Hud.Text(hint, x + Pad, top + height - 0.018f, 0.24f, Palette.TextDim, Hud.FontLabel, centre: false);
        }
    }
}
