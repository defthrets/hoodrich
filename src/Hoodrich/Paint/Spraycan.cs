using System;
using GTA;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Paint
{
    /// <summary>
    /// The same mechanic, holding a spray can instead.
    ///
    /// NOTHING ABOUT THE PAINT CHANGES. The extinguisher is still the weapon underneath -- it
    /// is what gives you the aiming camera, the reticle and a trigger, and all three are things
    /// a prop cannot do. What changes is that its model is hidden, a can is put in his hand,
    /// and his upper body plays the game's own tagging animation over the top.
    ///
    /// The animation is anim@scripted@freemode@postertag@graffiti_spray@male@, which is what
    /// GTA Online's poster tagging uses.
    ///
    /// Flag 51 is what makes it work at all: the native's own list says 48 to 63 is "Upper body
    /// > Controllable", meaning it blends over whatever the legs are doing and leaves the player
    /// in charge. A full-body clip here would plant him at a wall, which is the online version's
    /// choreography and the opposite of aiming.
    /// </summary>
    internal sealed class Spraycan
    {
        /// <summary>
        /// The can, in the order worth trying.
        ///
        /// prop_cs_spray_can first because it is the one Rockstar themselves attach to a hand:
        /// re_monkey.c4 creates it and attaches it to bone 28422, which is exactly this. Its
        /// cs_ prefix suggests a cutscene-only prop and it is not -- a world script uses it, so
        /// it streams like anything else. The others are behind it in case a build disagrees.
        /// </summary>
        private static readonly string[] Cans =
        {
            "prop_cs_spray_can",
            "prop_spray_can",
            "prop_paint_spray01a"
        };

        private const string Dict = "anim@scripted@freemode@postertag@graffiti_spray@male@";

        /// <summary>
        /// The four things he can be doing with a can.
        ///
        /// Idle is arm UP, holding it at the wall ready to go -- right while he is aiming and
        /// wrong the rest of the time, because a man walking down the street with his arm
        /// permanently extended looks broken.
        ///
        /// Rest is arm DOWN, and Shake is the shake. Both come out of the same dictionary as
        /// the spray, so they blend into each other rather than snapping.
        /// </summary>
        private const string Idle = "spray_can_idle_male";
        private const string Spray = "spray_can_male";
        private const string Shake = "shake_can_male";
        private const string Rest = "shake_can_idle_male";

        /// <summary>How long a shake runs, and roughly how often he bothers.</summary>
        private const int ShakeMs = 1700;
        private const int ShakeGapMs = 5000;
        private const int ShakeSpreadMs = 4000;

        /// <summary>Upper body, controllable. See the class note.</summary>
        private const int UpperControllable = 51;

        /// <summary>
        /// PH_R_Hand -- and the name matters, because it is not the one I first wrote.
        ///
        /// SKEL_R_Hand is 57005 and is the WRIST JOINT, the thing the arm deforms around, so a
        /// prop hung off it sits beside the hand rather than in the grip and every clip that
        /// changes the grip moves it again. 28422 is a non-deforming helper bone the animators
        /// put there specifically to hang props on -- which is why props attached to it want no
        /// offset and no rotation at all.
        ///
        /// It is also what aims the spray. Rockstar's graffiti jet is authored to come out of a
        /// can sitting on THIS bone at THIS rotation, so a can twisted to look right by eye
        /// sends the paint out sideways.
        /// </summary>
        private const int RightHand = 28422;

        private readonly PaintConfig _cfg;

        private readonly Random _rng = new Random();

        private Prop _can;
        private bool _spraying;

        /// <summary>Which clip is running, and the shake's timing.</summary>
        private string _clip;
        private int _shakeUntil;
        private int _nextShake;
        private int _nextTry;
        private bool _moaned;

        public Spraycan(PaintConfig cfg)
        {
            _cfg = cfg;
        }

        /// <summary>Whether the can is currently in his hand.</summary>
        public bool Out => _can != null && _can.Exists();

        /// <summary>
        /// The can's entity, or 0.
        ///
        /// The plume hangs off this. It has to hang off something you can SEE, and the
        /// weapon -- which is what it used to use -- is deliberately invisible whenever
        /// this look is on.
        /// </summary>
        public int Handle => Out ? _can.Handle : 0;

        /// <summary>
        /// Called every tick. Puts the can up when the tool is out, takes it away when it is not.
        /// </summary>
        public void Update(bool spraying, bool aiming)
        {
            // OFF MEANS INERT, NOT "OFF BUT STILL TIDYING UP".
            //
            // PaintEnabled is checked here as well as in the sprayer, and it gets its own
            // branch rather than being folded into the one below, because the two cases want
            // opposite behaviour.
            //
            // Switched off, this must stop touching the weapon at all -- Posted Up runs the
            // same engine and may well be using that weapon for its own can, and two scripts
            // taking turns to show and hide one model every frame is a flicker with no
            // visible cause. So the teardown runs ONCE and then this leaves the world alone.
            if (!_cfg.PaintEnabled)
            {
                Away();
                return;
            }

            if (!_cfg.SprayCanLook || !Can.Out())
            {
                Away();

                // Kept visible here, though, rather than restored once: the mod is ON and the
                // extinguisher is its business, so anything that re-equips it -- a cutscene,
                // an arrest -- must not be able to leave him holding an invisible one.
                if (Can.Out()) Show();

                return;
            }

            // Anything past here is the mod working, so the next teardown has work to do.
            _down = false;

            Ready();

            // THE LOOK HAPPENS WHETHER OR NOT THE PROP DID. This used to return here when the
            // can had not spawned, which meant one failure -- a model that would not stream --
            // silently took the hidden weapon and the whole animation down with it, and what
            // you got was an ordinary man walking about with nothing in his hands and no clue
            // as to why.
            try
            {
                // Hidden every tick rather than once. Drawing, holstering and every animation
                // that re-equips it puts the model back, so a one-off hide lasts until the
                // first time he does anything with his hands.
                // 0, 1, 1, 0 -- hidden, deselected, and the last two as Rockstar's own
                // scripts pass them. p4 was true here, which is a value they use once in the
                // entire decompiled set.
                Function.Call(Hash.SET_PED_CURRENT_WEAPON_VISIBLE,
                              Game.Player.Character.Handle, false, true, true, false);
            }
            catch
            {
                // Then he is holding an extinguisher and a spray can, which is odd but works.
            }

            // FACING THE WAY HE IS AIMING.
            //
            // Normally the weapon does this -- a man pointing a gun turns to point it. Here
            // the weapon is invisible and an upper-body clip is blended over the aim pose, and
            // between them the turn stops happening: he sprays across his own shoulder while
            // the paint lands wherever the camera is looking, which reads as the paint being
            // wrong rather than the man being wrong.
            //
            // Desired rather than SET_ENTITY_HEADING, so he turns rather than snapping, and
            // only while he is actually aiming or painting -- forcing it the rest of the time
            // would fight every step he takes.
            if (aiming || spraying) FaceTheAim();

            var want = Want(spraying, aiming);

            if (want == _clip && Playing(want)) return;

            _clip = want;
            _spraying = spraying;

            Play(want);
        }

        /// <summary>
        /// Puts the weapon model back.
        ///
        /// DESELECT IS FALSE HERE AND THAT IS THE POINT. The second parameter is
        /// deselectWeapon, not a spare flag -- so restoring visibility with it set was asking
        /// the game to make the extinguisher visible AND take it out of his hands in the same
        /// call, which is a good way to swap to the extinguisher and end up holding nothing.
        /// </summary>
        private static void Show()
        {
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return;

                Function.Call(Hash.SET_PED_CURRENT_WEAPON_VISIBLE,
                              me.Handle, true, false, true, false);
            }
            catch
            {
                // He keeps whatever visibility he had.
            }
        }

        /// <summary>Turns him to look where the camera is looking.</summary>
        private static void FaceTheAim()
        {
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return;

                var dir = GameplayCamera.Direction;

                // Heading is degrees about the vertical, zero pointing north (+Y), and it
                // increases the other way round from atan2 -- hence the negated X.
                var heading = (float)(Math.Atan2(-dir.X, dir.Y) * 180d / Math.PI);

                Function.Call(Hash.SET_PED_DESIRED_HEADING, me.Handle, heading);
            }
            catch
            {
                // He keeps facing wherever he was. Nothing else depends on it.
            }
        }

        /// <summary>What he should be doing with the can right now.</summary>
        private string Want(bool spraying, bool aiming)
        {
            if (spraying) return Spray;

            // Arm up only while he is actually aiming. That is the pose the spray comes out
            // of, so holding it means he is ready -- and the rest of the time it is just a
            // man walking about with his arm stuck out.
            if (aiming) return Idle;

            var now = Game.GameTime;

            // Mid-shake: let it finish rather than cutting it off every frame.
            if (now < _shakeUntil) return Shake;

            if (now >= _nextShake)
            {
                _shakeUntil = now + ShakeMs;
                _nextShake = _shakeUntil + ShakeGapMs + _rng.Next(ShakeSpreadMs);

                return Shake;
            }

            // NOT SHAKING CONSTANTLY. A can rattling without pause reads as a stuck animation
            // rather than a habit, and the resting clip is what the shake is authored to fall
            // back into -- so the two together look like somebody idly keeping it mixed.
            return Rest;
        }

        /// <summary>Whether a given clip is still running, since anything can interrupt one.</summary>
        private static bool Playing(string clip)
        {
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return false;

                return Function.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM, me.Handle, Dict, clip, 3);
            }
            catch
            {
                return true;   // assume it is, rather than restarting it sixty times a second
            }
        }

        /// <summary>Makes sure the animations and the prop are in hand.</summary>
        private void Ready()
        {
            // Asked for every tick until it arrives. A single request that is dropped -- and
            // they are dropped, under streaming pressure -- otherwise never gets made again.
            if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, Dict))
            {
                Function.Call(Hash.REQUEST_ANIM_DICT, Dict);
                return;
            }

            if (Out) return;

            // Not on every single tick: a model that will not load should not cost a blocking
            // request sixty times a second for as long as the can is out.
            if (Game.GameTime < _nextTry) return;
            _nextTry = Game.GameTime + 1000;

            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return;

                foreach (var name in Cans)
                {
                    var model = new Model(name);

                    if (!model.IsValid || !model.IsInCdImage) continue;
                    if (!model.Request(500)) continue;

                    _can = World.CreateProp(model, me.Position, false, false);
                    model.MarkAsNoLongerNeeded();

                    if (_can == null || !_can.Exists()) continue;

                    // ROCKSTAR'S OWN NUMBERS. re_monkey.c4 attaches this same model to this
                    // same bone at 0.0, 0.01, 0.02 -- practically at the bone origin, because
                    // the hand is what carries it and the animation was authored around a can
                    // sitting there. My first attempt pushed it 10cm along the palm, which is
                    // how you get a can floating beside a fist.
                    var bone = Function.Call<int>(Hash.GET_PED_BONE_INDEX, me.Handle, RightHand);

                    Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, _can.Handle, me.Handle, bone,
                                  0f, 0f, _cfg.CanSeat,
                                  0f, 0f, 0f,
                                  false, false, false, false, 2, true);

                    // NO PLAY_ENTITY_ANIM. The dictionary ships _spraycan clips that animate
                    // the can, and they are for a can standing loose in a scene -- driving a
                    // prop's own transform while it is also bolted to a moving bone is two
                    // things writing to one matrix. Attached to the hand, the hand carries it.

                    _moaned = false;
                    Log.Info("Spray can out: " + name + ".");
                    return;
                }

                if (_moaned) return;

                _moaned = true;
                Log.Warn("No spray can model would load -- tried " + string.Join(", ", Cans) +
                         ". The animation and the hidden extinguisher still work, so he will " +
                         "mime it. Paint is unaffected.");
            }
            catch (Exception ex)
            {
                Log.Debug("Could not put the can up: " + ex.Message);
            }
        }

        /// <summary>The tagging clip, over the upper body only.</summary>
        private void Play(string clip)
        {
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return;

                Function.Call(Hash.TASK_PLAY_ANIM, me.Handle, Dict, clip,
                              4f, -4f, -1, UpperControllable, 0f, false, false, false);
            }
            catch
            {
                // No animation is a man holding a can still, which is survivable.
            }
        }

        /// <summary>Whether the teardown has already run, so it is not redone every tick.</summary>
        private bool _down;

        /// <summary>Can gone, weapon visible again. Safe to call every tick; does the work once.</summary>
        public void Away()
        {
            _spraying = false;
            _clip = null;

            // Idempotent, because the off branch above calls this on every single tick for as
            // long as the mod is switched off. Without the latch that is two STOP_ANIM_TASKs
            // and a visibility call sixty times a second, forever, for a mod doing nothing.
            if (_down) return;
            _down = true;

            try
            {
                var me = Game.Player.Character;

                if (me != null && me.Exists())
                {
                    Show();

                    // Only the upper-body task, so this does not cancel whatever else he is
                    // doing with his legs.
                    // All four. Stopping only the two it used to play would leave whichever
                    // of the others was running still going after the can was gone.
                    foreach (var clip in new[] { Idle, Spray, Shake, Rest })
                    {
                        Function.Call(Hash.STOP_ANIM_TASK, me.Handle, Dict, clip, 3f);
                    }
                }
            }
            catch
            {
                // Teardown.
            }

            if (_can == null) return;

            try { if (_can.Exists()) _can.Delete(); }
            catch { /* the streamer gets it */ }

            _can = null;
        }
    }
}
