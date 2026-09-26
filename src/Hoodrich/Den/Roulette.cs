using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.UI;

namespace Hoodrich.Den
{
    /// <summary>
    /// Roulette, at the den's table, the way the Diamond plays it.
    ///
    /// Michael asked on 2026-09-26 for "the actual gambling feature, same graphics, everything --
    /// vanilla function". The Diamond's own table is a GTA Online script and does not run in the
    /// story game, so this is that table rebuilt out of the casino's own parts: you sit in one of
    /// the table's chairs, the camera goes over the felt, a chip follows your cursor across the
    /// layout and the numbers it covers light up with the casino's markers, the chips you put down
    /// stay on the felt, and the wheel turns and the ball goes round it on the casino's clips,
    /// into the pocket the clip is for. The dealer says the number.
    ///
    /// THE BALL WAS INVISIBLE UNTIL 2026-09-26, and the reason is worth keeping. Its clips are
    /// authored from the WHEEL, not from the table: every working script that drives it puts the
    /// ball on the table's Roulette_Wheel bone, turned a quarter round, and plays the clip on the
    /// ball itself. Played in a scene on the table's origin, the ball went round inside the base
    /// of the table, a metre under the felt.
    ///
    /// WHICH POCKET IS WHICH NUMBER. The clips are numbered by pocket -- exit_1 to exit_38 -- and
    /// two scripts written independently agree on the order: exit_1 is the double zero, and it
    /// goes 00, 27, 10, 25 ... round to exit_20, the zero, and on to exit_38, the one. The table
    /// before 2026-09-26 started at the zero and was half a wheel out: every number the den
    /// called was the one opposite where the ball lay.
    ///
    /// AMERICAN, because the wheel is: 0 to 36 and 00, and a zero of either kind takes every
    /// outside bet. Straight up 35 to 1, split 17, street 11, corner 8, six line 5, dozens and
    /// columns 2, and the even-money bets.
    /// </summary>
    internal sealed class Roulette
    {
        private enum Stage { Sitting, Betting, Closing, Spinning, Settling, Clearing, Standing, Done }

        private enum Wheeling { Intro, Loop, Exit, Rest }

        private enum Kind { Straight, Split, Street, Corner, SixLine, Red, Black, Odd, Even, Low, High, Dozen1, Dozen2, Dozen3, Column1, Column2, Column3 }

        private static readonly string[] KindNames =
        {
            "Straight up", "Split", "Street", "Corner", "Six line", "Red", "Black", "Odd", "Even", "1 to 18", "19 to 36",
            "1st 12", "2nd 12", "3rd 12", "Column 1", "Column 2", "Column 3"
        };

        /// <summary>What each kind pays, to one.</summary>
        private static readonly int[] KindPays = { 35, 17, 11, 8, 5, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2 };

        /// <summary>
        /// Pocket clip index (1..38) to the number in it; 37 stands for 00. The order two
        /// independent scripts agree on -- see the note on the class.
        /// </summary>
        private static readonly int[] Wheel =
        {
            37, 27, 10, 25, 29, 12, 8, 19, 31, 18, 6, 21, 33, 16, 4, 23, 35, 14, 2,
            0, 28, 9, 26, 30, 11, 7, 20, 32, 17, 5, 22, 34, 15, 3, 24, 36, 13, 1
        };

        private static readonly HashSet<int> Reds = new HashSet<int>
        {
            1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36
        };

        // ---- the layout, in the table's own space -----------------------------------------
        //
        // Twelve rows of three from the zeros end, 1-2-3 nearest the wheel: row u, column v, cell
        // centres GridX + CellX * u across and GridY + CellY * v along. The dozens and the
        // even-money boxes run below column one, the 2 to 1 boxes past the last row. Measured by
        // the scripts that drive this table and checked against each other.

        private const float GridX = -0.057f;
        private const float CellX = 0.081f;
        private const float GridY = -0.192f;
        private const float CellY = 0.167f;
        private const float Felt = 0.9448f;

        /// <summary>Where the two zeros sit, in cells.</summary>
        private const float ZeroU = -0.9877f;
        private const float ZeroV = 0.2635f;
        private const float DoubleZeroV = 1.7904f;

        /// <summary>The rows of the dozens and of the even-money boxes, in cells.</summary>
        private const float DozenV = -0.665f;
        private const float OutsideV = -1.14f;

        /// <summary>How near a line the cursor has to be to bet on the line rather than the number.</summary>
        private const float Edge = 0.3f;

        /// <summary>Where the arrows stop: every number, every line between, the zeros, the boxes.</summary>
        private static readonly float[] StopsU = MakeStopsU();
        private static readonly float[] StopsV = { OutsideV, DozenV, -0.5f, 0f, 0.5f, 1f, 1.5f, 2f };

        private static float[] MakeStopsU()
        {
            var list = new List<float> { -1f };
            for (var i = 0; i <= 22; i++) list.Add(i * 0.5f);
            list.Add(12f);
            return list.ToArray();
        }

        /// <summary>A place on the layout a chip can go, what it covers, and where the chip sits.</summary>
        private sealed class Spot
        {
            public Kind Kind;
            public int[] Covers;
            public float U;
            public float V;

            public string Key => Kind + ":" + string.Join(",", Covers);
        }

        private sealed class Bet
        {
            public Spot Spot;
            public int Stake;
            public Prop Chip;
            public int ChipFor;
        }

