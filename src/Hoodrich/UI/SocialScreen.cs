using System;
using System.Collections.Generic;
using System.Drawing;
using Control = GTA.Control;
using GTA;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Social;
using Hud = Hoodrich.UI.Draw;

namespace Hoodrich.UI
{
    /// <summary>
    /// The feed.
    ///
    /// Built to be read rather than operated, which is a different job from every other screen
    /// in the mod. There is no value column and nothing lines up on the right, because a
    /// timeline is a column of paragraphs and forcing it into a label/value grid is what makes
    /// a feed look like a spreadsheet with avatars.
    ///
    /// Three faces do the separating: the display name in the standard HUD face at full weight,
    /// the handle and the timestamp in the condensed face at low contrast, and the body in the
    /// reading face. That is the whole visual system, and it is enough -- the thing that makes a
    /// real timeline legible is not decoration, it is that the eye can find the name, skip the
    /// handle, and land on the words.
    /// </summary>
    internal sealed class SocialScreen
    {
        private const float PanelWidth = 0.360f;
        private const float PanelTop = 0.070f;
        private const float PanelHeight = 0.860f;

        private const float Pad = 0.014f;
        private const float AvatarSize = 0.038f;
        private const float BodyScale = 0.315f;
        private const float LineHeight = 0.0248f;

        /// <summary>Gap under a post, before the next one's rule.</summary>
        private const float PostGap = 0.012f;

        private const int OpenGraceMs = 220;

        private readonly SocialFeed _feed;

        private readonly Dictionary<string, List<string>> _wrapped =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);

        private int _scroll;
        private int _openedAt;

        /// <summary>Franklin's actual head, borrowed from the game's own contact-photo system.</summary>
        private int _mugshot;
        private string _mugshotTxd = "";

        public SocialScreen(SocialFeed feed)
        {
            _feed = feed;
        }

        public bool IsOpen { get; private set; }

