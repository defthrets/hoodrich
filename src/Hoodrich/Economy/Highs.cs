using System;
using GTA;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.UI;

namespace Hoodrich.Economy
{
    /// <summary>
    /// Taking your own product, and what it does to you.
    ///
    /// THE ONE THING YOU COULD NEVER DO WITH IT WAS USE IT. Every gram in this mod exists to be
    /// bought, cut, bagged, carried and sold -- a supply chain with a player standing outside
    /// it holding a bag he is not allowed to open. Seven products with seven prices and seven
    /// customers, and no answer at all to "what is this stuff actually like".
    ///
    /// SEVEN DIFFERENT ANSWERS, NOT SEVEN DURATIONS OF ONE. A high that is the same wobble for
    /// a different number of seconds is a stat, and nobody would ever choose one over another.
    /// Each of these has ONE thing it does that none of the others do, and the numbers around
    /// it are there to support that one thing:
    ///
    ///   weed      time slows and the world drifts. Nothing is urgent.
    ///   oxy       everything goes soft. You stop taking damage properly and heal as you walk.
    ///   bars      a bright shiny day, and you cannot walk in a straight line.
    ///   crack     rage. Time drags, you hit like a truck, and it is over in forty seconds.
    ///   coke      speed. You run faster than the game normally lets you, and you cannot stand still.
    ///   meth      the long one. Wired for five minutes and wrecked for one afterwards.
    ///   heroin    the trip. Half speed, trails, and you are not going anywhere.
    ///
    /// AND THE HARD ONES COST YOU AFTERWARDS. A comedown is the only thing that makes taking
    /// your own supply a decision rather than a free buff -- and it is also the honest version.
    ///
    /// EVERY EFFECT NAME IN HERE IS A GUESS UNTIL THE GAME ACCEPTS IT. A timecycle that does
    /// not exist is not an error: SET_TIMECYCLE_MODIFIER takes the string, does nothing, and
    /// says nothing. So they are LADDERS -- a list per drug, tried in order, and the first one
    /// the game admits to is kept and written to the log. That is the same arrangement the
    /// paint decals use and for exactly the same reason.
    /// </summary>
    internal sealed class Highs
    {
        /// <summary>What one product does, and for how long.</summary>
        private sealed class Recipe
        {
            public string Drug = "";

            /// <summary>Timecycle names to try, in order. The first that takes is used.</summary>
            public string[] Cycles = new string[0];

            /// <summary>How hard the timecycle is laid on, nought to one.</summary>
            public float Strength = 1f;

            /// <summary>A screen effect to run underneath it, or "".</summary>
            public string Fx = "";

            /// <summary>How he walks. "" leaves his own gait alone.</summary>
            public string Clipset = "";

            /// <summary>Camera sway, nought for a steady one.</summary>
            public float Shake;

            /// <summary>Where time runs. One is normal; nothing above one is possible.</summary>
            public float Time = 1f;

            /// <summary>Sprint multiplier, one to one and a half. The game refuses more.</summary>
            public float Run = 1f;

            /// <summary>What he dishes out and what he soaks up. One is normal.</summary>
            public float Hits = 1f;
            public float Takes = 1f;

            /// <summary>How fast he heals while it lasts.</summary>
            public float Heals = 1f;

            /// <summary>A bright day, held for as long as it lasts.</summary>
            public bool Sunny;

            /// <summary>The special meter, filled the moment it lands.</summary>
            public bool Rage;

            public int Ms = 60000;

            /// <summary>How long the wreckage afterwards lasts. Nought for none.</summary>
            public int DownMs;

            public string Line = "";
        }

        // ---- what each one is ---------------------------------------------------

