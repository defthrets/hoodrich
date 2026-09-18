using System;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.Locations
{
    /// <summary>
    /// One of his own vehicles, kept on the map whether or not it is loaded.
    ///
    /// A BLIP ON A CAR ONLY EXISTS WHILE THE CAR DOES, and a car three streets away does not.
    /// The game streams vehicles in and out constantly, so an entity blip is a blip that
    /// appears when you are close enough to see the car anyway and vanishes the moment you are
    /// not -- which is the exact opposite of what a map is for. You do not open the map to
    /// find the thing in front of you.
    ///
    /// That is why his car "was not on the map": it was, for the fifty metres either side of
    /// standing next to it.
    ///
    /// SO IT IS ONE STANDING BLIP, MOVED. While the vehicle is loaded the mark is put where
    /// the vehicle is, every look; the moment it streams out the mark stays on the spot it
    /// was last seen. Between them the car is on the map from anywhere on it, which is what
    /// the game's own story-vehicle marks do and what was lost when this mod started deleting
    /// the car those marks were attached to.
    ///
    /// NEVER A BLIP ON THE ENTITY. It was two blips -- one attached to the car while it was
    /// loaded, the standing one otherwise -- and the attached one is the bug in Gta5gamer__'s
    /// report: the game streams the car out without telling the blip, the handle it was
    /// attached to is handed to whatever spawns next, and his car's mark drives off on a
    /// stranger's Sentinel. Seen on the bike too, as a green motorcycle on some traffic
    /// bike that had just appeared. A blip we place by coordinate cannot follow anything.
    ///
    /// IN HIS COLOUR RATHER THAN GREY. These were dark grey at two thirds alpha, on the
    /// reasoning that a car he was given should not look like a car he bought -- which is a
    /// fair distinction and was drawn in the one colour that cannot be seen against the map.
    /// Green is Franklin's own colour in this game, which says "his" more exactly than grey
    /// said "not bought", and it is legible.
    ///
    /// WHAT IT DOES NOT DO is survive a session. The spot is learned by seeing the car, so on
    /// a fresh load there is no mark until you have been near it once -- and you start at his
    /// house, so that is usually the first thirty seconds. Writing it into the save would be a
    /// coordinate the mod insists on against a game that moves his car about on its own terms.
    /// </summary>
    internal sealed class Parked
    {
        private readonly BlipSprite _sprite;
        private readonly BlipColor _colour;
        private readonly float _scale;
        private readonly string _name;

        private Blip _standing;
        private Vector3 _lastSeen;
        private bool _known;

        public Parked(BlipSprite sprite, string name, float scale = 0.85f,
                      BlipColor colour = BlipColor.Green)
        {
            _sprite = sprite;
            _name = name;
            _scale = scale;
            _colour = colour;
        }

        /// <summary>
        /// The vehicle is loaded and this is it: it wears its own mark, and the standing one
        /// comes up.
        /// </summary>
        public void Here(Vehicle car)
        {
            if (car == null || !car.Exists()) return;

            _lastSeen = car.Position;
            _known = true;

            // NOT WHILE HE IS IN IT. A mark on the vehicle you are driving is a mark sitting
            // exactly under the arrow that already says where you are -- two pictures on the
            // same spot, one of which is telling you something you could not fail to know.
            // The game hides a personal vehicle's blip for the same reason, and it comes
            // straight back when you get out because this runs every tick.
            if (Driving(car)) { Away(); return; }

            Place(_lastSeen);
        }

        /// <summary>
        /// Nothing of his is loaded, so the mark holds the last place it was seen.
        ///
        /// Called every tick that Here was not. Cheap: it does nothing at all once the blip is
        /// down, and there is only ever one.
        /// </summary>
        public void Gone()
        {
            if (!_known) return;

            Place(_lastSeen);
        }

        /// <summary>The one blip, made if it is missing and moved if it is not.</summary>
        private void Place(Vector3 at)
        {
            try
            {
                if (_standing != null && !_standing.Exists()) _standing = null;

                if (_standing == null)
                {
                    _standing = World.CreateBlip(at);
                    if (_standing == null || !_standing.Exists()) { _standing = null; return; }

                    Dress(_standing);
                    return;
                }

                if (_standing.Position.DistanceTo(at) > 0.5f) _standing.Position = at;
            }
            catch (Exception ex)
            {
                Log.Debug("Could not mark " + _name + ": " + ex.Message);
                _standing = null;
            }
        }

        /// <summary>Whether the player is the one at the wheel of this.</summary>
        private static bool Driving(Vehicle car)
        {
            try
            {
                var me = Game.Player.Character;

                if (me == null || !me.Exists()) return false;

                var seat = me.CurrentVehicle;

                return seat != null && seat.Exists() && seat.Handle == car.Handle;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>The standing mark taken down, because the real thing is back.</summary>
        private void Away()
        {
            if (_standing == null) return;

            try
            {
                if (_standing.Exists()) _standing.Delete();
            }
            catch
            {
                // It goes when the session does.
            }

            _standing = null;
        }

        /// <summary>Everything taken off the map. For a teardown.</summary>
        public void Clear()
        {
            Away();
            _known = false;
        }

        /// <summary>
        /// The same picture on both, so the mark does not change appearance when the car
        /// streams in. Full alpha, and shown at any distance -- a short-range blip is one that
        /// only draws on the minimap, which would put this straight back where it started.
        /// </summary>
        private void Dress(Blip blip)
        {
            blip.Sprite = _sprite;
            blip.Color = _colour;
            blip.Scale = _scale;
            blip.Name = _name;

            Function.Call(Hash.SET_BLIP_AS_SHORT_RANGE, blip.Handle, false);
        }
    }
}