        private const string TableDict = Scene.RouletteTable;
        private const string BallModel = "vw_prop_roulette_ball";
        private const string MarkerNumber = "vw_prop_vw_marker_02a";
        private const string MarkerZero = "vw_prop_vw_marker_01a";

        /// <summary>SET_OBJECT_TEXTURE_VARIATION, which ScriptHookVDotNet 3.6 has no name for.</summary>
        private const ulong TextureVariation = 0x971DA0055324D033;

        /// <summary>How long the ball is left going round before it drops.</summary>
        private const int LoopMs = 4200;

        /// <summary>The most any one stage of the wheel is waited on, in case a clip never reports.</summary>
        private const int WheelMostMs = 16000;

        /// <summary>How long a clip is given to start before "not playing it" is believed.</summary>
        private const int WheelGraceMs = 450;

        /// <summary>How long the result stands before the dealer rakes the table.</summary>
        private const int ResultMs = 4200;

        /// <summary>How long the camera stays on the wheel once the ball is in.</summary>
        private const int WheelLingerMs = 2400;

        /// <summary>How often a held arrow moves the chip.</summary>
        private const int RepeatMs = 140;

        private readonly Entity _table;
        private readonly Dealer _dealer;
        private readonly Ped _me;
        private readonly Random _rng;
        private readonly int _minBet;
        private readonly int _maxBet;
        private readonly Sitter _sitter;
        private readonly TableCam _cam = new TableCam();

        private Stage _stage = Stage.Sitting;
        private Wheeling _wheeling = Wheeling.Rest;
        private int _wheelFrom;

        private float _u = 5f;
        private float _v = 1f;
        private Spot _aim;
        private int _repeatAt;

        private readonly int[] _chips;
        private int _chip;

        private readonly List<Bet> _bets = new List<Bet>();
        private Prop _cursor;
        private int _cursorFor = -1;

        /// <summary>The casino's markers, one on every number: 1..36, then 0, then 00.</summary>
        private readonly Prop[] _markers = new Prop[38];
        private readonly bool[] _lit = new bool[38];

        private Prop _ball;
        private bool _saidBone;
        private int _pocket;

        /// <summary>The pocket whose clip the wheel is still holding, from the last spin.</summary>
        private int _held;

        private int _number;
        private int _won;
        private int _staked;
        private int _session;
        private int _at;
        private int _lingerAt;
        private bool _onWheel;
        private int _ballSound = -1;
        private string _said = "";
        private int _saidUntil;
        private bool _placeSaid;

        private readonly List<int> _zones = new List<int>();

        public Roulette(Entity table, Dealer dealer, Ped me, Seat seat, int minBet, int maxBet, Random rng)
        {
            _table = table;
            _dealer = dealer;
            _me = me;
            _rng = rng;
            _minBet = Math.Max(1, minBet);
            _maxBet = Math.Max(_minBet, maxBet);

            var chips = new List<int>();

            foreach (var v in Chips.Values)
            {
                if (v >= _minBet && v <= _maxBet) chips.Add(v);
            }

            if (chips.Count == 0) chips.Add(_minBet);
            _chips = chips.ToArray();

            Chips.Ask();
            Models.Ready(new Model(BallModel));
            Models.Ready(new Model(MarkerNumber));
            Models.Ready(new Model(MarkerZero));

            if (seat != null)
            {
                _sitter = new Sitter(me, seat, "idle_a", me.Gender == Gender.Female ? FemaleFidgets : MaleFidgets, rng);
                _sitter.Enter();
            }
            else
            {
                _stage = Stage.Betting;
            }

            Hud(false);
            _dealer.Say("MINIGAME_DEALER_GREET");

            Log.Info("Den: sat down at the roulette" + (seat != null ? ", chair " + seat.Number : ", stood (the table has no chairs)") + ".");
        }

        private static readonly string[] MaleFidgets =
        {
            "idle_var_01", "idle_var_02", "idle_var_03", "idle_var_04", "idle_var_05", "idle_var_06", "idle_var_07",
            "idle_var_08", "idle_var_09", "idle_var_10", "idle_var_11", "idle_var_12", "idle_var_13", "idle_b", "idle_c", "idle_d"
        };

        private static readonly string[] FemaleFidgets =
        {
            "female_idle_var_01", "female_idle_var_02", "female_idle_var_03", "female_idle_var_04",
            "female_idle_var_05", "female_idle_var_06", "female_idle_var_07", "female_idle_var_08"
        };

        public bool Finished => _stage == Stage.Done;

        public void Update()
        {
            if (_table == null || !_table.Exists()) { Abandon(); return; }

            if (_sitter != null) _sitter.Update();
            _cam.Update();

            switch (_stage)
            {
                case Stage.Sitting:
                    if (_sitter == null || _sitter.Down) Open();
                    break;

                case Stage.Betting:
                    Betting();
                    break;

                case Stage.Closing:
                    // The wheel goes the moment her hand does: the spin is queued behind the wave-off.
                    if (_dealer.Doing == "spin_wheel" || !_dealer.Busy) Spin();
                    break;

                case Stage.Spinning:
                    Spinning();
                    break;

                case Stage.Settling:
                    Settling();
                    break;

                case Stage.Clearing:
                    Clearing();
                    break;

                case Stage.Standing:
                    if (_sitter == null || _sitter.Gone) Finish();
                    break;
            }

            if (_stage != Stage.Done) Draw();
        }

        // ---- the felt -----------------------------------------------------------------------

