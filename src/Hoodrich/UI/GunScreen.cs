using System;
using System.Collections.Generic;
using GTA;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Locations;
using Hoodrich.State;
using Hoodrich.Weapons;
using Control = GTA.Control;
using Hud = Hoodrich.UI.Draw;

namespace Hoodrich.UI
{
    /// <summary>
    /// Stretch's counter: what he has, by rack, with the one you are looking at laid out on
    /// the right -- its picture, what it is for, rounds by the box, and everything that bolts
    /// to it.
    ///
    /// TWO COLUMNS, TWO CURSORS. The left is the stock, the right is the gun in your hand,
    /// and R (X on a pad) steps the cursor across to the parts shelf and back. Everything on
    /// the right is about the gun the left is pointing at, so a rack change or a step down
    /// the list rewrites the right side entirely.
    ///
    /// THE PICTURES ARE THE GAME'S OWN, found rather than named. Every gun's icon is a
    /// texture called what weapons.json calls it -- w_pi_pistol, w_me_dagger -- inside one
    /// of a couple of dozen mpweapons dictionaries that arrived one an update, and nothing
    /// says which. So each icon is looked for in all of them as they stream in, and the
    /// dictionary that has it is remembered for the rest of the session. The old code asked
    /// for a dictionary named after the weapon, which does not exist, which is why the shop
    /// had been a list of plain names for as long as it had existed.
    /// </summary>
    internal sealed class GunScreen
    {
        private const float PanelWidthH = 0.74f;

        /// <summary>How far in from the left edge the panel sits while the bench is in view.</summary>
        private const float Aside = 0.030f;
        private const float RowHeight = 0.030f;
        private const float PartRow = 0.028f;
        private const float PadH = 0.024f;
        private const int OpenGraceMs = 220;
        private const int EnterMs = 170;
        private const float EnterRise = 0.014f;
        private const float IconW = 0.052f;
        private const float IconH = 0.026f;
        private const float BigW = 0.150f;
        private const float BigH = 0.075f;
        private const int PartsShown = 6;

        /// <summary>How many boxes of rounds at a time.</summary>
        private static readonly int[] Lots = { 1, 2, 5, 10 };

        private sealed class Rack
        {
            public readonly string Name;
            public readonly Piece[] Stock;
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
            new Rack("HANDGUNS",  Armourer.Handguns,   156),
            new Rack("SMGS",      Armourer.Smgs,       159),
            new Rack("SHOTGUNS",  Armourer.Shotguns,   158),
            new Rack("RIFLES",    Armourer.Rifles,     150),
            new Rack("SNIPERS",   Armourer.Snipers,    160),
            new Rack("BLADES",    Armourer.Melee,      154),
            new Rack("THROWN",    Armourer.Throwables, 152),
            new Rack("HEAVY",     Armourer.Heavy,      157),
        };

        private readonly PlayerState _state;
        private readonly Curtain _curtain = new Curtain();
        private readonly Glide _glide = new Glide();

        private int _rack;
        private int _row;
        private int _lot;
        private int _lastRow = -1;
        private int _pickedAt;
        private int _openedAt;
        private int _shownAt;
        private float _tabAt;
        private float _tabWide;
        private const float TabRate = 0.28f;

        /// <summary>Which column the cursor is in: the stock, or the parts shelf for the chosen gun.</summary>
        private bool _onParts;

        /// <summary>
        /// The real gun, turning in the window. See UI.GunModel for why this exists at all.
        /// </summary>
        /// <summary>
        /// When the thing being shown last changed, and which way it went.
        ///
        /// THE ANIMATION IS THE LIST NOW. A panel could show ten guns at once and let a cursor
        /// say which one you meant; one gun on a table cannot, so the only thing that says you
        /// have moved is the movement. The name that is leaving goes out the way you came from
        /// and the new one comes in from the way you are going, which is the whole of it.
        /// </summary>
        private int _slidAt;
        private int _slidDir;
        private string _slidOff = "";

        private readonly GunModel _model = new GunModel();

        /// <summary>Set by Main: where the bench is, and which way it faces. See GunModel.</summary>
        public Func<GTA.Math.Vector3> Bench
        {
            get { return _model.Bench; }
            set { _model.Bench = value; }
        }

        public Func<float> Facing
        {
            get { return _model.Facing; }
            set { _model.Facing = value; }
        }


        private int _part;
        private int _lastPart = -1;
        private int _partTop;
        private List<Parts.Part> _parts = new List<Parts.Part>();
        private Piece _partsFor;

        /// <summary>Parts paid for this session, "WEAPON|COMPONENT", so taking one off and putting it back is free.</summary>
        private readonly HashSet<string> _paid = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public GunScreen(PlayerState state)
        {
            _state = state;
        }

        public bool IsOpen => _curtain.Showing;

