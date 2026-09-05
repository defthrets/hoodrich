using System;
using System.Collections.Generic;
using GTA;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Locations
{
    /// <summary>
    /// Everything the muffler shop can do to a car, as one record: read off a car when you
    /// drive out, kept on the owned record, and put back on when the car is stood up again.
    ///
    /// ONE RECORD FOR ALL OF IT, rather than a field a mod. The game has thirty-odd slots
    /// and they arrive in every update; a record that lists what it found means a slot this
    /// file has never heard of is still carried, and a car that was never in the shop has no
    /// record and is left exactly as the game made it.
    /// </summary>
    internal sealed class Kit
    {
        /// <summary>Mod slot to index. -1 is stock. Only slots the car actually has.</summary>
        public readonly Dictionary<int, int> Mods = new Dictionary<int, int>();

        public bool Turbo;
        public bool Xenon;
        public bool Smoke;
        public int WheelType = -1;
        public int Paint = -1;
        public int Paint2 = -1;
        public int Pearl = -1;
        public int WheelColour = -1;
        public int Tint = -1;
        public int Livery = -1;
        public int PlateStyle;
        public bool Neon;
        public int NeonR = 255;
        public int NeonG = 255;
        public int NeonB = 255;

        /// <summary>The slots worth carrying. Toggles (18, 20, 22) are separate; 17, 19 and 21 do not exist.</summary>
        public static readonly int[] Slots =
        {
            0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 23, 24, 25, 26, 27, 28, 29, 30,
            31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48
        };

        public const int TurboSlot = 18;
        public const int SmokeSlot = 20;
        public const int XenonSlot = 22;

        public static Kit Capture(Vehicle car)
        {
            var kit = new Kit();
            if (car == null || !car.Exists()) return kit;

            var h = car.Handle;

            try
            {
                foreach (var slot in Slots)
                {
                    var n = Function.Call<int>(Hash.GET_NUM_VEHICLE_MODS, h, slot);
                    if (n <= 0) continue;
                    kit.Mods[slot] = Function.Call<int>(Hash.GET_VEHICLE_MOD, h, slot);
                }

                kit.Turbo = Function.Call<bool>(Hash.IS_TOGGLE_MOD_ON, h, TurboSlot);
                kit.Smoke = Function.Call<bool>(Hash.IS_TOGGLE_MOD_ON, h, SmokeSlot);
                kit.Xenon = Function.Call<bool>(Hash.IS_TOGGLE_MOD_ON, h, XenonSlot);
                kit.WheelType = Function.Call<int>(Hash.GET_VEHICLE_WHEEL_TYPE, h);

                var a = new OutputArgument();
                var b = new OutputArgument();
                Function.Call(Hash.GET_VEHICLE_COLOURS, h, a, b);
                kit.Paint = a.GetResult<int>();
                kit.Paint2 = b.GetResult<int>();

                var p = new OutputArgument();
                var w = new OutputArgument();
                Function.Call(Hash.GET_VEHICLE_EXTRA_COLOURS, h, p, w);
                kit.Pearl = p.GetResult<int>();
                kit.WheelColour = w.GetResult<int>();

                kit.Tint = Function.Call<int>(Hash.GET_VEHICLE_WINDOW_TINT, h);
                kit.Livery = Function.Call<int>(Hash.GET_VEHICLE_LIVERY, h);
                kit.PlateStyle = Function.Call<int>(Hash.GET_VEHICLE_NUMBER_PLATE_TEXT_INDEX, h);

                kit.Neon = Function.Call<bool>(Hash.GET_VEHICLE_NEON_ENABLED, h, 0);

                var r = new OutputArgument();
                var g = new OutputArgument();
                var bl = new OutputArgument();
                Function.Call(Hash.GET_VEHICLE_NEON_COLOUR, h, r, g, bl);
                kit.NeonR = r.GetResult<int>();
                kit.NeonG = g.GetResult<int>();
                kit.NeonB = bl.GetResult<int>();
            }
            catch (Exception ex)
            {
                Log.Debug("Could not read a car's kit: " + ex.Message);
            }

            return kit;
        }

        public void Apply(Vehicle car)
        {
            if (car == null || !car.Exists()) return;

            var h = car.Handle;

            try
            {
                // THE KIT FIRST, THEN THE WHEEL TYPE, THEN THE WHEELS. A mod set before the kit
                // is thrown away, and a wheel index means nothing until its type is on.
                Function.Call(Hash.SET_VEHICLE_MOD_KIT, h, 0);

                if (WheelType >= 0) Function.Call(Hash.SET_VEHICLE_WHEEL_TYPE, h, WheelType);

                foreach (var pair in Mods)
                {
                    if (pair.Value < 0) Function.Call(Hash.REMOVE_VEHICLE_MOD, h, pair.Key);
                    else Function.Call(Hash.SET_VEHICLE_MOD, h, pair.Key, pair.Value, false);
                }

                Function.Call(Hash.TOGGLE_VEHICLE_MOD, h, TurboSlot, Turbo);
                Function.Call(Hash.TOGGLE_VEHICLE_MOD, h, SmokeSlot, Smoke);
                Function.Call(Hash.TOGGLE_VEHICLE_MOD, h, XenonSlot, Xenon);

                if (Paint >= 0) Function.Call(Hash.SET_VEHICLE_COLOURS, h, Paint, Paint2 >= 0 ? Paint2 : Paint);
                if (Pearl >= 0 || WheelColour >= 0)
                {
                    Function.Call(Hash.SET_VEHICLE_EXTRA_COLOURS, h, Math.Max(0, Pearl), Math.Max(0, WheelColour));
                }

                if (Tint >= 0) Function.Call(Hash.SET_VEHICLE_WINDOW_TINT, h, Tint);
                if (Livery >= 0) Function.Call(Hash.SET_VEHICLE_LIVERY, h, Livery);
                Function.Call(Hash.SET_VEHICLE_NUMBER_PLATE_TEXT_INDEX, h, PlateStyle);

                for (var i = 0; i < 4; i++) Function.Call(Hash.SET_VEHICLE_NEON_ENABLED, h, i, Neon);
                if (Neon) Function.Call(Hash.SET_VEHICLE_NEON_COLOUR, h, NeonR, NeonG, NeonB);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not put a car's kit back on: " + ex.Message);
            }
        }

        // ---- the neons, which the shop menu shares --------------------------------

        public static bool NeonIsOn(Vehicle car)
        {
            try { return car != null && car.Exists() && Function.Call<bool>(Hash.GET_VEHICLE_NEON_ENABLED, car.Handle, 0); }
            catch { return false; }
        }

        public static void NeonOn(Vehicle car, int side, bool on)
        {
            try { if (car != null && car.Exists()) Function.Call(Hash.SET_VEHICLE_NEON_ENABLED, car.Handle, side, on); }
            catch { }
        }

        public static void NeonColour(Vehicle car, int r, int g, int b)
        {
            try { if (car != null && car.Exists()) Function.Call(Hash.SET_VEHICLE_NEON_COLOUR, car.Handle, r, g, b); }
            catch { }
        }

        public static void NeonRead(Vehicle car, out int r, out int g, out int b)
        {
            r = g = b = 255;
            if (car == null || !car.Exists()) return;

            try
            {
                var rr = new OutputArgument();
                var gg = new OutputArgument();
                var bb = new OutputArgument();
                Function.Call(Hash.GET_VEHICLE_NEON_COLOUR, car.Handle, rr, gg, bb);
                r = rr.GetResult<int>();
                g = gg.GetResult<int>();
                b = bb.GetResult<int>();
            }
            catch
            {
            }
        }

        // ---- to and from the save ------------------------------------------------

        public Json ToJson()
        {
            var mods = Json.Array();
            foreach (var pair in Mods)
            {
                mods.Add(Json.Object().Set("s", pair.Key).Set("i", pair.Value));
            }

            return Json.Object()
                .Set("mods", mods)
                .Set("turbo", Turbo)
                .Set("xenon", Xenon)
                .Set("smoke", Smoke)
                .Set("wheelType", WheelType)
                .Set("paint", Paint)
                .Set("paint2", Paint2)
                .Set("pearl", Pearl)
                .Set("wheelColour", WheelColour)
                .Set("tint", Tint)
                .Set("livery", Livery)
                .Set("plateStyle", PlateStyle)
                .Set("neon", Neon)
                .Set("neonR", NeonR)
                .Set("neonG", NeonG)
                .Set("neonB", NeonB);
        }

        public static Kit FromJson(Json node)
        {
            if (node == null || !node.Has("mods")) return null;

            var kit = new Kit();

            foreach (var m in node["mods"].Items)
            {
                kit.Mods[m["s"].AsInt(-1)] = m["i"].AsInt(-1);
            }
            kit.Mods.Remove(-1);

            kit.Turbo = node["turbo"].AsBool();
            kit.Xenon = node["xenon"].AsBool();
            kit.Smoke = node["smoke"].AsBool();
            kit.WheelType = node["wheelType"].AsInt(-1);
            kit.Paint = node["paint"].AsInt(-1);
            kit.Paint2 = node["paint2"].AsInt(-1);
            kit.Pearl = node["pearl"].AsInt(-1);
            kit.WheelColour = node["wheelColour"].AsInt(-1);
            kit.Tint = node["tint"].AsInt(-1);
            kit.Livery = node["livery"].AsInt(-1);
            kit.PlateStyle = node["plateStyle"].AsInt(0);
            kit.Neon = node["neon"].AsBool();
            kit.NeonR = node["neonR"].AsInt(255);
            kit.NeonG = node["neonG"].AsInt(255);
            kit.NeonB = node["neonB"].AsInt(255);

            return kit;
        }
    }
}