        /// <summary>Bets open: the camera over the felt, the chip in your hand, and the dealer asking.</summary>
        private void Open()
        {
            _stage = Stage.Betting;
            FeltCam();

            if (!_placeSaid)
            {
                _placeSaid = true;
                _dealer.Say("MINIGAME_DEALER_PLACE_BET");
            }
        }

        private void Betting()
        {
            Cursor();
            Markers();

            if (Keys.Back || Keys.RightClick)
            {
                if (_bets.Count == 0) { Stand(); return; }

                // The last chip back off the table.
                var last = _bets[_bets.Count - 1];
                _bets.RemoveAt(_bets.Count - 1);
                Game.Player.Money += last.Stake;
                Chips.Gone(last.Chip);
                Sfx.Front("DLC_VW_REMOVE_BET");
                Say(Describe(last.Spot) + " taken back.");
                return;
            }

            if (Keys.Lower && _chip > 0) { _chip--; Sfx.Front("DLC_VW_BET_DOWN"); }
            if (Keys.Raise && _chip < _chips.Length - 1) { _chip++; Sfx.Front("DLC_VW_BET_UP"); }

            if (Keys.Space)
            {
                if (_bets.Count == 0)
                {
                    Sfx.Front("DLC_VW_ERROR_MAX");
                    Say("Put something on the table first.");
                    return;
                }

                Close();
                return;
            }

            if (Keys.Select || Keys.Click) Place();
        }

        /// <summary>A chip where the cursor is: a new bet, or more on one already there.</summary>
        private void Place()
        {
            if (_aim == null)
            {
                Sfx.Front("DLC_VW_ERROR_MAX");
                return;
            }

            var chip = _chips[_chip];

            if (Game.Player.Money < chip)
            {
                Sfx.Front("DLC_VW_ERROR_MAX");
                Say("You ain't got it.");
                return;
            }

            Bet same = null;

            foreach (var b in _bets)
            {
                if (b.Spot.Key == _aim.Key) { same = b; break; }
            }

            if (same != null && same.Stake + chip > _maxBet)
            {
                Sfx.Front("DLC_VW_ERROR_MAX");
                Say("$" + _maxBet.ToString("N0") + " is the most on one spot.");
                return;
            }

            Game.Player.Money -= chip;

            if (same != null)
            {
                same.Stake += chip;

                // Last in the list is what Backspace takes back.
                _bets.Remove(same);
                _bets.Add(same);
            }
            else
            {
                _bets.Add(new Bet { Spot = _aim, Stake = chip });
            }

            Sfx.Front("DLC_VW_BET_DOWN");
        }

        /// <summary>
        /// Where the chip in your hand is, moved by the arrows a stop at a time -- every number,
        /// every line between two, every corner, the zeros and the boxes -- or by the mouse.
        /// </summary>
        private void Cursor()
        {
            var now = Game.GameTime;

            var up = Keys.HeldUp;
            var down = Keys.HeldDown;
            var left = Keys.HeldLeft;
            var right = Keys.HeldRight;

            if ((up || down || left || right) && now >= _repeatAt)
            {
                var first = Keys.Up || Keys.Down || Keys.Left || Keys.Right;

                if (first || _repeatAt > 0)
                {
                    Vector2 screenUp, screenRight;
                    Axes(out screenUp, out screenRight);

                    if (up) Step(screenUp);
                    if (down) Step(-screenUp);
                    if (right) Step(screenRight);
                    if (left) Step(-screenRight);

                    _repeatAt = now + (first ? RepeatMs * 3 : RepeatMs);
                }
            }

            if (!up && !down && !left && !right) _repeatAt = 0;

            // The mouse, freely, and on a pad the LEFT stick -- the right one looks about the room
            // (Michael asked to move the camera sat down, on 2026-09-26). A MOUSE READS AS HOW FAR
            // IT MOVED THIS FRAME; A STICK AS HOW FAR IT IS PUSHED, every frame it is held. Read
            // the stick like the mouse and a nudge sends the chip off the felt in a quarter of a
            // second -- so on a pad it is a speed, with a dead zone.
            var pad = Game.LastInputMethod == InputMethod.GamePad;
            var mx = pad ? Keys.MoveX : Keys.MouseX;
            var my = pad ? Keys.MoveY : Keys.MouseY;

            if (pad)
            {
                if (Math.Abs(mx) < 0.2f) mx = 0f;
                if (Math.Abs(my) < 0.2f) my = 0f;
            }

            if (Math.Abs(mx) > 0.0001f || Math.Abs(my) > 0.0001f)
            {
                Vector2 screenUp, screenRight;
                Axes(out screenUp, out screenRight);

                var scale = pad ? 0.55f * Game.LastFrameTime : 0.35f;
                var dx = (screenRight.X * mx - screenUp.X * my) * scale;
                var dy = (screenRight.Y * mx - screenUp.Y * my) * scale;

                _u = Clamp(_u + dx / CellX, -1.6f, 12.6f);
                _v = Clamp(_v + dy / CellY, -1.45f, 2.5f);
            }

            var aim = Aim(_u, _v);

            if ((aim == null) != (_aim == null) || aim != null && aim.Key != _aim.Key) Sfx.Front("DLC_VW_BET_HIGHLIGHT");

            _aim = aim;

            // The chip in your hand, hovering over where it would go.
            var chip = _chips[_chip];

            if (_cursor == null || !_cursor.Exists() || _cursorFor != chip)
            {
                Chips.Gone(_cursor);
                _cursor = Chips.Put(_table, Local(_u, _v, 0.02f), chip, _table.Heading);
                _cursorFor = _cursor != null ? chip : -1;
            }
            else
            {
                var at = _table.GetOffsetPosition(Local(_u, _v, 0.02f));
                Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, _cursor.Handle, at.X, at.Y, at.Z, false, false, false);
            }

