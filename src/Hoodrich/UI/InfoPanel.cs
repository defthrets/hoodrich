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
        private const float PanelWidth = 0.40f;
        private const float RowHeight = 0.026f;
        private const float SectionGap = 0.020f;
        private const float Pad = 0.016f;

        /// <summary>Ignore input briefly, or the button that opened this closes it.</summary>
        private const int OpenGraceMs = 220;

        /// <summary>Rows visible at once before it scrolls.</summary>
        private const int MaxVisibleRows = 22;

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
            var height = 0.062f + bodyHeight + 0.028f;

            var x = 0.5f - PanelWidth * 0.5f;
            var top = 0.5f - height * 0.5f;

            Hud.Rect(x, top, PanelWidth, height, Color.FromArgb(215, 12, 13, 15));
            Hud.Rect(x, top, PanelWidth, 0.0035f, Palette.Accent);

            var y = top + 0.014f;

            Hud.Text(_title.ToUpperInvariant(), x + Pad, y, 0.40f, Palette.Text, Hud.FontLabel);
            if (!string.IsNullOrEmpty(_subtitle))
            {
                Hud.TextRight(_subtitle, x + PanelWidth - Pad, y + 0.004f, 0.30f,
                              Palette.TextDim, Hud.FontBody);
            }

            y += 0.044f;

            // Flattened once, so scrolling is a plain row offset rather than section bookkeeping.
            var index = 0;
            var drawn = 0;

            foreach (var section in _sections)
            {
                if (!string.IsNullOrEmpty(section.Title))
                {
                    if (index++ >= _scroll && drawn < MaxVisibleRows)
                    {
                        Hud.Text(section.Title.ToUpperInvariant(), x + Pad, y, 0.28f,
                                 Palette.TextDim, Hud.FontLabel);
                        y += RowHeight;
                        drawn++;
                    }
                }

                foreach (var row in section.Rows)
                {
                    if (index++ < _scroll) continue;
                    if (drawn >= MaxVisibleRows) break;

                    if (!row.IsSpacer)
                    {
                        Hud.Text(row.Label, x + Pad, y, 0.32f, Palette.TextDim, Hud.FontBody);
                        Hud.TextRight(row.Value, x + PanelWidth - Pad, y, 0.32f, row.Colour, Hud.FontBody);

                        if (!string.IsNullOrEmpty(row.Note))
                        {
                            Hud.Text(row.Note, x + Pad + 0.010f, y + 0.016f, 0.26f,
                                     Palette.TextDim, Hud.FontBody);
                            y += 0.016f;
                            // The note borrows the next row's space rather than its own slot.
                        }
                    }

                    y += RowHeight;
                    drawn++;
                }

                y += SectionGap;
            }

            var hint = total > MaxVisibleRows
                ? "UP / DOWN  SCROLL      ENTER  CLOSE"
                : "ENTER OR BACKSPACE  CLOSE";

            Hud.Text(hint, x + Pad, top + height - 0.022f, 0.27f, Palette.TextDim, Hud.FontLabel);
        }
    }
}
