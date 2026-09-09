using System;
using System.Collections.Generic;
using GTA;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.UI
{
    /// <summary>
    /// A headstone on the map where you left somebody, for a minute or so.
    ///
    /// WHY IT IS WORTH HAVING. Bodies are lootable now, and a body you have walked away from is
    /// a body you will not find again -- a ped on the ground is invisible from six feet away in
    /// this game, let alone from a car. Every other thing in this mod that you can come back to
    /// has a mark on the map while it is worth coming back to, and a corpse with a gun on it is
    /// the same kind of thing.
    ///
    /// AND IT EXPIRES, which is most of the design. A permanent mark per kill is a map that
    /// fills up and never empties; the point of this one is "that just happened, over there",
    /// which stops being useful about as fast as the body stops being interesting. Fifty
    /// seconds by default, and it is in the ini.
    ///
    /// ONLY YOURS. GET_PED_SOURCE_OF_DEATH says who did it, so a shootout between two gangs in
    /// the next street does not paint the map -- and running somebody over counts, because the
    /// game names the CAR as the killer and a man under your wheels is still a man you killed.
    ///
    /// CAPPED. A dozen at once. Past that the oldest goes, because a minigun in a crowd should
    /// not put forty icons on a minimap the size of a beermat.
    /// </summary>
    internal sealed class Graves
    {
        /// <summary>885 radar_yankton, which is a headstone. See BLIPS.md.</summary>
        private const int Sprite = 885;

        /// <summary>Red. A body is a red thing, and the headstone shape keeps it out of the
        /// way of anything else red on that map.</summary>
        private const int Colour = 1;

        private const float Scale = 0.75f;

        /// <summary>How far out a death is noticed, and how often the street is looked at.</summary>
        private const float Reach = 110f;
        private const int LookEveryMs = 400;

        /// <summary>How many can be up at once.</summary>
        private const int Most = 12;

        /// <summary>Set by Main: off when the player has turned it off.</summary>
        public Func<bool> On;

        /// <summary>Set by Main: how long one stays up.</summary>
        public Func<int> Seconds;

        private sealed class Stone
        {
            public Blip Mark;
            public int Until;
        }

        private readonly List<Stone> _stones = new List<Stone>();

        /// <summary>
        /// Whose death has already been marked, and until when that is worth remembering.
        ///
        /// A DICTIONARY RATHER THAN A SET, because handles are REUSED. A set that only ever
        /// grows would eventually hold the handle of somebody the game has since given to
        /// another ped, and that ped's death would be silently ignored for the rest of the
        /// session. Entries are dropped on the same clock the stones are, which is the longest
        /// this has any reason to remember anything.
        /// </summary>
        private readonly Dictionary<int, int> _marked = new Dictionary<int, int>();

        private readonly List<int> _stale = new List<int>();

        private int _nextLook;

        public void Update(Ped player)
        {
            var now = Game.GameTime;

            Expire(now);

            if (On != null && !On()) return;
            if (player == null || !player.Exists()) return;
            if (now < _nextLook) return;

            _nextLook = now + LookEveryMs;

            try
            {
                var car = player.CurrentVehicle;
                var wheels = car != null && car.Exists() ? car.Handle : 0;

                foreach (var ped in World.GetNearbyPeds(player, Reach))
                {
                    if (ped == null || !ped.Exists() || ped.IsAlive) continue;
                    if (ped.Handle == player.Handle) continue;
                    if (_marked.ContainsKey(ped.Handle)) continue;

                    var killer = Function.Call<int>(Hash.GET_PED_SOURCE_OF_DEATH, ped.Handle);

                    // The car counts. The game names whatever did the damage, and a man under
                    // your wheels is a man you killed.
                    if (killer != player.Handle && (wheels == 0 || killer != wheels)) continue;

                    Put(ped, now);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not mark a body: " + ex.Message);
            }
        }

        /// <summary>One stone, where he fell.</summary>
        private void Put(Ped who, int now)
        {
            var lasts = Seconds == null ? 50 : Seconds();

            _marked[who.Handle] = now + lasts * 1000;

            if (lasts <= 0) return;

            // The oldest goes rather than the newest being refused: the one you have just made
            // is the one you are looking for.
            while (_stones.Count >= Most) Take(0);

            try
            {
                // AT THE COORDINATE, NOT ON THE PED. A blip attached to a body is deleted with
                // the body -- and the game clears corpses on its own schedule, which is usually
                // sooner than this. The point is the SPOT.
                var mark = World.CreateBlip(who.Position);
                if (mark == null || !mark.Exists()) return;

                Function.Call(Hash.SET_BLIP_SPRITE, mark.Handle, Sprite);
                Function.Call(Hash.SET_BLIP_COLOUR, mark.Handle, Colour);
                Function.Call(Hash.SET_BLIP_SCALE, mark.Handle, Scale);
                Function.Call(Hash.SET_BLIP_AS_SHORT_RANGE, mark.Handle, true);

                mark.Name = "Body";

                _stones.Add(new Stone { Mark = mark, Until = now + lasts * 1000 });
            }
            catch (Exception ex)
            {
                Log.Debug("Could not put a stone down: " + ex.Message);
            }
        }

        private void Expire(int now)
        {
            for (var i = _stones.Count - 1; i >= 0; i--)
            {
                var stone = _stones[i];

                if (stone.Mark != null && stone.Mark.Exists() && now < stone.Until) continue;

                Take(i);
            }

            if (_marked.Count == 0) return;

            _stale.Clear();

            foreach (var pair in _marked)
            {
                if (now >= pair.Value) _stale.Add(pair.Key);
            }

            foreach (var handle in _stale) _marked.Remove(handle);
        }

        private void Take(int i)
        {
            if (i < 0 || i >= _stones.Count) return;

            try
            {
                var mark = _stones[i].Mark;
                if (mark != null && mark.Exists()) mark.Delete();
            }
            catch
            {
                // Nothing else to do about a blip.
            }

            _stones.RemoveAt(i);
        }

        /// <summary>Everything off the map. Called when the mod stands down.</summary>
        public void Clear()
        {
            for (var i = _stones.Count - 1; i >= 0; i--) Take(i);

            _stones.Clear();
            _marked.Clear();
            _nextLook = 0;
        }
    }
}
