using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using Hoodrich.Core;
using Hoodrich.Social;
using Hud = Hoodrich.UI.Draw;

namespace Hoodrich.UI
{
    /// <summary>
    /// Tweets down the right-hand side, drawn rather than posted.
    ///
    /// The feed used to go through the game's own notification system, which is anchored to the
    /// top left and cannot be moved -- so every post landed in the same stack as "you were
    /// busted", "the plug is on his way" and everything else the mod has to say. Two unrelated
    /// kinds of message in one column, and the important one buried under chatter.
    ///
    /// Drawing them puts them where a phone puts them, and it gets back the thing the native
    /// feed could never do: the author's own avatar. The native version had to use a phone
    /// contact picture or a blank card, because a made-up Balla wearing a stock photograph of a
    /// middle-aged man is the most obviously wrong thing this whole system can produce. Here
    /// they get the coloured disc and the initial, the same as on the feed screen itself.
    ///
    /// Everything else -- busts, deliveries, money, warnings -- carries on going out the native
    /// way on the left, untouched, which is the entire point of the split.
    ///
    /// If it looks wrong in motion, Hoodrich.ini has TweetsOnTheRight=false and it all goes back
    /// through the notification system exactly as it was.
    /// </summary>
    internal sealed class TweetToast
    {
        /// <summary>Right edge of the card, just inside the safe area.</summary>
        private const float Right = 0.988f;

        /// <summary>Below the top of the screen, clear of anything the game puts up there.</summary>
        private const float Top = 0.070f;

        /// <summary>Width in normalized-X. Narrow enough to read as a phone, not a panel.</summary>
        private const float Width = 0.208f;

        private const float Pad = 0.0085f;
        private const float AvatarSize = 0.030f;
        private const float Gap = 0.007f;

        /// <summary>
        /// The name in the sign-painter script, the handle underneath it in Chalet London.
        ///
        /// The same pair every other heading in this mod uses -- THE KITCHEN, POST UP, SOCIALS
        /// -- so a tweet arriving looks like part of the same thing rather than a notification
        /// somebody bolted on. The script needs the room, so the handle goes on its own line
        /// under it instead of trailing after it the way it does on the feed screen, where
        /// there is width to spare.
        /// </summary>
        private const float NameScale = 0.42f;
        private const float HandleScale = 0.225f;
        private const float BodyScale = 0.275f;

        private const float NameHeight = 0.0225f;
        private const float HandleHeight = 0.0145f;

        /// <summary>Hairline under the header, and the air either side of it.</summary>
        private const float RuleGap = 0.0055f;

        private const float LineHeight = 0.0162f;

        /// <summary>Three at once. A fourth waits rather than pushing one off mid-sentence.</summary>
        private const int MostAtOnce = 3;

        private const int LifeMs = 8200;
        private const int FadeInMs = 220;
        private const int FadeOutMs = 520;

        /// <summary>How far it slides in from, so it arrives rather than blinking on.</summary>
        private const float SlideIn = 0.022f;

        /// <summary>Anything longer than this gets cut. A toast is a glance, not a read.</summary>
        private const int MostLines = 3;

        private sealed class Card
        {
            public Author By;
            public string Handle = "";

            /// <summary>The game's own contact picture for the named cast, or empty.</summary>
            public string Pic = "";
            public List<string> Lines = new List<string>();
            public Color Tint;
            public int ShownAt;
            public float Height;
        }

        private readonly List<Card> _live = new List<Card>();
        private readonly Queue<Post> _waiting = new Queue<Post>();

        /// <summary>Set by Main: false puts everything back through the native feed.</summary>
        public bool Enabled = true;

        /// <summary>Set by Main: true while a full-screen UI owns the display.</summary>
        public Func<bool> Hidden;

        /// <summary>
        /// A post arrives. It goes up now if there is room, and waits its turn if not.
        /// </summary>
        public void Show(Post post)
        {
            if (post == null || post.By == null) return;

            if (_live.Count >= MostAtOnce)
            {
                // A cap on the queue as well. During a raid the feed can produce faster than
                // this can show them, and a backlog that outlives the fight would still be
                // narrating it four minutes later.
                if (_waiting.Count < 6) _waiting.Enqueue(post);
                return;
            }

            Put(post);
        }

        private void Put(Post post)
        {
            try
            {
                var card = new Card
                {
                    By = post.By,
                    Handle = post.By.Handle,
                    Pic = post.By.Pic ?? "",
                    Tint = post.By.Tint,
                    ShownAt = Game.GameTime,
                };

                // The body runs the full width of the card, under the header rather than
                // beside the avatar. Three lines squeezed into the column left over next to a
                // disc is a column about nine words wide.
                var textWidth = Width - Pad * 2f;
                card.Lines = Wrap(post.Body, textWidth, BodyScale);

                while (card.Lines.Count > MostLines) card.Lines.RemoveAt(card.Lines.Count - 1);

                var header = Math.Max(AvatarSize, NameHeight + HandleHeight);
                var body = card.Lines.Count * LineHeight;

                card.Height = Pad * 2f + header + RuleGap * 2f + body;

                _live.Add(card);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not build a tweet card: " + ex.Message);
            }
        }

