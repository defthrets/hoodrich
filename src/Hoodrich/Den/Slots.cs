using System;
using System.Collections.Generic;
using GTA;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.UI;

namespace Hoodrich.Den
{
    /// <summary>
    /// A slot machine, played from the stool.
    ///
    /// You are put in the machine's scene the way the casino puts you there -- walk up, sit,
    /// pull the arm and the arm comes down with you, sit through the spin, take it or wear it,
    /// get up -- and the reels are on the card at the bottom of the screen, three of them,
    /// stopping one after another. The machine's own reels are separate props the casino
    /// scripts turn by hand, and where they sit in the cabinet is not written anywhere on this
    /// machine, so the card has them for now. See Den for what is still to come.
    ///
    /// Six symbols on twenty stops a reel, weighted so it pays back about eighty-six cents in
    /// the dollar across a long night, which is a den and not a charity. Three sevens is the
    /// one worth telling somebody about.
    /// </summary>
    internal sealed class Slots
    {
        private enum Stage { Entering, Ready, Pulling, Spinning, Outcome, Exiting, Done }

        /// <summary>The reel strip: what is on the twenty stops, in order. Seven is the rare one.</summary>
        private static readonly string[] Strip =
        {
            "7", "BAR", "*", "$", "o", "o", "BAR", "*", "$", "o",
            "7", "*", "o", "$", "o", "BAR", "*", "o", "$", "o"
        };

        private readonly Entity _machine;
        private readonly Ped _me;
        private readonly Random _rng;
        private readonly int _minBet;
        private readonly int _maxBet;
        private readonly string _dict;

        private Stage _stage = Stage.Entering;
        private int _scene = -1;
        private int _at;
        private bool _max;
        private int _stake;
        private int _won;
        private string _said = "";

        private readonly int[] _stops = new int[3];
        private readonly int[] _shown = new int[3];
        private readonly int[] _stopAt = new int[3];
        private bool _pulled;

        private const int SpinMs = 2600;
        private const int OutcomeMs = 3000;

        public Slots(Entity machine, Ped me, int minBet, int maxBet, Random rng)
        {
            _machine = machine;
            _me = me;
            _rng = rng;
            _minBet = Math.Max(1, minBet);
            _maxBet = Math.Max(_minBet, maxBet);

            _dict = me.Gender == Gender.Female ? Scene.SlotsFemale : Scene.SlotsMale;

            for (var i = 0; i < 3; i++) _shown[i] = _rng.Next(Strip.Length);

            Enter();
            Log.Info("Den: sat down at a slot machine.");
        }

        public bool Finished => _stage == Stage.Done;

        /// <summary>The stake: one, or five of them.</summary>
        private int Stake => Math.Min(_maxBet, _max ? _minBet * 5 : _minBet);

        public void Update()
        {
            if (_machine == null || !_machine.Exists() || _me == null || !_me.Exists()) { LetGo(); return; }

            switch (_stage)
            {
                case Stage.Entering: if (Scene.Done(_scene)) Sit(); break;
                case Stage.Ready: Ready(); break;
                case Stage.Pulling: if (Scene.Done(_scene)) Spin(); break;
                case Stage.Spinning: Spinning(); break;
                case Stage.Outcome: if (Scene.Done(_scene) || Game.GameTime >= _at) Sit(); break;
                case Stage.Exiting: if (Scene.Done(_scene)) LetGo(); break;
            }

            Draw();
        }

        // ---- the stool ----------------------------------------------------------------

        private void Enter()
        {
            if (!Scene.Loaded(_dict))
            {
                // Sat straight down, then. The clips are asked for on the way in and this is
                // only ever the first few seconds after it.
                _stage = Stage.Ready;
                return;
            }

            _scene = Scene.At(_machine);
            Scene.Ped(_scene, _me, _dict, "enter_left", false);
            _stage = Stage.Entering;
        }

        private void Sit()
        {
            _scene = Scene.At(_machine);
            Scene.Ped(_scene, _me, _dict, "base_idle_a", true);
            _stage = Stage.Ready;
        }

        private void Ready()
        {
            if (Keys.Back) { Exit(); return; }

            if (Keys.Left || Keys.Right) { _max = !_max; Click(); }

            if (!Keys.Select) return;

            if (Game.Player.Money < Stake)
            {
                _said = "You ain't got it.";
                _at = Game.GameTime + 1500;
                UI.Draw.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                return;
            }

            _stake = Stake;
            Game.Player.Money -= _stake;
            _said = "";

            // Decided now, shown as the reels stop.
            for (var i = 0; i < 3; i++) _stops[i] = _rng.Next(Strip.Length);

            // The arm, and the arm comes down: you and the machine in one scene.
            _scene = Scene.At(_machine);
            Scene.Ped(_scene, _me, _dict, "pull_spin_a", false);
            Scene.Prop(_scene, _machine, _dict, "pull_spin_a_slotmachine");

            _pulled = true;
            _stage = Stage.Pulling;
        }

