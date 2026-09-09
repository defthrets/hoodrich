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

        /// <summary>
        /// How many bodies are HELD against the game's clean-up.
        ///
        /// THE GAME TAKES CORPSES AWAY ON ITS OWN SCHEDULE, and it does not care that one of
        /// them has a rifle on it you were coming back for. Walk a street away, or let the area
        /// stream, and the body is gone -- which makes both the headstone and the looting a
        /// promise the mod cannot keep.
        ///
        /// So a body you made is claimed as ours, which is the one thing that stops the
        /// population manager clearing it. That is not free -- a claimed ped is one the game
        /// can never reuse -- so it is bounded three ways: a cap, a clock, and letting go the
        /// moment there is nothing left on him.
        /// </summary>
        private const int Hold = 20;

        /// <summary>Set by Main: how long a body is held before the street can have it.</summary>
        public Func<int> HoldMinutes;

        /// <summary>Set by Main: off when the player has turned it off.</summary>
        public Func<bool> On;

        /// <summary>Set by Main: how long one stays up.</summary>
        public Func<int> Seconds;

        /// <summary>
        /// Set by Main: whether this body has been gone through.
        ///
        /// THE MARK IS A DIRECTION, NOT A TROPHY. It exists to get you back to a body you
        /// cannot see from six feet away -- so the moment you have been through his pockets it
        /// has done its job and the only thing it can do after that is take up room on a
        /// minimap next to the one you have not searched yet. See Economy.Bodies.Done.
        /// </summary>
        public Func<Ped, bool> Looted;

        private sealed class Stone
        {
            public Blip Mark;
            public int Until;

            /// <summary>Who is under it, so the mark can go when he has been searched.</summary>
            public Ped Who;
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

        /// <summary>The bodies being held, and until when. See Hold.</summary>
        private sealed class Kept
        {
            public Ped Who;
            public int Until;
        }

        private readonly List<Kept> _held = new List<Kept>();

        private int _nextLook;

        public void Update(Ped player)
        {
            var now = Game.GameTime;

            Expire(now);
            Loosening(now);

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

            // HELD WHETHER OR NOT THERE IS A MARK. The mark is a convenience and can be turned
            // off; the body still has his pockets on him and the street will still take him.
            Keep(who, now);

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

                _stones.Add(new Stone { Mark = mark, Until = now + lasts * 1000, Who = who });
            }
            catch (Exception ex)
            {
                Log.Debug("Could not put a stone down: " + ex.Message);
            }
        }

        /// <summary>Claimed as ours, so the street cannot clear him. Released by Loosen.</summary>
        private void Keep(Ped who, int now)
        {
            if (who == null || !who.Exists()) return;

            var mins = HoldMinutes == null ? 10 : HoldMinutes();
            if (mins <= 0) return;

            // The oldest is let go rather than the newest refused: the one you have just made
            // is the one you are walking towards.
            while (_held.Count >= Hold) Loosen(0);

            try
            {
                who.IsPersistent = true;
            }
            catch
            {
                // Then the game keeps him for as long as it feels like, as before.
            }

            _held.Add(new Kept { Who = who, Until = now + mins * 60 * 1000 });
        }

        /// <summary>Handed back to the game, which may then do as it likes with him.</summary>
        private void Loosen(int i)
        {
            if (i < 0 || i >= _held.Count) return;

            var kept = _held[i];
            _held.RemoveAt(i);

            try
            {
                if (kept.Who != null && kept.Who.Exists()) kept.Who.MarkAsNoLongerNeeded();
            }
            catch
            {
                // He goes when he goes.
            }
        }

        /// <summary>Anybody there is no reason to hold: gone through, timed out, or gone.</summary>
        private void Loosening(int now)
        {
            for (var i = _held.Count - 1; i >= 0; i--)
            {
                var kept = _held[i];

                if (kept.Who == null || !kept.Who.Exists() || now >= kept.Until)
                {
                    Loosen(i);
                    continue;
                }

                if (Looted == null) continue;

                try
                {
                    if (Looted(kept.Who)) Loosen(i);
                }
                catch
                {
                    // He is let go on the clock instead.
                }
            }
        }

        private void Expire(int now)
        {
            for (var i = _stones.Count - 1; i >= 0; i--)
            {
                var stone = _stones[i];

                if (stone.Mark == null || !stone.Mark.Exists() || now >= stone.Until)
                {
                    Take(i);
                    continue;
                }

                // GONE THROUGH, SO GONE. Asked of the body rather than of a flag set when the
                // loot screen closed -- somebody who opens the pockets, takes the gun and
                // leaves the sandwich has not finished with him, and the mark is what gets him
                // back. Done answers true when there is nothing left on him.
                if (Looted == null) continue;
                if (stone.Who == null || !stone.Who.Exists()) continue;

                try
                {
                    if (Looted(stone.Who)) Take(i);
                }
                catch
                {
                    // It goes on its own clock instead.
                }
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

            // EVERY BODY HANDED BACK. A held ped the mod forgets about is a ped the game can
            // never clear for the rest of the session, which is the one genuinely expensive
            // way this could go wrong.
            for (var i = _held.Count - 1; i >= 0; i--) Loosen(i);

            _held.Clear();
        }
    }
}