        private static readonly Recipe[] Book =
        {
            // WEED. Nothing happens quickly. The drift is a slight drunk gait rather than a
            // stagger -- somebody stoned does not fall over, they arrive late.
            new Recipe
            {
                Drug = "weed",
                Cycles = new[] { "drug_wobbly", "stoned", "drug_flying_base", "spectator5" },
                Strength = 0.85f,
                Clipset = "move_m@drunk@slightlydrunk",
                Shake = 0.10f,
                Time = 0.80f,
                Ms = 100000,
                Line = "everything's fine. everything's real slow and fine"
            },

            // OXYCODONE. The painkiller one, so it is about not feeling things: half damage,
            // healing as you walk, and a soft sway. No speed, no strength, nothing sharp.
            new Recipe
            {
                Drug = "ecstasy",
                Cycles = new[] { "drug_flying_01", "drug_flying_base", "drug_wobbly" },
                Strength = 0.7f,
                Clipset = "move_m@drunk@moderatedrunk",
                Shake = 0.14f,
                Time = 0.9f,
                Takes = 0.45f,
                Heals = 4f,
                Ms = 120000,
                DownMs = 12000,
                Line = "cant feel a thing. not one thing"
            },

            // BARS. A BRIGHT SHINY DAY AND A DRUNK WALK, which is the whole brief.
            //
            // The weather is overridden rather than a timecycle being asked to look sunny --
            // it is the one way to be certain, it works at three in the morning in the rain,
            // and coming out of a blackout into blazing sunshine is exactly the joke.
            new Recipe
            {
                Drug = "xanax",
                Cycles = new[] { "drug_flying_base", "drug_wobbly" },
                Strength = 0.6f,
                Clipset = "move_m@drunk@verydrunk",
                Shake = 0.45f,
                Time = 0.92f,
                Takes = 0.7f,
                Sunny = true,
                Ms = 150000,
                DownMs = 15000,
                Line = "beautiful day. beautiful. wheres my car"
            },

            // CRACK. Rage: time drags, everything you hit goes down, and it is over almost
            // before you have used it. The comedown is as long as the high.
            new Recipe
            {
                Drug = "crack",
                Cycles = new[] { "REDMIST", "drug_drivingfast", "spectator9" },
                Fx = "DrugsTrevorClownsFightIn",
                Shake = 0.35f,
                Time = 0.65f,
                Run = 1.35f,
                Hits = 2.2f,
                Takes = 0.5f,
                Rage = true,
                Ms = 40000,
                DownMs = 40000,
                Line = "MOVE"
            },

            // COKE. Not strength -- SPEED. Full sprint multiplier, a twitchy camera and a
            // sharp bright picture, and it is gone in a minute.
            new Recipe
            {
                Drug = "coke",
                Cycles = new[] { "drug_drivingfast", "spectator10", "REDMIST" },
                Fx = "RaceTurbo",
                Strength = 0.9f,
                Shake = 0.22f,
                Run = 1.49f,
                Hits = 1.3f,
                Rage = true,
                Ms = 60000,
                DownMs = 30000,
                Line = "yeah. yeah yeah yeah. what we doing"
            },

            // METH. The long one. Five minutes wired, and then a minute of being no use to
            // anybody -- which is the point of it being the long one.
            new Recipe
            {
                Drug = "meth",
                Cycles = new[] { "spectator10", "drug_drivingfast", "drug_wobbly" },
                Strength = 0.8f,
                Shake = 0.30f,
                Run = 1.40f,
                Hits = 1.2f,
                Takes = 0.85f,
                Ms = 300000,
                DownMs = 60000,
                Line = "not even tired. could go all night"
            },

            // HEROIN. The trip. Half speed, trails, and you are not walking anywhere at any
            // pace -- the run multiplier goes DOWN, which nothing else here does.
            new Recipe
            {
                Drug = "heroin",
                Cycles = new[] { "DRUG_2_TRAILS", "drug_flying_02", "drug_flying_base", "drug_wobbly" },
                Fx = "PeyoteEndOut",
                Clipset = "move_m@drunk@verydrunk",
                Shake = 0.20f,
                Time = 0.55f,
                Run = 1f,
                Takes = 0.6f,
                Ms = 180000,
                DownMs = 45000,
                Line = "..."
            }
        };

