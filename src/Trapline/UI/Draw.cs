using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.Native;
using Trapline.Core;

namespace Trapline.UI
{
    /// <summary>
    /// Screen-space drawing primitives over the raw natives.
    ///
    /// Coordinate convention throughout Trapline's UI:
    ///   x, y      normalized 0..1 across the screen, and always the CENTRE of the shape
    ///             (matching DRAW_RECT / DRAW_SPRITE rather than a top-left convention).
    ///   radii,
    ///   heights   expressed as a fraction of screen HEIGHT, so a circle stays a circle at
    ///             any aspect ratio. Convert to normalized-x with <see cref="ToX"/>.
    /// </summary>
    internal static class Draw
    {
        private static readonly HashSet<string> RequestedDicts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Screen aspect (width / height). Recomputed each frame by <see cref="BeginFrame"/>.</summary>
        public static float Aspect { get; private set; } = 16f / 9f;

        public static void BeginFrame()
        {
            try
            {
                var res = GTA.UI.Screen.Resolution;
                if (res.Height > 0)
                {
                    var a = (float)res.Width / res.Height;
                    // Guard against a bogus resolution report during a resolution change.
                    if (a > 0.5f && a < 5f) Aspect = a;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Aspect probe failed: " + ex.Message);
            }
        }

        /// <summary>Converts a height-relative length into a normalized-x length.</summary>
        public static float ToX(float heightFraction) => heightFraction / Aspect;

        // ---- texture dictionaries ---------------------------------------------

        /// <summary>
        /// Requests a streamed texture dict once and reports whether it is resident.
        /// Safe to call every frame.
        /// </summary>
        public static bool EnsureTextureDict(string dict)
        {
            if (string.IsNullOrEmpty(dict)) return false;

            if (Function.Call<bool>(Hash.HAS_STREAMED_TEXTURE_DICT_LOADED, dict)) return true;

            if (!RequestedDicts.Contains(dict))
            {
                RequestedDicts.Add(dict);
                Log.Debug("Requesting texture dict '" + dict + "'.");
            }
            Function.Call(Hash.REQUEST_STREAMED_TEXTURE_DICT, dict, false);
            return false;
        }

        // ---- primitives --------------------------------------------------------

        /// <summary>Axis-aligned filled rectangle. w/h are normalized screen fractions.</summary>
        public static void Rect(float x, float y, float w, float h, Color c)
        {
            Function.Call(Hash.DRAW_RECT, x, y, w, h, (int)c.R, (int)c.G, (int)c.B, (int)c.A);
        }

        /// <summary>
        /// Rectangle sized in height-fractions on both axes, so it keeps its shape at any aspect.
        /// </summary>
        public static void RectUniform(float x, float y, float w, float h, Color c)
        {
            Rect(x, y, ToX(w), h, c);
        }

        /// <summary>Sprite with rotation in degrees, clockwise, 0 = upright.</summary>
        public static void Sprite(string dict, string texture, float x, float y, float w, float h,
                                  float rotationDeg, Color c)
        {
            Function.Call(Hash.DRAW_SPRITE, dict, texture, x, y, w, h, rotationDeg,
                          (int)c.R, (int)c.G, (int)c.B, (int)c.A);
        }

        /// <summary>
        /// Filled annulus sector ("wedge") between two radii and two angles, built from a fan
        /// of rotated sprite slivers. Angles are in degrees measured clockwise from screen-up,
        /// matching the way a wheel is naturally described.
        /// </summary>
        public static void Wedge(string dict, string texture, float cx, float cy,
                                 float rInner, float rOuter, float angFromDeg, float angToDeg,
                                 Color c, int slices)
        {
            if (slices < 1) slices = 1;
            if (rOuter <= rInner) return;

            var span = angToDeg - angFromDeg;
            var step = span / slices;
            var rMid = (rInner + rOuter) * 0.5f;
            var radial = rOuter - rInner;

            // Tangential width of one sliver at the mid radius, plus a hair of overlap so the
            // seams between slivers do not show as darker/lighter lines.
            var halfStepRad = Math.Abs(step) * 0.5f * (float)(Math.PI / 180.0);
            var tangential = 2f * rMid * (float)Math.Tan(halfStepRad) * 1.08f;

            for (var i = 0; i < slices; i++)
            {
                var mid = angFromDeg + step * (i + 0.5f);
                var rad = mid * (float)(Math.PI / 180.0);

                // Screen-up is -y, and angles run clockwise.
                var px = cx + ToX(rMid * (float)Math.Sin(rad));
                var py = cy - rMid * (float)Math.Cos(rad);

                Sprite(dict, texture, px, py, ToX(tangential), radial, mid, c);
            }
        }

        /// <summary>Ring outline approximated by short rotated slivers.</summary>
        public static void Arc(string dict, string texture, float cx, float cy, float radius,
                               float angFromDeg, float angToDeg, float thickness, Color c, int slices)
        {
            Wedge(dict, texture, cx, cy, radius - thickness * 0.5f, radius + thickness * 0.5f,
                  angFromDeg, angToDeg, c, slices);
        }

        /// <summary>Filled disc, drawn as a single wedge covering the full turn.</summary>
        public static void Disc(string dict, string texture, float cx, float cy, float radius, Color c, int slices = 48)
        {
            Wedge(dict, texture, cx, cy, 0f, radius, 0f, 360f, c, slices);
        }

        // ---- text --------------------------------------------------------------

        public const int FontStandard = 0;
        public const int FontCursive = 1;
        public const int FontRockstarTag = 2;
        public const int FontHandwritten = 3;
        public const int FontChaletComprimeCologne = 4;
        public const int FontPricedown = 7;

        /// <summary>
        /// Draws a single line of text. <paramref name="scale"/> is the game's text scale
        /// (0.35 is roughly HUD-caption sized).
        /// </summary>
        public static void Text(string text, float x, float y, float scale, Color c,
                                int font = FontChaletComprimeCologne, bool centre = true,
                                bool shadow = true, bool outline = false)
        {
            if (string.IsNullOrEmpty(text)) return;

            Function.Call(Hash.SET_TEXT_FONT, font);
            Function.Call(Hash.SET_TEXT_SCALE, scale, scale);
            Function.Call(Hash.SET_TEXT_COLOUR, (int)c.R, (int)c.G, (int)c.B, (int)c.A);
            Function.Call(Hash.SET_TEXT_CENTRE, centre);
            if (shadow) Function.Call(Hash.SET_TEXT_DROP_SHADOW);
            if (outline) Function.Call(Hash.SET_TEXT_OUTLINE);

            Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
            AddLongString(text);
            Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT, x, y);
        }

