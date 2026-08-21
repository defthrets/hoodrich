using System;
using System.Collections.Generic;
using System.Drawing;
using Control = GTA.Control;
using GTA;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Locations;
using Hoodrich.State;
using Hud = Hoodrich.UI.Draw;

namespace Hoodrich.UI
{
    /// <summary>
    /// Grimes's rack.
    ///
    /// He used to sell through the dialogue panel, which is the right shape for a person and
    /// the wrong shape for a stock list: every rack was its own page, the rows were plain text
    /// with the price bolted on the end, and rounds came in one fixed lot you bought over and
    /// over. It read as a conversation you were having with a spreadsheet.
    ///
    /// This is a screen, laid out the way the rest of the mod lays out screens: his name in the
    /// house script, the racks along the top so all five are visible at once, the guns as rows
    /// with their own art, and the rounds for whatever is under the cursor in their own block
    /// underneath with an amount you choose.
    ///
    /// The conversation is still how you START it -- you walk up to a man and he says something.
    /// This is what he shows you once you have asked.
    /// </summary>
    internal sealed class GunScreen
    {
        private const float PanelWidthH = 0.62f;
        private const float RowHeight = 0.030f;
        private const float PadH = 0.024f;

        private const int OpenGraceMs = 220;

        /// <summary>Rounds are bought in lots, so a full load is one decision and not eight.</summary>
        private static readonly int[] Lots = { 1, 2, 5, 10 };

        private sealed class Rack
        {
            public readonly string Name;
            public readonly Piece[] Stock;

            /// <summary>Blip sprite for the kind, so a rack is told apart before it is read.</summary>
            public readonly int Sprite;

            public Rack(string name, Piece[] stock, int sprite)
            {
                Name = name;
                Stock = stock;
                Sprite = sprite;
            }
        }

        private static readonly Rack[] Racks =
        {
            new Rack("HANDGUNS",   Armourer.Handguns,   156),
            new Rack("AUTOMATICS", Armourer.Automatics, 159),
            new Rack("SHOTGUNS",   Armourer.Shotguns,   158),
            new Rack("BLADES",     Armourer.Melee,      154),
            new Rack("THROWN",     Armourer.Throwables, 152),
        };

        private readonly PlayerState _state;

        private int _rack;
        private int _row;
        private int _lot;
        private int _openedAt;

        public GunScreen(PlayerState state)
        {
            _state = state;
        }

        public bool IsOpen { get; private set; }

        /// <summary>Set by Main: what he says when money changes hands.</summary>
        public Action<Piece, bool> OnBought;

