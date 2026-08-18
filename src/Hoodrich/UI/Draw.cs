using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.UI
{
    /// <summary>
    /// Screen-space drawing primitives over the raw natives.
    ///
    /// Coordinate convention throughout Hoodrich's UI:
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

        /// <summary>
        /// Whether a texture actually exists in a dictionary.
        ///
        /// There is no "does this sprite exist" native, but a missing texture has no resolution,
        /// so asking for its size answers the question. Without this a wrong texture name draws
        /// nothing at all and looks identical to a rendering bug.
        /// </summary>
        public static bool HasTexture(string dict, string texture)
        {
            float aspect;
            return HasTexture(dict, texture, out aspect);
        }

        /// <summary>
        /// As above, and hands back the texture's own width-to-height ratio.
        ///
        /// The resolution is already being fetched to answer the existence question, so
        /// throwing it away and drawing every sprite square was a waste of a native call and
        /// the reason wide art came out squashed to half its width.
        /// </summary>
        public static bool HasTexture(string dict, string texture, out float aspect)
        {
            aspect = 1f;

            if (string.IsNullOrEmpty(dict) || string.IsNullOrEmpty(texture)) return false;
            if (!EnsureTextureDict(dict)) return false;

            try
            {
                var size = Function.Call<GTA.Math.Vector3>(Hash.GET_TEXTURE_RESOLUTION, dict, texture);
                if (size.X <= 0f || size.Y <= 0f) return false;

                aspect = size.X / size.Y;
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ---- primitives --------------------------------------------------------

        /// <summary>Axis-aligned filled rectangle. w/h are normalized screen fractions.</summary>
        public static void Rect(float x, float y, float w, float h, Color c)
        {
            Function.Call(Hash.DRAW_RECT, x, y, w, h, (int)c.R, (int)c.G, (int)c.B, (int)c.A);
        }

        /// <summary>
        /// Filled rectangle placed by its TOP-LEFT corner.
        ///
        /// DRAW_RECT is centre-anchored, which is convenient for a wheel and wrong for a panel:
        /// laying a card out from a left edge and then handing those numbers to Rect draws the
        /// box half off the side of the screen while the text inside it sits where intended.
        /// </summary>
        public static void RectFrom(float left, float top, float w, float h, Color c)
        {
            Rect(left + w * 0.5f, top + h * 0.5f, w, h, c);
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
        /// Height of one scanline row, as a fraction of screen height. Smaller is smoother and
        /// costs more rectangles. At 0.0035 the curve visibly stair-stepped; 0.0014 is roughly
        /// 1.5px at 1080p, which reads as a clean edge, and the whole ring still costs only a
        /// few hundred DRAW_RECT calls.
        /// </summary>
        private const float RowHeight = 0.0014f;

        /// <summary>A span wider than this is split, so the half-plane clip stays valid.</summary>
        private const float MaxSpanDegrees = 170f;

        /// <summary>
        /// Solid filled annulus sector ("wedge"). Angles are degrees clockwise from screen-up.
        ///
        /// Rasterised as horizontal scanlines of DRAW_RECT rather than as rotated sprites. The
        /// sprite approach needs a texture that is opaque everywhere, and the ones the game
        /// ships are gradients -- tiling fourteen gradients side by side produced a starburst
        /// instead of an arc. DRAW_RECT has no texture and no rotation, so there is nothing left
        /// to get wrong: the fill is exact, solid, and needs nothing streamed.
        ///
        /// Each row solves the sector analytically. The annulus gives the horizontal extent
        /// (an outer circle, minus an inner circle that punches a hole), and the two boundary
        /// rays are half-planes that clip it -- so there is no sampling and no approximation
        /// beyond the row height itself.
        /// </summary>
        public static void Wedge(float cx, float cy, float rInner, float rOuter,
                                 float angFromDeg, float angToDeg, Color c)
        {
            if (rOuter <= rInner) return;
            if (c.A <= 0) return;

            var span = angToDeg - angFromDeg;
            if (span <= 0f) return;

            // The half-plane clip below assumes a convex wedge, so split anything too wide.
            if (span > MaxSpanDegrees)
            {
                var mid = angFromDeg + span * 0.5f;
                Wedge(cx, cy, rInner, rOuter, angFromDeg, mid, c);
                Wedge(cx, cy, rInner, rOuter, mid, angToDeg, c);
                return;
            }

            const double deg2rad = Math.PI / 180.0;
            var a0 = angFromDeg * deg2rad;
            var a1 = angToDeg * deg2rad;

            // Direction vectors in (dx right, dy up). Angle runs clockwise from up.
            var d0x = (float)Math.Sin(a0);
            var d0y = (float)Math.Cos(a0);
            var d1x = (float)Math.Sin(a1);
            var d1y = (float)Math.Cos(a1);

            var rOut2 = rOuter * rOuter;
            var rIn2 = rInner * rInner;

            for (var dy = -rOuter; dy <= rOuter; dy += RowHeight)
            {
                var dy2 = dy * dy;
                if (dy2 > rOut2) continue;

                var hi = (float)Math.Sqrt(rOut2 - dy2);
                var lo = dy2 < rIn2 ? (float)Math.Sqrt(rIn2 - dy2) : 0f;

                // Clip the row to the wedge: inside means clockwise of d0 and anticlockwise of d1.
                var min = -hi;
                var max = hi;

                if (!ClipHalfPlane(d0x, d0y, dy, true, ref min, ref max)) continue;
                if (!ClipHalfPlane(d1x, d1y, dy, false, ref min, ref max)) continue;

                // The inner radius punches a hole, leaving up to two runs on this row.
                if (lo <= 0f)
                {
                    EmitRow(cx, cy, dy, min, max, c);
                }
                else
                {
                    EmitRow(cx, cy, dy, min, Math.Min(max, -lo), c);
                    EmitRow(cx, cy, dy, Math.Max(min, lo), max, c);
                }
            }
        }

        /// <summary>
        /// Narrows [min,max] to the side of a boundary ray the sector lives on.
        /// Returns false when the row falls entirely outside.
        /// </summary>
        private static bool ClipHalfPlane(float dirX, float dirY, float dy, bool isStartRay,
                                          ref float min, ref float max)
        {
            // Start ray keeps cross(d, p) <= 0; end ray keeps cross(d, p) >= 0.
            // cross(d, p) = d.x*dy - d.y*dx, so each becomes a bound on dx.
            if (Math.Abs(dirY) < 1e-6f)
            {
                // Ray is horizontal: the constraint no longer involves dx at all.
                var side = isStartRay ? -dirX * dy : dirX * dy;
                return side >= 0f;
            }

            var bound = dirX * dy / dirY;
            var lowerBound = isStartRay ? dirY > 0f : dirY < 0f;

            if (lowerBound)
            {
                if (bound > min) min = bound;
            }
            else
            {
                if (bound < max) max = bound;
            }

            return min < max;
        }

        private static void EmitRow(float cx, float cy, float dy, float x0, float x1, Color c)
        {
            if (x1 <= x0) return;

            var width = x1 - x0;
            var centreDx = (x0 + x1) * 0.5f;

            // dy is measured upward; screen y grows downward. The slight overlap hides seams
            // between rows without visibly thickening the shape.
            Rect(cx + ToX(centreDx), cy - dy, ToX(width), RowHeight * 1.6f, c);
        }

        /// <summary>Ring outline, drawn as a thin wedge covering the given sweep.</summary>
        public static void Arc(float cx, float cy, float radius,
                               float angFromDeg, float angToDeg, float thickness, Color c)
        {
            Wedge(cx, cy, radius - thickness * 0.5f, radius + thickness * 0.5f,
                  angFromDeg, angToDeg, c);
        }

        /// <summary>Filled disc.</summary>
        public static void Disc(float cx, float cy, float radius, Color c)
        {
            Wedge(cx, cy, 0f, radius, 0f, 360f, c);
        }

        // ---- text --------------------------------------------------------------

        /// <summary>
        /// The game's built-in font slots. Hoodrich only ever uses fonts the game already
        /// ships, which is what makes it read as stock rather than as an overlay.
        ///
        /// 0 Chalet London Nineteen Sixty -- the standard HUD/menu face. Body text, values,
        ///   descriptions: anything meant to be READ.
        /// 4 Chalet Comprime Cologne Sixty -- condensed. Labels, titles and anything that has
        ///   to fit a tight space, exactly as the vanilla weapon wheel uses it.
        /// 7 Pricedown -- the GTA logo face. Reserved; too loud for HUD use.
        /// </summary>
        public const int FontChaletLondon = 0;
        public const int FontCursive = 1;
        public const int FontRockstarTag = 2;
        public const int FontHandwritten = 3;
        public const int FontChaletComprimeCologne = 4;
        public const int FontPricedown = 7;

        /// <summary>Body text: panel rows, descriptions, the hub's weapon/item name.</summary>
        public const int FontBody = FontChaletLondon;

        /// <summary>Condensed: wedge labels, page titles, the footer hint.</summary>
        public const int FontLabel = FontChaletComprimeCologne;

        /// <summary>
        /// Draws a single line of text. <paramref name="scale"/> is the game's text scale
        /// (0.35 is roughly HUD-caption sized).
        /// </summary>
        public static void Text(string text, float x, float y, float scale, Color c,
                                int font = FontLabel, bool centre = true,
                                bool shadow = true, bool outline = false)
        {
            if (string.IsNullOrEmpty(text)) return;

            Function.Call(Hash.SET_TEXT_FONT, font);
            Function.Call(Hash.SET_TEXT_SCALE, scale, scale);
            Function.Call(Hash.SET_TEXT_COLOUR, (int)c.R, (int)c.G, (int)c.B, (int)c.A);
            Function.Call(Hash.SET_TEXT_CENTRE, centre);

            // Never inherit somebody else''s wrap region.
            Function.Call(Hash.SET_TEXT_JUSTIFICATION, centre ? 0 : 1);
            Function.Call(Hash.SET_TEXT_WRAP, 0f, 1f);

            if (shadow) Function.Call(Hash.SET_TEXT_DROP_SHADOW);
            if (outline) Function.Call(Hash.SET_TEXT_OUTLINE);

            Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
            AddLongString(text);
            Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT, x, y);
        }

        /// <summary>
        /// Draws text right-aligned to <paramref name="rightX"/>.
        ///
        /// GTA has no "draw at this right edge" call, so this uses the wrap region: justify
        /// right, wrap from 0 to rightX, and emit at x = 0. The wrap end becomes the right edge.
        /// </summary>
        public static void TextRight(string text, float rightX, float y, float scale, Color c,
                                     int font = FontBody, bool shadow = true)
        {
            if (string.IsNullOrEmpty(text)) return;

            Function.Call(Hash.SET_TEXT_FONT, font);
            Function.Call(Hash.SET_TEXT_SCALE, scale, scale);
            Function.Call(Hash.SET_TEXT_COLOUR, (int)c.R, (int)c.G, (int)c.B, (int)c.A);
            Function.Call(Hash.SET_TEXT_CENTRE, false);
            Function.Call(Hash.SET_TEXT_JUSTIFICATION, 2);
            Function.Call(Hash.SET_TEXT_WRAP, 0f, rightX);
            if (shadow) Function.Call(Hash.SET_TEXT_DROP_SHADOW);

            Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
            AddLongString(text);
            Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT, 0f, y);

            // Justification AND the wrap region are sticky across draws. Leaving the wrap set
            // is what made every panel look broken: the next left-aligned label inherited a
            // region ending at this right edge and got re-flowed inside it, which reads as text
            // wandering out of its box for no reason.
            Function.Call(Hash.SET_TEXT_JUSTIFICATION, 0);
            Function.Call(Hash.SET_TEXT_WRAP, 0f, 1f);
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
