using System;
using GTA;
using GTA.Math;
using Hoodrich.Core;
using Hoodrich.Gangs;
using Hoodrich.Social;
using Hoodrich.State;
using Hoodrich.UI;

namespace Hoodrich.Locations
{
    /// <summary>
    /// The house can be taken from you.
    ///
    /// Until now it could not, and that quietly removed the point of half the mod. Nothing in
    /// the game had any claim on what was in Denise's, so the correct play was to buy weight,
    /// put all of it in the house, and let it sit -- and the dead drop, which exists precisely
    /// so there is somewhere safer than the house, was solving a problem the player did not
    /// have. Three hundred kilos of free storage is not a stash, it is a bank.
    ///
    /// So: get loud enough, hold enough, and somebody comes while you are out.
    ///
    /// THE WARNING IS THE MECHANIC. A raid that simply happens is a tax, and a tax the player
    /// cannot see coming is indistinguishable from the mod losing their product. A raid you
    /// were told about, and drove past, is a decision you made. The text goes out minutes
    /// before anybody turns up, and everything after it is in the player's hands: move the
    /// weight to the drop, carry it, sell it off, or take the hit and keep doing what you were
    /// doing. All four are legitimate.
    ///
    /// Two flavours, because two different people want what is in there. The police seize it
    /// and that is that. A gang you are beefing with steals it, which is worse in one specific
    /// way -- they will be back, and there is somebody to hold responsible.
    /// </summary>
    internal sealed class StashRaid
    {
        /// <summary>Twice a minute is often enough for something that happens once an hour.</summary>
        private const int CheckMs = 30_000;

        /// <summary>
        /// How far away counts as away.
        ///
        /// Nobody kicks a door while the man who lives there is stood in the front room. This
        /// is also the mitigation nobody has to be told about: being home is being home.
        /// </summary>
        private const float AwayRange = 150f;

        /// <summary>
        /// Under this much weight nobody is interested.
        ///
        /// A raid that empties a nearly empty house is all cost and no drama -- it takes
        /// nothing worth mourning and teaches the player that the house is unsafe at exactly
        /// the point they have nothing to protect. The threat should arrive with the hoard.
        /// </summary>
        private const float WorthTaking = 250f;

        /// <summary>Long enough that being raided is an event rather than a weather pattern.</summary>
        private const int CooldownMs = 1_800_000;

        private readonly Settings _cfg;
        private readonly PlayerState _state;
        private readonly StashHouse _house;
        private readonly Random _rng = new Random();

        /// <summary>Set by Main. Both optional -- the raid works without either.</summary>
        public Affiliation Crew;
        public SocialFeed Social;

        private int _nextCheck;
        private int _dueAt;
        private int _lastRaid;
        private bool _police;

        public StashRaid(Settings cfg, PlayerState state, StashHouse house)
        {
            _cfg = cfg;
            _state = state;
            _house = house;
        }

        /// <summary>True while a raid has been called and has not landed yet.</summary>
        public bool Coming => _dueAt != 0;

        /// <summary>Minutes left before they arrive, for anything that wants to say so.</summary>
        public float MinutesOut =>
            _dueAt == 0 ? 0f : Math.Max(0f, (_dueAt - Game.GameTime) / 60_000f);

        public void Update()
        {
            if (!_cfg.StashRaidsEnabled || _state == null || _house == null) return;

            var now = Game.GameTime;

            if (_dueAt != 0)
            {
                if (now < _dueAt) return;

                Land();
                return;
            }

            if (now < _nextCheck) return;
            _nextCheck = now + CheckMs;

            if (_lastRaid != 0 && now - _lastRaid < CooldownMs) return;

            Consider();
        }

        /// <summary>Whether anybody is minded to come, and if so, the text goes out.</summary>
        private void Consider()
        {
            var weight = _house.Stash.Total;
            if (weight < WorthTaking) return;

            if (Home()) return;

            // HEAT IS THE PRICE OF THE HOARD. Notoriety is what the player has been building by
            // being seen working, and it is the only reason anybody knows there is a house
            // worth knocking on. A quiet dealer with a full house is not in danger, which is
            // the correct lesson and the one the dead drop exists to teach.
            var heat = Math.Min(1f, _state.Notoriety / 100f);
            if (heat <= 0.05f) return;

            // More in the house is more worth the trip, but it flattens off -- a hundred kilos
            // is not ten times the target a ten kilo house is, it is the same rumour.
            var fat = Math.Min(1.6f, 0.6f + weight / (WorthTaking * 8f));

            var beefing = Crew != null && Crew.BeefingWith().Count > 0;

            var chance = _cfg.StashRaidChancePercent / 100f * heat * fat;
            if (beefing) chance *= 1.5f;

            if (_rng.NextDouble() >= chance) return;

            // Whoever has more reason. Beef makes it personal; otherwise it is the heat that
            // brought them, and heat means uniforms.
            _police = !beefing || _rng.NextDouble() < 0.4;

            _dueAt = Game.GameTime + (int)(Math.Max(0.5f, _cfg.StashRaidWarningMinutes) * 60_000f);

            Warn();
        }

