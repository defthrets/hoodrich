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

        /// <summary>
        /// Screen height in real device pixels.
        ///
        /// Needed because the wedge filler has to land its rows on whole pixels. Everything
        /// else in here is happily resolution-independent; a stack of rectangles pretending to
        /// be a solid shape is the one thing that is not.
        /// </summary>
        public static int ScreenHeight { get; private set; } = 1080;

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

                    if (res.Height >= 240 && res.Height <= 8192) ScreenHeight = res.Height;
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
        /// <summary>
        /// Scanline height for the wedge filler, in whole device pixels.
        ///
        /// This used to be a normalised fraction -- 0.0018 of the screen height, which at 1080p
        /// is 1.94 pixels. Not two. Every row therefore started on a different sub-pixel phase,
        /// and the game resolved each one slightly differently: two pixels here, one there, a
        /// half-lit edge somewhere else. The fills are semi-transparent, so wherever the phase
        /// drift left two rows overlapping, that sliver blended twice and came out darker.
        ///
        /// That is what the horizontal banding across the wheel was. It is also why tuning the
        /// number never fixed it -- 0.0022, 0.0010 and 0.0018 are all fractions of a pixel, so
        /// each one simply moved the stripes somewhere else.
        ///
        /// Three pixels rather than two is the budget. Every row is a DRAW_RECT and the game
        /// quietly stops drawing them once a frame has asked for too many; it stops on whatever
        /// was asked for LAST, so the final segments came out as missing chunks and detached
        /// blobs. That is the broken wheel, and it was never a geometry bug.
        ///
        /// Three still tiles perfectly -- whole pixels are whole pixels -- and costs a third
        /// fewer rows. The only thing lost is a little smoothness on the curve, which the
        /// hairline arc along the edge covers anyway.
        /// </summary>
        private const int RowPixels = 3;

        /// <summary>The row height as the game wants it: a fraction of screen height.</summary>
        private static float RowHeight => RowPixels / (float)ScreenHeight;

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
        /// <summary>
        /// A whole annulus, with no angular clipping at all.
        ///
        /// This exists because the wheel was drawing one Wedge per segment and running the game
        /// out of rectangles. DRAW_RECT has a per-frame cap; past it the game simply stops
        /// drawing, and it stops on whatever was asked for LAST -- so the final two segments
        /// came out as broken shapes and missing chunks. Not a rendering artefact: the game
        /// refusing to draw any more.
        ///
        /// Five segments in the same colour do not need five fills. The ring is laid down once
        /// here, in one pass with no half-plane maths and at most two runs per row, and only the
        /// segments that differ -- the one under the cursor, and anything disabled -- are drawn
        /// on top of it. That is two or three fills instead of seven, and the budget stops being
        /// something the wheel can run into.
        /// </summary>
        public static void Ring(float cx, float cy, float rInner, float rOuter, Color c)
        {
            if (rOuter <= rInner || c.A <= 0) return;

            var rOut2 = rOuter * rOuter;
            var rIn2 = rInner * rInner;

            var pxTop = (int)Math.Floor((cy - rOuter) * ScreenHeight);
            var pxBottom = (int)Math.Ceiling((cy + rOuter) * ScreenHeight);

            var pxCentre = (int)Math.Round(cy * ScreenHeight);
            pxTop -= ((pxTop - pxCentre) % RowPixels + RowPixels) % RowPixels;

            var rowHeight = RowHeight;

            for (var py = pxTop; py < pxBottom; py += RowPixels)
            {
                var rowY = (py + RowPixels * 0.5f) / ScreenHeight;
                var dy = cy - rowY;

                var dy2 = dy * dy;
                if (dy2 > rOut2) continue;

                var hi = (float)Math.Sqrt(rOut2 - dy2);
                var lo = dy2 < rIn2 ? (float)Math.Sqrt(rIn2 - dy2) : 0f;

                if (lo <= 0f)
                {
                    EmitRow(cx, rowY, rowHeight, -hi, hi, c);
                }
                else
                {
                    EmitRow(cx, rowY, rowHeight, -hi, -lo, c);
                    EmitRow(cx, rowY, rowHeight, lo, hi, c);
                }
            }
        }

        /// <summary>
        /// A radial line from the inner edge to the outer one.
        ///
        /// Used to cut the ring back into segments once it is drawn in one piece. Walked along
        /// the spoke rather than rasterised, so it costs a couple of dozen small squares rather
        /// than a fill.
        /// </summary>
        public static void Spoke(float cx, float cy, float rInner, float rOuter,
                                 float angleDeg, float thickness, Color c)
        {
            if (rOuter <= rInner || c.A <= 0) return;

            var rad = angleDeg * (float)(Math.PI / 180.0);
            var dx = (float)Math.Sin(rad);
            var dy = (float)Math.Cos(rad);

            // One step per two pixels along the spoke, so the line is solid without being
            // drawn more times than the screen can show.
            var steps = Math.Max(2, (int)((rOuter - rInner) * ScreenHeight / 2f));

            for (var i = 0; i <= steps; i++)
            {
                var r = rInner + (rOuter - rInner) * (i / (float)steps);

                Rect(cx + ToX(dx * r), cy - dy * r, ToX(thickness), thickness, c);
            }
        }

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

            // Every row of the disc, every time, walked in whole device pixels.
            //
            // Two things are going on here. The first is that the loop covers the full height
            // of the disc rather than the band the wedge occupies: a previous version worked
            // out that band to skip the three quarters a quarter-circle wedge throws away, and
            // got it wrong, because the extreme of a sector is not always at a boundary ray --
            // it is at the top of the arc whenever the sector crosses an axis. It cut chunks
            // out of wedges. The loop is cheap; being right is not optional.
            //
            // The second is that the iteration is over integer pixel rows, not over a float
            // stepped by a fraction. Row n covers exactly the pixels [n, n + RowPixels), so
            // consecutive rows tile: no gap for the background to show through, and no overlap
            // for a semi-transparent fill to blend twice at. Stepping a float by 0.0018 did
            // neither, and the stripes it left are the whole reason this is written this way.
            var pxTop = (int)Math.Floor((cy - rOuter) * ScreenHeight);
            var pxBottom = (int)Math.Ceiling((cy + rOuter) * ScreenHeight);

            // Anchor the phase to the wheel's centre rather than to the top of the disc, so a
            // hovered wedge that reaches further out still lands on the same pixel grid as its
            // neighbours. Otherwise growing one wedge shifts its rows half a pixel and draws a
            // seam down both of its edges.
            var pxCentre = (int)Math.Round(cy * ScreenHeight);
            pxTop -= ((pxTop - pxCentre) % RowPixels + RowPixels) % RowPixels;

            var rowHeight = RowHeight;

            for (var py = pxTop; py < pxBottom; py += RowPixels)
            {
                // The centre of this row, on the pixel grid.
                var rowY = (py + RowPixels * 0.5f) / ScreenHeight;
                var dy = cy - rowY;

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
                    EmitRow(cx, rowY, rowHeight, min, max, c);
                }
                else
                {
                    EmitRow(cx, rowY, rowHeight, min, Math.Min(max, -lo), c);
                    EmitRow(cx, rowY, rowHeight, Math.Max(min, lo), max, c);
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

        /// <summary>
        /// One horizontal run of a wedge, at an already pixel-aligned y.
        ///
        /// Exactly the row height -- no fudge factor. Rows tile because they are placed on the
        /// pixel grid, so there is nothing left to paper over, and the 1.02x that used to be
        /// here was itself part of the problem: a two percent overlap on a translucent fill is
        /// a darker line every two pixels.
        /// </summary>
        private static void EmitRow(float cx, float rowY, float rowHeight, float x0, float x1, Color c)
        {
            if (x1 <= x0) return;

            var width = x1 - x0;
            var centreDx = (x0 + x1) * 0.5f;

            Rect(cx + ToX(centreDx), rowY, ToX(width), rowHeight, c);
        }

        /// <summary>
        /// Ring outline, walked around the circumference rather than filled row by row.
        ///
        /// Scanning rows to draw a hairline ring means covering the whole height of the disc to
        /// light up a couple of pixels at each end of every row, which cost hundreds of
        /// rectangles for a line you can barely see. Stepping along the arc costs one small
        /// square per step and looks the same.
        /// </summary>
        public static void Arc(float cx, float cy, float radius,
                               float angFromDeg, float angToDeg, float thickness, Color c)
        {
            if (radius <= 0f || thickness <= 0f || c.A <= 0) return;

            var span = angToDeg - angFromDeg;
            if (span <= 0f) return;

            // One step per unit of arc length roughly equal to the line thickness, so the
            // squares overlap into a continuous ring, with a ceiling so a big circle cannot
            // run away with the frame.
            var circumference = (float)(2.0 * Math.PI * radius) * (span / 360f);
            // Capped lower than it wants to be. This is a hairline: past about a hundred and
            // thirty steps nobody can tell, and every step is a rectangle the wedges are not
            // getting -- and the wedges are the thing people actually look at.
            var steps = (int)Math.Min(130f, Math.Max(24f, circumference / Math.Max(0.0015f, thickness)));

            const double deg2rad = Math.PI / 180.0;
            var size = thickness * 1.7f;

            for (var i = 0; i <= steps; i++)
            {
                var ang = (angFromDeg + span * i / steps) * deg2rad;

                var dx = (float)Math.Sin(ang) * radius;
                var dy = (float)Math.Cos(ang) * radius;

                Rect(cx + ToX(dx), cy - dy, ToX(size), size, c);
            }
        }

        /// <summary>
        /// Filled disc.
        ///
        /// Its own row scan rather than a full-circle wedge. Going through Wedge splits the
        /// circle into four sectors, each of which walks every row of the whole disc to fill
        /// its own quarter -- so three quarters of the work is thrown away four times over.
        /// </summary>
        public static void Disc(float cx, float cy, float radius, Color c)
        {
            if (radius <= 0f || c.A <= 0) return;

            var r2 = radius * radius;

            // Twice the row height of a wedge. This is solid fill with opaque text on top of it,
            // so the extra resolution was being spent somewhere nobody was ever going to look.
            // Same pixel grid as the wedges. A hub drawn on its own phase banded exactly the
            // way they did, which is why the centre of the wheel had stripes across it too.
            var step = RowPixels * 2 / (float)ScreenHeight;

            for (var dy = -radius; dy <= radius; dy += step)
            {
                var dy2 = dy * dy;
                if (dy2 > r2) continue;

                var half = (float)Math.Sqrt(r2 - dy2);
                if (half <= 0f) continue;

                Rect(cx, cy - dy, ToX(half * 2f), step * 1.02f, c);
            }
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

            // Never inherit somebody else's wrap region.
            Function.Call(Hash.SET_TEXT_JUSTIFICATION, centre ? 0 : 1);
            Function.Call(Hash.SET_TEXT_WRAP, 0f, 1f);

            if (shadow) Function.Call(Hash.SET_TEXT_DROP_SHADOW);
            if (outline) Function.Call(Hash.SET_TEXT_OUTLINE);

            Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, FormatFor(text));
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

            Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, FormatFor(text));
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
            Function.Call(Hash.BEGIN_TEXT_COMMAND_GET_SCREEN_WIDTH_OF_DISPLAY_TEXT, FormatFor(text));
            AddLongString(text);
            return Function.Call<float>(Hash.END_TEXT_COMMAND_GET_SCREEN_WIDTH_OF_DISPLAY_TEXT, true);
        }

        /// <summary>
        /// Format string to open a text command with.
        ///
        /// "STRING" honours exactly ONE substring component, so anything pushed after the first
        /// 96-character chunk is thrown away without a word -- which silently cut every line of
        /// dialogue longer than that, and made the wrap measure the truncated version and decide
        /// it all fitted on one line. "CELL_EMAIL_BCON" is the game's own multi-component
        /// format and concatenates every chunk, which is what the chunking assumed all along.
        /// </summary>
        private const int ChunkSize = 96;

        public static string FormatFor(string text)
        {
            return text != null && text.Length > ChunkSize ? "CELL_EMAIL_BCON" : "STRING";
        }

        /// <summary>
        /// ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME truncates past 99 bytes, so long strings
        /// are pushed as consecutive components.
        /// </summary>
        private static void AddLongString(string text)
        {
            for (var i = 0; i < text.Length; i += ChunkSize)
            {
                var len = Math.Min(ChunkSize, text.Length - i);
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