            // The chips on the table, put down as their models come in.
            foreach (var b in _bets)
            {
                if (b.Chip != null && b.Chip.Exists() && b.ChipFor == b.Stake) continue;

                Chips.Gone(b.Chip);
                b.Chip = Chips.Put(_table, Local(b.Spot.U, b.Spot.V, 0f), b.Stake, _table.Heading);
                b.ChipFor = b.Chip != null ? b.Stake : -1;
            }
        }

        /// <summary>
        /// Which way on the table the top of the screen and its right-hand side are, so the arrows
        /// move the chip the way they point from wherever you are sat.
        /// </summary>
        private void Axes(out Vector2 screenUp, out Vector2 screenRight)
        {
            var to = FeltCentre();
            var from = CamFrom(to);

            // Into the table's own space: the layout is laid out in it.
            var a = _table.GetPositionOffset(from);
            var b = _table.GetPositionOffset(to);

            var fx = b.X - a.X;
            var fy = b.Y - a.Y;
            var len = (float)Math.Sqrt(fx * fx + fy * fy);

            if (len < 0.001f) { fx = 0f; fy = 1f; len = 1f; }

            screenUp = new Vector2(fx / len, fy / len);
            screenRight = new Vector2(screenUp.Y, -screenUp.X);
        }

        /// <summary>One stop along whichever of the layout's two directions the screen direction is nearest.</summary>
        private void Step(Vector2 dir)
        {
            if (Math.Abs(dir.X) >= Math.Abs(dir.Y))
            {
                _u = Next(StopsU, _u, dir.X > 0 ? 1 : -1, u => Aim(u, _v) != null);
            }
            else
            {
                _v = Next(StopsV, _v, dir.Y > 0 ? 1 : -1, v => Aim(_u, v) != null);
            }
        }

        /// <summary>The next stop along from where the cursor is that has a bet on it, or where it is.</summary>
        private static float Next(float[] stops, float at, int sign, Func<float, bool> ok)
        {
            if (sign > 0)
            {
                for (var i = 0; i < stops.Length; i++)
                {
                    if (stops[i] > at + 0.01f && ok(stops[i])) return stops[i];
                }
            }
            else
            {
                for (var i = stops.Length - 1; i >= 0; i--)
                {
                    if (stops[i] < at - 0.01f && ok(stops[i])) return stops[i];
                }
            }

            return at;
        }

        /// <summary>
        /// What the chip would be on, at a point on the layout in cells. Null off the layout.
        /// Near a line between two numbers it is the split; where four meet, the corner; on the
        /// line along the bottom of a row, the street, and where two rows meet on it, the six line.
        /// </summary>
        private static Spot Aim(float u, float v)
        {
            // The zeros, at the wheel end.
            if (u < -0.5f)
            {
                if (u < -1.6f || v < -0.3f || v > 2.5f) return null;

                return v < 1.03f
                    ? new Spot { Kind = Kind.Straight, Covers = new[] { 0 }, U = ZeroU, V = ZeroV }
                    : new Spot { Kind = Kind.Straight, Covers = new[] { 37 }, U = ZeroU, V = DoubleZeroV };
            }

            // The 2 to 1 boxes past the last row.
            if (u > 11.5f)
            {
                if (u > 12.6f || v < -0.3f || v > 2.5f) return null;

                var c = Math.Max(0, Math.Min(2, (int)Math.Round(v)));
                var covers = new List<int>();
                for (var r = 0; r < 12; r++) covers.Add(3 * r + c + 1);

                return new Spot { Kind = Kind.Column1 + c, Covers = covers.ToArray(), U = 12f, V = c };
            }

            // The even-money boxes along the bottom.
            if (v < -0.83f)
            {
                if (v < -1.45f) return null;

                var k = Math.Max(0, Math.Min(5, (int)Math.Floor((u + 0.5f) / 2f)));
                Kind[] kinds = { Kind.Low, Kind.Even, Kind.Red, Kind.Black, Kind.Odd, Kind.High };

                return new Spot { Kind = kinds[k], Covers = Outside(kinds[k]), U = 2 * k + 0.5f, V = OutsideV };
            }

            // The dozens.
            if (v < -0.5f)
            {
                var d = Math.Max(0, Math.Min(2, (int)Math.Floor((u + 0.5f) / 4f)));
                var covers = new int[12];
                for (var i = 0; i < 12; i++) covers[i] = 12 * d + i + 1;

                return new Spot { Kind = Kind.Dozen1 + d, Covers = covers, U = 4 * d + 1.5f, V = DozenV };
            }

            if (v > 2.5f) return null;

            // The numbers, and the lines between them.
            var ru = Math.Max(0, Math.Min(11, (int)Math.Round(u)));
            var rv = Math.Max(0, Math.Min(2, (int)Math.Round(v)));
            var du = u - ru;
            var dv = v - rv;

            if (rv == 0 && dv < -Edge)
            {
                if (Math.Abs(du) > Edge)
                {
                    var r = du > 0 ? ru : ru - 1;
                    r = Math.Max(0, Math.Min(10, r));

                    return new Spot
                    {
                        Kind = Kind.SixLine,
                        Covers = new[] { 3 * r + 1, 3 * r + 2, 3 * r + 3, 3 * r + 4, 3 * r + 5, 3 * r + 6 },
                        U = r + 0.5f,
                        V = -0.5f
                    };
                }

                return new Spot { Kind = Kind.Street, Covers = new[] { 3 * ru + 1, 3 * ru + 2, 3 * ru + 3 }, U = ru, V = -0.5f };
            }

            var su = du > 0 ? 1 : -1;
            var sv = dv > 0 ? 1 : -1;
            var nearU = Math.Abs(du) > Edge && ru + su >= 0 && ru + su <= 11;
            var nearV = Math.Abs(dv) > Edge && rv + sv >= 0 && rv + sv <= 2;

            if (nearU && nearV)
            {
                var r0 = Math.Min(ru, ru + su);
                var c0 = Math.Min(rv, rv + sv);

                return new Spot
                {
                    Kind = Kind.Corner,
                    Covers = new[] { 3 * r0 + c0 + 1, 3 * r0 + c0 + 2, 3 * (r0 + 1) + c0 + 1, 3 * (r0 + 1) + c0 + 2 },
                    U = r0 + 0.5f,
                    V = c0 + 0.5f
                };
            }

            if (nearU)
            {
                var r0 = Math.Min(ru, ru + su);
                return new Spot { Kind = Kind.Split, Covers = new[] { 3 * r0 + rv + 1, 3 * (r0 + 1) + rv + 1 }, U = r0 + 0.5f, V = rv };
            }

            if (nearV)
            {
                var c0 = Math.Min(rv, rv + sv);
                return new Spot { Kind = Kind.Split, Covers = new[] { 3 * ru + c0 + 1, 3 * ru + c0 + 2 }, U = ru, V = c0 + 0.5f };
            }

            return new Spot { Kind = Kind.Straight, Covers = new[] { 3 * ru + rv + 1 }, U = ru, V = rv };
        }

