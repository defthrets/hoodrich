using System;
using GTA.Math;

namespace Hoodrich.Core
{
    /// <summary>
    /// A place he can rent, the way the Dynasty 8 app shows it: what it is called, where it is,
    /// what it costs, where the rent is up to -- and what can be done about it from a phone.
    ///
    /// ONE LIST OVER TWO LANDLORDS. The stash houses are Locations.InteriorDoor, kept in
    /// leases.txt by Main's script; the rooms on the Parkview block are Parkview's Rooms, kept in
    /// rooms.sav by Parkview's script, which Main cannot reach except through Core.Home. Neither
    /// knows the other exists, so each hands the app these and the app never asks which it is
    /// looking at. Filled fresh every time the app is opened: a snapshot, and the three hooks are
    /// what change anything.
    /// </summary>
    internal sealed class Listing
    {
        public string Name = "";

        /// <summary>The neighbourhood, by the game's own zone name.</summary>
        public string Area = "";

        /// <summary>What is behind the door: "Low-end", "Mid-range", "Room".</summary>
        public string Kind = "";

        /// <summary>A week's rent.</summary>
        public int Rent;

        public bool Rented;

        /// <summary>Days until the next week's rent is taken. Nought when it is not his.</summary>
        public int DaysLeft;

        /// <summary>Whole weeks paid beyond the one he is in.</summary>
        public int WeeksAhead;

        /// <summary>The most weeks the phone lets him pay beyond the one he is in.</summary>
        public int MostWeeksAhead = 12;

        /// <summary>He is stood in it right now.</summary>
        public bool Inside;

        /// <summary>The front door, for a waypoint.</summary>
        public Vector3 Door;

        /// <summary>Pays this many weeks ahead, fewer if the limit is nearer. Returns the weeks paid; nought for none.</summary>
        public Func<int, int> PayAhead;

        /// <summary>Gives it up. Returns what came back for the weeks paid ahead, or -1 when it could not be done.</summary>
        public Func<int> GiveUp;

        /// <summary>Rents it from the phone, the first week up front. Returns whether it is his now.</summary>
        public Func<bool> Take;
    }
}
