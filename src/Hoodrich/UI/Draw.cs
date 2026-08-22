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

        // ---- our own art ------------------------------------------------------

        /// <summary>
        /// A square icon loaded from a PNG on disk, centred on x,y and sized by screen height.
        ///
        /// This exists because the game does not have every symbol. A skull is the plain case:
        /// "skull" returns nothing across every texture dictionary in every dump, and the four
        /// names tried in the reputation bar all fell through -- the log has it landing on
        /// shop_franklin_icon_a, which is Franklin's face. There is no police badge either.
        /// Guessing at more names is guessing at art that was never shipped.
        ///
        /// CustomSprite hands the file to ScriptHookV's texture loader, so the art can simply be
        /// drawn and put in data\icons. White art on transparent, tinted by the colour argument,
        /// exactly like a stock sprite.
        ///
        /// ScaledDraw, NOT Draw, and that is the difference between this working and not.
        /// CustomSprite.Draw hands InternalDraw a hardcoded 1280 by 720 -- it is not a pixel
        /// space at all, it is a fixed 16:9 grid, so on any other aspect it puts art in the
        /// wrong place and stretches it. ScaledDraw passes Screen.ScaledWidth by 720 instead,
        /// which is aspect-corrected, and in that space equal width and height IS square on
        /// screen. Checked against the assembly rather than assumed.
        /// </summary>
        public static bool File(string file, float x, float y, float heightFraction,
                                float rotationDeg, Color c)
        {
            return File(file, x, y, ToX(heightFraction), heightFraction, rotationDeg, c);
        }

        /// <summary>
        /// The same, for art that is not square.
        ///
        /// Every icon in the set is, so File forced a square and that was right for all of
        /// them. A wordmark is five times wider than it is tall, and squeezed into a square it
        /// is unreadable -- so the width is its own argument, in X units, and the square
        /// version above is now the special case rather than the only one.
        /// </summary>
        public static bool File(string file, float x, float y, float widthFraction,
                                float heightFraction, float rotationDeg, Color c)
        {
            var sprite = Load(file);
            if (sprite == null) return false;

            try
            {
                var wide = widthFraction * GTA.UI.Screen.ScaledWidth;
                var tall = heightFraction * ScaledHeight;
                if (wide < 1f || tall < 1f) return false;

                sprite.Size = new SizeF(wide, tall);
                sprite.Position = new PointF(x * GTA.UI.Screen.ScaledWidth, y * ScaledHeight);
                sprite.Color = c;
                sprite.Rotation = rotationDeg;
                sprite.ScaledDraw();
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Icon '" + file + "' would not draw: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// The mod's own name, as art.
        ///
        /// Drawn rather than typed because it is a wordmark: an arched varsity block that no
        /// font on the machine can set, so it is a PNG and the colour goes on here.
        ///
        /// Placed by its LEFT edge and vertical middle, because every header it sits in is
        /// built left to right and a centre-anchored mark would have to be positioned by
        /// working backwards from its own width at every call site.
        /// </summary>
        public static void Brand(float left, float middle, float height, Color c)
        {
            var wide = ToX(height) * WordmarkAspect;
            File("logo.png", left + wide * 0.5f, middle, wide, height, 0f, c);
        }

        /// <summary>
        /// How many times wider than tall logo.png is. Printed by tools/make_logo.py.
        ///
        /// A constant rather than something read off the texture, because CustomSprite does not
        /// expose the source size -- and a wordmark whose proportions are guessed is a wordmark
        /// that is subtly squashed on every screen it appears on.
        /// </summary>
        public const float WordmarkAspect = 5.2513f;

        /// <summary>
        /// The sprite for a file, made once.
        ///
        /// Cached because constructing one loads the texture, and a texture loaded every frame
        /// is a handle leaked every frame. A miss is cached too -- as a null -- so a file that
        /// is not there costs one look at the disk rather than one per frame forever.
        /// </summary>
        private static GTA.UI.CustomSprite Load(string file)
        {
            GTA.UI.CustomSprite found;
            if (Icons.TryGetValue(file, out found)) return found;

            Icons[file] = null;

            try
            {
                var path = System.IO.Path.Combine(System.IO.Path.Combine(Core.Paths.Data, "icons"), file);

                if (!System.IO.File.Exists(path))
                {
                    Log.Info("No icon file at " + path + "; falling back.");
                    return null;
                }

                found = new GTA.UI.CustomSprite(path, new SizeF(32f, 32f), new PointF(0f, 0f),
                                                Color.White, 0f, true);

                Icons[file] = found;
                Log.Info("Icon file loaded: " + file + ".");
                return found;
            }
            catch (Exception ex)
            {
                Log.Info("Icon file '" + file + "' would not load: " + ex.Message);
                return null;
            }
        }

        private static readonly Dictionary<string, GTA.UI.CustomSprite> Icons =
            new Dictionary<string, GTA.UI.CustomSprite>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The height of the space ScaledDraw draws into. Fixed, and not the screen's.
        ///
        /// GTA.UI.Screen.Height is this same 720 -- a constant in the assembly, not a
        /// resolution. Named here so the maths above reads as deliberate rather than as
        /// somebody having typed a magic number.
        /// </summary>
        private const float ScaledHeight = 720f;

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
        /// Two pixels, and it fits now that a sector only scans the rows it can reach. Every row is a DRAW_RECT and the game
        /// quietly stops drawing them once a frame has asked for too many; it stops on whatever
        /// was asked for LAST, so the final segments came out as missing chunks and detached
        /// blobs. That is the broken wheel, and it was never a geometry bug.
        ///
        /// Three pixels was a stopgap while every segment scanned the whole disc. Two is
        /// noticeably smoother on the diagonals and still tiles perfectly, because whole pixels
        /// are whole pixels.
        /// </summary>
        /// <summary>
        /// How tall one scanline row of a filled shape is, in device pixels.
        ///
        /// SCALED WITH THE SCREEN, and that is the whole point. It was a flat 2, which meant
        /// the cost of every wedge and disc grew with resolution -- and GTA silently stops
        /// drawing once a script has issued too many DRAW_RECTs in a frame. Measured on the
        /// redesigned wheel: 954 rectangles at 1080p and 1,382 at 1440p with eight items,
        /// against roughly 593 for the wheel it replaced.
        ///
        /// What that looks like is not a warning. It is the LAST things drawn quietly going
        /// missing -- the fifth wedge cut off halfway down its own scan, and the hub discs
        /// after it never appearing at all, while every icon and label still drew because
        /// sprites and text do not come out of the same budget.
        ///
        /// Dividing by 270 keeps the row count per ring constant instead: three rows at 720p,
        /// four at 1080p, five at 1440p, eight at 2160p, and the whole wheel lands between 423
        /// and 555 rectangles at every one of them.
        /// </summary>
        private static int RowPixels
        {
            get
            {
                var n = (int)Math.Round(ScreenHeight / 270f);
                return n < 2 ? 2 : n;
            }
        }

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

            // Only the rows the sector can actually reach, walked in whole device pixels.
            //
            // Two things matter here. The first is the band: a 72 degree sector that scanned
            // the full height of the disc would throw away two rows in three. An earlier
            // version tried this saving and got it wrong, taking the extremes from the two
            // boundary rays alone -- which only holds for a sector that does not cross an
            // axis. One straddling straight-up reaches rOuter at the top whatever its rays
            // say, and chunks came out of wedges. Both cases are handled below.
            //
            // The second is that the iteration is over integer pixel rows, not over a float
            // stepped by a fraction. Row n covers exactly the pixels [n, n + RowPixels), so
            // consecutive rows tile: no gap for the background to show through, and no overlap
            // for a semi-transparent fill to blend twice at. Stepping a float by 0.0018 did
            // neither, and the stripes it left are the whole reason this is written this way.
            var topDy = Crosses(angFromDeg, angToDeg, 0f)
                ? rOuter
                : Math.Max(ReachTop(a0, rInner, rOuter), ReachTop(a1, rInner, rOuter));

            var bottomDy = Crosses(angFromDeg, angToDeg, 180f)
                ? -rOuter
                : Math.Min(ReachBottom(a0, rInner, rOuter), ReachBottom(a1, rInner, rOuter));

            var pxTop = (int)Math.Floor((cy - topDy) * ScreenHeight);
            var pxBottom = (int)Math.Ceiling((cy - bottomDy) * ScreenHeight);

            // Anchor the phase to the wheel's centre rather than to the top of the band, so a
            // hovered wedge that reaches further out still lands on the same pixel grid as its
            // neighbours. Otherwise growing one wedge shifts its rows half a pixel and draws a
            // seam down both of its edges.
            var pxCentre = (int)Math.Round(cy * ScreenHeight);
            var rows = RowPixels;
            pxTop -= ((pxTop - pxCentre) % rows + rows) % rows;

            var rowHeight = RowHeight;

            for (var py = pxTop; py < pxBottom; py += rows)
            {
                // The centre of this row, on the pixel grid.
                var rowY = (py + rows * 0.5f) / ScreenHeight;
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
        /// The HIGHEST point on a boundary ray, in the dy the scan measures.
        ///
        /// dy is r * cos(angle) and r runs from rInner to rOuter, so where cos is positive the
        /// highest point is at the outer edge, and where it is negative -- the ray pointing
        /// downward -- the highest point is the INNER one, because the annulus has a hole in
        /// the middle and the ray starts at the edge of it.
        /// </summary>
        private static float ReachTop(double angleRad, float rInner, float rOuter)
        {
            var c = (float)Math.Cos(angleRad);
            return c > 0f ? rOuter * c : rInner * c;
        }

        /// <summary>
        /// The LOWEST point on a boundary ray, which is the other way round.
        ///
        /// This is the one that was missing, and it is why a wedge in the lower half came out
        /// as a crescent. Both bounds were taken from the function above, so for a downward ray
        /// the bottom of the band was computed at the INNER radius -- cutting off every row
        /// between there and the outer edge, which is most of the wedge.
        /// </summary>
        private static float ReachBottom(double angleRad, float rInner, float rOuter)
        {
            var c = (float)Math.Cos(angleRad);
            return c < 0f ? rOuter * c : rInner * c;
        }

        /// <summary>Whether a sector spans a given bearing, wrapping properly at 360.</summary>
        private static bool Crosses(float fromDeg, float toDeg, float bearing)
        {
            var span = toDeg - fromDeg;
            var at = (((bearing - fromDeg) % 360f) + 360f) % 360f;

            return at <= span;
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
        /// Filled disc.
        ///
        /// Its own row scan rather than a full-circle wedge. Going through Wedge splits the
        /// circle into four sectors, each of which walks every row of the whole disc to fill
        /// its own quarter -- so three quarters of the work is thrown away four times over.
        /// </summary>
        /// <summary>
        /// A filled circle, as horizontal rows.
        ///
        /// Rewritten because the hub looked chewed round the edge, and there were two separate
        /// faults doing it -- both of them ones Wedge already documents and solves. This was
        /// written the naive way and never revisited.
        ///
        /// THE FIRST is that each band took its width at its own CENTRE, so both outer corners
        /// of every band stuck out past the true circle. The rim sprite drawn over the top
        /// follows the real curve, so what showed was a ring of dark notches poking through it
        /// -- the "dotted" edge. Measuring at the band's FAR edge instead keeps every band
        /// inside the circle, and the rim covers the hairline that leaves.
        ///
        /// THE SECOND is that dy was a float stepped by a fraction while each band was drawn
        /// 1.02x taller than its step, so bands OVERLAPPED. The fill is 88% opaque, so every
        /// overlap blended twice and left a horizontal seam -- stripes across the middle of the
        /// wheel. Walking whole device pixels makes rows tile exactly: no gap for the
        /// background to show through, no overlap to blend twice.
        ///
        /// The rows were also deliberately half resolution, on the grounds that nobody studies
        /// solid fill. True of the middle and false of the edge, which is the only part anybody
        /// sees. A row per pixel costs 268 rectangles for the wheel hub at 1080p, which is
        /// affordable now the wedges are sprites rather than five hundred rectangles.
        /// </summary>
        /// <param name="rowPx">Row height in device pixels. 0 picks one from the size.</param>
        public static void Disc(float cx, float cy, float radius, Color c, int rowPx = 0)
        {
            if (radius <= 0f || c.A <= 0) return;

            var r2 = radius * radius;

            // A rectangle per pixel row, unless the disc is big enough that that is silly.
            var rows = rowPx > 0
                ? rowPx
                : Math.Max(1, (int)Math.Ceiling(radius * 2f * ScreenHeight / 300f));

            var pxTop = (int)Math.Floor((cy - radius) * ScreenHeight);
            var pxBottom = (int)Math.Ceiling((cy + radius) * ScreenHeight);

            // Anchored to the centre for the same reason Wedge is: so a disc and anything drawn
            // concentric with it land on the same grid rather than half a row apart.
            var pxCentre = (int)Math.Round(cy * ScreenHeight);
            pxTop -= ((pxTop - pxCentre) % rows + rows) % rows;

            var rowHeight = rows / (float)ScreenHeight;

            for (var py = pxTop; py < pxBottom; py += rows)
            {
                var rowY = (py + rows * 0.5f) / ScreenHeight;

                // The row's far edge from the equator, not its centre. This is the whole
                // difference between a clean edge and a ring of notches.
                var edge = Math.Abs(cy - rowY) + rowHeight * 0.5f;
                var e2 = edge * edge;
                if (e2 >= r2) continue;

                var half = (float)Math.Sqrt(r2 - e2);
                if (half <= 0f) continue;

                Rect(cx, rowY, ToX(half * 2f), rowHeight, c);
            }
        }

        // ---- text --------------------------------------------------------------

        /// <summary>
        /// Breaks text over a fixed number of lines, each with its own width.
        ///
        /// Per-line widths because the thing being written into is a CIRCLE. The room on the
        /// second line of a hub caption is a shorter chord than on the first, so wrapping to a
        /// single width either wastes the wide line or overruns the narrow one.
        ///
        /// Anything still left after the last line is ellipsised there rather than dropped, so
        /// a caption that genuinely will not fit says so, instead of stopping mid-word with no
        /// sign that there was ever any more of it.
        /// </summary>
        public static string[] Wrap(string text, float scale, int font, params float[] widths)
        {
            if (string.IsNullOrEmpty(text) || widths == null || widths.Length == 0)
            {
                return new string[0];
            }

            var words = text.Split(' ');
            var lines = new List<string>();
            var i = 0;

            while (i < words.Length && lines.Count < widths.Length)
            {
                var max = widths[lines.Count];
                var line = "";

                while (i < words.Length)
                {
                    if (words[i].Length == 0) { i++; continue; }

                    var candidate = line.Length == 0 ? words[i] : line + " " + words[i];

                    // A word wider than the whole line still goes on it, or nothing is ever
                    // taken and the loop never ends.
                    if (line.Length > 0 && MeasureText(candidate, scale, font) > max) break;

                    line = candidate;
                    i++;
                }

                lines.Add(line);
            }

            if (i < words.Length && lines.Count > 0)
            {
                var rest = lines[lines.Count - 1];
                for (var j = i; j < words.Length; j++) rest += " " + words[j];

                lines[lines.Count - 1] = Fit(rest, widths[widths.Length - 1], scale, font);
            }

            return lines.ToArray();
        }

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
        /// Trims text until it fits a given width, with an ellipsis if anything was lost.
        ///
        /// The panel lays a label out from the left edge and a value from the right, and
        /// nothing was checking that the two did not meet. A gang with four rivals produced a
        /// value wide enough to run straight through its own label -- "Their old riva" with
        /// the list printed over the top of it, which is what the Families page was doing.
        ///
        /// Binary search rather than a character at a time: measuring is a native call, and a
        /// long string trimmed one letter per pass is fifty of them for one row.
        /// </summary>
        public static string Fit(string text, float maxWidth, float scale, int font)
        {
            if (string.IsNullOrEmpty(text) || maxWidth <= 0f) return text;
            if (MeasureText(text, scale, font) <= maxWidth) return text;

            const string Ellipsis = "...";

            var lo = 0;
            var hi = text.Length;

            while (lo < hi)
            {
                var mid = (lo + hi + 1) / 2;

                if (MeasureText(text.Substring(0, mid) + Ellipsis, scale, font) <= maxWidth)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            // Not even one character and an ellipsis fits, so there is nothing honest to show.
            if (lo <= 0) return "";

            return text.Substring(0, lo).TrimEnd(' ', ',') + Ellipsis;
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