        /// <summary>The numbers an even-money bet covers. Neither zero is in any of them.</summary>
        private static int[] Outside(Kind kind)
        {
            var list = new List<int>();

            for (var n = 1; n <= 36; n++)
            {
                bool hit;

                switch (kind)
                {
                    case Kind.Red: hit = Reds.Contains(n); break;
                    case Kind.Black: hit = !Reds.Contains(n); break;
                    case Kind.Odd: hit = n % 2 == 1; break;
                    case Kind.Even: hit = n % 2 == 0; break;
                    case Kind.Low: hit = n <= 18; break;
                    case Kind.High: hit = n >= 19; break;
                    default: hit = false; break;
                }

                if (hit) list.Add(n);
            }

            return list.ToArray();
        }

        /// <summary>What a bet pays, to one, or -1 for a loser. 37 is 00.</summary>
        private static int Pays(Kind kind, int[] covers, int number)
        {
            foreach (var n in covers)
            {
                if (n == number) return KindPays[(int)kind];
            }

            return -1;
        }

        /// <summary>A point on the layout, in cells, in the table's own space.</summary>
        private static Vector3 Local(float u, float v, float up)
        {
            return new Vector3(GridX + CellX * u, GridY + CellY * v, Felt + up);
        }

        /// <summary>The markers lit on the numbers the chip in your hand covers, and only those.</summary>
        private void Markers()
        {
            var want = new bool[38];

            if (_aim != null)
            {
                foreach (var n in _aim.Covers) want[Mark(n)] = true;
            }

            Light(want);
        }

        private static int Mark(int number)
        {
            if (number == 0) return 36;
            if (number == 37) return 37;
            return number - 1;
        }

        private void Light(bool[] want)
        {
            for (var i = 0; i < 38; i++)
            {
                if (want[i] && (_markers[i] == null || !_markers[i].Exists()))
                {
                    var number = i < 36 ? i + 1 : i == 36 ? 0 : 37;
                    var at = number == 0 ? Local(ZeroU, ZeroV, 0f)
                           : number == 37 ? Local(ZeroU, DoubleZeroV, 0f)
                           : Local((number - 1) / 3, (number - 1) % 3, 0f);

                    _markers[i] = Chips.Place(i < 36 ? MarkerNumber : MarkerZero, _table, at, _table.Heading, false);
                    _lit[i] = false;

                    if (_markers[i] != null)
                    {
                        try { Function.Call((Hash)TextureVariation, _markers[i].Handle, 3); }
                        catch { }
                    }
                }

                if (_markers[i] == null || _lit[i] == want[i]) continue;

                Chips.Show(_markers[i], want[i]);
                _lit[i] = want[i];
            }
        }

        // ---- the spin -------------------------------------------------------------------------

        /// <summary>No more bets: the chip out of your hand, the dealer waves the table off and spins.</summary>
        private void Close()
        {
            _staked = 0;
            foreach (var b in _bets) _staked += b.Stake;

            Chips.Gone(_cursor);
            _cursor = null;
            _cursorFor = -1;
            Light(new bool[38]);

            // The pocket is decided now and kept to: the clip that drops the ball into it is the
            // clip that plays, so what you see is what you get.
            _pocket = 1 + _rng.Next(38);
            _number = Wheel[_pocket - 1];

            _dealer.Say("MINIGAME_DEALER_CLOSED_BETS");
            _dealer.Act(Scene.RouletteDealer, "no_more_bets");
            _dealer.Act(Scene.RouletteDealer, "spin_wheel");
            _stage = Stage.Closing;
        }