        // ---- the comedown -------------------------------------------------------

        /// <summary>
        /// What is left of you afterwards. One recipe for all of them, because being wrecked
        /// is being wrecked whatever did it -- what differs is how LONG, and that is per drug.
        /// </summary>
        private static readonly string[] DownCycles = { "drug_deadman", "CAMERA_BW", "drug_wobbly" };

        private const string DownClipset = "move_m@injured";
        private const float DownShake = 0.18f;
        private const float DownTakes = 1.6f;

        // ---- where it is up to --------------------------------------------------

        private Recipe _on;
        private int _until;

        private bool _coming;
        private int _downUntil;

        private string _cycle = "";
        private string _fx = "";
        private string _clip = "";
        private bool _shaking;
        private bool _weather;

        public bool IsHigh => _on != null;
        public bool IsRough => _coming;

        /// <summary>What is running, for a readout. "" when nothing is.</summary>
        public string Current { get { return _on == null ? "" : _on.Drug; } }

        // ---- taking one ---------------------------------------------------------

        /// <summary>
        /// Why he cannot take this one, or null if he can.
        ///
        /// Asked by the pocket screen so a row that will not work is greyed with a reason on
        /// it, rather than pressable and silent.
        /// </summary>
        public string Refusal(string drugId)
        {
            if (string.IsNullOrEmpty(drugId)) return "Not that";
            if (Find(drugId) == null) return "Nothing to do with this one";
            if (_coming) return "Give it a minute";
            if (_on != null) return _on.Drug == drugId ? "You're already on it" : "One at a time";

            return null;
        }

        /// <summary>Take it. Returns why not, or null once it has landed.</summary>
        public string Take(string drugId, string what)
        {
            var no = Refusal(drugId);
            if (no != null) return no;

            var recipe = Find(drugId);

            _on = recipe;
            _until = Game.GameTime + recipe.Ms;

            _cycle = "";
            _clip = "";

            Apply(recipe);

            Log.Info("High: " + drugId + " for " + (recipe.Ms / 1000) + "s.");

            Notify.Ticker("~g~" + what + "~s~ -- " + recipe.Line);

            return null;
        }

        private static Recipe Find(string drugId)
        {
            foreach (var r in Book)
            {
                if (string.Equals(r.Drug, drugId, StringComparison.OrdinalIgnoreCase)) return r;
            }

            return null;
        }

        // ---- keeping it up ------------------------------------------------------

        /// <summary>
        /// Called every tick. Mostly nothing, like everything else that runs per frame here.
        ///
        /// The clipset is re-attempted rather than applied once, because SET_PED_MOVEMENT_CLIPSET
        /// on a set that is not in memory does nothing at all -- no error and no log line, and
        /// he simply walks normally. It has to be streamed first, which takes a few frames.
        /// </summary>
        public void Update()
        {
            if (_on == null && !_coming) return;

            var now = Game.GameTime;

            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) { Off(); return; }

                // Dying sobers you up, which is the one mercy in here.
                if (!me.IsAlive) { Off(); return; }

                if (_on != null)
                {
                    if (now >= _until) { Crash(); return; }

                    Hold(me, _on.Clipset, _on.Shake, _on.Sunny);
                    return;
                }

                if (now >= _downUntil) { Off(); return; }