        public void Open()
        {
            IsOpen = true;
            _scroll = 0;
            _openedAt = Game.GameTime;

            RequestMugshot();
            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        public void Close()
        {
            IsOpen = false;
            ReleaseMugshot();
        }

        // ---- the profile picture -----------------------------------------------

        /// <summary>
        /// Asks the game for a headshot of the player.
        ///
        /// This is the same machinery the phone uses for contact photos, so it is a real render
        /// of the actual character -- his face, his haircut, whatever he is wearing right now.
        /// A drawn stand-in would have been easier and would have looked like a stand-in.
        /// </summary>
        private void RequestMugshot()
        {
            if (_mugshot != 0) return;

            try
            {
                var player = Game.Player.Character;
                if (player == null || !player.Exists()) return;

                _mugshot = Function.Call<int>(Hash.REGISTER_PEDHEADSHOT, player.Handle);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not ask for a headshot: " + ex.Message);
            }
        }

        private void ReleaseMugshot()
        {
            try
            {
                if (_mugshot != 0) Function.Call(Hash.UNREGISTER_PEDHEADSHOT, _mugshot);
            }
            catch { /* teardown */ }

            _mugshot = 0;
            _mugshotTxd = "";
        }

        private bool MugshotReady()
        {
            if (_mugshot == 0) return false;
            if (!string.IsNullOrEmpty(_mugshotTxd)) return true;

            try
            {
                if (!Function.Call<bool>(Hash.IS_PEDHEADSHOT_READY, _mugshot)) return false;
                if (!Function.Call<bool>(Hash.IS_PEDHEADSHOT_VALID, _mugshot)) return false;

                _mugshotTxd = Function.Call<string>(Hash.GET_PEDHEADSHOT_TXD_STRING, _mugshot);
                return !string.IsNullOrEmpty(_mugshotTxd);
            }
            catch
            {
                return false;
            }
        }

        // ---- input -------------------------------------------------------------

        public void Update()
        {
            if (!IsOpen) return;

            LockControls();

            if (Game.GameTime - _openedAt < OpenGraceMs) return;

            if (Pressed(Control.PhoneUp)) Scroll(-1);
            else if (Pressed(Control.PhoneDown)) Scroll(1);
            else if (Pressed(Control.PhoneCancel) || Pressed(Control.PhoneSelect))
            {
                Hud.PlaySound("BACK", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                Close();
            }
        }

        private void Scroll(int step)
        {
            var count = _feed.Timeline.Count;
            if (count == 0) return;

            _scroll = Math.Max(0, Math.Min(count - 1, _scroll + step));
            Hud.PlaySound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        private static bool Pressed(Control control)
        {
            return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, (int)control);
        }

        private static void LockControls()
        {
            Game.DisableControlThisFrame(Control.Attack);
            Game.DisableControlThisFrame(Control.Attack2);
            Game.DisableControlThisFrame(Control.Aim);
            Game.DisableControlThisFrame(Control.Jump);
            Game.DisableControlThisFrame(Control.Enter);
            Game.DisableControlThisFrame(Control.Phone);
            Game.DisableControlThisFrame(Control.SelectWeapon);
            Game.DisableControlThisFrame(Control.PhoneUp);
            Game.DisableControlThisFrame(Control.PhoneDown);
            Game.DisableControlThisFrame(Control.PhoneLeft);
            Game.DisableControlThisFrame(Control.PhoneRight);
            Game.DisableControlThisFrame(Control.PhoneSelect);
            Game.DisableControlThisFrame(Control.PhoneCancel);
        }

        // ---- drawing -----------------------------------------------------------

        public void Draw()
        {
            if (!IsOpen) return;

            var left = 0.5f - PanelWidth * 0.5f;

            // Ground, hairline, and a header that stays put while the timeline moves under it.
            Hud.RectFrom(left - 0.0016f, PanelTop - 0.0016f, PanelWidth + 0.0032f, PanelHeight + 0.0032f,
                         Color.FromArgb(160, 90, 96, 92));

            Hud.RectFrom(left, PanelTop, PanelWidth, PanelHeight, Color.FromArgb(242, 9, 10, 12));

            var y = DrawHeader(left);

            var bottom = PanelTop + PanelHeight - 0.030f;

            for (var i = _scroll; i < _feed.Timeline.Count; i++)
            {
                var post = _feed.Timeline[i];

                var height = PostHeight(post);
                if (y + height > bottom) break;

                DrawPost(left, y, post);
                y += height;
            }

            if (_feed.Timeline.Count == 0)
            {
                Hud.Text("Nothing yet. Go and do something.", left + Pad, PanelTop + 0.24f,
                         BodyScale, Palette.TextDim, Hud.FontBody, centre: false);
            }

            Hud.RectFrom(left, PanelTop + PanelHeight - 0.026f, PanelWidth, 0.0012f,
                         Color.FromArgb(60, 200, 205, 200));

            Hud.Text("UP / DOWN SCROLL      ENTER CLOSE",
                     left + Pad, PanelTop + PanelHeight - 0.019f, 0.24f,
                     Palette.TextDim, Hud.FontLabel, centre: false);
        }

        /// <summary>Your own card at the top: face, name, and the two numbers that move.</summary>
        private float DrawHeader(float left)
        {
            const float height = 0.108f;

            Hud.RectFrom(left, PanelTop, PanelWidth, height, Color.FromArgb(240, 18, 20, 22));
            Hud.RectFrom(left, PanelTop + height - 0.0022f, PanelWidth, 0.0022f, Palette.Accent);

            Hud.Text("SOCIALS", left + Pad, PanelTop + 0.006f, 0.62f, Palette.Text,
                     Hud.FontCursive, centre: false);

            var y = PanelTop + 0.044f;

            var avatar = 0.046f;
            var cx = left + Pad + Hud.ToX(avatar) * 0.5f;
            var cy = y + avatar * 0.5f;

            if (MugshotReady())
            {
                Hud.Sprite(_mugshotTxd, _mugshotTxd, cx, cy, Hud.ToX(avatar), avatar, 0f, Color.White);
            }
            else
            {
                // Until the render lands, a disc rather than a hole.
                Hud.Disc(cx, cy, avatar * 0.5f, Color.FromArgb(255, 58, 72, 60));
                Hud.Text("F", cx, cy - 0.016f, 0.60f, Palette.Text, Hud.FontChaletLondon);
            }

            var textX = left + Pad + Hud.ToX(avatar) + 0.010f;

            Hud.Text(_feed.DisplayName, textX, y - 0.002f, 0.38f, Palette.Text,
                     Hud.FontChaletLondon, centre: false);

            Hud.Text(_feed.Handle, textX, y + 0.022f, 0.28f, Palette.TextDim,
                     Hud.FontLabel, centre: false);

            // Followers first and in the money colour, because it is the number that moves and
            // the only one anybody actually looks at.
            var stats = left + PanelWidth - Pad;

            Hud.TextRight(_feed.Followers.ToString("N0"), stats, y - 0.002f, 0.38f,
                          Palette.Cash, Hud.FontChaletLondon);

            Hud.TextRight("FOLLOWERS  ·  " + _feed.Following.ToString("N0") + " FOLLOWING",
                          stats, y + 0.024f, 0.24f, Palette.TextDim, Hud.FontLabel);

            return PanelTop + height + 0.008f;
        }

        private void DrawPost(float left, float top, Post post)
        {
            var lines = Lines(post);

            // A rail down the left for anything about you, so it can be found while scrolling.
            if (post.AboutYou)
            {
                Hud.RectFrom(left + 0.002f, top, 0.0022f, PostHeight(post) - PostGap * 0.5f,
                             Palette.Accent);
            }

            var cx = left + Pad + Hud.ToX(AvatarSize) * 0.5f;
            var cy = top + 0.004f + AvatarSize * 0.5f;

            Hud.Disc(cx, cy, AvatarSize * 0.5f, post.By.Tint);

            Hud.Text(post.By.Initial, cx, cy - 0.0135f, 0.46f,
                     Color.FromArgb(235, 250, 250, 248), Hud.FontChaletLondon);

            var textX = left + Pad + Hud.ToX(AvatarSize) + 0.010f;
            var y = top + 0.002f;

            // Name, then handle and stamp trailing it in the quiet face. Measured so the handle
            // sits directly after the name whatever the name happens to be.
            Hud.Text(post.By.Name, textX, y, 0.34f, Palette.Text, Hud.FontChaletLondon, centre: false);

            var nameWidth = 0.06f;
            try { nameWidth = Hud.MeasureText(post.By.Name, 0.34f, Hud.FontChaletLondon); }
            catch { /* the estimate above will do */ }

            var tail = textX + nameWidth + 0.006f;

            if (post.By.Verified)
            {
                Hud.Disc(tail + 0.004f, y + 0.010f, 0.005f, Palette.Accent);
                tail += 0.014f;
            }

            Hud.Text(post.By.Handle + "  ·  " + SocialFeed.Ago(post.At), tail, y + 0.003f, 0.26f,
                     Palette.TextDim, Hud.FontLabel, centre: false);

            y += 0.023f;

            foreach (var line in lines)
            {
                Hud.Text(line, textX, y, BodyScale, Palette.Text, Hud.FontBody, centre: false);
                y += LineHeight;
            }

            y += 0.004f;

            // Engagement, spaced across rather than crammed together, in the quiet face.
            var metrics = post.Replies + "   REPLIES        " +
                          post.Reposts + "   REPOSTS        " +
                          post.Likes + "   LIKES";

            Hud.Text(metrics, textX, y, 0.235f, Color.FromArgb(150, 150, 158, 152),
                     Hud.FontLabel, centre: false);

            // Divider under the post, inset so it reads as a separator rather than a box edge.
            Hud.RectFrom(left + Pad, top + PostHeight(post) - PostGap * 0.6f,
                         PanelWidth - Pad * 2f, 0.0010f,
                         Color.FromArgb(40, 200, 205, 200));
        }

        private float PostHeight(Post post)
        {
            var lines = Lines(post).Count;

            var body = Math.Max(1, lines) * LineHeight;
            return 0.023f + body + 0.022f + PostGap;
        }

        /// <summary>
        /// Wrapped once and remembered.
        ///
        /// Wrapping measures text through the game, which is a native call per word per line --
        /// doing that for every visible post every frame is hundreds of calls a frame for text
        /// that never changes after it is written.
        /// </summary>
        private List<string> Lines(Post post)
        {
            List<string> cached;
            if (_wrapped.TryGetValue(post.Body, out cached)) return cached;

            var width = PanelWidth - Pad * 2f - Hud.ToX(AvatarSize) - 0.012f;
            var lines = Wrap(post.Body, width, BodyScale);

            // Bounded, because the cache lives as long as the session and a very long game
            // would otherwise keep every post ever written.
            if (_wrapped.Count > 400) _wrapped.Clear();

            _wrapped[post.Body] = lines;
            return lines;
        }

        private static List<string> Wrap(string text, float width, float scale)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text)) return lines;

            var words = text.Split(' ');
            var current = "";

            foreach (var word in words)
            {
                var candidate = current.Length == 0 ? word : current + " " + word;

                float measured;
                try { measured = Hud.MeasureText(candidate, scale, Hud.FontBody); }
                catch { measured = candidate.Length * scale * 0.011f; }

                if (measured <= width || current.Length == 0)
                {
                    current = candidate;
                    continue;
                }

                lines.Add(current);
                current = word;
            }

            if (current.Length > 0) lines.Add(current);
            return lines;
        }

        public void RestoreWorld() => ReleaseMugshot();
    }
}