        /// <summary>The wheel set going and the ball put in, as her hand goes to it.</summary>
        private void Spin()
        {
            if (!Scene.Loaded(TableDict))
            {
                Log.Info("Den: the table's clips are not loaded; the spin is decided without the wheel turning.");
                Settle();
                return;
            }

            Ball();

            Sfx.SceneOn("DLC_VW_Casino_Roulette_Focus_Wheel");
            Sfx.Stop(ref _ballSound);
            _ballSound = Sfx.From(_table, "DLC_VW_ROULETTE_BALL_LOOP", Sfx.TableGames);

            // The last spin's pocket let go of, so the wheel starts from rest.
            if (_held > 0) Scene.StopEntity(_table, TableDict, "exit_" + _held + "_wheel");
            _held = 0;

            Scene.Entity(_table, TableDict, "intro_wheel", false);
            Play("intro_ball", false);

            _wheeling = Wheeling.Intro;
            _wheelFrom = Game.GameTime;
            _stage = Stage.Spinning;
        }

        private void Spinning()
        {
            var now = Game.GameTime;
            var waited = now - _wheelFrom;

            switch (_wheeling)
            {
                case Wheeling.Intro:
                    if (!Over("intro", waited)) return;

                    Scene.Entity(_table, TableDict, "loop_wheel", true);
                    Play("loop_ball", true);
                    _at = now + LoopMs;
                    _wheeling = Wheeling.Loop;
                    _wheelFrom = now;
                    return;

                case Wheeling.Loop:
                    if (now < _at) return;

                    Scene.Entity(_table, TableDict, "exit_" + _pocket + "_wheel", false);
                    Play("exit_" + _pocket + "_ball", false);
                    _held = _pocket;
                    Sfx.Stop(ref _ballSound);
                    Sfx.Once(_table, "dlc_vw_roulette_exit_" + _pocket, Sfx.RouletteExits);
                    _wheeling = Wheeling.Exit;
                    _wheelFrom = now;
                    return;

                case Wheeling.Exit:
                    if (!Over("exit_" + _pocket, waited)) return;

                    _wheeling = Wheeling.Rest;
                    Settle();
                    return;
            }
        }

        /// <summary>
        /// Whether a stage of the wheel has run its course: the ball's clip if there is a ball, the
        /// wheel's if not. A clip is given a moment to start before "not playing" is believed, and
        /// no stage is waited on for ever.
        /// </summary>
        private bool Over(string stem, int waited)
        {
            if (waited > WheelMostMs) return true;

            var phase = _ball != null && _ball.Exists()
                ? Scene.EntityPhase(_ball, TableDict, stem + "_ball")
                : Scene.EntityPhase(_table, TableDict, stem + "_wheel");

            if (phase > 1.5f) return waited > WheelGraceMs;
            return phase >= 0.985f;
        }

        /// <summary>The ball, on the wheel's own bone and turned a quarter round, the way its clips were made.</summary>
        private void Ball()
        {
            try
            {
                if (_ball != null && _ball.Exists()) return;

                var model = new Model(BallModel);

                if (!Models.Ready(model))
                {
                    Log.Info("Den: the ball's model is not in yet; this spin goes without it.");
                    return;
                }

                _ball = Chips.Make(model, WheelCentre(), Vector3.Zero, true, false);

                if (_ball != null) Log.Info("Den: the ball is on the wheel.");
            }
            catch (Exception ex)
            {
                Log.Info("Den: could not make the ball: " + ex.Message);
            }
        }

