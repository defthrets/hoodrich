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
    /// The bet is three fields -- what, which number, how much -- picked with the arrows,
    /// spun with Enter. One bet a spin: it is a den, not the Diamond.
    /// </summary>
    internal sealed class Roulette
    {
        private enum Stage { Betting, NoMoreBets, Intro, Loop, Exit, Result, Leaving, Done }

        private enum Kind { Straight, Red, Black, Odd, Even, Low, High, Dozen1, Dozen2, Dozen3, Column1, Column2, Column3 }

        private static readonly string[] KindNames =
        {
            "Straight up", "Red", "Black", "Odd", "Even", "1 to 18", "19 to 36",
            "1st 12", "2nd 12", "3rd 12", "Column 1", "Column 2", "Column 3"
        };

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

        private readonly Entity _table;
        private readonly Dealer _dealer;
        private readonly Random _rng;
        private readonly int _minBet;
        private readonly int _maxBet;

        private Stage _stage = Stage.Betting;
        private int _field;
        private Kind _kind = Kind.Red;
        private int _number = 17;
        private int _stake;

        private Prop _ball;
        private int _scene = -1;
        private int _at;

        private int _pocket;
        private int _won;
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

        // ---- the bet ------------------------------------------------------------------

        private void Betting()
        {
            if (Keys.Back) { Leave(); return; }

            if (Keys.Up) { _field = (_field + 2) % 3; Click(); }
            if (Keys.Down) { _field = (_field + 1) % 3; Click(); }

            // Straight is the only kind with a number, so the number field is skipped for
            // every other kind -- the arrows go past it rather than landing on nothing.
            if (_field == 1 && _kind != Kind.Straight) _field = 2;

            var step = Keys.Right ? 1 : Keys.Left ? -1 : 0;

            if (step != 0)
            {
                Click();

                if (_field == 0)
                {
                    var kinds = KindNames.Length;
                    _kind = (Kind)(((int)_kind + step + kinds) % kinds);
                }
                else if (_field == 1)
                {
                    // 0 .. 36, then 00 as 37, round and round.
                    _number = (_number + step + 38) % 38;
                }
                else
                {
                    _stake = NextStake(_stake, step);
                }
            }

            if (!Keys.Select) return;

            if (Game.Player.Money < _stake)
            {
                _said = "You ain't got it.";
                _at = Game.GameTime + 1500;
                UI.Draw.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                return;
            }

            Game.Player.Money -= _stake;
            Click();

            _said = "";
            _stage = Stage.NoMoreBets;
            _dealer.Act(Scene.RouletteDealer, "no_more_bets");
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
            var pays = Pays(number);

            _won = pays < 0 ? 0 : _stake * (pays + 1);

            if (_won > 0)
            {
                Game.Player.Money += _won;
                UI.Draw.PlaySound("CHECKPOINT_PERFECT", "HUD_MINI_GAME_SOUNDSET");
                _dealer.React(true);
            }
            else
            {
                UI.Draw.PlaySound("LOSER", "HUD_AWARDS");
                _dealer.Act(Scene.RouletteDealer, "clear_chips_zone2");
            }

            _said = Name(number) + (_won > 0 ? " -- you win $" + (_won - _stake).ToString("N0") : " -- house takes it");

            Log.Info("Den: roulette landed pocket " + _pocket + " (" + Name(number) + "); " +
                     KindNames[(int)_kind] + (_kind == Kind.Straight ? " " + Name(_number) : "") +
                     " for $" + _stake.ToString("N0") + (_won > 0 ? " paid $" + _won.ToString("N0") : " lost") + ".");

            _at = Game.GameTime + ResultMs;
            _stage = Stage.Result;
        }

        /// <summary>What the bet pays, to one, or -1 for a loser. 37 is 00.</summary>
        private int Pays(int number)
        {
            if (_kind == Kind.Straight) return number == _number ? 35 : -1;

            // Either zero takes every outside bet.
            if (number == 0 || number == 37) return -1;

            switch (_kind)
            {
                case Kind.Red: return Reds.Contains(number) ? 1 : -1;
                case Kind.Black: return Reds.Contains(number) ? -1 : 1;
                case Kind.Odd: return number % 2 == 1 ? 1 : -1;
                case Kind.Even: return number % 2 == 0 ? 1 : -1;
                case Kind.Low: return number <= 18 ? 1 : -1;
                case Kind.High: return number >= 19 ? 1 : -1;
                case Kind.Dozen1: return number <= 12 ? 2 : -1;
                case Kind.Dozen2: return number >= 13 && number <= 24 ? 2 : -1;
                case Kind.Dozen3: return number >= 25 ? 2 : -1;
                case Kind.Column1: return number % 3 == 1 ? 2 : -1;
                case Kind.Column2: return number % 3 == 2 ? 2 : -1;
                case Kind.Column3: return number % 3 == 0 ? 2 : -1;
            }

            return -1;
        }

        private static string Name(int number)
        {
            if (number == 37) return "00 green";
            if (number == 0) return "0 green";
            return number + (Reds.Contains(number) ? " red" : " black");
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

        private static void Click()
        {
            UI.Draw.PlaySound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        // ---- the card -------------------------------------------------------------------

        private void Draw()
        {
            var lines = new List<string>();
            var choices = new List<string>
            {
                KindNames[(int)_kind],
                _kind == Kind.Straight ? Name(_number) : "--",
                "$" + _stake.ToString("N0")
            };

            string hint;

            switch (_stage)
            {
                case Stage.Betting:
                    lines.Add(_said.Length > 0 && Game.GameTime < _at ? _said : "Place your bet.");
                    hint = "Up/Down field   Left/Right change   Enter spin   Backspace leave";
                    break;

                case Stage.NoMoreBets:
                    lines.Add("No more bets.");
                    hint = "";
                    break;

                case Stage.Result:
                    lines.Add(_said);
                    hint = "";
                    Panel.Banner(_won > 0 ? "WIN" : "HOUSE", _won > 0 ? Palette.Cash : Palette.Danger);
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
