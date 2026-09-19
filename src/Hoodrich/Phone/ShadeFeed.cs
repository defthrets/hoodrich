﻿using System;
using System.Collections.Generic;
using GTA;
using Hoodrich.Social;

namespace Hoodrich.Phone
{
    /// <summary>
    /// The feed's last few posts, held for the phone's notification shade.
    ///
    /// THE FEED IN THE PHONE, AND ONLY WHILE THE PHONE IS UP. The cards down the right are
    /// the feed narrating the block at you while you play; some people want the block to
    /// talk only when they take the phone out. So with Settings.FeedOnPhone a post comes
    /// here instead of to the card or the game's notifications, and the shade at the top of
    /// the home screen -- the same line that says a text is waiting -- shows it, smaller,
    /// with the poster's face and the first line of what they said.
    ///
    /// HELD FOR A FEW SECONDS OF PHONE-UP TIME, NOT OF CLOCK TIME. A post that arrives while
    /// the handset is in his pocket waits for it to come out, the way a notification does,
    /// and is then up for about as long as a card on the right would have been. But not for
    /// ever: anything older than a minute is gone whether or not it was seen, because the
    /// shade is what is happening now and a minute-old post is what the timeline is for.
    ///
    /// THREE AT MOST, newest kept. A raid produces posts faster than a shade can turn, and a
    /// backlog that outlived the fight would still be narrating it four minutes later.
    ///
    /// THE SAME Alert HANDED BACK ON EVERY ASK, rather than a fresh one built each time the
    /// shade rebuilds its list, so the picture the line settled on stays settled. See
    /// Alert.Art.
    /// </summary>
    internal sealed class ShadeFeed
    {
        private const int Most = 3;

        /// <summary>How long a post is up for, counted only while the shade is asking.</summary>
        private const int UpMs = 7000;

        /// <summary>And gone regardless this long after it arrived.</summary>
        private const int StaleMs = 60000;

        /// <summary>Asks further apart than this are the phone having been away; the gap is not counted.</summary>
        private const int AwayMs = 4000;

        private sealed class Held
        {
            public Alert Line;
            public int ArrivedAt;
            public int UpFor;
            public int LastAsk;
        }

        private readonly List<Held> _held = new List<Held>();

        /// <summary>A post arrives. It waits here for the phone.</summary>
        public void Show(Post post)
        {
            if (post == null || post.By == null) return;

            var line = new Alert
            {
                By = post.By,
                Text = post.By.Name + "  " + post.By.Handle,
                Sub = post.Body ?? "",
            };

            _held.Add(new Held { Line = line, ArrivedAt = Game.GameTime });

            while (_held.Count > Most) _held.RemoveAt(0);
        }

        /// <summary>
        /// Adds the lines still worth showing, oldest first, and moves each one's clock on.
        /// Called by Main's shade builder, which the phone asks only while it is up and on
        /// its home screen -- so time only passes here while the shade can be seen.
        /// </summary>
        public void Into(List<Alert> list)
        {
            var now = Game.GameTime;

            for (var i = _held.Count - 1; i >= 0; i--)
            {
                var h = _held[i];

                if (h.LastAsk != 0 && now - h.LastAsk < AwayMs) h.UpFor += now - h.LastAsk;
                h.LastAsk = now;

                if (h.UpFor >= UpMs || now - h.ArrivedAt > StaleMs)
                {
                    _held.RemoveAt(i);
                }
            }

            foreach (var h in _held) list.Add(h.Line);
        }

        /// <summary>Nothing waiting. For the feed being switched off, and for a wipe.</summary>
        public void Clear()
        {
            _held.Clear();
        }
    }
}
