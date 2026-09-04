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
        /// <summary>
        /// How many may be up at once when the block is quiet, and when it is not.
        ///
        /// ONE, NORMALLY. Three was the cap for everything, so the feed reached for three
        /// whenever it had three -- and it usually did, because most events queue a couple of
        /// reactions behind them. A permanent stack of three in the corner is not a feed, it is
        /// a wall: nothing on it is new, nothing is worth looking at, and the one post that
        /// actually mattered arrived in the middle of two that did not.
        ///
        /// A single card is READ. It goes up, you look at it, it goes. The others are not lost
        /// -- they queue and follow, one after another, which is what a phone does anyway.
        ///
        /// THREE WHEN SOMETHING IS ACTUALLY HAPPENING. A gang war or a takeover is the one time
        /// the block genuinely is all talking at once, and the stack reading as noisy is then
        /// the correct impression rather than a fault. See Loud.
        /// </summary>
        private const int Quietly = 1;
        private const int WhenLoud = 3;

        /// <summary>And two when there is genuinely a queue. See Room.</summary>
        private const int WhenBusy = 2;

        /// <summary>Set by Main: true while a war or a takeover is on. See Room.</summary>
        public Func<bool> Loud;

        /// <summary>How many cards may be up right now.</summary>
        private int Room()
        {
            try
            {
                if (Loud != null && Loud()) return WhenLoud;

                // TWO WHEN THERE IS ACTUALLY A QUEUE, and that is what makes it occasional
                // rather than random. A second card appears because two things really did
                // happen close together, not because a dice roll said so -- so the pace of the
                // feed matches the pace of the block, and a quiet stretch still shows one at a
                // time. Nothing has to decide how often "occasionally" is.
                return _waiting.Count >= WhenBusy ? WhenBusy : Quietly;
            }
            catch
            {
                return Quietly;
            }
        }

        /// <summary>
        /// How long a card stays up. Down from 8.2 seconds.
        ///
        /// Long enough to read a tweet twice was the old length, and reading it once is the
        /// job. Shorter also means the queue behind it drains faster, which is most of what
        /// makes the feed feel quick.
        /// </summary>
        private const int LifeMs = 6600;
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

            /// <summary>
            /// The texture this card settled on, once it has one. Never unset. See DrawCard.
            ///
            /// THIS IS WHAT STOPS THE FLICKER. The avatar had three ways of being drawn -- the
            /// contact picture, a made face, and a coloured monogram -- and it chose between
            /// them EVERY FRAME on whether the texture happened to be loaded at that instant.
            /// A dictionary that streams out for a moment, or a face registered a beat later
            /// than the card, therefore did not appear late: it appeared, vanished, came back.
            /// That is a picture blinking in the corner of the screen.
            ///
            /// Latched, and it only ever goes one way. A card starts on the monogram and moves
            /// to a real image the first time one is there. It never moves back, whatever the
            /// streamer does afterwards.
            /// </summary>
            public string Art = "";
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

            if (_live.Count >= Room())
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
            // Room can SHRINK -- a war ends and the cap drops from three to one -- and
            // nothing is yanked off the screen when it does. The ones already up finish their
            // eight seconds and are simply not replaced until there is room again, which is a
            // stack thinning out rather than two cards vanishing mid-sentence.
            while (_live.Count < Room() && _waiting.Count > 0) Put(_waiting.Dequeue());

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
            Hud.RectFrom(left, y, Width, 0.0012f, Alpha(Palette.Gold, (int)(44 * fade)));
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
            // WHICH PICTURE, DECIDED ONCE. See Card.Art. Only asked while it has not settled
            // on one, so nothing here can take an image back off a card that already had it.
            if (string.IsNullOrEmpty(card.Art)
                && !string.IsNullOrEmpty(card.Pic)
                && Hud.EnsureTextureDict(card.Pic))
            {
                card.Art = card.Pic;
            }

            // THE SAME MADE FACE THE FEED USES. The toast is the half of this screen most
            // people see most of the time -- it is on screen without anybody opening
            // anything -- and it was the half still drawing a coloured letter, which is why
            // the faces looked like they were not working at all.
            // Same rule as the feed: a business wears its own logo or nothing at all.
            if (string.IsNullOrEmpty(card.Art) && card.By != null && !card.By.IsOrg)
            {
                var made = Headshots.Txd(card.By.Handle);

                if (string.IsNullOrEmpty(made)) Headshots.Want(card.By.Handle, card.By.Gang, card.By.Gender);
                else card.Art = made;
            }

            // Asked for every frame it is drawn, which is also what keeps it: the face store
            // evicts whichever has gone longest without being asked for, and the dictionary
            // table works the same way. A picture on screen is never the one thrown away.
            if (!string.IsNullOrEmpty(card.Art))
            {
                if (card.Art == card.Pic)
                {
                    Hud.EnsureTextureDict(card.Art);
                }
                else if (string.IsNullOrEmpty(Headshots.Txd(card.By.Handle)))
                {
                    // GONE, SO ASK FOR IT BACK. Asking is also what keeps a face alive -- the
                    // store evicts whichever has gone longest without being asked for -- but a
                    // face that has ALREADY been thrown away is not protected by asking about
                    // it, it is just absent. Without this the card sat on its coloured square
                    // for the rest of its life, or worse, flickered as something else happened
                    // to ask for the same person.
                    Headshots.Want(card.By.Handle, card.By.Gang, card.By.Gender);
                }
            }

            // THE SQUARE GOES UNDERNEATH. THE LETTER DOES NOT.
            //
            // Putting the monogram behind the picture rather than instead of it is what stops a
            // frame where the image is not ready showing a coloured square where a face had
            // been -- it is simply what is behind the face, and a missed frame shows the same
            // square that was there before the face arrived rather than a hole.
            //
            // That works for the SQUARE because it is a rectangle and a sprite draws over a
            // rectangle whatever order the two are issued in. IT DOES NOT WORK FOR THE LETTER,
            // and the difference cost a screenshot of a T sat across somebody's face: text is
            // drawn in a later pass than sprites, so a monogram letter issued first still comes
            // out in front of the picture issued after it.
            //
            // So the letter is the one thing here that stays conditional. It is the fallback
            // and it is only drawn when there is nothing to fall back FROM.
            {
                // A square, not a disc. The width is converted through ToX so it comes out
                // square on the screen rather than square in the coordinate system -- 0.03 by
                // 0.03 in normalised space is a landscape oblong on any monitor wider than it
                // is tall, which is all of them.
                Hud.RectFrom(left + Pad, y + Pad, Hud.ToX(AvatarSize), AvatarSize,
                             Alpha(card.Tint, solid));

                if (string.IsNullOrEmpty(card.Art))
                {
                    Hud.Text(card.By.Initial, cx, cy - 0.0112f, 0.38f,
                             Color.FromArgb((int)(240 * fade), 250, 250, 248), Hud.FontChaletLondon);
                }
            }

            // And the face over the top of it, once there is one.
            if (!string.IsNullOrEmpty(card.Art))
            {
                Hud.Sprite(card.Art, card.Art, cx, cy, Hud.ToX(AvatarSize), AvatarSize, 0f,
                           Color.FromArgb(solid, 255, 255, 255));
            }

            var textX = left + Pad + Hud.ToX(AvatarSize) + Gap;
            var line = y + Pad - 0.005f;

            // The name, in the sign-painter script.
            Hud.Text(card.By.Name, textX, line, NameScale,
                     Alpha(Palette.Text, solid), Hud.FontCursive, centre: false);

            // AND THE TICK BESIDE IT, which is where a tick means what a tick means: it is
            // about the account, and the account is the name. Sat down on the handle line it
            // read as a mark on the @, and it moved about depending on how long the name was.
            //
            // ONLY FOR THE REAL ONES. The condition used to be "verified OR has a picture",
            // which was true when a picture meant one of the game's own contact photos and
            // therefore a story character. Everybody has a made face now, so that clause made
            // it everybody -- and a badge everybody has is not a badge, it is decoration. Four
            // accounts on this feed are flagged and those are the four that get it.
            if (card.By.Verified)
            {
                var nameW = 0.05f;

                try { nameW = Hud.MeasureText(card.By.Name, NameScale, Hud.FontCursive); }
                catch { /* the estimate will do */ }

                const float th = 0.012f;

                // Lifted to sit against the CAP of the name rather than its centre. The script
                // face has a tall ascender and a badge hung off its middle looks dropped.
                var bx = textX + nameW + Hud.ToX(th) * 0.5f + 0.004f;
                var by = line + 0.0052f;

                Hud.Disc(bx, by, 0.0055f, Alpha(Palette.Verified, solid));

                if (!Hud.File("tick.png", bx, by, th * 0.72f, 0f,
                              Alpha(Color.FromArgb(255, 18, 20, 22), solid)))
                {
                    Hud.Disc(bx, by, 0.0022f, Alpha(Color.FromArgb(255, 18, 20, 22), solid));
                }
            }

            line += NameHeight;

            // The handle under it, quieter, in the plain face. No badge on this line any
            // more -- it belongs to the name above it.
            Hud.Text(card.Handle, textX, line, HandleScale,
                     Alpha(Palette.TextDim, solid), Hud.FontChaletLondon, centre: false);

            // A hairline the full width of the card, which is what makes it read as a card with
            // a header rather than four pieces of text at different sizes.
            var rule = y + Pad + Math.Max(AvatarSize, NameHeight + HandleHeight) + RuleGap;

            // The same rule every panel draws under a heading: the hairline, with a short ember
            // stroke at its left end.
            Hud.RectFrom(left + Pad, rule, Width - Pad * 2f, 0.0011f,
                         Color.FromArgb((int)(48 * fade), 255, 255, 255));
            Hud.RectFrom(left + Pad, rule - 0.0003f, (Width - Pad * 2f) * 0.14f, 0.0017f,
                         Alpha(Palette.Ember, (int)(205 * fade)));

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