        /// <summary>The ball put back on the wheel's centre before each of its clips: they are all authored from there.</summary>
        private void Wheel0()
        {
            if (_ball == null || !_ball.Exists()) return;

            try
            {
                var at = WheelCentre();
                var rot = _table.Rotation;

                Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, _ball.Handle, at.X, at.Y, at.Z, false, false, false);
                Function.Call(Hash.SET_ENTITY_ROTATION, _ball.Handle, rot.X, rot.Y, rot.Z + 90f, 2, true);
                Chips.Show(_ball, true);
            }
            catch { }
        }

        private void Play(string clip, bool loop)
        {
            if (_ball == null || !_ball.Exists()) return;

            Wheel0();
            Scene.Entity(_ball, TableDict, clip, loop);
        }

        /// <summary>The middle of the wheel: its bone, or where the bone is on the Diamond's table if the model has none.</summary>
        private Vector3 WheelCentre()
        {
            var bone = Bone.Index(_table, "Roulette_Wheel");

            if (!_saidBone)
            {
                _saidBone = true;
                Log.Info(bone >= 0 ? "Den: the roulette's wheel bone is " + bone + "."
                                   : "Den: the roulette has no Roulette_Wheel bone; the ball goes on the Diamond's offset.");
            }

            if (bone >= 0) return Bone.Position(_table, bone);
            return _table.GetOffsetPosition(new Vector3(-0.734742f, -0.16617f, 1.0715f));
        }

        // ---- the result -------------------------------------------------------------------------

        private void Settle()
        {
            var winners = 0;
            _won = 0;
            _zones.Clear();

            foreach (var b in _bets)
            {
                var pays = Pays(b.Spot.Kind, b.Spot.Covers, _number);

                if (pays >= 0)
                {
                    _won += b.Stake * (pays + 1);
                    winners++;
                }
                else
                {
                    var z = Zone(b.Spot.U);
                    if (!_zones.Contains(z)) _zones.Add(z);
                }
            }

            _zones.Sort();

            if (_won > 0) Game.Player.Money += _won;

            var net = _won - _staked;
            _session += net;

            _dealer.Say("MINIGAME_ROULETTE_BALL_" + (_number == 37 ? "00" : _number.ToString()));

            if (net > 0)
            {
                Sfx.Front("DLC_VW_WIN_CHIPS");
                React(net >= _staked * 5 ? "great" : "good");
            }
            else if (_won > 0)
            {
                React("impartial");
            }
            else
            {
                React(_staked >= _minBet * 10 ? "terrible" : "bad");
            }

            _said = Name(_number) + (net > 0 ? " -- you win $" + net.ToString("N0")
                                  : _won > 0 ? " -- $" + _won.ToString("N0") + " back of $" + _staked.ToString("N0")
                                  : " -- the house takes it");
            _saidUntil = Game.GameTime + ResultMs + 6000;

            // The number it landed on, lit.
            var lit = new bool[38];
            lit[Mark(_number)] = true;
            Light(lit);

            Log.Info("Den: roulette landed pocket " + _pocket + " (" + Name(_number) + "); " + _bets.Count + " bet(s), $" +
                     _staked.ToString("N0") + " down, " + winners + " won, paid $" + _won.ToString("N0") + ".");

            _lingerAt = Game.GameTime + WheelLingerMs;
            _at = Game.GameTime + ResultMs;
            _onWheel = true;
            _stage = Stage.Settling;
        }

        private void Settling()
        {
            if (_onWheel && Game.GameTime >= _lingerAt)
            {
                _onWheel = false;
                Sfx.SceneOff("DLC_VW_Casino_Roulette_Focus_Wheel");
                FeltCam();
            }

            if (Game.GameTime < _at) return;

            // The losers raked, a third of the layout at a time; the winners stay while it is done.
            if (_zones.Count > 0)
            {
                _dealer.Act(Scene.RouletteDealer, "clear_chips_intro");
                foreach (var z in _zones) _dealer.Act(Scene.RouletteDealer, "clear_chips_zone" + z);
                _dealer.Act(Scene.RouletteDealer, "clear_chips_outro");
            }

            _stage = Stage.Clearing;
        }

        private void Clearing()
        {
            // Each zone's losing chips go as her hand passes over them.
            var doing = _dealer.Doing;

            if (doing != null && doing.StartsWith("clear_chips_zone") && _dealer.Phase >= 0.45f)
            {
                int zone;
                if (int.TryParse(doing.Substring("clear_chips_zone".Length), out zone)) Rake(zone);
            }

            if (_dealer.Busy) return;

            for (var z = 1; z <= 3; z++) Rake(z);

            // The winners pushed back to you, and the table clear for the next spin.
            foreach (var b in _bets) Chips.Gone(b.Chip);
            _bets.Clear();
            Light(new bool[38]);

            _dealer.Say("MINIGAME_DEALER_PLACE_BET");
            _stage = Stage.Betting;
        }

        /// <summary>The losing chips in one zone of the layout, off the table.</summary>
        private void Rake(int zone)
        {
            for (var i = _bets.Count - 1; i >= 0; i--)
            {
                var b = _bets[i];
                if (Pays(b.Spot.Kind, b.Spot.Covers, _number) >= 0 || Zone(b.Spot.U) != zone) continue;

                Chips.Gone(b.Chip);
                _bets.RemoveAt(i);
            }
        }

        /// <summary>Which third of the layout a chip is in, from the wheel end: the dealer rakes them a third at a time.</summary>
        private static int Zone(float u)
        {
            if (u < 3.5f) return 1;
            if (u < 7.5f) return 2;
            return 3;
        }

        /// <summary>How you take it, in your chair: the casino has a seated reaction for every size of luck.</summary>
        private void React(string how)
        {
            if (_sitter == null) return;

            string clip;

            if (_sitter.Female)
            {
                var most = how == "great" || how == "terrible" ? 5 : how == "impartial" ? 7 : 4;
                clip = "female_reaction_" + how + "_var_0" + (1 + _rng.Next(most));
            }
            else
            {
                var most = how == "impartial" ? 8 : 4;
                clip = "reaction_" + how + "_var_0" + (1 + _rng.Next(most));
            }

            _sitter.Once(Scene.SharedPlayer, clip);
        }

        // ---- the cameras --------------------------------------------------------------------

        private Vector3 FeltCentre()
        {
            return _table.GetOffsetPosition(new Vector3(0.39f, -0.05f, Felt));
        }

        /// <summary>Above and in front of wherever you are sat, looking down at a point on the table.</summary>
        private Vector3 CamFrom(Vector3 look)
        {
            var side = _sitter != null ? _sitter.Seat.At : _me.Position;
            var dir = side - look;
            dir.Z = 0f;

            if (dir.Length() < 0.05f) dir = -_table.RightVector;
            dir.Normalize();

            return look + dir * 0.62f + new Vector3(0f, 0f, 1.32f);
        }

        private void FeltCam()
        {
            var look = FeltCentre();
            _cam.LookFree(CamFrom(look), look, 52f, 900, 0.12f, false);
        }

        private void WheelCam()
        {
            var look = WheelCentre();
            var side = _sitter != null ? _sitter.Seat.At : _me.Position;
            var dir = side - look;
            dir.Z = 0f;

            if (dir.Length() < 0.05f) dir = -_table.RightVector;
            dir.Normalize();

            _cam.LookFree(look + dir * 0.26f + new Vector3(0f, 0f, 0.78f), look, 58f, 700, 0.08f);
        }

        // ---- out ------------------------------------------------------------------------------

        private void Stand()
        {
            // Whatever is still on the table comes back with you.
            foreach (var b in _bets)
            {
                Game.Player.Money += b.Stake;
                Chips.Gone(b.Chip);
            }

            _bets.Clear();

            Tidy();

            _dealer.Say(_session > 0 ? "MINIGAME_DEALER_LEAVE_GOOD_GAME"
                      : _session < 0 ? "MINIGAME_DEALER_LEAVE_BAD_GAME"
                      : "MINIGAME_DEALER_LEAVE_NEUTRAL_GAME");

            if (_sitter != null) _sitter.Stand();
            _stage = Stage.Standing;
        }

        /// <summary>Everything the game put on the table, off it, and the cameras back to the game.</summary>
        private void Tidy()
        {
            Chips.Gone(_cursor);
            _cursor = null;

            for (var i = 0; i < 38; i++)
            {
                Chips.Gone(_markers[i]);
                _markers[i] = null;
                _lit[i] = false;
            }

            Sfx.Stop(ref _ballSound);
            Sfx.SceneOff("DLC_VW_Casino_Roulette_Focus_Wheel");

            if (_held > 0) Scene.StopEntity(_table, TableDict, "exit_" + _held + "_wheel");
            _held = 0;
            Scene.StopEntity(_table, TableDict, "loop_wheel");
            Scene.StopEntity(_table, TableDict, "intro_wheel");

            Chips.Gone(_ball);
            _ball = null;

            _cam.Stop();
            Hud(true);
        }

        /// <summary>Straight out, whatever state it is in: the den is being left, or the table went.</summary>
        public void Abandon()
        {
            if (_stage == Stage.Done) return;

            foreach (var b in _bets)
            {
                Game.Player.Money += b.Stake;
                Chips.Gone(b.Chip);
            }

            _bets.Clear();
            Finish();
        }

        private void Finish()
        {
            if (_stage == Stage.Done) return;

            Tidy();
            if (_sitter != null) _sitter.Let();
            _stage = Stage.Done;
            Log.Info("Den: up from the roulette.");
        }

        private static void Hud(bool on)
        {
            try { Function.Call(Hash.DISPLAY_RADAR, on); }
            catch { }
        }

        // ---- words -------------------------------------------------------------------------------

        private static string Name(int number)
        {
            if (number == 37) return "00 green";
            if (number == 0) return "0 green";
            return number + (Reds.Contains(number) ? " red" : " black");
        }

        private static string Short(int number)
        {
            return number == 37 ? "00" : number.ToString();
        }

        /// <summary>A bet in words: the kind, and the numbers it covers where it has them.</summary>
        private static string Describe(Spot spot)
        {
            if (spot.Kind > Kind.SixLine) return KindNames[(int)spot.Kind];

            var nums = new List<string>();
            foreach (var n in spot.Covers) nums.Add(Short(n));

            return KindNames[(int)spot.Kind] + " " + string.Join("-", nums);
        }

        private void Say(string words)
        {
            _said = words;
            _saidUntil = Game.GameTime + 2200;
        }

        private static float Clamp(float x, float lo, float hi)
        {
            return x < lo ? lo : x > hi ? hi : x;
        }

        private static readonly Color Gold = Color.FromArgb(255, 240, 200, 80);
        private static readonly Color Green = Color.FromArgb(255, 114, 204, 114);
        private static readonly Color Red = Color.FromArgb(255, 224, 50, 50);

        private void Draw()
        {
            var down = 0;
            foreach (var b in _bets) down += b.Stake;

            var cash = "$" + Game.Player.Money.ToString("N0");

            switch (_stage)
            {
                case Stage.Betting:
                    Help.ShowThisFrame(_said.Length > 0 && Game.GameTime < _saidUntil ? _said
                                     : _aim != null ? Describe(_aim) + " -- pays " + KindPays[(int)_aim.Kind] + " to 1."
                                     : Game.LastInputMethod == InputMethod.GamePad
                                         ? "Place your bets. The left stick moves the chip; the right stick looks around."
                                         : "Place your bets.");

                    Bars.Draw("CASH", cash, Color.White,
                              "CHIP", "$" + _chips[_chip].ToString("N0"), Color.White,
                              "TOTAL BET", "$" + down.ToString("N0"), Gold);

                    Buttons.Show(Control.PhoneCancel, _bets.Count == 0 ? "Leave table" : "Take back chip",
                                 Control.Jump, "Spin",
                                 Control.PhoneSelect, "Place chip",
                                 Control.FrontendRb, "Chip up",
                                 Control.FrontendLb, "Chip down",
                                 Control.PhoneRight, "Move chip");
                    break;

                case Stage.Settling:
                case Stage.Clearing:
                    if (_said.Length > 0 && Game.GameTime < _saidUntil) Help.ShowThisFrame(_said);

                    Bars.Draw("CASH", cash, Color.White,
                              _won > _staked ? "WON" : "PAID", "$" + _won.ToString("N0"),
                              _won > _staked ? Green : _won > 0 ? Gold : Red,
                              "TOTAL BET", "$" + _staked.ToString("N0"), Color.White);
                    break;

                case Stage.Closing:
                case Stage.Spinning:
                    if (_stage == Stage.Spinning && !_onWheel)
                    {
                        _onWheel = true;
                        WheelCam();
                    }

                    Help.ShowThisFrame(Game.LastInputMethod == InputMethod.GamePad
                        ? "Right stick to look around."
                        : "Move the mouse to look around.");

                    Bars.Draw("CASH", cash, Color.White, "TOTAL BET", "$" + down.ToString("N0"), Gold);
                    break;
            }
        }
    }
}