        private void Spin()
        {
            _scene = Scene.At(_machine);
            Scene.Ped(_scene, _me, _dict, "spinning_a", true);

            var now = Game.GameTime;
            _stopAt[0] = now + SpinMs - 1400;
            _stopAt[1] = now + SpinMs - 700;
            _stopAt[2] = now + SpinMs;

            _at = now + SpinMs;
            _stage = Stage.Spinning;
        }

        private void Spinning()
        {
            var now = Game.GameTime;

            for (var i = 0; i < 3; i++)
            {
                if (now < _stopAt[i]) _shown[i] = (_shown[i] + 1) % Strip.Length;
                else _shown[i] = _stops[i];
            }

            if (now < _at) return;

            Settle();
        }

        private void Settle()
        {
            var a = Strip[_stops[0]];
            var b = Strip[_stops[1]];
            var c = Strip[_stops[2]];

            var times = Pays(a, b, c);
            _won = times * _stake;

            string clip;

            if (times >= 25)
            {
                clip = "win_big_" + (char)('a' + _rng.Next(3));
                UI.Draw.PlaySound("CHECKPOINT_PERFECT", "HUD_MINI_GAME_SOUNDSET");
                _said = a + " " + b + " " + c + " -- $" + _won.ToString("N0");
            }
            else if (times > 0)
            {
                clip = "win_" + (char)('a' + _rng.Next(7));
                UI.Draw.PlaySound("CHECKPOINT_PERFECT", "HUD_MINI_GAME_SOUNDSET");
                _said = a + " " + b + " " + c + " -- $" + _won.ToString("N0");
            }
            else
            {
                clip = "lose_" + (char)('a' + _rng.Next(6));
                _said = a + " " + b + " " + c;
            }

            if (_won > 0) Game.Player.Money += _won;

            Log.Info("Den: slots " + a + " " + b + " " + c + " for $" + _stake.ToString("N0") +
                     (_won > 0 ? " paid $" + _won.ToString("N0") : " lost") + ".");

            _scene = Scene.At(_machine);
            Scene.Ped(_scene, _me, _dict, clip, false);

            _at = Game.GameTime + OutcomeMs;
            _stage = Stage.Outcome;
        }

        /// <summary>What three symbols pay, times the stake. Nought is a loser.</summary>
        private static int Pays(string a, string b, string c)
        {
            if (a == b && b == c)
            {
                switch (a)
                {
                    case "7": return 60;
                    case "BAR": return 25;
                    case "*": return 12;
                    case "$": return 8;
                    default: return 5;
                }
            }

            var sevens = (a == "7" ? 1 : 0) + (b == "7" ? 1 : 0) + (c == "7" ? 1 : 0);
            if (sevens == 2) return 4;

            var os = (a == "o" ? 1 : 0) + (b == "o" ? 1 : 0) + (c == "o" ? 1 : 0);
            if (os == 2) return 1;

            return 0;
        }

        private void Exit()
        {
            Click();

            if (!Scene.Loaded(_dict) || !_pulled && _scene < 0)
            {
                LetGo();
                return;
            }

            _scene = Scene.At(_machine);
            Scene.Ped(_scene, _me, _dict, "exit_left", false);
            _stage = Stage.Exiting;
        }

        private void LetGo()
        {
            try
            {
                Scene.Stop(_machine);
                if (_me != null && _me.Exists()) Function.Call(Hash.CLEAR_PED_TASKS, _me.Handle);
            }
            catch { }

            _stage = Stage.Done;
            Log.Info("Den: up from the slot machine.");
        }

        private static void Click()
        {
            UI.Draw.PlaySound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        // ---- the card -----------------------------------------------------------------

        private void Draw()
        {
            var reels = "[ " + Strip[_shown[0]] + " ]   [ " + Strip[_shown[1]] + " ]   [ " + Strip[_shown[2]] + " ]";

            var lines = new List<string> { reels };
            List<string> choices = null;
            var picked = -1;
            string hint;

            switch (_stage)
            {
                case Stage.Ready:
                    lines.Add(_said.Length > 0 && Game.GameTime < _at ? _said : "777 pays 60   BAR 25   * 12   $ 8   o 5   two 7s 4");
                    choices = new List<string> { "Bet $" + _minBet.ToString("N0"), "Bet max $" + Math.Min(_maxBet, _minBet * 5).ToString("N0") };
                    picked = _max ? 1 : 0;
                    hint = "Left/Right stake   Enter pull   Backspace get up";
                    break;

                case Stage.Outcome:
                    lines.Add(_said);
                    hint = "";
                    if (_won > 0) Panel.Banner(_won >= _stake * 25 ? "JACKPOT" : "WIN", Palette.Cash);
                    break;

                case Stage.Spinning:
                case Stage.Pulling:
                    lines.Add("...");
                    hint = "";
                    break;

                default:
                    lines.Add("");
                    hint = "";
                    break;
            }

            Panel.Draw("Slots", lines, choices, picked, hint);
        }
    }
}
