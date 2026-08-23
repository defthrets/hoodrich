using System;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Social
{
    /// <summary>
    /// The two stories the feed cannot be told about, watched for directly.
    ///
    /// Everything else the papers report reaches them through SocialEvent -- a mission ends, a
    /// war turns, somebody drops a cop, and the desk hears about it because the mod itself
    /// raised the event. That covers the mod's OWN work and nothing else, and a city desk that
    /// only reports the player's missions is not a city desk, it is a scoreboard.
    ///
    /// So this watches the street. A car going up outside a shop you are walking past is not
    /// any system's event and never will be; neither is two carloads of strangers shooting at
    /// each other over a parking space. Both are news, and both are things you can see out of
    /// the window while the feed says nothing.
    ///
    /// Deliberately shallow. It does not try to work out WHO, or why, or whether you had
    /// anything to do with it -- a newsroom that got the details right about a firefight it
    /// watched through a helicopter camera would be a worse newsroom. Something loud happened
    /// somewhere near you, and the paper writes it up as loud and near you.
    /// </summary>
    internal sealed class Newsroom
    {
        /// <summary>
        /// How far out a blast still counts as local.
        ///
        /// Generous, because an explosion is loud enough to be somebody's business three
        /// streets away and the whole point of the story is that a neighbourhood heard it.
        /// </summary>
        private const float BlastRange = 130f;

        /// <summary>
        /// And how far for gunfire, which is not.
        ///
        /// Tighter than the blast on purpose. This one is a head count of people actually
        /// firing, and the wider the circle the more likely it is counting a police chase on
        /// the freeway rather than a shootout on a block.
        /// </summary>
        private const float ShotRange = 80f;

        /// <summary>How often the peds get counted. The blast check is every frame; it is one
        /// native and misses the whole story if it is throttled past the bang.</summary>
        private const int CountMs = 900;

        /// <summary>
        /// How long a story stays covered, per kind.
        ///
        /// Not the same as the desk's own minute between anything and anything else. A firefight
        /// is a rolling thing that would otherwise file a fresh report every second and a half
        /// for as long as it lasts, which is what a wire service does and not what a paper does.
        /// </summary>
        private const int SameStoryMs = 120000;

        /// <summary>
        /// How many people have to be firing before it is a shootout.
        ///
        /// Two, and the player counts as one of them, because a man shooting at nobody is a man
        /// with a gun and a man shooting at somebody who is shooting back is a story. The count
        /// also has to survive a second look a second later -- one round through a window is
        /// not a firefight, and a scanner feed that says otherwise is the reason nobody believes
        /// scanner feeds.
        /// </summary>
        private const int Enough = 2;

        private readonly SocialFeed _feed;

        private int _nextCount;
        private int _blastAt = -SameStoryMs;
        private int _shotsAt = -SameStoryMs;
        private int _wereShooting;

        public Newsroom(SocialFeed feed)
        {
            _feed = feed;
        }

        public void Update()
        {
            if (_feed == null) return;

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return;

            var at = player.Position;

            if (Blast(at)) return;

            if (Game.GameTime < _nextCount) return;
            _nextCount = Game.GameTime + CountMs;

            Shooting(at);
        }

        /// <summary>Something went up near enough to hear. Checked every frame -- a bang is
        /// over in a few hundred milliseconds and a throttle sleeps straight through it.</summary>
        private bool Blast(Vector3 at)
        {
            if (Game.GameTime - _blastAt < SameStoryMs) return false;

            try
            {
                // -1 is any kind of explosion. Which kind it was is a detail a paper would get
                // wrong anyway.
                if (!Function.Call<bool>(Hash.IS_EXPLOSION_IN_SPHERE, -1,
                                         at.X, at.Y, at.Z, BlastRange))
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            _blastAt = Game.GameTime;

            if (!_feed.NewsReady) return false;

            return _feed.Report("NewsBlast", "") != null;
        }

        private void Shooting(Vector3 at)
        {
            var now = Shooters(at);

            var story = now >= Enough
                        && _wereShooting >= Enough
                        && Game.GameTime - _shotsAt >= SameStoryMs;

            _wereShooting = now;

            if (!story) return;

            _shotsAt = Game.GameTime;

            if (!_feed.NewsReady) return;

            _feed.Report("NewsShots", "");
        }

        private static int Shooters(Vector3 at)
        {
            var many = 0;

            try
            {
                foreach (var ped in World.GetNearbyPeds(at, ShotRange))
                {
                    if (ped == null || !ped.Exists() || !ped.IsAlive) continue;
                    if (!Function.Call<bool>(Hash.IS_PED_SHOOTING, ped.Handle)) continue;

                    many++;

                    // No point counting a whole riot. Two is the whole question.
                    if (many >= Enough) return many;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("The newsroom could not count the shooters: " + ex.Message);
            }

            return many;
        }
    }
}
