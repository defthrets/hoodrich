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
    /// A machine's three reels: the casino's own reel props, set into the cabinet where the
    /// casino sets them.
    ///
    /// THE REELS ARE NOT PART OF THE MACHINE. Each cabinet has an empty window, and the casino
    /// puts three props in it -- vw_prop_casino_slot_04a_reels for Fame or Shame, and so on --
    /// 0.906 up, 0.047 in, and 0.115 either side of the middle, and turns them itself: sixteen
    /// symbols round a reel, a symbol every 22.5 degrees. While they spin it swaps each for the
    /// same reel blurred, the 04b_reels, and swaps back as it stops. So the den builds them for
    /// every machine in the room the moment it finds it, and they sit on whatever they last
    /// stopped on, the way a machine in a casino does. Until 2026-09-26 the den's machines had
    /// an empty window and the reels on a card at the bottom of the screen.
    /// </summary>
    internal sealed class Reels
    {
        public readonly Entity Machine;

        /// <summary>Which machine it is: "04a" for Fame or Shame.</summary>
        public readonly string Kind;

        private readonly Prop[] _sharp = new Prop[3];
        private readonly Prop[] _blur = new Prop[3];
        private readonly bool[] _spinning = new bool[3];
        private readonly float[] _offset = new float[3];
        private int _spinFrom;

        public readonly int[] Stops = new int[3];

        /// <summary>
        /// Where the three sit in the window, in the machine's own space. 1.1 up and 0.05 in, as
        /// NoPixel's, Pulsar's and a RageMP casino's slot scripts all have it. The 0.906 this
        /// started with came from a fourth script and put every reel a fifth of a metre down
        /// inside the cabinet, where nothing that turned could be seen (2026-09-26).
        /// </summary>
        private static readonly float[] Across = { -0.117f, 0.003f, 0.127f };
        private const float In = 0.05f;
        private const float Up = 1.1f;

        /// <summary>A symbol a stop, sixteen stops round.</summary>
        public const float StopDegrees = 22.5f;

        private static readonly Dictionary<int, string> Kinds = MakeKinds();

        private static Dictionary<int, string> MakeKinds()
        {
            var map = new Dictionary<int, string>();

            for (var n = 1; n <= 8; n++)
            {
                var kind = "0" + n + "a";
                map[Game.GenerateHash("vw_prop_casino_slot_" + kind)] = kind;
                map[Game.GenerateHash("ch_prop_casino_slot_" + kind)] = kind;
            }

            return map;
        }

        public static string KindOf(Entity machine)
        {
            string kind;
            return machine != null && Kinds.TryGetValue(machine.Model.Hash, out kind) ? kind : null;
        }

        public Reels(Entity machine, string kind, Random rng)
        {
            Machine = machine;
            Kind = kind;

            for (var i = 0; i < 3; i++)
            {
                Stops[i] = rng.Next(16);
                _offset[i] = (float)rng.NextDouble() * 360f;
            }
        }

        private string Sharp => "vw_prop_casino_slot_" + Kind.Substring(0, 2) + "a_reels";
        private string Blurred => "vw_prop_casino_slot_" + Kind.Substring(0, 2) + "b_reels";

        /// <summary>All three in the window.</summary>
        public bool Built => _sharp[0] != null && _sharp[1] != null && _sharp[2] != null;

        /// <summary>Puts in whatever is missing, as the models come in. Called every second until it is done.</summary>
        public void Build()
        {
            if (Machine == null || !Machine.Exists()) return;

            var model = new Model(Sharp);
            Models.Ready(new Model(Blurred));
            if (!Models.Ready(model)) return;

            for (var i = 0; i < 3; i++)
            {
                if (_sharp[i] != null && _sharp[i].Exists()) continue;

                _sharp[i] = Chips.Make(model, Place(i), Turned(Stops[i] * StopDegrees), true, true);
            }

            if (Built && !_said)
            {
                _said = true;
                var mid = Place(1);
                Log.Info("Den: " + Slots.Title(Kind) + "'s reels are in its window, " + (mid.Z - Machine.Position.Z).ToString("0.00") +
                         " m up (" + Sharp + ").");
            }
        }

        private bool _said;
        private bool _saidBlur;

        private Vector3 Place(int i)
        {
            return Machine.GetOffsetPosition(new Vector3(Across[i], In, Up));
        }

        private Vector3 Turned(float degrees)
        {
            return new Vector3(degrees % 360f, 0f, Machine.Heading);
        }

        /// <summary>One reel off and turning, blurred.</summary>
        public void Spin(int i)
        {
            if (!Built) return;

            var model = new Model(Blurred);

            if (_blur[i] == null || !_blur[i].Exists())
            {
                _blur[i] = Models.Ready(model) ? Chips.Make(model, Place(i), Turned(_offset[i]), true, true) : null;
            }

            // No blurred reel to hand: the sharp one turns instead, which is only less smooth.
            if (_blur[i] != null) Chips.Show(_sharp[i], false);

            if (!_saidBlur)
            {
                _saidBlur = true;
                Log.Info("Den: " + Slots.Title(Kind) + "'s reels spin " + (_blur[i] != null ? "blurred (" + Blurred + ")." : "sharp; the blurred reel is not in yet."));
            }

            _spinning[i] = true;
            _spinFrom = Game.GameTime;
        }

        /// <summary>The turning, a frame at a time.</summary>
        public void Update()
        {
            var t = (Game.GameTime - _spinFrom) * 1.35f;

            for (var i = 0; i < 3; i++)
            {
                if (!_spinning[i]) continue;

                var reel = _blur[i] != null && _blur[i].Exists() ? _blur[i] : _sharp[i];
                if (reel == null || !reel.Exists()) continue;

                var r = Turned(_offset[i] + t + i * 40f);
                Function.Call(Hash.SET_ENTITY_ROTATION, reel.Handle, r.X, r.Y, r.Z, 2, true);
            }
        }

        public bool Spinning(int i) => _spinning[i];

        /// <summary>One reel stopped dead on a symbol, the sharp one back in the window.</summary>
        public void Stop(int i, int stop)
        {
            Stops[i] = stop;
            _spinning[i] = false;

            Chips.Gone(_blur[i]);
            _blur[i] = null;

            if (_sharp[i] == null || !_sharp[i].Exists()) return;

            var r = Turned(stop * StopDegrees);
            Function.Call(Hash.SET_ENTITY_ROTATION, _sharp[i].Handle, r.X, r.Y, r.Z, 2, true);
            Chips.Show(_sharp[i], true);
        }

        public void Remove()
        {
            for (var i = 0; i < 3; i++)
            {
                Chips.Gone(_sharp[i]);
                Chips.Gone(_blur[i]);
                _sharp[i] = null;
                _blur[i] = null;
                _spinning[i] = false;
            }
        }
    }

    /// <summary>
    /// A slot machine, played from the stool, the way the Diamond's are.
    ///
    /// You are put in the machine's scene the way the casino puts you there -- walk up, sit,
    /// bet one or bet max with a press of the button, pull the arm and the arm comes down with
    /// you -- and the reels in the window spin and stop one after another on what you got. The
    /// machine's own screen says so: the casino's slot machine display, drawn onto the cabinet's
    /// screen, themed for the machine. Michael asked for the den's games to be the casino's own
    /// on 2026-09-26.
    ///
    /// THE REELS. Sixteen stops, and which symbol is on which stop is the casino's layout as a
    /// script that plays these machines reads it: sevens on 0 and 8, plums on 1, 9 and 12,
    /// cherries on 2, 6 and 14, melons on 3 and 10, bells on 7 and 13, the jackpot on 5, and the
    /// machine's own symbol on 4, 11 and 15. Three of a kind pays by the symbol; the machine's
    /// own symbol pays on its own -- one gives the stake back, two pay three times it, three pay
    /// ten. About eighty-eight cents back in the dollar over a long night, which is a den and
    /// not a charity.
    /// </summary>
    internal sealed class Slots
    {
        private enum Stage { Entering, Ready, Betting, Pulling, Spinning, Outcome, Exiting, Done }

        private enum Sym { Seven, Plum, Cherry, Melon, Bell, Jackpot, Special }

        private static readonly Sym[] Strip =
        {
            Sym.Seven, Sym.Plum, Sym.Cherry, Sym.Melon, Sym.Special, Sym.Jackpot, Sym.Cherry, Sym.Bell,
            Sym.Seven, Sym.Plum, Sym.Melon, Sym.Special, Sym.Plum, Sym.Bell, Sym.Cherry, Sym.Special
        };

        private static readonly string[] SymNames = { "7", "plum", "cherry", "melon", "bell", "jackpot", "bonus" };

        private static readonly Dictionary<string, string> Titles = new Dictionary<string, string>
        {
            { "01a", "Angel and the Knight" }, { "02a", "Impotent Rage" }, { "03a", "Republican Space Rangers" },
            { "04a", "Fame or Shame" }, { "05a", "Deity of the Sun" }, { "06a", "Twilight Knife" },
            { "07a", "Diamond Miner" }, { "08a", "Evacuator" }
        };

        /// <summary>Each machine's own sounds, by the casino's short names for them.</summary>
        private static readonly Dictionary<string, string> Sounds = new Dictionary<string, string>
        {
            { "01a", "ak" }, { "02a", "ir" }, { "03a", "rsr" }, { "04a", "fs" },
            { "05a", "ds" }, { "06a", "kd" }, { "07a", "td" }, { "08a", "hz" }
        };

        /// <summary>The screen's theme, for the machines that have one of their own.</summary>
        private static readonly Dictionary<string, int> Themes = new Dictionary<string, int>
        {
            { "02a", 2 }, { "05a", 5 }, { "06a", 6 }, { "07a", 7 }, { "08a", 8 }
        };

        public static string Title(string kind)
        {
            string t;
            return kind != null && Titles.TryGetValue(kind, out t) ? t : "the slots";
        }

        private readonly Entity _machine;
        private readonly Reels _reels;
        private readonly Ped _me;
        private readonly Random _rng;
        private readonly int _minBet;
        private readonly int _maxBet;
        private readonly string _dict;
        private readonly string _kind;
        private readonly string _soundSet;
        private readonly TableCam _cam = new TableCam();

        private Stage _stage = Stage.Entering;
        private int _scene = -1;
        private int _at;
        private bool _max;
        private int _stake;
        private int _won;
        private int _times;
        private int _session;
        private string _said = "";
        private int _saidUntil;

        private readonly int[] _stops = new int[3];
        private readonly int[] _stopAt = new int[3];
        private int _spinSound = -1;
        private bool _started;

        private int _movie;
        private int _target = -1;
        private string _targetName;
        private bool _themed;
        private int _screenFrom;
        private bool _saidSlow;

        private const int SpinMs = 3000;
        private const int OutcomeMs = 3200;

        public Slots(Entity machine, Reels reels, Ped me, int minBet, int maxBet, Random rng)
        {
            _machine = machine;
            _reels = reels;
            _me = me;
            _rng = rng;
            _minBet = Math.Max(1, minBet);
            _maxBet = Math.Max(_minBet, maxBet);
            _kind = reels != null ? reels.Kind : Reels.KindOf(machine) ?? "04a";

            string code;
            _soundSet = "dlc_vw_casino_slot_machine_" + (Sounds.TryGetValue(_kind, out code) ? code : "fs") + "_npc_sounds";

            _dict = me.Gender == Gender.Female ? Scene.SlotsFemale : Scene.SlotsMale;

            Screen();
            Enter();
            Cam();

            Sfx.Once(_machine, "welcome_stinger", _soundSet);

            try { Function.Call(Hash.DISPLAY_RADAR, false); }
            catch { }

            Log.Info("Den: sat down at " + Title(_kind) + ", $" + _minBet.ToString("N0") + " a spin, $" +
                     Math.Min(_maxBet, _minBet * 5).ToString("N0") + " bet max.");
        }

        public bool Finished => _stage == Stage.Done;

        /// <summary>The stake: one, or five of them.</summary>
        private int Stake => Math.Min(_maxBet, _max ? _minBet * 5 : _minBet);

        public void Update()
        {
            if (_machine == null || !_machine.Exists() || _me == null || !_me.Exists()) { LetGo(); return; }

            _cam.Update();
            if (_reels != null) _reels.Update();

            switch (_stage)
            {
                case Stage.Entering: if (Scene.Done(_scene)) Sit(); break;
                case Stage.Ready: Ready(); break;
                case Stage.Betting: if (Scene.Done(_scene)) Sit(); break;
                case Stage.Pulling: Pulling(); break;
                case Stage.Spinning: Spinning(); break;
                case Stage.Outcome: if (Scene.Done(_scene) || Game.GameTime >= _at) Sit(); break;
                case Stage.Exiting: if (Scene.Done(_scene)) LetGo(); break;
            }

            if (_stage != Stage.Done)
            {
                Paint();
                Draw();
            }
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
            Scene.Ped(_scene, _me, _dict, _rng.Next(2) == 0 ? "enter_left" : "enter_left_short", false);
            _stage = Stage.Entering;
        }

        private void Sit()
        {
            _scene = Scene.At(_machine);
            Scene.Ped(_scene, _me, _dict, "base_idle_" + (char)('a' + _rng.Next(6)), true);
            _stage = Stage.Ready;
        }

        private void Ready()
        {
            if (Keys.Back) { Exit(); return; }

            if (Keys.Left || Keys.Right || Keys.Lower || Keys.Raise)
            {
                var max = Keys.Right || Keys.Raise;
                if (max != _max)
                {
                    _max = max;
                    Sfx.Once(_machine, _max ? "place_max_bet" : "place_bet", _soundSet);
                    Press(_max ? "press_betmax_a" : "press_betone_a");
                }

                return;
            }

            var pull = Keys.Select;
            var press = Keys.Space;
            if (!pull && !press) return;

            if (Game.Player.Money < Stake)
            {
                Say("You ain't got it.");
                Sfx.Front("DLC_VW_ERROR_MAX");
                return;
            }

            _stake = Stake;
            Game.Player.Money -= _stake;
            _said = "";

            // Decided now, shown as the reels stop.
            for (var i = 0; i < 3; i++) _stops[i] = _rng.Next(16);

            _scene = Scene.At(_machine);

            if (pull)
            {
                // The arm, and the arm comes down: you and the machine in one scene.
                Scene.Ped(_scene, _me, _dict, "pull_spin_a", false);
                Scene.Prop(_scene, _machine, _dict, "pull_spin_a_slotmachine");
            }
            else
            {
                Scene.Ped(_scene, _me, _dict, _rng.Next(2) == 0 ? "press_spin_a" : "press_spin_b", false);
            }

            Sfx.Once(_machine, "start_spin", _soundSet);
            _started = false;
            _stage = Stage.Pulling;
        }

        /// <summary>A button pressed on the cabinet: bet one, bet max.</summary>
        private void Press(string clip)
        {
            if (!Scene.Loaded(_dict)) return;

            _scene = Scene.At(_machine);
            Scene.Ped(_scene, _me, _dict, clip, false);
            _stage = Stage.Betting;
        }

        /// <summary>The reels go the moment the arm is down, and you sit back and watch them.</summary>
        private void Pulling()
        {
            if (!_started && (Scene.Phase(_scene) >= 0.45f || Scene.Done(_scene)))
            {
                _started = true;

                if (_reels != null) for (var i = 0; i < 3; i++) _reels.Spin(i);

                Sfx.Stop(ref _spinSound);
                _spinSound = Sfx.From(_machine, "spinning", _soundSet);

                var now = Game.GameTime;
                _stopAt[0] = now + SpinMs - 1300;
                _stopAt[1] = now + SpinMs - 650;
                _stopAt[2] = now + SpinMs;
            }

            if (!Scene.Done(_scene)) return;

            _scene = Scene.At(_machine);
            Scene.Ped(_scene, _me, _dict, "spinning_" + (char)('a' + _rng.Next(3)), true);
            _stage = Stage.Spinning;
        }

        private void Spinning()
        {
            var now = Game.GameTime;

            for (var i = 0; i < 3; i++)
            {
                if (_reels == null || !_reels.Spinning(i) || now < _stopAt[i]) continue;

                _reels.Stop(i, _stops[i]);
                Sfx.Once(_machine, i == 2 && Pays(Strip[_stops[0]], Strip[_stops[1]], Strip[_stops[2]]) > 1 ? "wheel_stop_on_prize" : "wheel_stop_clunk", _soundSet);
            }

            if (now < _stopAt[2]) return;

            Settle();
        }

        private void Settle()
        {
            Sfx.Stop(ref _spinSound);

            var a = Strip[_stops[0]];
            var b = Strip[_stops[1]];
            var c = Strip[_stops[2]];

            _times = Pays(a, b, c);
            _won = _times * _stake;
            _session += _won - _stake;

            var shown = SymNames[(int)a] + ", " + SymNames[(int)b] + ", " + SymNames[(int)c];
            string clip = null;

            if (_times >= 25)
            {
                clip = "win_big_" + (char)('a' + _rng.Next(3));
                Sfx.Once(_machine, a == Sym.Jackpot && b == a && c == a ? "jackpot" : "big_win", _soundSet);
                _said = shown + " -- $" + _won.ToString("N0") + "!";
            }
            else if (_times > 1)
            {
                clip = "win_" + (char)('a' + _rng.Next(7));
                Sfx.Once(_machine, "small_win", _soundSet);
                _said = shown + " -- $" + _won.ToString("N0");
            }
            else if (_times == 1)
            {
                _said = shown + " -- stake back";
            }
            else
            {
                clip = "lose_" + (char)('a' + _rng.Next(6));
                Sfx.Once(_machine, "no_win", _soundSet);
                _said = shown;
            }

            _saidUntil = Game.GameTime + OutcomeMs + 3000;

            if (_won > 0) Game.Player.Money += _won;

            Method("SET_LAST_WIN", _won);

            Log.Info("Den: " + Title(_kind) + " stopped " + _stops[0] + "/" + _stops[1] + "/" + _stops[2] + " (" + shown + ") for $" +
                     _stake.ToString("N0") + (_won > 0 ? ", paid $" + _won.ToString("N0") : ", lost") + ".");

            if (clip != null)
            {
                _scene = Scene.At(_machine);
                Scene.Ped(_scene, _me, _dict, clip, false);
            }

            _at = Game.GameTime + OutcomeMs;
            _stage = Stage.Outcome;
        }

        /// <summary>What three symbols pay, times the stake. Nought is a loser; one is the stake back.</summary>
        private static int Pays(Sym a, Sym b, Sym c)
        {
            if (a == b && b == c)
            {
                switch (a)
                {
                    case Sym.Jackpot: return 100;
                    case Sym.Seven: return 30;
                    case Sym.Bell: return 15;
                    case Sym.Melon: return 10;
                    case Sym.Special: return 10;
                    case Sym.Cherry: return 5;
                    case Sym.Plum: return 4;
                }
            }

            var specials = (a == Sym.Special ? 1 : 0) + (b == Sym.Special ? 1 : 0) + (c == Sym.Special ? 1 : 0);
            if (specials == 2) return 3;
            if (specials == 1) return 1;

            return 0;
        }

        private void Exit()
        {
            Sfx.Front("DLC_VW_CONTINUE");

            if (!Scene.Loaded(_dict))
            {
                LetGo();
                return;
            }

            _scene = Scene.At(_machine);
            Scene.Ped(_scene, _me, _dict, _rng.Next(2) == 0 ? "exit_left" : "exit_right", false);
            _stage = Stage.Exiting;
            Tidy();
        }

        /// <summary>Straight out, whatever it was doing.</summary>
        public void LetGo()
        {
            if (_stage == Stage.Done) return;

            Tidy();

            try
            {
                Scene.Stop(_machine);
                if (_me != null && _me.Exists()) Function.Call(Hash.CLEAR_PED_TASKS, _me.Handle);
            }
            catch { }

            // Any reel still going is stopped where it is now.
            if (_reels != null)
            {
                for (var i = 0; i < 3; i++)
                {
                    if (_reels.Spinning(i)) _reels.Stop(i, _stops[i]);
                }
            }

            _stage = Stage.Done;
            Log.Info("Den: up from " + Title(_kind) + ".");
        }

        private void Tidy()
        {
            Sfx.Stop(ref _spinSound);
            _cam.Stop();
            ScreenOff();

            try { Function.Call(Hash.DISPLAY_RADAR, true); }
            catch { }
        }

        // ---- the camera and the machine's screen -------------------------------------------

        /// <summary>Over your right shoulder at the reels and the screen above them.</summary>
        private void Cam()
        {
            var from = _machine.GetOffsetPosition(new Vector3(0.3f, -1.1f, 1.5f));
            var at = _machine.GetOffsetPosition(new Vector3(0f, 0.05f, 1.1f));
            _cam.LookFree(from, at, 50f, 900, 0.1f);
        }

        /// <summary>The casino's slot machine display, set up to draw onto this cabinet's screen.</summary>
        private void Screen()
        {
            try
            {
                _movie = Function.Call<int>(Hash.REQUEST_SCALEFORM_MOVIE, "SLOT_MACHINE");
                _targetName = "machine_" + _kind;

                if (!Function.Call<bool>(Hash.IS_NAMED_RENDERTARGET_REGISTERED, _targetName))
                    Function.Call(Hash.REGISTER_NAMED_RENDERTARGET, _targetName, false);

                if (!Function.Call<bool>(Hash.IS_NAMED_RENDERTARGET_LINKED, _machine.Model.Hash))
                    Function.Call(Hash.LINK_NAMED_RENDERTARGET, _machine.Model.Hash);

                _target = Function.Call<int>(Hash.GET_NAMED_RENDERTARGET_RENDER_ID, _targetName);
                _screenFrom = Game.GameTime;

                Log.Info("Den: " + Title(_kind) + "'s screen: render target " + _targetName + " is " + _target + ", display movie " + _movie + ".");
            }
            catch (Exception ex)
            {
                Log.Info("Den: the machine's screen would not set up: " + ex.Message);
                _target = -1;
            }
        }

        /// <summary>The screen drawn this frame. Its theme and first message the first time it is ready.</summary>
        private void Paint()
        {
            if (_movie == 0 || _target < 0) return;

            try
            {
                if (!Function.Call<bool>(Hash.HAS_SCALEFORM_MOVIE_LOADED, _movie))
                {
                    if (!_saidSlow && Game.GameTime - _screenFrom > 6000)
                    {
                        _saidSlow = true;
                        Log.Info("Den: " + Title(_kind) + "'s display movie SLOT_MACHINE has not loaded after six seconds.");
                    }

                    return;
                }

                if (!_themed)
                {
                    _themed = true;

                    int theme;
                    if (Themes.TryGetValue(_kind, out theme)) Method("SET_THEME", theme);
                    else Method("SET_THEME");

                    Message("Place your bet");
                    Method("SET_BET", Stake);
                    Log.Info("Den: " + Title(_kind) + "'s screen is up.");
                }

                Function.Call(Hash.SET_TEXT_RENDER_ID, _target);
                Function.Call(Hash.SET_SCRIPT_GFX_DRAW_ORDER, 4);
                Function.Call(Hash.SET_SCRIPT_GFX_DRAW_BEHIND_PAUSEMENU, true);
                Function.Call(Hash.DRAW_SCALEFORM_MOVIE, _movie, 0.401f, 0.09f, 0.805f, 0.195f, 255, 255, 255, 255, 0);
                Function.Call(Hash.SET_TEXT_RENDER_ID, Function.Call<int>(Hash.GET_DEFAULT_SCRIPT_RENDERTARGET_RENDER_ID));
            }
            catch (Exception ex)
            {
                Log.Info("Den: the machine's screen would not draw: " + ex.Message);
                _target = -1;
            }
        }

        private void ScreenOff()
        {
            try
            {
                if (_movie != 0)
                {
                    Method("SET_BET");
                    Method("SET_LAST_WIN");
                    Message("");

                    var handle = new OutputArgument(_movie);
                    Function.Call(Hash.SET_SCALEFORM_MOVIE_AS_NO_LONGER_NEEDED, handle);
                }

                if (!string.IsNullOrEmpty(_targetName) && Function.Call<bool>(Hash.IS_NAMED_RENDERTARGET_REGISTERED, _targetName))
                    Function.Call(Hash.RELEASE_NAMED_RENDERTARGET, _targetName);
            }
            catch { }

            _movie = 0;
            _target = -1;
        }

        private void Method(string name, params int[] args)
        {
            if (_movie == 0) return;

            try
            {
                Function.Call(Hash.BEGIN_SCALEFORM_MOVIE_METHOD, _movie, name);
                foreach (var a in args) Function.Call(Hash.SCALEFORM_MOVIE_METHOD_ADD_PARAM_INT, a);
                Function.Call(Hash.END_SCALEFORM_MOVIE_METHOD);
            }
            catch { }
        }

        private void Message(string text)
        {
            if (_movie == 0) return;

            try
            {
                Function.Call(Hash.BEGIN_SCALEFORM_MOVIE_METHOD, _movie, "SET_MESSAGE");
                Function.Call(Hash.SCALEFORM_MOVIE_METHOD_ADD_PARAM_TEXTURE_NAME_STRING, text);
                Function.Call(Hash.END_SCALEFORM_MOVIE_METHOD);
            }
            catch { }
        }

        private void Say(string words)
        {
            _said = words;
            _saidUntil = Game.GameTime + 1800;
        }

        // ---- the screen -------------------------------------------------------------------

        private int _betShown = -1;

        private void Draw()
        {
            // The machine's own screen keeps up with the bet.
            if (_themed && _betShown != Stake && _stage == Stage.Ready)
            {
                _betShown = Stake;
                Method("SET_BET", Stake);
            }

            var cash = "$" + Game.Player.Money.ToString("N0");
            var saying = _said.Length > 0 && Game.GameTime < _saidUntil;

            switch (_stage)
            {
                case Stage.Ready:
                case Stage.Betting:
                    Help.ShowThisFrame(saying ? _said : Title(_kind) + ". Three of a kind pays; the machine's own symbol pays on its own. " +
                                       (Game.LastInputMethod == InputMethod.GamePad ? "Right stick to look around." : "Move the mouse to look around."));

                    Bars.Draw("CASH", cash, Color.White,
                              "BET", "$" + Stake.ToString("N0"), Color.FromArgb(255, 240, 200, 80));

                    Buttons.Show(Control.PhoneCancel, "Leave machine",
                                 Control.Jump, "Spin",
                                 Control.PhoneSelect, "Pull",
                                 Control.PhoneRight, "Bet max: $" + Math.Min(_maxBet, _minBet * 5).ToString("N0"),
                                 Control.PhoneLeft, "Bet one: $" + _minBet.ToString("N0"));
                    break;

                case Stage.Outcome:
                    if (saying) Help.ShowThisFrame(_said);

                    Bars.Draw("CASH", cash, Color.White,
                              _won > _stake ? "WON" : _won > 0 ? "BACK" : "LOST", "$" + (_won > 0 ? _won : _stake).ToString("N0"),
                              _won > _stake ? Color.FromArgb(255, 114, 204, 114) : _won > 0 ? Color.FromArgb(255, 240, 200, 80) : Color.FromArgb(255, 224, 50, 50));
                    break;

                case Stage.Pulling:
                case Stage.Spinning:
                    Bars.Draw("CASH", cash, Color.White, "BET", "$" + _stake.ToString("N0"), Color.FromArgb(255, 240, 200, 80));
                    break;
            }
        }
    }
}