        /// <summary>Called every frame. Ages the stack out and draws what is left.</summary>
        public void Draw()
        {
            if (!Enabled) return;

            var now = Game.GameTime;

            for (var i = _live.Count - 1; i >= 0; i--)
            {
                if (now - _live[i].ShownAt < LifeMs) continue;
                _live.RemoveAt(i);
            }

            // Room freed by one ageing out is filled straight away, so a backlog drains at the
            // rate they can actually be read rather than all at once.
            while (_live.Count < MostAtOnce && _waiting.Count > 0) Put(_waiting.Dequeue());

            if (_live.Count == 0) return;

            // Out of the way of anything that owns the screen. They are still queued and still
            // ageing, so nothing is lost -- they are just not drawn over a menu.
            if (Hidden != null && Hidden()) return;

            var y = Top;

            foreach (var card in _live)
            {
                DrawCard(card, y, now);
                y += card.Height + 0.006f;
            }
        }

        private void DrawCard(Card card, float y, int now)
        {
            var age = now - card.ShownAt;

            // In quickly, out slowly. A notification that snaps off is a notification you were
            // still reading.
            var fade = 1f;
            if (age < FadeInMs) fade = age / (float)FadeInMs;
            else if (age > LifeMs - FadeOutMs) fade = (LifeMs - age) / (float)FadeOutMs;

            if (fade <= 0.02f) return;
            if (fade > 1f) fade = 1f;

            // Eased, so the slide has some weight to it rather than moving at a constant rate.
            var slide = SlideIn * (1f - fade) * (1f - fade);
            var left = Right - Width + slide;

            var solid = (int)(255 * fade);

            Hud.RectFrom(left, y, Width, card.Height, Color.FromArgb((int)(236 * fade), 13, 14, 16));

            // A thin lift along the top edge, and the author's colour down the left. The colour
            // belongs on the edge on a card this small -- anywhere else and a purple card and a
            // green card stop reading as the same object.
            Hud.RectFrom(left, y, Width, 0.0012f, Color.FromArgb((int)(26 * fade), 255, 255, 255));
            Hud.RectFrom(left, y, 0.0024f, card.Height, Alpha(card.Tint, solid));

            var cx = left + Pad + Hud.ToX(AvatarSize) * 0.5f;
            var cy = y + Pad + AvatarSize * 0.5f;

            // A photograph for the forty people the game actually has a face for, and a
            // monogram for everybody else.
            //
            // Moving off the native feed quietly lost the photographs -- it could pass a
            // contact picture straight to the notification, and drawing the card ourselves
            // meant every single author, Michael and Trevor and Lamar included, came out as a
            // coloured letter. Which is the wrong way round: the whole reason the invented
            // names get a monogram is so that the real ones can have their real face.
            //
            // Falls back to the monogram while the dictionary streams, so the card never has a
            // hole in it waiting for a texture.
            var drew = false;

            if (!string.IsNullOrEmpty(card.Pic) && Hud.EnsureTextureDict(card.Pic))
            {
                Hud.Sprite(card.Pic, card.Pic, cx, cy, Hud.ToX(AvatarSize), AvatarSize, 0f,
                           Color.FromArgb(solid, 255, 255, 255));

                drew = true;
            }

            if (!drew)
            {
                // A square, not a disc. The width is converted through ToX so it comes out
                // square on the screen rather than square in the coordinate system -- 0.03 by
                // 0.03 in normalised space is a landscape oblong on any monitor wider than it
                // is tall, which is all of them.
                Hud.RectFrom(left + Pad, y + Pad, Hud.ToX(AvatarSize), AvatarSize,
                             Alpha(card.Tint, solid));

                Hud.Text(card.By.Initial, cx, cy - 0.0112f, 0.38f,
                         Color.FromArgb((int)(240 * fade), 250, 250, 248), Hud.FontChaletLondon);
            }

            var textX = left + Pad + Hud.ToX(AvatarSize) + Gap;
            var line = y + Pad - 0.005f;

            // The name, in the sign-painter script.
            Hud.Text(card.By.Name, textX, line, NameScale,
                     Alpha(Palette.Text, solid), Hud.FontCursive, centre: false);

            line += NameHeight;

            // The handle under it, quieter, in the plain face.
            if (card.By.Verified)
            {
                Hud.Disc(textX + 0.0035f, line + 0.0068f, 0.0033f, Alpha(Palette.Accent, solid));
                textX += 0.011f;
            }

            Hud.Text(card.Handle, textX, line, HandleScale,
                     Alpha(Palette.TextDim, solid), Hud.FontChaletLondon, centre: false);

            // A hairline the full width of the card, which is what makes it read as a card with
            // a header rather than four pieces of text at different sizes.
            var rule = y + Pad + Math.Max(AvatarSize, NameHeight + HandleHeight) + RuleGap;

            Hud.RectFrom(left + Pad, rule, Width - Pad * 2f, 0.0011f,
                         Color.FromArgb((int)(48 * fade), 255, 255, 255));

            line = rule + RuleGap;

            foreach (var text in card.Lines)
            {
                Hud.Text(text, left + Pad, line, BodyScale,
                         Alpha(Palette.Text, (int)(242 * fade)), Hud.FontBody, centre: false);

                line += LineHeight;
            }
        }

        private static Color Alpha(Color c, int a)
        {
            if (a < 0) a = 0;
            if (a > 255) a = 255;
            return Color.FromArgb(a, c.R, c.G, c.B);
        }

        /// <summary>
        /// Breaks a line to the card width.
        ///
        /// Measured through the game rather than counted in characters, because a proportional
        /// font makes "illicit" and "WWWWWWW" the same number of letters and nothing like the
        /// same width. One native call per word, on a card that is built once when it arrives
        /// rather than every frame.
        /// </summary>
        private static List<string> Wrap(string text, float width, float scale)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text)) return lines;

            var current = "";

            foreach (var word in text.Split(' '))
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

        /// <summary>Clears everything, for a wipe or a teardown.</summary>
        public void Clear()
        {
            _live.Clear();
            _waiting.Clear();
        }
    }
}
