using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Gangs;

namespace Hoodrich.Den
{
    /// <summary>
    /// The woman behind the table.
    ///
    /// Stood where the casino's own animators put a dealer, doing what a dealer does between
    /// hands: idling, and now and then one of the idle variations so she is not a statue. Asked
    /// for something -- spin the wheel, deal the cards, rake the chips -- she does it, and the
    /// next thing asked of her starts the moment it ends, so a deal of three clips is one
    /// movement and not three with a shrug between. When there is nothing left to do she goes
    /// back to idling on her own.
    ///
    /// TWO WAYS OF STANDING THERE. The roulette and poker dealers' clips are authored from the
    /// table -- every one of them is a scene on the table's origin and she ends up exactly where
    /// the animators put her (see Scene). The blackjack dealer's are not: the casino stands her
    /// on her mark, 0.79 behind the table's middle and facing across it, and plays her clips
    /// where she stands. A dealer made with a spot is that kind; one without is the other.
    ///
    /// NOT SOMEBODY YOU CAN TALK TO, since 2026-09-26. She was stamped for NPC Mind like everybody
    /// else the mod puts down, and Michael asked for the dealers to be left out of it: they talk
    /// when you play, and that is all. Unstamped, she is a script's ped, and NPC Mind leaves a
    /// script's ped alone (its PedState, "owned by another script").
    ///
    /// SHE TALKS LIKE A DIAMOND DEALER. Her voice is set to one of the casino croupiers', whose
    /// lines are the table's own -- "place your bets", "no more bets", the number the ball fell
    /// in, the count of your hand -- and if the game will not give her that voice she keeps her
    /// own and says hello and goodbye in it.
    /// </summary>
    internal sealed class Dealer
    {
        public Ped Ped { get; private set; }

        /// <summary>What she is stood at, and which set of clips she idles in.</summary>
        private readonly Entity _table;
        private readonly string _idleDict;
        private readonly string[] _idles;
        private readonly string _who;
        private readonly string _voice;

        /// <summary>Her mark, in the table's own space, and which way she faces from the table's heading. See the class.</summary>
        private readonly bool _inPlace;
        private readonly Vector3 _spot;
        private readonly float _turn;

        private readonly Random _rng;

        private int _scene = -1;
        private bool _acting;
        private bool _holding;
        private string _actDict;
        private int _actFrom;
        private int _nextVariant;
        private string _pinned;
        private bool _voiced;

        /// <summary>What has been asked of her and not started yet, in order.</summary>
        private readonly Queue<Step> _queue = new Queue<Step>();

        private sealed class Step
        {
            public string Dict;
            public string Clip;
            public Action<int> With;
        }

        /// <summary>How long one idle runs before she tries another.</summary>
        private const int VariantMinMs = 14000;
        private const int VariantMaxMs = 30000;

        /// <summary>How long a clip played where she stands is given to start before "not playing it" is believed.</summary>
        private const int StartGraceMs = 350;

        /// <summary>
        /// Who she is: strippers since 2026-09-26 -- Michael asked for them behind the den's
        /// tables. A female model either way, because the casino's dealer clips are a woman's.
        /// </summary>
        private readonly string _model;

        public Dealer(Entity table, string idleDict, string[] idles, string who, Random rng,
                      string model = "s_f_y_stripper_01", string voice = "S_F_Y_Casino_01_LATINA_01",
                      Vector3? spot = null, float turn = 0f)
        {
            _model = string.IsNullOrEmpty(model) ? "s_f_y_stripper_01" : model;
            _voice = voice;
            _table = table;
            _idleDict = idleDict;
            _idles = idles;
            _who = who;
            _rng = rng;
            _inPlace = spot.HasValue;
            _spot = spot ?? Vector3.Zero;
            _turn = turn;
        }

        public bool Exists => Ped != null && Ped.Exists() && Ped.IsAlive;

        /// <summary>She is in the middle of something asked of her, or has more to do; the game waits.</summary>
        public bool Busy => _acting && !ActDone() || _queue.Count > 0;

        /// <summary>The clip she is on and how far through it, for a game that times something to it.</summary>
        public string Doing { get; private set; }

