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
    /// Roulette, at the den's table.
    ///
    /// AMERICAN, because the wheel is. The casino's table clips have an exit for every pocket
    /// -- exit_1 through exit_38 -- and thirty-eight pockets is a wheel with a double zero. So
    /// the numbers are 0 to 36 and 00, red and black as they are on every such wheel, and a zero
    /// of either kind takes every outside bet.
    ///
    /// WHICH POCKET IS WHICH NUMBER IS THE ONE THING IN HERE THAT COULD NOT BE VERIFIED FROM
    /// A DESK. The clips are numbered by pocket, not by the number painted in it, and nothing
    /// on this machine says which order the animators counted round. The order below is the
    /// standard American wheel read clockwise from the 0, on the guess that they started there
    /// and went the same way. If the ball settles in a pocket that does not match the number
    /// the card announces, that table is wrong and it is one table to fix -- say which pocket
    /// showed and which number was called.
    ///
    /// THE DIAMOND'S BETS, AS MANY AS YOU LIKE A SPIN. Michael asked for the den's gambling to
    /// be "just like vanilla" on 2026-09-26, and the Diamond's table takes chips all over the
    /// layout before the wheel goes. So a bet is still three fields -- what, which number, how
    /// much -- but Enter puts it on the table and you can put down another, and the spin is its
    /// own field at the end. Straight up 35 to 1, split 17, street 11, corner 8, six line 5,
    /// dozens and columns 2, red, black, odd, even, low and high evens. Backspace takes the last
    /// chip back, and with nothing down it leaves the table.
    /// </summary>
    internal sealed class Roulette
    {
        private enum Stage { Betting, NoMoreBets, Intro, Loop, Exit, Result, Leaving, Done }

        private enum Kind { Straight, Split, Street, Corner, SixLine, Red, Black, Odd, Even, Low, High, Dozen1, Dozen2, Dozen3, Column1, Column2, Column3 }

        private static readonly string[] KindNames =
        {
            "Straight up", "Split", "Street", "Corner", "Six line", "Red", "Black", "Odd", "Even", "1 to 18", "19 to 36",
            "1st 12", "2nd 12", "3rd 12", "Column 1", "Column 2", "Column 3"
        };

        /// <summary>What each kind pays, to one.</summary>
        private static readonly int[] KindPays = { 35, 17, 11, 8, 5, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2 };

        /// <summary>
        /// Pocket clip index (1..38) to the number in it, clockwise from 0 on an American
        /// wheel. 37 stands for 00. See the note on the class.
        /// </summary>
        private static readonly int[] Wheel =
        {
            0, 28, 9, 26, 30, 11, 7, 20, 32, 17, 5, 22, 34, 15, 3, 24, 36, 13, 1, 37,
            27, 10, 25, 29, 12, 8, 19, 31, 18, 6, 21, 33, 16, 4, 23, 35, 14, 2
        };

        private static readonly HashSet<int> Reds = new HashSet<int>
        {
            1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36
        };

        private sealed class Bet
        {
            public Kind Kind;
            public int Number;
            public int Stake;
        }

        private readonly Entity _table;
        private readonly Dealer _dealer;
        private readonly Random _rng;
        private readonly int _minBet;
        private readonly int _maxBet;

        private Stage _stage = Stage.Betting;

        /// <summary>0 what, 1 which number, 2 how much, 3 spin.</summary>
        private int _field;
        private Kind _kind = Kind.Red;
        private int _number = 17;
        private int _stake;

        private readonly List<Bet> _bets = new List<Bet>();

        private Prop _ball;
        private int _scene = -1;
        private int _at;
        private int _pocket;
        private int _won;
        private int _staked;
        private string _said = "";

        /// <summary>How long the wheel is left going round before the ball is let drop.</summary>
        private const int LoopMs = 4500;
        private const int ResultMs = 4500;
        private const string BallModel = "vw_prop_roulette_ball";

        public Roulette(Entity table, Dealer dealer, int minBet, int maxBet, Random rng)
        {
            _table = table;
            _dealer = dealer;
            _rng = rng;
            _minBet = Math.Max(1, minBet);
            _maxBet = Math.Max(_minBet, maxBet);
            _stake = _minBet;
            Log.Info("Den: sat down at the roulette.");
        }

        public bool Finished => _stage == Stage.Done;

        public void Update()
        {
            if (_table == null || !_table.Exists()) { Leave(); return; }

            switch (_stage)
            {
                case Stage.Betting: Betting(); break;
                case Stage.NoMoreBets: if (!_dealer.Busy) Spin(); break;
                case Stage.Intro: if (Scene.Done(_scene)) Loop(); break;
                case Stage.Loop: if (Game.GameTime >= _at) Drop(); break;
                case Stage.Exit: if (Scene.Done(_scene)) Settle(); break;
                case Stage.Result: if (Game.GameTime >= _at) _stage = Stage.Betting; break;
                case Stage.Leaving: Leave(); break;
            }

            Draw();
        }

        // ---- the bets -----------------------------------------------------------------

        private static bool Inside(Kind kind)
        {
            return kind <= Kind.SixLine;
        }

        private void Betting()
        {
            if (Keys.Back)
            {
                if (_bets.Count == 0) { Leave(); return; }

                // The last chip back off the table.
                var last = _bets[_bets.Count - 1];
                _bets.RemoveAt(_bets.Count - 1);
                Game.Player.Money += last.Stake;
                Click();
                Say(Describe(last) + " taken back.", false);
                return;
            }

            if (Keys.Up) { _field = (_field + 3) % 4; Click(); }
            if (Keys.Down) { _field = (_field + 1) % 4; Click(); }

            // The number is only a field for the bets that have one, so the arrows go past it
            // rather than landing on nothing.
            if (_field == 1 && !Inside(_kind)) _field = Keys.Up ? 0 : 2;

            var step = Keys.Right ? 1 : Keys.Left ? -1 : 0;

            if (step != 0 && _field < 3)
            {
                Click();

                if (_field == 0)
                {
                    var kinds = KindNames.Length;
                    _kind = (Kind)(((int)_kind + step + kinds) % kinds);

                    // Only a straight bet may be on a zero.
                    if (_kind != Kind.Straight && (_number == 0 || _number == 37)) _number = 1;
                }
                else if (_field == 1)
                {
                    // 0 .. 36 and 00 as 37 for a straight bet; 1 .. 36 for the rest.
                    _number = _kind == Kind.Straight ? (_number + step + 38) % 38 : 1 + (_number - 1 + step + 36) % 36;
                }
                else
                {
                    _stake = NextStake(_stake, step);
                }
            }

            if (!Keys.Select) return;

            if (_field == 3)
            {
                if (_bets.Count == 0)
                {
                    Say("Put something on the table first.", true);
                    return;
                }

                Click();
                _said = "";
                _staked = 0;
                foreach (var b in _bets) _staked += b.Stake;

                _stage = Stage.NoMoreBets;
                _dealer.Act(Scene.RouletteDealer, "no_more_bets");
                return;
            }

            if (Game.Player.Money < _stake)
            {
                Say("You ain't got it.", true);
                return;
            }

            Game.Player.Money -= _stake;
            Click();

            // The same bet again is more chips on the same spot, not a second spot.
            Bet same = null;

            foreach (var b in _bets)
            {
                if (b.Kind == _kind && (!Inside(_kind) || b.Number == _number)) { same = b; break; }
            }

            if (same != null) same.Stake += _stake;
            else _bets.Add(new Bet { Kind = _kind, Number = _number, Stake = _stake });

            Say("$" + _stake.ToString("N0") + " on " + Describe(new Bet { Kind = _kind, Number = _number }) + ".", false);
        }

        /// <summary>The stakes step by the chips a den would have: the minimum, then multiples of it.</summary>
        private int NextStake(int stake, int step)
        {
            int[] mult = { 1, 2, 5, 10, 25, 50, 100 };
            var i = 0;

            for (var k = 0; k < mult.Length; k++)
            {
                if (_minBet * mult[k] <= stake) i = k;
            }

            i += step;
            if (i < 0) i = 0;
            if (i >= mult.Length) i = mult.Length - 1;

            return Math.Min(_maxBet, _minBet * mult[i]);
        }

        /// <summary>
        /// The numbers an inside bet covers, off the layout: twelve rows of three, 1-2-3 at the
        /// top. A split is a number and the next in its row (or the one below, from the end of a
        /// row), a street is its row, a corner the square of four it starts, a six line its row
        /// and the next.
        /// </summary>
        private static int[] Covered(Kind kind, int n)
        {
            switch (kind)
            {
                case Kind.Straight:
                    return new[] { n };

                case Kind.Split:
                    if (n % 3 != 0) return new[] { n, n + 1 };
                    return n <= 33 ? new[] { n, n + 3 } : new[] { n - 1, n };

                case Kind.Street:
                    {
                        var r = (n - 1) / 3;
                        return new[] { 3 * r + 1, 3 * r + 2, 3 * r + 3 };
                    }

                case Kind.Corner:
                    {
                        var b = n;
                        if (b % 3 == 0) b -= 1;
                        if (b > 32) b -= 3;
                        return new[] { b, b + 1, b + 3, b + 4 };
                    }

                case Kind.SixLine:
                    {
                        var r = (n - 1) / 3;
                        if (r > 10) r = 10;
                        return new[] { 3 * r + 1, 3 * r + 2, 3 * r + 3, 3 * r + 4, 3 * r + 5, 3 * r + 6 };
                    }
            }

            return new int[0];
        }

        // ---- the spin -----------------------------------------------------------------

        private void Spin()
        {
            // The pocket is decided now and kept to: the clip that drops the ball into it is
            // the clip that plays, so what you see is what you get.
            _pocket = 1 + _rng.Next(38);

            if (!Ball()) { Log.Debug("Den: no ball; the wheel spins without one."); }

            if (!Scene.Loaded(Scene.RouletteTable))
            {
                Log.Info("Den: the table's clips are not loaded; the spin is decided without the wheel turning.");
                Settle();
                return;
            }

            // Dealer, wheel and ball in one scene on the table: she spins it and it spins.
            _scene = _dealer.Begin(Scene.RouletteDealer, "spin_wheel");
            if (_scene < 0) _scene = Scene.At(_table);

            Scene.Prop(_scene, _table, Scene.RouletteTable, "intro_wheel");
            if (_ball != null) Scene.Prop(_scene, _ball, Scene.RouletteTable, "intro_ball");

            _stage = Stage.Intro;
        }

        private void Loop()
        {
            _scene = Scene.At(_table);
            Scene.Set(_scene, true, false);
            Scene.Prop(_scene, _table, Scene.RouletteTable, "loop_wheel");
            if (_ball != null) Scene.Prop(_scene, _ball, Scene.RouletteTable, "loop_ball");

            _at = Game.GameTime + LoopMs;
            _stage = Stage.Loop;
        }

        private void Drop()
        {
            _scene = Scene.At(_table);
            Scene.Set(_scene, false, true);
            Scene.Prop(_scene, _table, Scene.RouletteTable, "exit_" + _pocket + "_wheel");
            if (_ball != null) Scene.Prop(_scene, _ball, Scene.RouletteTable, "exit_" + _pocket + "_ball");

            _stage = Stage.Exit;
        }

        private void Settle()
        {
            var number = Wheel[_pocket - 1];
            var winners = 0;

            _won = 0;

            foreach (var b in _bets)
            {
                var pays = Pays(b, number);
                if (pays < 0) continue;

                _won += b.Stake * (pays + 1);
                winners++;
            }

            if (_won > 0) Game.Player.Money += _won;

            var net = _won - _staked;

            if (net > 0)
            {
                UI.Draw.PlaySound("CHECKPOINT_PERFECT", "HUD_MINI_GAME_SOUNDSET");
                _dealer.React(true);
            }
            else
            {
                UI.Draw.PlaySound("LOSER", "HUD_AWARDS");
                _dealer.Act(Scene.RouletteDealer, "clear_chips_zone2");
            }

            _said = Name(number) + " -- " +
                    (net > 0 ? "you win $" + net.ToString("N0")
                     : _won > 0 ? "$" + _won.ToString("N0") + " back of $" + _staked.ToString("N0")
                     : "house takes it");

            Log.Info("Den: roulette landed pocket " + _pocket + " (" + Name(number) + "); " + _bets.Count + " bet(s), $" +
                     _staked.ToString("N0") + " down, " + winners + " won, paid $" + _won.ToString("N0") + ".");

            // The table is cleared for the next spin: winners paid, losers raked.
            _bets.Clear();

            _at = Game.GameTime + ResultMs;
            _stage = Stage.Result;
        }

        /// <summary>What a bet pays, to one, or -1 for a loser. 37 is 00.</summary>
        private static int Pays(Bet bet, int number)
        {
            if (Inside(bet.Kind))
            {
                foreach (var n in Covered(bet.Kind, bet.Number))
                {
                    if (n == number) return KindPays[(int)bet.Kind];
                }

                return -1;
            }

            // Either zero takes every outside bet.
            if (number == 0 || number == 37) return -1;

            bool hit;

            switch (bet.Kind)
            {
                case Kind.Red: hit = Reds.Contains(number); break;
                case Kind.Black: hit = !Reds.Contains(number); break;
                case Kind.Odd: hit = number % 2 == 1; break;
                case Kind.Even: hit = number % 2 == 0; break;
                case Kind.Low: hit = number <= 18; break;
                case Kind.High: hit = number >= 19; break;
                case Kind.Dozen1: hit = number <= 12; break;
                case Kind.Dozen2: hit = number >= 13 && number <= 24; break;
                case Kind.Dozen3: hit = number >= 25; break;
                case Kind.Column1: hit = number % 3 == 1; break;
                case Kind.Column2: hit = number % 3 == 2; break;
                case Kind.Column3: hit = number % 3 == 0; break;
                default: hit = false; break;
            }

            return hit ? KindPays[(int)bet.Kind] : -1;
        }

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
        private static string Describe(Bet bet)
        {
            if (!Inside(bet.Kind)) return KindNames[(int)bet.Kind];

            var nums = new List<string>();
            foreach (var n in Covered(bet.Kind, bet.Number)) nums.Add(Short(n));

            return KindNames[(int)bet.Kind] + " " + string.Join("-", nums);
        }

        // ---- the ball -----------------------------------------------------------------

        private bool Ball()
        {
            if (_ball != null && _ball.Exists()) return true;

            try
            {
                var model = new Model(BallModel);
                if (!model.IsValid || !model.IsInCdImage || !Models.Ready(model)) return false;

                _ball = World.CreateProp(model, _table.Position, false, false);
                model.MarkAsNoLongerNeeded();

                if (_ball == null || !_ball.Exists()) return false;

                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, _ball.Handle, true, true);
                Function.Call(Hash.SET_ENTITY_COLLISION, _ball.Handle, false, false);
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Den: could not make the ball: " + ex.Message);
                return false;
            }
        }

        // ---- out ------------------------------------------------------------------------

        private void Leave()
        {
            // Whatever is still on the table comes back with you.
            foreach (var b in _bets) Game.Player.Money += b.Stake;
            _bets.Clear();

            Scene.Stop(_table);

            try
            {
                if (_ball != null && _ball.Exists()) _ball.Delete();
            }
            catch { }

            _ball = null;
            _stage = Stage.Done;
            Log.Info("Den: up from the roulette.");
        }

        private void Say(string words, bool bad)
        {
            _said = words;
            _at = Game.GameTime + 1800;
            if (bad) UI.Draw.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        private static void Click()
        {
            UI.Draw.PlaySound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        // ---- the card -------------------------------------------------------------------

        private void Draw()
        {
            var lines = new List<string>();
            var down = 0;
            foreach (var b in _bets) down += b.Stake;

            var choices = new List<string>
            {
                KindNames[(int)_kind],
                Inside(_kind) ? Describe(new Bet { Kind = _kind, Number = _number }).Substring(KindNames[(int)_kind].Length).Trim() : "--",
                "$" + _stake.ToString("N0"),
                _bets.Count == 0 ? "Spin" : "Spin -- " + _bets.Count + " bet(s), $" + down.ToString("N0")
            };

            string hint;

            switch (_stage)
            {
                case Stage.Betting:
                    lines.Add(_said.Length > 0 && Game.GameTime < _at ? _said : "Place your bets.");

                    for (var i = 0; i < _bets.Count && i < 3; i++)
                    {
                        lines.Add("$" + _bets[i].Stake.ToString("N0") + "  " + Describe(_bets[i]));
                    }

                    if (_bets.Count > 3) lines.Add("and " + (_bets.Count - 3) + " more");

                    hint = "Up/Down field   Left/Right change   Enter bet / spin   Backspace take back / leave";
                    break;

                case Stage.NoMoreBets:
                    lines.Add("No more bets.");
                    hint = "";
                    break;

                case Stage.Result:
                    lines.Add(_said);
                    hint = "";
                    Panel.Banner(_won > _staked ? "WIN" : _won > 0 ? "BACK" : "HOUSE",
                                 _won > _staked ? Palette.Cash : _won > 0 ? Palette.Warn : Palette.Danger);
                    break;

                default:
                    lines.Add("Spinning...");
                    hint = "";
                    break;
            }

            Panel.Draw("Roulette", lines, choices, _stage == Stage.Betting ? _field : -1, hint);
        }
    }
}
