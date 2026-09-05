using System;
using System.Collections.Generic;
using System.Globalization;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Economy
{
    /// <summary>
    /// Where a held thing sits in his hand, per prop, and the try-on that sets it.
    ///
    /// A PROP HAS ONE ORIGIN AND EVERY ANIMATION WANTS A DIFFERENT ONE. The joint that the
    /// pot animation was built around sits right at nothing; a cutscene pipe attached to
    /// the same bone points out of the back of his fist. There is no table anywhere of
    /// what each one wants, and guessing it from here costs a build and a screenshot a go.
    /// So the numbers are yours: the settings screen puts the thing in his hand with the
    /// pose it is used in, six sliders move and turn it while you watch, and what you settle
    /// on is written to Hoodrich.ini under [HandFit] and added to every ritual that uses
    /// that prop from then on. Centimetres and degrees, because those are units a person can
    /// nudge by.
    /// </summary>
    internal static class Fit
    {
        /// <summary>The props that can be fitted, in the order the settings screen offers them.</summary>
        public static readonly string[] Names =
        {
            "p_amb_joint_01", "prop_cs_meth_pipe", "prop_cs_crackpipe",
            "prop_syringe_01", "prop_meth_bag_01", "prop_cs_pills"
        };

        public static readonly string[] Labels =
        {
            "Joint", "Meth pipe", "Crack pipe", "Needle", "Coke bag", "Pills"
        };

        /// <summary>The ritual each one is held in, wired by Highs; the try-on borrows its hand and its pose.</summary>
        private static Ritual.Recipe[] _recipes = new Ritual.Recipe[0];

        private static readonly Dictionary<string, float[]> _by =
            new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);

        public static void Wire(Ritual.Recipe[] recipes)
        {
            _recipes = recipes ?? new Ritual.Recipe[0];
        }

        // ---- the numbers ----------------------------------------------------------

        /// <summary>Six values: forward, right, up in centimetres; pitch, roll, yaw in degrees. Zeros when unset.</summary>
        public static float[] Of(string prop)
        {
            float[] v;
            return prop != null && _by.TryGetValue(prop, out v) ? v : new float[6];
        }

        public static Vector3 Offset(string prop)
        {
            var v = Of(prop);
            return new Vector3(v[0], v[1], v[2]) * 0.01f;
        }

        public static Vector3 Turn(string prop)
        {
            var v = Of(prop);
            return new Vector3(v[3], v[4], v[5]);
        }

        /// <summary>Remembers a fit; to the ini as well unless told otherwise (loading is the exception).</summary>
        public static void Set(string prop, float[] v, bool persist = true)
        {
            if (string.IsNullOrEmpty(prop) || v == null || v.Length != 6) return;

            _by[prop] = v;

            if (persist) Settings.Put("HandFit", prop, Pack(v));
        }

        public static string Pack(float[] v)
        {
            var parts = new string[6];
            for (var i = 0; i < 6; i++) parts[i] = v[i].ToString("0.##", CultureInfo.InvariantCulture);
            return string.Join(",", parts);
        }

        public static float[] Unpack(string packed)
        {
            var v = new float[6];
            if (string.IsNullOrEmpty(packed)) return v;

            var parts = packed.Split(',');
            for (var i = 0; i < 6 && i < parts.Length; i++)
            {
                float f;
                if (float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out f)) v[i] = f;
            }

            return v;
        }

        // ---- the try-on, from the settings screen -----------------------------------

        private static int _picked;
        private static Prop _shown;
        private static string _dict = "";
        private static string _clip = "";
        private static bool _posed;
        private static int _poseUntil;

        private const int PoseStreamMs = 3000;

        public static int Picked => _picked;

        public static string PickedName => _picked >= 0 && _picked < Names.Length ? Names[_picked] : "";

        private static Ritual.Recipe PickedRecipe => _picked >= 0 && _picked < _recipes.Length ? _recipes[_picked] : null;

        /// <summary>One of the six numbers of the picked prop.</summary>
        public static float Value(int k)
        {
            var v = Of(PickedName);
            return k >= 0 && k < 6 ? v[k] : 0f;
        }

        public static void Value(int k, float value)
        {
            if (k < 0 || k > 5) return;

            var v = Of(PickedName);
            v[k] = value;
            Set(PickedName, v);

            Refresh();
        }

        /// <summary>Puts the picked prop in his hand with the pose it is used in. Called again to change the pick.</summary>
        public static void Pick(int i)
        {
            if (i < 0 || i >= Names.Length) return;

            Hide();
            _picked = i;

            try
            {
                var me = Game.Player.Character;
                var recipe = PickedRecipe;
                if (me == null || !me.Exists() || recipe == null) return;

                _shown = Ritual.InHand(me, new[] { PickedName }, recipe.Sits, recipe.Turned, recipe.Lefty);

                if (recipe.Pairs.Length >= 2)
                {
                    _dict = recipe.Pairs[0];
                    _clip = recipe.Pairs[1];
                    _poseUntil = Game.GameTime + PoseStreamMs;
                    Function.Call(Hash.REQUEST_ANIM_DICT, _dict);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not try a prop on: " + ex.Message);
            }
        }

        /// <summary>Each tick while the settings screen is up: the pose starts once its dictionary is in.</summary>
        public static void Tick()
        {
            if (_shown == null || _posed || _dict.Length == 0) return;
            if (Game.GameTime > _poseUntil) { _dict = ""; return; }

            try
            {
                if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, _dict)) return;

                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return;

                // 49: upper body, looping, feet left alone -- the same flags the rituals use.
                Function.Call(Hash.TASK_PLAY_ANIM, me.Handle, _dict, _clip, 4f, -2f, -1, 49, 0f, false, false, false);
                _posed = true;
            }
            catch
            {
                _dict = "";
            }
        }

        private static void Refresh()
        {
            if (_shown == null || !_shown.Exists()) return;

            try
            {
                var me = Game.Player.Character;
                var recipe = PickedRecipe;
                if (me == null || !me.Exists() || recipe == null) return;

                Ritual.Give(_shown, me, recipe.Sits + Offset(PickedName), recipe.Turned + Turn(PickedName), recipe.Lefty);
            }
            catch
            {
            }
        }

        /// <summary>Takes the try-on out of his hand and lets his arm down. Safe to call with nothing shown.</summary>
        public static void Hide()
        {
            try
            {
                if (_shown != null && _shown.Exists()) _shown.Delete();

                if (_posed)
                {
                    var me = Game.Player.Character;
                    if (me != null && me.Exists()) Function.Call(Hash.STOP_ANIM_TASK, me.Handle, _dict, _clip, -4f);
                }
            }
            catch
            {
            }

            _shown = null;
            _posed = false;
            _dict = "";
            _clip = "";
        }
    }
}