        /// <summary>Measures rendered text width as a normalized screen fraction.</summary>
        public static float MeasureText(string text, float scale, int font = FontChaletComprimeCologne)
        {
            if (string.IsNullOrEmpty(text)) return 0f;

            Function.Call(Hash.SET_TEXT_FONT, font);
            Function.Call(Hash.SET_TEXT_SCALE, scale, scale);
            Function.Call(Hash.BEGIN_TEXT_COMMAND_GET_SCREEN_WIDTH_OF_DISPLAY_TEXT, "STRING");
            AddLongString(text);
            return Function.Call<float>(Hash.END_TEXT_COMMAND_GET_SCREEN_WIDTH_OF_DISPLAY_TEXT, true);
        }

        /// <summary>
        /// ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME truncates past 99 bytes, so long strings
        /// are pushed as consecutive components.
        /// </summary>
        private static void AddLongString(string text)
        {
            const int chunk = 96;
            for (var i = 0; i < text.Length; i += chunk)
            {
                var len = Math.Min(chunk, text.Length - i);
                Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, text.Substring(i, len));
            }
        }

        // ---- misc --------------------------------------------------------------

        /// <summary>0 = behind the HUD, 1..7 progressively in front.</summary>
        public static void SetDrawOrder(int order)
        {
            Function.Call(Hash.SET_SCRIPT_GFX_DRAW_ORDER, order);
        }

        public static void PlaySound(string sound, string set)
        {
            Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, sound, set, false);
        }
    }
}