        /// <summary>Set by Main: a piece, or rounds for one, just changed hands.</summary>
        public Action<Piece, bool> OnBought;

        public Weapons.GunLocker Locker;

        /// <summary>Set by Main: the registry, for the icon each piece wears.</summary>
        public WeaponRegistry Guns;

        // ---- open and close ---------------------------------------------------------

        public void Open()
        {
            _curtain.Open();
            _shownAt = Game.GameTime;
            _glide.Reset();
            _lastRow = -1;
            _pickedAt = Game.GameTime;
            _tabAt = 0f;
            _tabWide = 0f;
            _openedAt = Game.GameTime;
            _rack = 0;
            _row = 0;
            _lot = 0;
            _onParts = false;
            _part = 0;
            _partTop = 0;
            _partsFor = null;
            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        /// <summary>
        /// Called when the mod stands down with the counter still up.
        ///
        /// Close returns early on a screen that is already shut, and Draw is only called while
        /// it is open -- so on a reload, an Abort or a crash-out neither of the two paths that
        /// clear the gun ever runs, and a rifle is left hanging in the air in the shop. This is
        /// the one that does not care what state anything is in.
        /// </summary>
        public void RestoreWorld()
        {
            _model.Stand();
        }

        public void Close()
        {
            if (!IsOpen) return;

            // THE GUN AND THE CAMERA BOTH GO WITH THE SCREEN. The gun is a real object on a
            // real table -- left behind it lies there for the night -- and a scripted camera
            // nobody put away is a player who cannot see where he is walking.
            _model.Stand();

            InputGuard.Swallow();
            _curtain.Close();
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

        // ---- input --------------------------------------------------------------

        public void Update()
        {
            if (!IsOpen) return;

            LockControls();

            if (!_curtain.Taking) return;
            if (Game.GameTime - _openedAt < OpenGraceMs) return;

            Shelve();

            if (Pressed(Control.PhoneCancel))
            {
                if (_onParts) { Across(); return; }
                Close();
                return;
            }

            // LEFT AND RIGHT WALK THE SHELF, DOWN GOES INTO WHAT BOLTS ON.
            //
            // The counter was a list you scrolled with a cursor because it was a panel. It is
            // not a panel any more -- it is one gun on a table with its name under it -- so the
            // shape of the input follows: sideways is the next gun, downwards is into that
            // gun's parts, and up is back out of them. There is nothing to run a cursor down.
            if (Pressed(Control.PhoneLeft)) { if (_onParts) PartMove(-1); else Move(-1); }
            else if (Pressed(Control.PhoneRight)) { if (_onParts) PartMove(1); else Move(1); }
            else if (Pressed(Control.PhoneDown)) { if (!_onParts && _parts.Count > 0) Across(); }
            else if (Pressed(Control.PhoneUp)) { if (_onParts) Across(); }

            // AMMO MOVED HERE off left and right, which now belong to the shelf. On a pad it
            // is X, which is where the hint on the bar says it is.
            else if (Pressed(Control.Reload)) Lot(1);
            else if (Pressed(Control.FrontendRb) || Pressed(Control.Jump)) Shelf(1);
            else if (Pressed(Control.FrontendLb) || Pressed(Control.Cover)) Shelf(-1);
            else if (Pressed(Control.PhoneSelect) || Pressed(Control.Context)) { if (_onParts) BuyPart(); else Buy(); }
        }

        private static bool Pressed(Control control)
        {
            return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, (int)control);
        }

        private static void LockControls()
        {
            Function.Call(Hash.DISABLE_ALL_CONTROL_ACTIONS, 0);

            foreach (var control in new[]
                     {
                         Control.PhoneUp, Control.PhoneDown, Control.PhoneLeft, Control.PhoneRight,
                         Control.PhoneSelect, Control.PhoneCancel, Control.FrontendRb, Control.FrontendLb,
                         Control.LookLeftRight, Control.LookUpDown
                     })
            {
                Function.Call(Hash.ENABLE_CONTROL_ACTION, 0, (int)control, true);
            }
        }

        /// <summary>The parts shelf follows the chosen gun; asked again only when the gun changes.</summary>
        private void Shelve()
        {
            var piece = Chosen;
            if (piece == _partsFor) return;

            _partsFor = piece;
            _parts = piece == null ? new List<Parts.Part>() : Parts.For(piece.Weapon);
            _part = 0;
            _lastPart = -1;
            _partTop = 0;
        }

        /// <summary>Remembers what is leaving and which way, for the slide. See _slidAt.</summary>
        private void Slid(int step, string leaving)
        {
            _slidAt = Game.GameTime;
            _slidDir = step >= 0 ? 1 : -1;
            _slidOff = leaving ?? "";
        }

        private void Move(int step)
        {
            var count = Current.Stock.Length;
            if (count == 0) return;

            var before = _row;

            // WHAT IS LEAVING, CAUGHT BEFORE IT GOES. The slide draws the outgoing name as
            // well as the incoming one, and once _row has moved there is nothing left that
            // knows what used to be there.
            var was = Chosen;

            _row = (_row + step + count) % count;

            if (_row != before)
            {
                _lastRow = before;
                _pickedAt = Game.GameTime;

                Slid(step, was == null ? "" : was.Name);
            }

            _lot = 0;
            Hud.PlaySound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        private void Shelf(int step)
        {
            _rack = (_rack + step + Racks.Length) % Racks.Length;
            _row = 0;
            _lastRow = -1;
            _pickedAt = Game.GameTime;
            _lot = 0;
            _onParts = false;
            Hud.PlaySound("NAV_LEFT_RIGHT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        private void Lot(int step)
        {
            var piece = Chosen;
            if (piece == null || piece.AmmoBox <= 0) return;

            _lot = (_lot + step + Lots.Length) % Lots.Length;
            Hud.PlaySound("NAV_LEFT_RIGHT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        /// <summary>Across to the parts shelf and back. The shelf only takes the cursor when there is something on it.</summary>
        private void Across()
        {
            if (!_onParts)
            {
                var piece = Chosen;
                if (piece == null || _parts.Count == 0) { Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET"); return; }
                if (!Owns(piece))
                {
                    Notify.Problem("buy the gun first.");
                    Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                    return;
                }
            }

            _onParts = !_onParts;
            _pickedAt = Game.GameTime;
            Hud.PlaySound("NAV_LEFT_RIGHT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        private void PartMove(int step)
        {
            if (_parts.Count == 0) return;

            var was = _part >= 0 && _part < _parts.Count ? _parts[_part].Name : "";

            _lastPart = _part;
            _part = (_part + step + _parts.Count) % _parts.Count;

            Slid(step, was);

            if (_part < _partTop) _partTop = _part;
            if (_part >= _partTop + PartsShown) _partTop = _part - PartsShown + 1;

            _pickedAt = Game.GameTime;
            Hud.PlaySound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        // ---- money --------------------------------------------------------------

        public static int AmmoPrice(Piece piece)
        {
            return Math.Max(40, (int)Math.Round(piece.Price * 0.2f / 10f) * 10);
        }

        private int LotsNow => Lots[Math.Max(0, Math.Min(_lot, Lots.Length - 1))];

        private static int Held(Piece piece)
        {
            try
            {
                return Function.Call<int>(Hash.GET_AMMO_IN_PED_WEAPON, Game.Player.Character.Handle, piece.Hash);
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
                return Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON, Game.Player.Character.Handle, piece.Hash, false);
            }
            catch
            {
                return false;
            }
        }

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

            if (owned && !rounds)
            {
                Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                Notify.Problem("you've already got one of them.");
                return;
            }

            var fitted = false;

            try
            {
                if (rounds)
                {
                    Function.Call(Hash.ADD_AMMO_TO_PED, player.Handle, piece.Hash, piece.AmmoBox * LotsNow);
                }
                else
                {
                    Function.Call(Hash.GIVE_WEAPON_TO_PED, player.Handle, piece.Hash, piece.StarterAmmo, false, false);
                    fitted = ExtendedClips.GiveTo(player, piece.Weapon);
                    if (Locker != null) Locker.Bought(piece.Weapon);
                }

                Cash.Take(cost);
                if (_state != null) _state.Touch();

                Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                Notify.Ticker("~y~-$" + cost.ToString("N0") + "~s~  " +
                              (rounds ? piece.AmmoBox * LotsNow + " rounds, " + piece.Name
                                      : piece.Name + (fitted ? "  ~g~+ extended mag~s~" : "")));
                Log.Info("Bought " + (rounds ? "rounds for " : "") + piece.Weapon + " off Stretch for $" + cost +
                         (fitted ? " (extended mag fitted)." : "."));

                OnBought?.Invoke(piece, rounds);
            }
            catch (Exception ex)
            {
                Log.Error("Could not hand over " + piece.Weapon + ".", ex);
                Notify.Problem("that one's not going anywhere. Pick something else.");
            }
        }

        /// <summary>
        /// A part: bought and fitted, or taken off again. Paid for once -- a scope you take off
        /// to try the other one goes back on for nothing, this session.
        /// </summary>
        private void BuyPart()
        {
            var piece = Chosen;
            if (piece == null || _parts.Count == 0) return;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            var part = _parts[Math.Max(0, Math.Min(_part, _parts.Count - 1))];
            var key = piece.Weapon + "|" + part.Component;

            try
            {
                if (Parts.Fitted(player, piece.Hash, part))
                {
                    Parts.Fit(player, piece.Hash, part, false);
                    Hud.PlaySound("BACK", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                    Notify.Ticker(part.Name + " off the " + piece.Name + ".");
                    if (_state != null) _state.Touch();
                    return;
                }

                var cost = _paid.Contains(key) ? 0 : part.Price;

                if (Game.Player.Money < cost)
                {
                    Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                    Notify.Problem("you're $" + (cost - Game.Player.Money).ToString("N0") + " short.");
                    return;
                }

                Parts.Fit(player, piece.Hash, part, true);

                if (!Parts.Fitted(player, piece.Hash, part))
                {
                    Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                    Notify.Problem("that won't go on this one.");
                    return;
                }

                if (cost > 0) Cash.Take(cost);
                _paid.Add(key);
                if (_state != null) _state.Touch();

                Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                Notify.Ticker((cost > 0 ? "~y~-$" + cost.ToString("N0") + "~s~  " : "") + part.Name + " on the " + piece.Name + ".");
                Log.Info("Fitted " + part.Component + " to " + piece.Weapon + (cost > 0 ? " for $" + cost + "." : "."));
            }
            catch (Exception ex)
            {
                Log.Error("Could not fit " + part.Component + ".", ex);
                Notify.Problem("that one won't go on.");
            }
        }

        // ---- the pictures ----------------------------------------------------------

        private string IconOf(Piece piece)
        {
            if (Guns == null || piece == null) return "";
            try
            {
                var def = Guns.Get(piece.Hash);
                return def == null ? "" : def.Icon ?? "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// The gun in the window: which one, wearing what, and where it is stood.
        ///
        /// EVERYTHING HE HAS ON IT, PLUS THE ONE UNDER THE CURSOR. A preview of the gun as it
        /// is would be a photograph with extra steps; the point of it is answering "what does
        /// this look like with that on", so the part being looked at is fitted whether it has
        /// been bought or not. Scroll off it and it comes back off.
        /// </summary>
        private void Posing()
        {
            var piece = Chosen;

            if (piece == null)
            {
                _model.Stand();
                return;
            }

            var parts = new List<uint>();

            try
            {
                var player = Game.Player.Character;

                if (player != null && player.Exists())
                {
                    for (var i = 0; i < _parts.Count; i++)
                    {
                        var part = _parts[i];
                        if (part == null) continue;

                        var on = Parts.Fitted(player, piece.Hash, part);

                        // The one being looked at goes on either way, and only while the parts
                        // list is the thing being read.
                        if (_onParts && i == _part) on = true;

                        if (on) parts.Add(part.Hash);
                    }
                }
            }
            catch
            {
                // A bare gun is still a gun.
            }

            _model.Show(piece.Hash, parts);

            // NOT GATED ON THERE BEING ONE YET. Turn is what drives the wait for a model that
            // is still streaming, so gating it on the object existing meant a gun that had not
            // arrived could never arrive -- which is every weapon he does not already own.
            _model.Turn();
            _model.Watch(true);
        }

        /// <summary>
        /// The gun's photograph, if the game has one on this install.
        ///
        /// All of the finding moved to UI.GunArt, which sweeps for the art packs once and then
        /// remembers what it found between sessions. This used to ask for forty texture
        /// dictionaries every frame and give up on each gun after five seconds, which found
        /// five guns out of twenty and wrote the other fifteen off as having no picture.
        /// </summary>
        private static bool Art(string icon, float cx, float cy, float w, float h, System.Drawing.Color ink)
        {
            return GunArt.Draw(icon, cx, cy, w, h, ink);
        }

        /// <summary>
        /// The name a part's picture goes by in the packs.
        ///
        /// DERIVED, THEN CHECKED BY THE PACK. The game names attachment art after the
        /// component with the prefix swapped -- COMPONENT_AT_PI_SUPP is w_at_pi_supp -- and a
        /// magazine after the gun it fits with _mag2 on the end. Both are spellings the packs
        /// are asked about and may say no to; a miss draws nothing and is written down once,
        /// so a wrong guess costs a blank, never a wrong picture.
        /// </summary>
        private static string PartIcon(string gunIcon, string component)
        {
            if (string.IsNullOrEmpty(component)) return "";

            var c = component.ToLowerInvariant();

            if (c.StartsWith("component_at_", StringComparison.Ordinal)) return "w_" + c.Substring("component_".Length);

            var clip = c.IndexOf("_clip_", StringComparison.Ordinal);
            if (clip >= 0 && !string.IsNullOrEmpty(gunIcon))
            {
                var n = c.Substring(clip + "_clip_".Length).TrimStart('0');
                return gunIcon + "_mag" + (n.Length == 0 ? "1" : n);
            }

            return "";
        }

        // ---- drawing --------------------------------------------------------------

        public void Draw()
        {
            // The one place the sweep is allowed to run: the counter is open, so a few frames
            // spent finding out which art packs this install has are frames nobody is driving.
            GunArt.Update();

            DrawIt();
        }

        private void DrawIt()
        {
            if (!IsOpen)
            {
                // Shut. Nothing of ours is left in the room and the camera is the player's
                // again. See GunModel.
                _model.Stand();
                return;
            }

            Shelve();

            // AFTER the guard, so a closed counter is not quietly spawning weapons behind it.
            Posing();

            var age = Game.GameTime - _shownAt;
            var arrive = age >= EnterMs ? 1f : age / (float)EnterMs;
            arrive = 1f - (1f - arrive) * (1f - arrive);

            // NO PANEL AT ALL, which is the whole of this layout.
            //
            // There is a gun on a table with a camera on it. A rectangle over the top of that
            // is a picture of a thing in front of the thing -- so everything here is text laid
            // on the shot, low enough to leave the bench clear, in the manner the corner HUD
            // already uses while you are posted up.
            const float mid = 0.5f;

            var piece = Chosen;

            // WHAT SHELF, and how much of it he has. Small, above the name, because it is the
            // thing you change least often.
            Hud.Text(Current.Name.ToUpperInvariant(), mid, RackY, 0.30f,
                     Fade(Palette.TextDim, arrive), Hud.FontLabel);

            Hud.Text(Held(Current) + " / " + Current.Stock.Length, mid, RackY + 0.024f, 0.26f,
                     Fade(Palette.TextDim, arrive), Hud.FontLabel);

            if (piece == null)
            {
                Keys(mid - Hud.ToX(0.34f), mid + Hud.ToX(0.34f), HintY);
                return;
            }

            // ---- the one that is leaving, and the one that is arriving ----
            var slid = Game.GameTime - _slidAt;
            var t = slid >= SlideMs ? 1f : slid / (float)SlideMs;
            t = 1f - (1f - t) * (1f - t);

            var shove = Hud.ToX(SlideBy);

            if (t < 1f && _slidOff.Length > 0)
            {
                // Out the way you came from, fading as it goes.
                Hud.Text(_slidOff.ToUpperInvariant(), mid - _slidDir * shove * t, NameY, 0.62f,
                         Fade(Palette.Text, (1f - t) * 0.55f * arrive), Hud.FontChaletLondon);
            }

            var owned = Owns(piece);

            Hud.Text(piece.Name.ToUpperInvariant(), mid + _slidDir * shove * (1f - t), NameY, 0.62f,
                     Fade(Palette.Text, t * arrive), Hud.FontChaletLondon);

            Hud.Text(owned ? "OWNED" : "$" + piece.Price.ToString("N0"), mid, PriceY, 0.40f,
                     Fade(owned ? Palette.Cash : Palette.Text, t * arrive), Hud.FontChaletLondon);

            if (!string.IsNullOrEmpty(piece.Note))
            {
                Hud.Text(piece.Note, mid, NoteY, 0.28f,
                         Fade(Palette.TextDim, t * arrive), Hud.FontBody);
            }

            // ---- rounds ----
            Hud.Text(Boxes(piece), mid, AmmoY, 0.30f,
                     Fade(Palette.TextDim, arrive), Hud.FontBody);

            // ---- and what bolts on, once you have gone down into it ----
            if (_onParts && _part >= 0 && _part < _parts.Count)
            {
                var part = _parts[_part];

                var on = Bolted(piece, part);

                Hud.Text((_part + 1) + " / " + _parts.Count, mid, PartCountY, 0.26f,
                         Fade(Palette.TextDim, arrive), Hud.FontLabel);

                Hud.Text(part.Name.ToUpperInvariant(),
                         mid + _slidDir * shove * (1f - t), PartY, 0.44f,
                         Fade(on ? Palette.Cash : Palette.Text, t * arrive), Hud.FontChaletLondon);

                Hud.Text(on ? "FITTED" : (part.Price > 0 ? "$" + part.Price.ToString("N0") : "FREE"),
                         mid, PartPriceY, 0.30f,
                         Fade(Palette.TextDim, t * arrive), Hud.FontBody);
            }
            else if (_parts.Count > 0)
            {
                Hud.Text(_parts.Count + " PARTS", mid, PartCountY, 0.26f,
                         Fade(Palette.TextDim, arrive), Hud.FontLabel);
            }

            Keys(mid - Hud.ToX(0.34f), mid + Hud.ToX(0.34f), HintY);
        }

        /// <summary>Where each line sits. Everything is low, so the bench stays clear.</summary>
        private const float RackY = 0.700f;
        private const float NameY = 0.748f;
        private const float PriceY = 0.800f;
        private const float NoteY = 0.828f;
        private const float AmmoY = 0.856f;
        private const float PartCountY = 0.884f;
        private const float PartY = 0.906f;
        private const float PartPriceY = 0.940f;
        private const float HintY = 0.968f;

        /// <summary>How far a name travels as it comes in, and how long it takes.</summary>
        private const float SlideBy = 0.13f;
        private const int SlideMs = 190;

        /// <summary>How many of a shelf he already has.</summary>
        private static int Held(Rack rack)
        {
            var got = 0;
            foreach (var piece in rack.Stock) if (Owns(piece)) got++;
            return got;
        }

        /// <summary>The rounds line, as one string rather than a column of them.</summary>
        private string Boxes(Piece piece)
        {
            if (piece.AmmoBox <= 0) return "NO ROUNDS FOR THAT ONE";

            // LotsNow is the number the lot index means, which is not the index.
            var lots = LotsNow;
            var rounds = piece.AmmoBox * lots;
            var cost = AmmoPrice(piece) * lots;

            return lots + (lots == 1 ? " box" : " boxes") + "  ·  " + rounds + " rounds  ·  $" + cost.ToString("N0");
        }

        /// <summary>Whether that part is already on that gun.</summary>
        private static bool Bolted(Piece piece, Parts.Part part)
        {
            try
            {
                var player = Game.Player.Character;
                return player != null && player.Exists() && Parts.Fitted(player, piece.Hash, part);
            }
            catch
            {
                return false;
            }
        }

        private static System.Drawing.Color Fade(System.Drawing.Color c, float by)
        {
            var a = (int)(c.A * Math.Max(0f, Math.Min(1f, by)));
            return System.Drawing.Color.FromArgb(a, c.R, c.G, c.B);
        }

        /// <summary>
        /// The shop's name, as its own sign rather than as typed words.
        ///
        /// IT WAS "HOOD WEAPONRY" SET IN THE SCRIPT FACE, which is the face the phone uses
        /// for a person's name -- so the counter was introducing itself in the same hand a
        /// contact does. A shop has a sign. This is one: heavy squared capitals, spaced,
        /// sat on a rule, drawn from tools/make_armoury.py.
        ///
        /// The words come back if the file is not there, so an install missing the icon
        /// gets a header rather than a gap.
        /// </summary>
        private void Sign(float x, float y)
        {
            var tall = SignHeight;
            var wide = Hud.ToX(tall * ArmouryAspect);

            if (Hud.File("armoury.png", x + wide * 0.5f, y + 0.013f, wide, tall, 0f, Palette.Text)) return;

            Hud.Text("HOOD ARMOURY", x, y - 0.004f, 0.74f, Palette.Text, Hud.FontCursive, centre: false);
        }

        /// <summary>How tall the sign is, and the shape of the file. See tools/make_armoury.py.</summary>
        private const float SignHeight = 0.030f;
        private const float ArmouryAspect = 5.9204f;

        private float Shelves(float x, float y, float panelWidth, float pad)
        {
            var cx = x;
            var at = new float[Racks.Length];
            var wide = new float[Racks.Length];

            for (var i = 0; i < Racks.Length; i++)
            {
                var width = 0.02f;
                try { width = Hud.MeasureText(Racks[i].Name, 0.26f, Hud.FontLabel); }
                catch { /* the estimate will do */ }

                at[i] = cx;
                wide[i] = width;
                cx += width + 0.022f;
            }

            if (_tabWide <= 0f)
            {
                _tabAt = at[_rack];
                _tabWide = wide[_rack];
            }

            _tabAt += (at[_rack] - _tabAt) * TabRate;
            _tabWide += (wide[_rack] - _tabWide) * TabRate;
            if (Math.Abs(at[_rack] - _tabAt) < 0.0005f) _tabAt = at[_rack];
            if (Math.Abs(wide[_rack] - _tabWide) < 0.0005f) _tabWide = wide[_rack];

            Hud.RectFrom(_tabAt - 0.004f, y - 0.004f, _tabWide + 0.008f, 0.024f, Palette.Alpha(Palette.Brand, 26));
            Hud.RectFrom(_tabAt - 0.004f, y + 0.019f, _tabWide + 0.008f, 0.0022f, Palette.BrandDeep);

            for (var i = 0; i < Racks.Length; i++)
            {
                var here = i == _rack;
                Hud.Text(Racks[i].Name, at[i], y, 0.26f, here ? Palette.Text : Palette.TextDim, Hud.FontLabel, centre: false);

                var got = 0;
                foreach (var piece in Racks[i].Stock)
                {
                    if (Owns(piece)) got++;
                }

                Hud.Text(got + "/" + Racks[i].Stock.Length, at[i], y + 0.021f, 0.20f,
                         Palette.Alpha(here ? Palette.Cash : Palette.TextDim, 190), Hud.FontLabel, centre: false);
            }

            y += 0.038f;
            Theme.Rule(x, y, panelWidth - pad * 2f);
            return y + 0.012f;
        }

        private void StockColumn(float x, float right, float y, float pad, float arrive)
        {
            Hud.Text("WHAT HE'S GOT", x, y, 0.26f, Palette.TextDim, Hud.FontLabel, centre: false);
            y += 0.026f;

            var grown = Theme.Grown(_pickedAt);
            var barWide = (right - x) + pad * 0.7f;

            for (var i = 0; i < Current.Stock.Length; i++)
            {
                var piece = Current.Stock[i];
                var here = i == _row;
                var owned = Owns(piece);

                // The cursor dims while it is across on the parts shelf, so one column reads
                // as live at a time.
                var lit = Theme.Lit(i, _row, _lastRow, grown) * arrive * (_onParts ? 0.45f : 1f);

                Theme.Plate(x - pad * 0.35f, y - 0.005f, barWide, RowHeight, lit);
                Theme.Sheen(x - pad * 0.35f, y - 0.005f, barWide, RowHeight, lit);
                if (here && !_onParts) _glide.Target(x - pad * 0.35f, y - 0.005f, barWide, RowHeight);

                var ink = Theme.Ink(here ? Palette.Text : Palette.TextDim, lit);

                // The space for the picture is kept whether or not it has arrived yet, so the
                // names do not jump while it streams in.
                Art(IconOf(piece), x + Hud.ToX(IconW) * 0.5f, y + 0.012f, Hud.ToX(IconW), IconH, ink);
                var art = Hud.ToX(IconW) + 0.006f;

                Hud.Text(piece.Name, x + art, y, 0.30f, ink, Hud.FontBody, centre: false);

                if (owned)
                {
                    Hud.TextRight("OWNED", right, y + 0.002f, 0.26f, Theme.Ink(Palette.Cash, lit), Hud.FontLabel);
                }
                else
                {
                    Hud.TextRight("$" + piece.Price.ToString("N0"), right, y, 0.30f,
                                  Theme.Ink(Game.Player.Money >= piece.Price ? Palette.Text : Palette.TextDisabled, lit),
                                  Hud.FontChaletLondon);
                }

                y += RowHeight;
            }
        }

        private void ChosenColumn(float x, float right, float y, float floor, float arrive)
        {
            var piece = Chosen;
            if (piece == null) return;

            var owned = Owns(piece);
            var wide = right - x;

            // NO PICTURE AT ALL WHILE THE REAL ONE IS ON THE BENCH. A photograph of the gun
            // beside a camera shot of the same gun is the same thing twice, and the smaller,
            // flatter copy is the one that loses.
            if (_model.Live) return;

            // A soft plate behind the picture, so a white icon has something to sit on.
            Hud.RectFrom(x, y, wide, BigH + 0.024f, Palette.Alpha(Palette.PanelRowAlt, 18));
            Theme.Rim(x, y, wide, BigH + 0.024f, 0.0014f, Palette.Alpha(Theme.RimInk, 40));

            var drawn = Art(IconOf(piece), x + wide * 0.5f, y + 0.012f + BigH * 0.5f, Hud.ToX(BigW), BigH, Palette.Text);
            if (!drawn)
            {
                Hud.Text(Current.Name, x + wide * 0.5f, y + 0.012f + BigH * 0.5f - 0.012f, 0.30f,
                         Palette.TextDim, Hud.FontLabel);
            }

            y += BigH + 0.034f;

            Hud.Text(piece.Name, x, y, 0.42f, Palette.Text, Hud.FontBody, centre: false);
            y += 0.036f;

            foreach (var line in Hud.Wrap(piece.Note, 0.26f, Hud.FontLabel, wide))
            {
                Hud.Text(line, x, y, 0.26f, Palette.TextDim, Hud.FontLabel, centre: false);
                y += 0.020f;
            }

            y += 0.008f;
            Theme.Rule(x, y, wide);
            y += 0.012f;

            y = Rounds(piece, owned, x, right, y);

            y += 0.008f;
            Theme.Rule(x, y, wide);
            y += 0.012f;

            PartsShelf(piece, owned, x, right, y, floor, arrive);
        }

        private float Rounds(Piece piece, bool owned, float x, float right, float y)
        {
            if (piece.AmmoBox <= 0)
            {
                Hud.Text("NO ROUNDS FOR THAT ONE", x, y, 0.26f, Palette.TextDim, Hud.FontLabel, centre: false);
                return y + 0.024f;
            }

            if (!owned)
            {
                Hud.Text("ROUNDS ONCE YOU'VE GOT ONE  ·  COMES WITH " + piece.StarterAmmo, x, y, 0.26f,
                         Palette.TextDim, Hud.FontLabel, centre: false);
                return y + 0.024f;
            }

            Hud.Text("ROUNDS", x, y, 0.26f, Palette.TextDim, Hud.FontLabel, centre: false);
            Hud.TextRight(Held(piece) + " HELD", right, y, 0.24f, Palette.TextDim, Hud.FontLabel);
            y += 0.024f;

            var lots = LotsNow;
            var rounds = piece.AmmoBox * lots;
            var cost = AmmoPrice(piece) * lots;
            var afford = Game.Player.Money >= cost;
            var ink = _onParts ? Palette.TextDim : Palette.Text;

            Hud.Text("<  " + lots + (lots == 1 ? " box" : " boxes") + "  ·  " + rounds + " rounds  >", x, y, 0.30f, ink,
                     Hud.FontChaletLondon, centre: false);
            Hud.TextRight("$" + cost.ToString("N0"), right, y, 0.30f,
                          afford ? Palette.Cash : Palette.TextDisabled, Hud.FontChaletLondon);

            return y + 0.030f;
        }

        private void PartsShelf(Piece piece, bool owned, float x, float right, float y, float floor, float arrive)
        {
            Hud.Text("WHAT BOLTS ON", x, y, 0.26f, Palette.TextDim, Hud.FontLabel, centre: false);

            if (_parts.Count == 0)
            {
                Hud.TextRight("NOTHING", right, y, 0.24f, Palette.TextDim, Hud.FontLabel);
                return;
            }

            Hud.TextRight(_parts.Count + (_parts.Count == 1 ? " PART" : " PARTS") + (owned ? "" : "  ·  BUY THE GUN FIRST"),
                          right, y, 0.24f, Palette.TextDim, Hud.FontLabel);
            y += 0.024f;

            var player = Game.Player.Character;
            var grown = Theme.Grown(_pickedAt);
            var wide = right - x;
            var shown = Math.Min(PartsShown, _parts.Count);

            for (var i = _partTop; i < Math.Min(_parts.Count, _partTop + shown); i++)
            {
                if (y + PartRow > floor) break;

                var part = _parts[i];
                var here = _onParts && i == _part;
                var fitted = owned && Parts.Fitted(player, piece.Hash, part);
                var lit = _onParts ? Theme.Lit(i, _part, _lastPart, grown) * arrive : 0f;

                Theme.Plate(x - Hud.ToX(0.006f), y - 0.004f, wide + Hud.ToX(0.012f), PartRow, lit);
                Theme.Sheen(x - Hud.ToX(0.006f), y - 0.004f, wide + Hud.ToX(0.012f), PartRow, lit);
                if (here) _glide.Target(x - Hud.ToX(0.006f), y - 0.004f, wide + Hud.ToX(0.012f), PartRow);

                var ink = Theme.Ink(here ? Palette.Text : Palette.TextDim, lit);

                // A tick for what is on it, drawn as a rail so it reads from across the panel.
                if (fitted) Hud.RectFrom(x - Hud.ToX(0.006f), y - 0.004f, Hud.ToX(0.0026f), PartRow, Palette.Cash);

                // THE PART'S OWN PICTURE, out of the same packs the guns come from. The slot is
                // reserved whether or not the art lands, so a row does not jump sideways the
                // frame its texture streams in; a pack that has not got it draws nothing and
                // the name stands on its own, the same as the guns.
                var slot = Hud.ToX(PartRow * 1.7f);
                Art(PartIcon(IconOf(piece), part.Component), x + slot * 0.5f, y + PartRow * 0.5f - 0.003f,
                    slot * 0.92f, PartRow * 0.92f, ink);

                Hud.Text(part.Name, x + slot + Hud.ToX(0.004f), y, 0.28f, ink, Hud.FontBody, centre: false);

                var paid = _paid.Contains(piece.Weapon + "|" + part.Component);
                var tag = fitted ? "FITTED" : paid ? "PAID" : "$" + part.Price.ToString("N0");
                Hud.TextRight(tag, right, y + 0.002f, 0.26f,
                              fitted ? Theme.Ink(Palette.Cash, lit) : Theme.Ink(Game.Player.Money >= part.Price || paid ? ink : Palette.TextDisabled, lit),
                              Hud.FontLabel);

                y += PartRow;
            }

            if (_parts.Count > shown)
            {
                Hud.TextRight((_part + 1) + " / " + _parts.Count, right, y, 0.22f, Palette.TextDim, Hud.FontLabel);
            }
        }

        private void Keys(float x, float right, float y)
        {
            var pad = Hud.OnPad;
            var ink = Palette.TextDim;

            var hx = Hud.Hint("arrow_updown.png", "PICK", x, y, 0.24f, ink);
            hx = Hud.Hint("arrow_leftright.png", _onParts ? "BACK" : "ROUNDS", hx, y, 0.24f, ink);
            hx = Hud.Hint("crate.png", (pad ? "LB / RB" : "SPACE") + "  RACK", hx, y, 0.24f, ink);
            hx = Hud.Hint("key.png", (pad ? "X" : "R") + (_onParts ? "  STOCK" : "  PARTS"), hx, y, 0.24f, ink);
            Hud.Hint("cash.png", (pad ? "A" : "ENTER") + (_onParts ? "  FIT / TAKE OFF" : "  BUY"), hx, y, 0.24f, ink);

            Hud.TextRight(pad ? "B  OUT" : "BACKSPACE  OUT", right, y, 0.24f, ink, Hud.FontLabel);
        }
    }
}