        public void Open()
        {
            IsOpen = true;
            _openedAt = Game.GameTime;
            _rack = 0;
            _row = 0;
            _lot = 0;

            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        public void Close()
        {
            if (!IsOpen) return;

            IsOpen = false;
            Hud.PlaySound("BACK", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        private Rack Current => Racks[Math.Max(0, Math.Min(_rack, Racks.Length - 1))];

        private Piece Chosen
        {
            get
            {
                var stock = Current.Stock;
                return stock.Length == 0 ? null : stock[Math.Max(0, Math.Min(_row, stock.Length - 1))];
            }
        }

        // ---- input -------------------------------------------------------------

        public void Update()
        {
            if (!IsOpen) return;

            LockControls();

            if (Game.GameTime - _openedAt < OpenGraceMs) return;

            if (Pressed(Control.PhoneCancel)) { Close(); return; }

            if (Pressed(Control.PhoneUp)) Move(-1);
            else if (Pressed(Control.PhoneDown)) Move(1);
            else if (Pressed(Control.PhoneLeft)) Lot(-1);
            else if (Pressed(Control.PhoneRight)) Lot(1);
            else if (Pressed(Control.Jump)) Shelf(1);
            else if (Pressed(Control.Cover)) Shelf(-1);
            else if (Pressed(Control.PhoneSelect) || Pressed(Control.Context)) Buy();
        }

        private static bool Pressed(Control control)
        {
            return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, (int)control);
        }

        /// <summary>
        /// Holds the controls the same way every other full screen in the mod does, so reading
        /// a price cannot also fire a gun or walk you into the road.
        /// </summary>
        private static void LockControls()
        {
            Function.Call(Hash.DISABLE_ALL_CONTROL_ACTIONS, 0);

            // The ones the screen itself needs back.
            foreach (var control in new[]
                     {
                         Control.PhoneUp, Control.PhoneDown, Control.PhoneLeft, Control.PhoneRight,
                         Control.PhoneSelect, Control.PhoneCancel, Control.Context,
                         Control.Jump, Control.Cover, Control.LookLeftRight, Control.LookUpDown
                     })
            {
                Function.Call(Hash.ENABLE_CONTROL_ACTION, 0, (int)control, true);
            }
        }

        private void Move(int step)
        {
            var count = Current.Stock.Length;
            if (count == 0) return;

            _row = (_row + step) % count;
            if (_row < 0) _row += count;

            // A gun with no magazine has no lots to step through, so the amount is put back to
            // the first rather than left pointing at an option the block below does not draw.
            _lot = 0;

            Hud.PlaySound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        private void Shelf(int step)
        {
            _rack = (_rack + step) % Racks.Length;
            if (_rack < 0) _rack += Racks.Length;

            _row = 0;
            _lot = 0;

            Hud.PlaySound("NAV_LEFT_RIGHT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        private void Lot(int step)
        {
            var piece = Chosen;
            if (piece == null || piece.AmmoBox <= 0) return;

            _lot = (_lot + step) % Lots.Length;
            if (_lot < 0) _lot += Lots.Length;

            Hud.PlaySound("NAV_LEFT_RIGHT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        // ---- what things cost --------------------------------------------------

        /// <summary>Rounds cost a fifth of what the gun did, rounded to something tidy.</summary>
        public static int AmmoPrice(Piece piece)
        {
            return Math.Max(40, (int)Math.Round(piece.Price * 0.2f / 10f) * 10);
        }

        private int LotsNow => Lots[Math.Max(0, Math.Min(_lot, Lots.Length - 1))];

        private static int Held(Piece piece)
        {
            try
            {
                return Function.Call<int>(Hash.GET_AMMO_IN_PED_WEAPON,
                                          Game.Player.Character.Handle, piece.Hash);
            }
            catch
            {
                return 0;
            }
        }

        private static bool Owns(Piece piece)
        {
            try
            {
                return Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON,
                                           Game.Player.Character.Handle, piece.Hash, false);
            }
            catch
            {
                return false;
            }
        }

        // ---- buying ------------------------------------------------------------

        private void Buy()
        {
            var piece = Chosen;
            if (piece == null) return;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            var owned = Owns(piece);
            var rounds = owned && piece.AmmoBox > 0;

            var cost = rounds ? AmmoPrice(piece) * LotsNow : piece.Price;

            if (Game.Player.Money < cost)
            {
                Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                Notify.Problem("you're $" + (cost - Game.Player.Money).ToString("N0") + " short.");
                return;
            }

            // A melee piece you already own is nothing to sell you twice. Rounds are the only
            // repeat purchase, and a knife has none.
            if (owned && !rounds)
            {
                Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                Notify.Problem("you've already got one of them.");
                return;
            }

            try
            {
                if (rounds)
                {
                    Function.Call(Hash.ADD_AMMO_TO_PED, player.Handle, piece.Hash,
                                  piece.AmmoBox * LotsNow);
                }
                else
                {
                    Function.Call(Hash.GIVE_WEAPON_TO_PED, player.Handle, piece.Hash,
                                  piece.StarterAmmo, false, false);
                }

                Game.Player.Money -= cost;
                if (_state != null) _state.Touch();

                Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                Notify.Ticker("~y~-$" + cost.ToString("N0") + "~s~  " +
                              (rounds ? piece.AmmoBox * LotsNow + " rounds, " + piece.Name : piece.Name));

                Log.Info("Bought " + (rounds ? "rounds for " : "") + piece.Weapon +
                         " off Grimes for $" + cost + ".");

                OnBought?.Invoke(piece, rounds);
            }
            catch (Exception ex)
            {
                Log.Error("Could not hand over " + piece.Weapon + ".", ex);
                Notify.Problem("that one's not going anywhere. Pick something else.");
            }
        }

        // ---- drawing -----------------------------------------------------------

        public void Draw()
        {
            if (!IsOpen) return;

            var rows = Current.Stock.Length;
            var height = 0.300f + rows * RowHeight;

            var panelWidth = Hud.ToX(PanelWidthH);
            var pad = Hud.ToX(PadH);

            var left = 0.5f - panelWidth * 0.5f;
            var top = 0.5f - height * 0.5f;

            Hud.RectFrom(left, top, panelWidth, height, Color.FromArgb(238, 12, 13, 15));
            Hud.RectFrom(left, top, panelWidth, 0.0028f, Palette.Accent);

            var x = left + pad;
            var right = left + panelWidth - pad;
            var y = top + 0.013f;

            // His name in the house script, and what is in your pocket, which is the only other
            // number that decides anything on this screen.
            Hud.Text("GRIMES", x, y - 0.004f, 0.74f, Palette.Text, Hud.FontCursive, centre: false);
            Hud.TextRight("$" + Game.Player.Money.ToString("N0"), right, y + 0.010f, 0.34f,
                          Palette.Cash, Hud.FontChaletLondon);

            y += 0.044f;

            y = Shelves(x, y, panelWidth, pad);

            y = Stock(x, right, y, panelWidth, pad);

            Rounds(x, right, y, panelWidth, pad);

            Keys(x, top + height - 0.020f);
        }

        /// <summary>
        /// The five racks along the top, all of them visible at once.
        ///
        /// The dialogue version made each one a page you had to go into and come back out of,
        /// so knowing whether he had a shotgun meant a round trip. Five words across the top
        /// answers that without a single press.
        /// </summary>
        private float Shelves(float x, float y, float panelWidth, float pad)
        {
            var cx = x;

            for (var i = 0; i < Racks.Length; i++)
            {
                var here = i == _rack;
                var label = Racks[i].Name;

                var width = 0.02f;
                try { width = Hud.MeasureText(label, 0.26f, Hud.FontLabel); }
                catch { /* the estimate will do */ }

                if (here)
                {
                    Hud.RectFrom(cx - 0.004f, y - 0.004f, width + 0.008f, 0.024f,
                                 Color.FromArgb(46, 255, 255, 255));
                    Hud.RectFrom(cx - 0.004f, y + 0.019f, width + 0.008f, 0.0022f, Palette.Accent);
                }

                Hud.Text(label, cx, y, 0.26f, here ? Palette.Text : Palette.TextDim,
                         Hud.FontLabel, centre: false);

                cx += width + 0.022f;
            }

            y += 0.032f;

            Hud.RectFrom(x, y, panelWidth - pad * 2f, 0.0022f, Palette.Accent);
            return y + 0.012f;
        }

        private float Stock(float x, float right, float y, float panelWidth, float pad)
        {
            Hud.Text("WHAT HE'S GOT", x, y, 0.26f, Palette.TextDim, Hud.FontLabel, centre: false);
            y += 0.026f;

            foreach (var piece in Current.Stock)
            {
                var here = piece == Chosen;
                var owned = Owns(piece);

                if (here)
                {
                    Hud.RectFrom(x - pad * 0.35f, y - 0.005f,
                                 panelWidth - pad * 1.3f, RowHeight,
                                 Color.FromArgb(52, 255, 255, 255));

                    Hud.RectFrom(x - pad * 0.35f, y - 0.005f, 0.0022f, RowHeight, Palette.Accent);
                }

                // The weapon's own art. Its dictionary is named after it, so the name is the
                // whole lookup -- and a piece whose art this install has not got simply gets
                // the space, rather than a hole where a row should be.
                var art = 0f;
                if (Hud.HasTexture(piece.Weapon, piece.Weapon))
                {
                    Hud.Sprite(piece.Weapon, piece.Weapon, x + Hud.ToX(IconW) * 0.5f, y + 0.012f,
                               Hud.ToX(IconW), IconH, 0f,
                               here ? Palette.Text : Palette.TextDim);

                    art = Hud.ToX(IconW) + 0.006f;
                }

                Hud.Text(piece.Name, x + art, y, 0.30f,
                         here ? Palette.Text : Palette.TextDim, Hud.FontBody, centre: false);

                // What it is for, quietly, because the name alone does not say why you would
                // take a Double Action over a Pistol.
                Hud.Text(piece.Note, x + art + Hud.ToX(0.20f), y + 0.003f, 0.24f,
                         Palette.TextDim, Hud.FontLabel, centre: false);

                if (owned)
                {
                    var rounds = piece.AmmoBox > 0 ? "  ·  " + Held(piece) + " rounds" : "";

                    Hud.TextRight("OWNED" + rounds, right, y + 0.002f, 0.26f,
                                  Palette.Cash, Hud.FontLabel);
                }
                else
                {
                    Hud.TextRight("$" + piece.Price.ToString("N0"), right, y, 0.30f,
                                  Game.Player.Money >= piece.Price ? Palette.Text : Palette.TextDisabled,
                                  Hud.FontChaletLondon);
                }

                y += RowHeight;
            }

            y += 0.010f;
            Hud.RectFrom(x, y, panelWidth - pad * 2f, 0.0022f, Palette.Accent);
            return y + 0.012f;
        }

        private const float IconW = 0.052f;
        private const float IconH = 0.026f;

        /// <summary>
        /// Rounds for whatever is under the cursor, in an amount you choose.
        ///
        /// The old version sold one fixed box at a time and you pressed it repeatedly, which is
        /// the same decision made four times. Lots of one, two, five and ten cover a top-up and
        /// a full load without turning into a number you have to type.
        /// </summary>
        private void Rounds(float x, float right, float y, float panelWidth, float pad)
        {
            var piece = Chosen;
            if (piece == null) return;

            if (piece.AmmoBox <= 0)
            {
                Hud.Text("NO ROUNDS FOR THAT ONE", x, y, 0.26f, Palette.TextDim,
                         Hud.FontLabel, centre: false);
                return;
            }

            if (!Owns(piece))
            {
                Hud.Text("ROUNDS ONCE YOU'VE GOT ONE  ·  COMES WITH " + piece.StarterAmmo,
                         x, y, 0.26f, Palette.TextDim, Hud.FontLabel, centre: false);
                return;
            }

            Hud.Text("ROUNDS FOR THE " + piece.Name.ToUpperInvariant(), x, y, 0.26f,
                     Palette.TextDim, Hud.FontLabel, centre: false);

            y += 0.026f;

            var lots = LotsNow;
            var rounds = piece.AmmoBox * lots;
            var cost = AmmoPrice(piece) * lots;
            var afford = Game.Player.Money >= cost;

            Hud.Text("<", x, y, 0.32f, Palette.Accent, Hud.FontChaletLondon, centre: false);

            Hud.Text(lots + (lots == 1 ? " box" : " boxes") + "   ·   " + rounds + " rounds",
                     x + 0.018f, y, 0.32f, Palette.Text, Hud.FontChaletLondon, centre: false);

            Hud.Text(">", x + Hud.ToX(0.30f), y, 0.32f, Palette.Accent,
                     Hud.FontChaletLondon, centre: false);

            Hud.TextRight("$" + cost.ToString("N0"), right, y, 0.32f,
                          afford ? Palette.Cash : Palette.TextDisabled, Hud.FontChaletLondon);
        }

        private static void Keys(float x, float y)
        {
            Hud.Text("ENTER  BUY      LEFT/RIGHT  ROUNDS      SPACE  RACK      BACKSPACE  OUT",
                     x, y, 0.24f, Palette.TextDim, Hud.FontLabel, centre: false);
        }
    }
}