        public float Phase
        {
            get
            {
                if (!_inPlace) return Scene.Phase(_scene);
                if (!_acting && !_holding || Doing == null || !Exists) return 2f;

                try
                {
                    if (!Function.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM, Ped.Handle, _actDict, Doing, 3)) return 2f;
                    return Function.Call<float>(Hash.GET_ENTITY_ANIM_CURRENT_TIME, Ped.Handle, _actDict, Doing);
                }
                catch
                {
                    return 2f;
                }
            }
        }

        /// <summary>Whether one of her clip's cues went off this frame: her hand closing on a card, opening on it.</summary>
        public bool Fired(int cue)
        {
            if (!Exists) return false;

            try { return Function.Call<bool>(Hash.HAS_ANIM_EVENT_FIRED, Ped.Handle, cue); }
            catch { return false; }
        }

        /// <summary>
        /// Stands her up beside the table. Only once the clips are in: her first pose is the
        /// idle, and a ped with no idle to play stands in the road for a second.
        /// </summary>
        public bool Spawn(GangDef gang)
        {
            if (Exists) return true;
            if (_table == null || !_table.Exists()) return false;
            if (!Scene.Loaded(_idleDict)) return false;
            if (Core.Crowded.Busy) { Core.Crowded.HeldOff("Den"); return false; }

            try
            {
                var model = new Model(_model);
                if (!model.IsValid || !model.IsInCdImage || !Models.Ready(model)) return false;

                // Beside the table and not in it, for a dealer the scene moves to her mark on
                // the first frame; on her mark already, for one who plays her clips where she stands.
                var at = _inPlace ? _table.GetOffsetPosition(_spot) : _table.Position + _table.ForwardVector * 1.5f;
                var heading = _inPlace ? _table.Heading + _turn : _table.Heading + 180f;
                var ped = World.CreatePed(model, at, heading);
                model.MarkAsNoLongerNeeded();

                if (ped == null || !ped.Exists()) return false;

                ped.IsPersistent = true;
                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, ped.Handle, true, true);
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, true);
                Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, ped.Handle, false);
                Function.Call(Hash.SET_PED_CAN_RAGDOLL, ped.Handle, false);
                Function.Call(Hash.SET_ENTITY_INVINCIBLE, ped.Handle, true);

                if (gang != null) Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle, gang.GroupHash);

                Core.Helmets.Off(ped);
                Dress(ped);
                Voice(ped);

                Ped = ped;
                Mark();
                Idle();

                Log.Info("Den: the dealer is stood at the " + _who + (_inPlace ? ", on her mark." : "."));
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Den: could not stand a dealer at the " + _who + ": " + ex.Message);
                return false;
            }
        }

        /// <summary>A dealer who plays her clips where she stands, put back exactly on her mark.</summary>
        private void Mark()
        {
            if (!_inPlace || !Exists) return;

            try
            {
                var at = _table.GetOffsetPosition(_spot);
                Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, Ped.Handle, at.X, at.Y, at.Z, false, false, false);
                Ped.Heading = _table.Heading + _turn;
            }
            catch { }
        }

        /// <summary>
        /// HER OWN CLOTHES, EVERY ONE OF THEM THERE. The game dresses a new ped at random, and on
        /// 2026-09-26 it put one of the den's strippers together without a torso -- a top the
        /// model does not have, drawn as nothing, so she stood behind the table with her arms and
        /// her head and air between. So she is put in the model's own default outfit, every
        /// piece is checked, and what she is wearing goes in the log.
        /// </summary>
        private void Dress(Ped ped)
        {
            try
            {
                Function.Call(Hash.SET_PED_DEFAULT_COMPONENT_VARIATION, ped.Handle);

                var worn = new List<string>();

                for (var c = 0; c < 12; c++)
                {
                    var d = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, ped.Handle, c);
                    var t = Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, ped.Handle, c);
                    var ok = Function.Call<bool>(Hash.IS_PED_COMPONENT_VARIATION_VALID, ped.Handle, c, d, t);

                    if (!ok && Function.Call<int>(Hash.GET_NUMBER_OF_PED_DRAWABLE_VARIATIONS, ped.Handle, c) > 0)
                    {
                        Function.Call(Hash.SET_PED_COMPONENT_VARIATION, ped.Handle, c, 0, 0, 0);
                        worn.Add(c + ":" + d + "/" + t + "->0/0");
                    }
                    else
                    {
                        worn.Add(c + ":" + d + "/" + t);
                    }
                }

                Log.Info("Den: the " + _who + " dealer (" + _model + ") is dressed " + string.Join(" ", worn) + ".");
            }
            catch (Exception ex)
            {
                Log.Debug("Den: could not dress the " + _who + " dealer: " + ex.Message);
            }
        }

        /// <summary>SET_PED_VOICE_GROUP, which ScriptHookVDotNet 3.6 has under no name the build can see.</summary>
        private const ulong SetPedVoiceGroup = 0x7CDC8C3B89F661B3;

        /// <summary>
        /// A croupier's voice from the Diamond, if the game will give her one. Checked by asking
        /// whether she now has the casino's greeting at all.
        /// </summary>
        private void Voice(Ped ped)
        {
            _voiced = false;

            if (string.IsNullOrEmpty(_voice)) return;

            try
            {
                Function.Call((Hash)SetPedVoiceGroup, ped.Handle, Game.GenerateHash(_voice));
                _voiced = Function.Call<bool>(Hash.DOES_CONTEXT_EXIST_FOR_THIS_PED, ped.Handle, "MINIGAME_DEALER_GREET", false);

                if (!_voiced)
                {
                    Function.Call(Hash.SET_AMBIENT_VOICE_NAME, ped.Handle, _voice);
                    _voiced = Function.Call<bool>(Hash.DOES_CONTEXT_EXIST_FOR_THIS_PED, ped.Handle, "MINIGAME_DEALER_GREET", false);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Den: could not give the " + _who + " dealer a voice: " + ex.Message);
            }

            Log.Info(_voiced
                ? "Den: the " + _who + " dealer talks like the Diamond's (" + _voice + ")."
                : "Den: the " + _who + " dealer keeps her own voice; " + _voice + " has no casino lines here.");
        }

        /// <summary>The lines she has in her own voice, for when the casino's will not come.</summary>
        private static readonly Dictionary<string, string> Plain = new Dictionary<string, string>
        {
            { "MINIGAME_DEALER_GREET", "GENERIC_HI" },
            { "MINIGAME_DEALER_LEAVE_GOOD_GAME", "GENERIC_BYE" },
            { "MINIGAME_DEALER_LEAVE_BAD_GAME", "GENERIC_BYE" },
            { "MINIGAME_DEALER_LEAVE_NEUTRAL_GAME", "GENERIC_BYE" },
            { "MINIGAME_DEALER_ANOTHER_GO", "GENERIC_HOWS_IT_GOING" }
        };

        /// <summary>
        /// One of the table's lines, said out loud. The casino's own if she has its voice, a
        /// plain one of hers if not, and nothing where she has nothing that fits.
        /// </summary>
        public void Say(string line)
        {
            if (!Exists || string.IsNullOrEmpty(line)) return;

            var said = line;

            if (!_voiced && !Plain.TryGetValue(line, out said)) return;

            try
            {
                Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, Ped.Handle, said, "SPEECH_PARAMS_FORCE_NORMAL_CLEAR");
            }
            catch (Exception ex)
            {
                Log.Debug("Den: the dealer could not say " + said + ": " + ex.Message);
            }
        }

        /// <summary>Between hands: her idle, looped.</summary>
        public void Idle()
        {
            if (!Exists) return;

            _acting = false;
            _holding = false;
            Doing = null;

            var clip = _pinned ?? _idles[_rng.Next(_idles.Length)];
            Start(_idleDict, clip, true);

            _nextVariant = Game.GameTime + _rng.Next(VariantMinMs, VariantMaxMs);
        }

        /// <summary>
        /// One idle and no other, until told otherwise: the blackjack dealer watching the one
        /// player at her table rather than the room. Null pins nothing.
        /// </summary>
        public void Pin(string clip)
        {
            if (_pinned == clip) return;

            _pinned = clip;
            if (!_acting && !_holding && _queue.Count == 0) Idle();
        }

        /// <summary>
        /// A clip kept going until the next thing is asked of her -- the blackjack dealer's eyes
        /// on your seat while you decide. She is not busy while she holds it.
        /// </summary>
        public void Hold(string dict, string clip)
        {
            if (!Exists || !Scene.Loaded(dict)) return;

            _queue.Clear();
            _acting = false;
            _holding = true;
            _actDict = dict;
            Doing = clip;
            Start(dict, clip, true);
        }

        /// <summary>
        /// Something asked of her, after whatever she is already doing. The props that go with
        /// it -- the cards in her hand -- are put in her scene by <paramref name="with"/> the
        /// same frame she starts it, which is the only way they move with her hands. A dealer who
        /// plays her clips where she stands has no scene; she is handed -1.
        /// </summary>
        public void Act(string dict, string clip, Action<int> with = null)
        {
            if (!Exists) return;

            if (!Scene.Loaded(dict))
            {
                Log.Debug("Den: " + dict + " is not loaded, so the dealer skips " + clip + ".");
                return;
            }

            _queue.Enqueue(new Step { Dict = dict, Clip = clip, With = with });

            if (!_acting || ActDone()) Next();
        }

        /// <summary>Everything still waiting, forgotten: the game has moved on without it.</summary>
        public void Drop()
        {
            _queue.Clear();
        }

        private void Next()
        {
            if (_queue.Count == 0) { Idle(); return; }

            var act = _queue.Dequeue();

            _acting = true;
            _holding = false;
            _actDict = act.Dict;
            _actFrom = Game.GameTime;
            Doing = act.Clip;
            Start(act.Dict, act.Clip, false);

            if (act.With != null)
            {
                try { act.With(_inPlace ? -1 : _scene); }
                catch (Exception ex) { Log.Debug("Den: the props for " + act.Clip + " would not join her: " + ex.Message); }
            }
        }

        /// <summary>A clip started, either way she stands. See the class.</summary>
        private void Start(string dict, string clip, bool loop)
        {
            if (_inPlace)
            {
                try
                {
                    Mark();
                    Function.Call(Hash.TASK_PLAY_ANIM, Ped.Handle, dict, clip, loop ? 1000f : 3f, -2f, -1,
                                  loop ? 1 : 2, 0f, false, false, false);
                }
                catch (Exception ex)
                {
                    Log.Debug("Den: the dealer could not play " + clip + ": " + ex.Message);
                }

                _scene = -1;
                return;
            }

            _scene = Scene.At(_table);
            Scene.Ped(_scene, Ped, dict, clip, loop);
        }

        /// <summary>Whether what she was asked to do has run its course.</summary>
        private bool ActDone()
        {
            if (!_inPlace) return Scene.Done(_scene);
            if (!Exists || Doing == null) return true;

            try
            {
                if (!Function.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM, Ped.Handle, _actDict, Doing, 3))
                    return Game.GameTime - _actFrom > StartGraceMs;

                return Function.Call<float>(Hash.GET_ENTITY_ANIM_CURRENT_TIME, Ped.Handle, _actDict, Doing) >= 0.99f;
            }
            catch
            {
                return true;
            }
        }

        /// <summary>A scene the game wants her in with other things.</summary>
        public int Begin(string dict, string clip)
        {
            if (!Exists) return -1;

            _queue.Clear();
            _acting = true;
            _holding = false;
            _actDict = dict;
            _actFrom = Game.GameTime;
            Doing = clip;
            Start(dict, clip, false);
            return _scene;
        }

        /// <summary>Her reaction to how it went for you, from the shared set.</summary>
        public void React(bool good)
        {
            var n = 1 + _rng.Next(3);
            Act(Scene.SharedDealer, (good ? "female_dealer_reaction_good_var0" : "female_dealer_reaction_bad_var0") + n);
        }

        public void Update()
        {
            if (!Exists) return;

            // An action that has run its course: the next one, or back to the table.
            if (_acting)
            {
                if (ActDone()) Next();
                return;
            }

            if (_queue.Count > 0) { Next(); return; }

            // Held on something a game asked for: left there until the game says otherwise.
            if (_holding) return;

            // Or an idle she has held long enough. A new one each time.
            if (Game.GameTime >= _nextVariant || !_inPlace && !Scene.Running(_scene)) Idle();
        }

        public void Remove()
        {
            try
            {
                if (Ped != null && Ped.Exists()) Ped.Delete();
            }
            catch { }

            Ped = null;
            _scene = -1;
            _acting = false;
            _holding = false;
            _queue.Clear();
        }
    }
}