        /// <summary>The heads-up, which is the whole point.</summary>
        private void Warn()
        {
            var mins = Math.Max(1, (int)Math.Round(_cfg.StashRaidWarningMinutes));

            if (_police)
            {
                Notify.Text(Faces.For("Denise"), "Denise", "you need to come home",
                            "there is a car been sat on forum drive since this morning with two " +
                            "of them in it and they are not looking at the house across the road. " +
                            "whatever you got in my house you get it out of my house.", true);
            }
            else
            {
                var who = Crew.BeefingWith();
                var name = who.Count > 0 && who[0] != null ? who[0].Name : "them boys";

                Notify.Text(Faces.For("Lamar"), "Lamar", "aye where you at",
                            "word is " + name + " been askin' who stay at that spot on forum. " +
                            "askin' by name, cuz. that ain't nothin' but one thing.\n\n" +
                            "if it's in the house it ain't safe, I'm just sayin' it out loud.",
                            true);
            }

            Notify.Important("~r~Somebody's coming for the house.~s~ About " + mins +
                             (mins == 1 ? " minute." : " minutes."));

            Log.Info("Stash raid called: " + (_police ? "police" : "rivals") + " in " + mins +
                     " min, house holding " + _house.Stash.Total.ToString("0") + "g.");
        }

        /// <summary>They turn up, or they do not.</summary>
        private void Land()
        {
            _dueAt = 0;
            _lastRaid = Game.GameTime;

            // HE WENT HOME. Not a dice roll -- the warning said come home, and he came home,
            // and the only honest thing to do with that is nothing at all. Making it fire
            // anyway would turn the warning into a message about something he cannot affect,
            // which is worse than having sent no warning.
            if (Home())
            {
                Notify.Important("~g~They kept driving.~s~ Somebody was home.");
                Log.Info("Stash raid called off; player was at the house.");
                return;
            }

            var share = Clamp(_cfg.StashRaidTakePercent / 100f, 0.05f, 0.95f);
            var took = _house.Stash.TakeShare(share);

            if (took <= 0.01f)
            {
                // Emptied it in time. This is the win condition and it gets said out loud.
                Notify.Important("~g~They got nothing.~s~ House was empty.");
                Log.Info("Stash raid landed on an empty house.");
                return;
            }

            var lost = took >= 1000f
                ? (took / 1000f).ToString("0.#") + " kilos"
                : took.ToString("0") + "g";

            if (_police)
            {
                Notify.Failure("Police took " + lost + " out of the house.");

                Notify.Text(Faces.For("Denise"), "Denise", "dont come here",
                            "they had a paper and they had a dog and they went through my " +
                            "kitchen. do not come to this house tonight.\n\n" +
                            "i told you. i told you and told you.", true);

                // A seizure is a closed file. They got what they came for, and the temperature
                // on the street goes down with it -- which is the one mercy in it, and the
                // reason a raid is a setback rather than a spiral.
                _state.AddNotoriety(-25f);
            }
            else
            {
                var who = Crew == null ? null : Crew.BeefingWith();
                var name = who != null && who.Count > 0 && who[0] != null ? who[0].Name : "Somebody";

                Notify.Failure(name + " cleaned out " + lost + " from the house.");

                Notify.Text(Faces.For("Lamar"), "Lamar", "they hit the spot",
                            "back door off the hinges, cuz. they knew where to look and they " +
                            "wasn't in there two minutes.\n\n" +
                            "somebody told 'em. we gon' find out who.", true);

                // Being robbed does not make you less known. If anything it is the story of
                // the week, which is exactly why it is not a way to cool off.
                _state.AddRespect(-8f);
            }

            _state.Touch();

            if (Social != null)
            {
                // Through On rather than PostAsYou, so the block reacts to it as well. Getting
                // robbed is not a status update, it is news, and the one thing worse than the
                // feed ignoring it is the feed ignoring it while everybody involved knows.
                Social.On(SocialEvent.HouseRaided, _police ? "the police" : "them");
            }

            Log.Info("Stash raid took " + took.ToString("0") + "g (" + (_police ? "police" : "rivals") +
                     "); " + _house.Stash.Total.ToString("0") + "g left.");
        }

        /// <summary>Whether he is close enough for anybody to think better of it.</summary>
        private bool Home()
        {
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return false;

                return me.Position.DistanceTo(_house.Position) <= AwayRange;
            }
            catch
            {
                // If we cannot tell, assume he is out. The warning already went to his phone.
                return false;
            }
        }

        private static float Clamp(float v, float lo, float hi)
        {
            return v < lo ? lo : v > hi ? hi : v;
        }
    }
}