                Hold(me, DownClipset, DownShake, false);
            }
            catch (Exception ex)
            {
                Log.Debug("A high tripped: " + ex.Message);
                Off();
            }
        }

        /// <summary>The per-frame half: the gait, the sway, and the sky.</summary>
        private void Hold(Ped me, string clipset, float shake, bool sunny)
        {
            if (!string.IsNullOrEmpty(clipset) && _clip != clipset && Streamed(clipset))
            {
                Function.Call(Hash.SET_PED_MOVEMENT_CLIPSET, me.Handle, clipset, 0.5f);
                _clip = clipset;
            }

            if (shake > 0.001f)
            {
                // STARTED ONCE AND THEN MODULATED. Calling SHAKE_GAMEPLAY_CAM again restarts
                // the shake from the beginning of its curve, so a per-frame call is a shake
                // that never gets anywhere and reads as a judder.
                if (!_shaking)
                {
                    Function.Call(Hash.SHAKE_GAMEPLAY_CAM, "DRUNK_SHAKE", shake);
                    _shaking = true;

                    Function.Call(Hash.SET_PED_IS_DRUNK, me.Handle, true);
                }
                else
                {
                    Function.Call(Hash.SET_GAMEPLAY_CAM_SHAKE_AMPLITUDE, shake);
                }
            }

            if (sunny && !_weather)
            {
                Function.Call(Hash.SET_OVERRIDE_WEATHER, "EXTRASUNNY");
                _weather = true;
            }
        }

        private static bool Streamed(string set)
        {
            try
            {
                if (Function.Call<bool>(Hash.HAS_CLIP_SET_LOADED, set)) return true;

                Function.Call(Hash.REQUEST_CLIP_SET, set);
                return false;
            }
            catch
            {
                return false;
            }
        }

        // ---- putting it on and taking it off -------------------------------------

        private void Apply(Recipe r)
        {
            var player = Game.Player;

            Cycle(r.Cycles, r.Strength);

            if (!string.IsNullOrEmpty(r.Fx)) Fx(r.Fx);

            try
            {
                if (r.Time < 0.999f) Function.Call(Hash.SET_TIME_SCALE, r.Time);

                Function.Call(Hash.SET_RUN_SPRINT_MULTIPLIER_FOR_PLAYER, player.Handle, r.Run);
                Function.Call(Hash.SET_PLAYER_WEAPON_DAMAGE_MODIFIER, player.Handle, r.Hits);
                Function.Call(Hash.SET_PLAYER_WEAPON_DEFENSE_MODIFIER, player.Handle, r.Takes);
                Function.Call(Hash.SET_PLAYER_HEALTH_RECHARGE_MULTIPLIER, player.Handle, r.Heals);

                if (r.Rage) Function.Call(Hash.SPECIAL_ABILITY_FILL_METER, player.Handle, true);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not apply a high: " + ex.Message);
            }
        }

        /// <summary>
        /// The timecycle ladder.
        ///
        /// SET_TIMECYCLE_MODIFIER takes any string at all and says nothing about it, so the
        /// only way to know a name is real is to set it and then ASK -- the index comes back
        /// negative when the game has no such modifier. Each drug names several and the first
        /// that answers is the one it keeps, which is written to the log so a build where a
        /// name has been renamed says so rather than looking merely uneventful.
        /// </summary>
        private void Cycle(string[] names, float strength)
        {
            foreach (var name in names)
            {
                try
                {
                    Function.Call(Hash.SET_TIMECYCLE_MODIFIER, name);

                    if (Function.Call<int>(Hash.GET_TIMECYCLE_MODIFIER_INDEX) < 0) continue;

                    Function.Call(Hash.SET_TIMECYCLE_MODIFIER_STRENGTH, strength);

                    _cycle = name;

                    Log.Debug("High look: " + name + ".");
                    return;
                }
                catch
                {
                    // Next one.
                }
            }

            try { Function.Call(Hash.CLEAR_TIMECYCLE_MODIFIER); }
            catch { }

            Log.Info("None of the timecycles for this one exist on this install: " +
                     string.Join(", ", names));
        }

        private void Fx(string name)
        {
            try
            {
                Function.Call(Hash.ANIMPOSTFX_PLAY, name, 0, true);

                if (Function.Call<bool>(Hash.ANIMPOSTFX_IS_RUNNING, name))
                {
                    _fx = name;
                    return;
                }

                Log.Debug("Screen effect " + name + " would not run.");
            }
            catch
            {
                // The timecycle is doing most of the work anyway.
            }
        }

        /// <summary>The high ends and the wreckage starts. Or it just ends.</summary>
        private void Crash()
        {
            var down = _on == null ? 0 : _on.DownMs;
            var line = _on == null ? "" : _on.Drug;

            Clear();

            if (down <= 0)
            {
                Log.Info("High: " + line + " wore off.");
                return;
            }

            _coming = true;
            _downUntil = Game.GameTime + down;

            Cycle(DownCycles, 1f);

            try
            {
                var player = Game.Player;

                Function.Call(Hash.SET_RUN_SPRINT_MULTIPLIER_FOR_PLAYER, player.Handle, 1f);
                Function.Call(Hash.SET_PLAYER_WEAPON_DAMAGE_MODIFIER, player.Handle, 0.8f);
                Function.Call(Hash.SET_PLAYER_WEAPON_DEFENSE_MODIFIER, player.Handle, DownTakes);
                Function.Call(Hash.SET_PLAYER_HEALTH_RECHARGE_MULTIPLIER, player.Handle, 0f);
            }
            catch
            {
                // He is merely fine, which is a smaller problem than a crash.
            }

            Log.Info("High: " + line + " wore off. Comedown for " + (down / 1000) + "s.");

            Notify.Problem("that's it gone. you feel terrible.");
        }

        /// <summary>Everything back to normal. Safe to call at any time, twice.</summary>
        public void Off()
        {
            Clear();

            _coming = false;
            _downUntil = 0;

            try
            {
                var player = Game.Player;

                Function.Call(Hash.SET_RUN_SPRINT_MULTIPLIER_FOR_PLAYER, player.Handle, 1f);
                Function.Call(Hash.SET_PLAYER_WEAPON_DAMAGE_MODIFIER, player.Handle, 1f);
                Function.Call(Hash.SET_PLAYER_WEAPON_DEFENSE_MODIFIER, player.Handle, 1f);
                Function.Call(Hash.SET_PLAYER_HEALTH_RECHARGE_MULTIPLIER, player.Handle, 1f);
            }
            catch
            {
                // Teardown.
            }
        }

        /// <summary>
        /// The look, the sway, the gait and the sky, put back.
        ///
        /// SEPARATE FROM Off BECAUSE THE COMEDOWN NEEDS IT. Going from high to wrecked has to
        /// take the high off without handing the player back a normal camera and a normal
        /// walk for one frame in between, which is what calling Off would do.
        /// </summary>
        private void Clear()
        {
            _on = null;
            _until = 0;

            try
            {
                if (!string.IsNullOrEmpty(_cycle))
                {
                    Function.Call(Hash.CLEAR_TIMECYCLE_MODIFIER);
                    _cycle = "";
                }

                if (!string.IsNullOrEmpty(_fx))
                {
                    Function.Call(Hash.ANIMPOSTFX_STOP, _fx);
                    _fx = "";
                }

                // TIME GOES BACK WHATEVER HAPPENED. It is a global the whole game reads, and a
                // mod that unloads while it is at 0.55 leaves the player in slow motion with
                // nothing left running that could put it right.
                Function.Call(Hash.SET_TIME_SCALE, 1f);

                if (_shaking)
                {
                    Function.Call(Hash.STOP_GAMEPLAY_CAM_SHAKING, true);
                    _shaking = false;
                }

                if (_weather)
                {
                    Function.Call(Hash.CLEAR_OVERRIDE_WEATHER);
                    _weather = false;
                }

                var me = Game.Player.Character;

                if (me != null && me.Exists())
                {
                    Function.Call(Hash.SET_PED_IS_DRUNK, me.Handle, false);

                    if (!string.IsNullOrEmpty(_clip))
                    {
                        Function.Call(Hash.RESET_PED_MOVEMENT_CLIPSET, me.Handle, 0.5f);
                        _clip = "";
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not clear a high: " + ex.Message);
            }
        }

        public void RestoreWorld()
        {
            Off();
        }
    }
}
