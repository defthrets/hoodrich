using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Gangs;
using Hoodrich.State;
using Hoodrich.UI;
using Hoodrich.Weapons;

namespace Hoodrich.Economy
{
    /// <summary>What one thing on a body is.</summary>
    internal enum LootKind
    {
        Gun = 0,
        Money,
        Food,
        Drug
    }

    /// <summary>One thing you can take off somebody.</summary>
    internal sealed class LootItem
    {
        public LootKind Kind;

        /// <summary>The weapon id, the drug id, the food id. Empty for money.</summary>
        public string Id = "";

        public string Name = "";

        /// <summary>Guns: the art pack name. Food: a full path to their picture. Otherwise empty.</summary>
        public string Icon = "";

        /// <summary>Rounds in the gun, notes in the roll, or how many of a thing.</summary>
        public int Count;

        /// <summary>Drugs only.</summary>
        public float Grams;
        public float Purity = 1f;

        /// <summary>What the chip under the picture says.</summary>
        public string Tag = "";

        public Color Tint = Palette.Text;
    }

    /// <summary>
    /// Who somebody was, and what was in their pockets.
    ///
    /// EVERYTHING HERE IS INVENTED EXCEPT THE GUNS. A ped in this game has a model, a
    /// relationship group and whatever weapons the game gave it, and nothing else -- no name,
    /// no age, no address. So the guns and the rounds in them are read off the man himself
    /// and the rest is made up: a name, a date of birth, a height, where the game says he is
    /// from, and a roll of notes.
    ///
    /// MADE UP ONCE, AND THE SAME EVERY TIME. Seeded off the ped's own handle and model, so
    /// the card you get the second time you open a body is the card you got the first time.
    /// A man whose name changes while you are looking at him is not a man, he is a dice roll,
    /// and the whole point of the card is that he was somebody.
    /// </summary>
    internal sealed class Body
    {
        public int Handle;

        public string Name = "";
        public string Born = "";
        public string Height = "";
        public string Ethnicity = "";
        public string Affiliation = "";

        /// <summary>
        /// Whether the body is a woman, which the screen needs and the pronouns need.
        ///
        /// It was worked out and thrown away: the sex picked the first name and the height and
        /// then went out of scope, so every line of copy on the screen said "him" over a woman
        /// lying on the pavement.
        /// </summary>
        public bool Female;

        /// <summary>"him" or "her", and the rest of it, so no screen has to work it out twice.</summary>
        public string Him { get { return Female ? "her" : "him"; } }
        public string He { get { return Female ? "she" : "he"; } }
        public string His { get { return Female ? "her" : "his"; } }

        /// <summary>Their picture, once the game has made one. See UI.LootScreen.</summary>
        public string Face = "";

        public readonly List<LootItem> Items = new List<LootItem>();

        public bool Empty => Items.Count == 0;
    }

    /// <summary>
    /// The bodies you have been through, and what was on them.
    ///
    /// SEARCHING IS THE ONLY WAY ANY OF IT MOVES. The game's own answer to a dead man with a
    /// gun is a pickup on the pavement you walk over without looking; this mod turns that off
    /// (see Locations.Search) and puts the gun in his pocket instead, where you have to kneel
    /// down and take it. That is the whole feature: the difference between loot arriving and
    /// loot being taken.
    ///
    /// ONE SEARCH PER MAN. What is on him is worked out the first time you open him and kept
    /// against his handle, so closing the screen and opening it again shows the same pockets
    /// with the same things missing. Handles are reused by the game once a body is cleaned
    /// up, so the table is swept of anything that is no longer a corpse.
    /// </summary>
    internal sealed class Bodies
    {
        /// <summary>How much cash somebody might be carrying, before who they are is considered.</summary>
        private const int NotesMin = 8;
        private const int NotesMax = 140;

        /// <summary>A gang member carries the day's money rather than bus fare.</summary>
        private const int GangNotesMin = 40;
        private const int GangNotesMax = 520;

        /// <summary>In a hundred: how often there is anything to eat, and anything to take.</summary>
        private const int FoodChance = 34;
        private const int DrugChance = 18;
        private const int GangDrugChance = 55;

        /// <summary>How long a searched body is remembered after it stops existing.</summary>
        private const int SweepEveryMs = 20000;

        private readonly Dictionary<int, Body> _known = new Dictionary<int, Body>();

        /// <summary>
        /// Everybody whose pockets have actually been opened, by handle.
        ///
        /// NOT THE SAME AS BEING ON THE BOOKS. _known holds anybody who has been LOOKED at --
        /// it is built on demand by the scan, before you have knelt down -- and it holds them
        /// whether or not you ever searched them. This is the narrower fact, and it is the one
        /// another mod wants: see Api.Corpse, where Five0 Patrol waits for it before offering
        /// to drag the body away.
        ///
        /// SWEPT WITH THE REST. The game reuses ped handles, so a set that is never cleared
        /// eventually tells you a fresh corpse has already been searched. See Sweep.
        /// </summary>
        private readonly HashSet<int> _opened = new HashSet<int>();

        private int _sweptAt;

        /// <summary>Set by Main: the weapons, the drugs and the pockets they go into.</summary>
        public WeaponRegistry Guns;
        public Drugs Catalogue;
        public PlayerState State;
        public GangRegistry Sets;

        /// <summary>Set by Main: money found goes through the same door as money earned.</summary>
        public Action<int> Pay;

        /// <summary>How many bodies are on the books, for the log.</summary>
        public int Count => _known.Count;

        /// <summary>
        /// Everything known about somebody, made the first time and kept after that.
        ///
        /// Null when there is nothing to make it from.
        /// </summary>
        public Body For(Ped who)
        {
            if (who == null || !who.Exists()) return null;

            Body body;
            if (_known.TryGetValue(who.Handle, out body)) return body;

            body = Build(who);
            _known[who.Handle] = body;

            return body;
        }

        /// <summary>Noted the moment his pockets are actually opened. See _opened.</summary>
        public void Open(Ped who)
        {
            if (who == null || !who.Exists()) return;

            _opened.Add(who.Handle);
        }

        /// <summary>Whether his pockets have been opened. By handle, for Api.Corpse.</summary>
        public bool Opened(int handle)
        {
            return handle != 0 && _opened.Contains(handle);
        }

        /// <summary>Whether this one has already been gone through and emptied.</summary>
        public bool Done(Ped who)
        {
            if (who == null || !who.Exists()) return false;

            Body body;
            return _known.TryGetValue(who.Handle, out body) && body.Empty;
        }

        /// <summary>
        /// Drops anything whose body is gone.
        ///
        /// THE GAME REUSES HANDLES. A table keyed by handle and never cleared eventually hands
        /// a fresh corpse the pockets of a man who died an hour ago -- emptied ones at that,
        /// so the new body would be unsearchable for no reason anybody could see.
        /// </summary>
        public void Sweep(int now)
        {
            if (now - _sweptAt < SweepEveryMs) return;
            _sweptAt = now;

            if (_known.Count == 0) return;

            var gone = new List<int>();

            foreach (var pair in _known)
            {
                var ped = Entity.FromHandle(pair.Key) as Ped;

                if (ped == null || !ped.Exists() || ped.IsAlive) gone.Add(pair.Key);
            }

            // AND THE OPENED SET GOES WITH IT, for exactly the same reason the table does: a
            // recycled handle would otherwise tell another mod that a fresh corpse had already
            // been searched, and it would quietly skip its own prompt on every new body.
            foreach (var handle in gone) { _known.Remove(handle); _opened.Remove(handle); }

            if (gone.Count > 0) Log.Debug("Bodies: forgot " + gone.Count + " that are no longer there.");
        }

        public void Forget()
        {
            _known.Clear();
            _opened.Clear();
        }

        // ---- who he was ---------------------------------------------------------

        private Body Build(Ped who)
        {
            var body = new Body { Handle = who.Handle };

            uint seed;

            try { seed = Seed(who); }
            catch { seed = (uint)who.Handle; }

            var model = Model(who);

            var male = Male(who, model);

            body.Female = !male;

            body.Affiliation = Set(who, model);
            body.Ethnicity = Race(model, ref seed);
            body.Name = Called(male, body.Ethnicity, ref seed);
            body.Born = Birthday(ref seed);
            body.Height = Tall(male, ref seed);

            Pockets(who, body, model, ref seed);

            return body;
        }

        /// <summary>
        /// The one number everything else comes out of.
        ///
        /// The handle and the model together, so two men of the same model standing next to
        /// each other are two different people, and so the same man is the same man.
        /// </summary>
        private static uint Seed(Ped who)
        {
            var h = 2166136261u;

            h ^= (uint)who.Handle;
            h *= 16777619u;

            h ^= unchecked((uint)who.Model.Hash);
            h *= 16777619u;

            return h;
        }

        /// <summary>One number off the seed, and the seed moves on. Deterministic, and not Random.</summary>
        private static int Roll(ref uint seed, int lessThan)
        {
            if (lessThan <= 1) return 0;

            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;

            return (int)(seed % (uint)lessThan);
        }

        private static string Model(Ped who)
        {
            try
            {
                var name = Names.Of(who.Model.Hash);
                return string.IsNullOrEmpty(name) ? "" : name.ToLowerInvariant();
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// Whose he was.
        ///
        /// ASKED OF THE RELATIONSHIP GROUP FIRST, because that is what actually decides gang
        /// membership in this game -- see the note on GangDef. Our own sets are checked
        /// against the registry, then the game's own ambient groups by name, and only then
        /// does it fall back to reading the model, which is a guess and is treated as one.
        /// </summary>
        private string Set(Ped who, string model)
        {
            try
            {
                var group = Function.Call<int>(Hash.GET_PED_RELATIONSHIP_GROUP_HASH, who.Handle);

                if (group != 0 && Sets != null)
                {
                    foreach (var gang in Sets.All)
                    {
                        if (gang == null || gang.GroupHash == 0) continue;
                        if (gang.GroupHash != group) continue;

                        return gang.Name;
                    }
                }

                if (group != 0)
                {
                    foreach (var pair in Ambient)
                    {
                        if (Function.Call<int>(Hash.GET_HASH_KEY, pair[0]) != group) continue;

                        return pair[1];
                    }
                }
            }
            catch
            {
                // The model has a guess in it.
            }

            foreach (var pair in ByModel)
            {
                if (model.IndexOf(pair[0], StringComparison.OrdinalIgnoreCase) < 0) continue;

                return pair[1];
            }

            if (model.StartsWith("s_m_y_cop", StringComparison.OrdinalIgnoreCase) ||
                model.StartsWith("s_f_y_cop", StringComparison.OrdinalIgnoreCase) ||
                model.IndexOf("sheriff", StringComparison.OrdinalIgnoreCase) >= 0 ||
                model.IndexOf("swat", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "LSPD";
            }

            return "None";
        }

        /// <summary>The game's own gang groups, by the names it hashes them from.</summary>
        private static readonly string[][] Ambient =
        {
            new[] { "AMBIENT_GANG_BALLAS", "Ballas" },
            new[] { "AMBIENT_GANG_FAMILY", "Families" },
            new[] { "AMBIENT_GANG_MEXICAN", "Vagos" },
            new[] { "AMBIENT_GANG_MARABUNTE", "Marabunta Grande" },
            new[] { "AMBIENT_GANG_SALVA", "Marabunta Grande" },
            new[] { "AMBIENT_GANG_LOST", "The Lost" },
            new[] { "AMBIENT_GANG_HILLBILLY", "Rednecks" },
            new[] { "AMBIENT_GANG_WEICHENG", "Wei Cheng Triads" },
            new[] { "COP", "LSPD" },
            new[] { "SECURITY_GUARD", "Security" },
            new[] { "MEDIC", "LSFD" },
            new[] { "FIREMAN", "LSFD" }
        };

        /// <summary>The last resort: what the model is called. A guess, and only used as one.</summary>
        private static readonly string[][] ByModel =
        {
            new[] { "balla", "Ballas" },
            new[] { "famca", "Families" },
            new[] { "famdnf", "Families" },
            new[] { "famfor", "Families" },
            new[] { "mexgoon", "Vagos" },
            new[] { "vagos", "Vagos" },
            new[] { "mexgang", "Vagos" },
            new[] { "salvaboss", "Marabunta Grande" },
            new[] { "salvagoon", "Marabunta Grande" },
            new[] { "lost", "The Lost" },
            new[] { "korean", "Kkangpae" },
            new[] { "chigoon", "Wei Cheng Triads" },
            new[] { "armgoon", "Armenian Mob" },
            new[] { "armboss", "Armenian Mob" },
            new[] { "armlieut", "Armenian Mob" }
        };

        /// <summary>
        /// Where the game says he is from.
        ///
        /// Taken from the SET where there is one, because the game's gangs are drawn along
        /// those lines and pretending otherwise would put a name on the card that does not
        /// match the man stood in front of you. Everybody else is a straight deterministic
        /// pick, weighted no way at all.
        /// </summary>
        private static string Race(string model, ref uint seed)
        {
            foreach (var pair in Looks)
            {
                if (model.IndexOf(pair[0], StringComparison.OrdinalIgnoreCase) < 0) continue;

                return pair[1];
            }

            return Anybody[Roll(ref seed, Anybody.Length)];
        }

        private static readonly string[][] Looks =
        {
            new[] { "balla", Black },
            new[] { "famca", Black },
            new[] { "famdnf", Black },
            new[] { "famfor", Black },
            new[] { "afri", Black },
            new[] { "mexgoon", "Hispanic" },
            new[] { "mexgang", "Hispanic" },
            new[] { "mexlabor", "Hispanic" },
            new[] { "salva", "Hispanic" },
            new[] { "vagos", "Hispanic" },
            new[] { "korean", "Korean" },
            new[] { "ktown", "Korean" },
            new[] { "chigoon", "Chinese" },
            new[] { "chin", "Chinese" },
            new[] { "arm", "Armenian" },
            new[] { "indian", "South Asian" }
        };

        /// <summary>
        /// What the card calls it, in one place.
        ///
        /// IT SAID "BLACK", WHICH IS NOT WHAT A CARD SAYS. Every other line on that card is
        /// written the way an official one would write it -- a date in full, a height in feet
        /// and inches, an affiliation or None -- and the ethnicity was the one field written
        /// the way a witness statement would. The rest of the list is already in that register:
        /// Hispanic, Middle Eastern, South Asian.
        ///
        /// A constant rather than five string literals, because it appears in the model table
        /// and in the fallback roll, and a rename that catches one and not the other is a card
        /// that says two things about the same man on two different days.
        /// </summary>
        private const string Black = "African American";

        private static readonly string[] Anybody =
        {
            "White", Black, "Hispanic", "Asian", "Mixed", "Middle Eastern"
        };

        /// <summary>
        /// Whether this one is a man, by the model's own name first and the flag second.
        ///
        /// IS_PED_MALE IS NOT RELIABLE AND THE MODEL NAME IS. Every ped model Rockstar ship
        /// carries its sex in the middle of its name -- a_f_y_beach_01, s_m_y_cop_01,
        /// mp_f_freemode_01 -- and that convention has no exceptions in the list this machine
        /// has. The flag does: a woman on the pavement came up as Errol Hobbs, six foot five,
        /// because the flag said male and nothing else was asked.
        ///
        /// So the name decides where it says anything, and the flag is the fallback for a
        /// model whose name follows no convention -- an add-on ped, mostly.
        /// </summary>
        private static bool Male(Ped who, string model)
        {
            if (!string.IsNullOrEmpty(model))
            {
                var name = model.ToLowerInvariant();

                if (name.Contains("_f_") || name.StartsWith("f_", StringComparison.Ordinal)) return false;
                if (name.Contains("_m_") || name.StartsWith("m_", StringComparison.Ordinal)) return true;
            }

            try { return Function.Call<bool>(Hash.IS_PED_MALE, who.Handle); }
            catch { return true; }
        }

        private static string Called(bool male, string race, ref uint seed)
        {
            var first = male ? MaleNames : FemaleNames;

            var last = string.Equals(race, "Hispanic", StringComparison.OrdinalIgnoreCase) ? Hispanic
                     : string.Equals(race, "Korean", StringComparison.OrdinalIgnoreCase) ? Korean
                     : string.Equals(race, "Chinese", StringComparison.OrdinalIgnoreCase) ? Chinese
                     : string.Equals(race, "Armenian", StringComparison.OrdinalIgnoreCase) ? Armenian
                     : Surnames;

            return first[Roll(ref seed, first.Length)] + " " + last[Roll(ref seed, last.Length)];
        }

        private static readonly string[] MaleNames =
        {
            "Darnell", "Marcus", "Terrell", "Andre", "Jamal", "Devon", "Keon", "Tyrone",
            "Lamar", "Curtis", "Ronnie", "Deshawn", "Malik", "Trevon", "Otis", "Wesley",
            "Hector", "Miguel", "Ramon", "Carlos", "Javier", "Ernesto", "Rafael", "Ruben",
            "Danny", "Wayne", "Craig", "Vernon", "Leon", "Errol", "Delroy", "Winston",
            "Kyle", "Brett", "Todd", "Shane", "Dale", "Gary", "Neil", "Duane"
        };

        private static readonly string[] FemaleNames =
        {
            "Tanisha", "Denise", "Latoya", "Keisha", "Yvette", "Simone", "Rochelle", "Andrea",
            "Marisol", "Carmen", "Yolanda", "Esperanza", "Lourdes", "Rosa", "Alma", "Consuelo",
            "Sharon", "Michelle", "Dawn", "Tracey", "Paula", "Bernice", "Loretta", "Faye"
        };

        private static readonly string[] Surnames =
        {
            "Wilkes", "Barnes", "Colley", "Mifflin", "Dupree", "Rand", "Hollis", "Beckett",
            "Vance", "Kearns", "Ostrander", "Pell", "Rutledge", "Sable", "Thurgood", "Wren",
            "Ashby", "Cutler", "Doyle", "Fenner", "Garrity", "Hobbs", "Ingram", "Judd",
            "Keane", "Lattimore", "Mabry", "Nash", "Orr", "Purvis", "Quill", "Reeves"
        };

        private static readonly string[] Hispanic =
        {
            "Delgado", "Ibarra", "Carrillo", "Mejia", "Salcedo", "Peralta", "Cuevas",
            "Herrera", "Robles", "Valdez", "Zamora", "Nava", "Orozco", "Padilla"
        };

        private static readonly string[] Korean =
        {
            "Park", "Choi", "Kwon", "Han", "Baek", "Jung", "Seo", "Yoon", "Shin", "Oh"
        };

        private static readonly string[] Chinese =
        {
            "Cheng", "Lau", "Ng", "Fung", "Tsang", "Yau", "Ho", "Kwan", "Sit", "Mak"
        };

        private static readonly string[] Armenian =
        {
            "Petrosyan", "Sarkisian", "Avakian", "Manukyan", "Hovsepian", "Zakarian"
        };

        /// <summary>
        /// A date of birth that puts him between seventeen and sixty-four.
        ///
        /// AGAINST THE GAME'S OWN YEAR, not against a number in a comment, so a card written
        /// on the same day the game is set does not say somebody was born after they died.
        /// </summary>
        private static string Birthday(ref uint seed)
        {
            var year = 2013;

            try { year = Function.Call<int>(Hash.GET_CLOCK_YEAR); }
            catch { /* the fallback is the year the game shipped set in */ }

            if (year < 1900 || year > 3000) year = 2013;

            var age = 17 + Roll(ref seed, 48);
            var month = 1 + Roll(ref seed, 12);
            var day = 1 + Roll(ref seed, 28);

            return day.ToString("00") + "/" + month.ToString("00") + "/" + (year - age);
        }

        /// <summary>Feet and inches, which is the unit an identity card in this city would use.</summary>
        private static string Tall(bool male, ref uint seed)
        {
            var inches = male ? 64 + Roll(ref seed, 14) : 60 + Roll(ref seed, 12);

            return (inches / 12) + "'" + (inches % 12) + "\"";
        }

        // ---- what was on him ----------------------------------------------------

        private void Pockets(Ped who, Body body, string model, ref uint seed)
        {
            var gang = !string.Equals(body.Affiliation, "None", StringComparison.OrdinalIgnoreCase);

            Iron(who, body);
            Notes(body, gang, model, ref seed);
            Bite(body, ref seed);
            Powder(body, gang, ref seed);
        }

        /// <summary>
        /// The guns, and these are not invented.
        ///
        /// Asked of the man himself, one name at a time, because the game has no call that
        /// hands back a list of what somebody is carrying -- HAS_PED_GOT_WEAPON is the whole
        /// of the interface. The registry is the same list the gun locker and the boot walk,
        /// so anything the mod knows how to name is anything that can come off a body.
        /// </summary>
        private void Iron(Ped who, Body body)
        {
            if (Guns == null) return;

            try
            {
                foreach (var def in Guns.All)
                {
                    if (def == null || def.Hash == WeaponRegistry.UnarmedHash) continue;

                    if (!Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON, who.Handle, def.Hash, false)) continue;

                    var rounds = 0;

                    try { rounds = Function.Call<int>(Hash.GET_AMMO_IN_PED_WEAPON, who.Handle, def.Hash); }
                    catch { /* it comes with nothing in it, then */ }

                    body.Items.Add(new LootItem
                    {
                        Kind = LootKind.Gun,
                        Id = def.Id,
                        Name = def.Name,
                        Icon = def.Icon ?? "",
                        Count = Math.Max(0, rounds),
                        Tag = rounds > 0 ? rounds + " RDS" : "EMPTY"
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Bodies: could not read the guns: " + ex.Message);
            }
        }

        /// <summary>
        /// A roll of notes.
        ///
        /// A GANG MEMBER IS CARRYING THE DAY'S MONEY and a man on his way home from work is
        /// carrying bus fare, which is the difference between a body worth going through and
        /// a body you learn not to bother with. Both are the same one deterministic number
        /// scaled differently.
        /// </summary>
        private static void Notes(Body body, bool gang, string model, ref uint seed)
        {
            var low = gang ? GangNotesMin : NotesMin;
            var high = gang ? GangNotesMax : NotesMax;

            // The two ends of the city, and the game names them plainly enough to use.
            if (model.IndexOf("business", StringComparison.OrdinalIgnoreCase) >= 0 ||
                model.IndexOf("vinewood", StringComparison.OrdinalIgnoreCase) >= 0 ||
                model.IndexOf("golfer", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                low = 90;
                high = 700;
            }
            else if (model.IndexOf("hobo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     model.IndexOf("tramp", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     model.IndexOf("downtown", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                low = 0;
                high = 18;
            }

            var notes = low + Roll(ref seed, Math.Max(1, high - low));
            if (notes <= 0) return;

            body.Items.Add(new LootItem
            {
                Kind = LootKind.Money,
                Name = "Cash",
                Count = notes,
                Tag = "$" + notes,
                Tint = Palette.Cash
            });
        }

        /// <summary>Something to eat or drink, now and then, and only when Bare Minimum is here.</summary>
        private static void Bite(Body body, ref uint seed)
        {
            if (!Pantry.CanOrder) return;
            if (Roll(ref seed, 100) >= FoodChance) return;

            var menu = Pantry.Menu();
            if (menu.Length == 0) return;

            var id = menu[Roll(ref seed, menu.Length)];
            var name = Pantry.NameOf(id);

            if (string.IsNullOrEmpty(name)) return;

            body.Items.Add(new LootItem
            {
                Kind = LootKind.Food,
                Id = id,
                Name = name,
                Icon = Pantry.IconOf(id),
                Count = 1,
                Tag = "1"
            });
        }

        /// <summary>
        /// A bit of product, more often on somebody who was in the life.
        ///
        /// BAGGED AND STEPPED ON. What comes off a body is street weight at street purity --
        /// a personal amount somebody was carrying, not a lot off a plug -- so it goes into
        /// the packaged side of your pockets with a purity to match, and the kitchen can do
        /// what it likes with it after that.
        /// </summary>
        private void Powder(Body body, bool gang, ref uint seed)
        {
            if (Catalogue == null) return;
            if (Roll(ref seed, 100) >= (gang ? GangDrugChance : DrugChance)) return;

            var all = Catalogue.All;
            if (all == null || all.Count == 0) return;

            var drug = all[Roll(ref seed, all.Count)];
            if (drug == null) return;

            var grams = 1f + Roll(ref seed, 14);
            var purity = 0.45f + Roll(ref seed, 40) / 100f;

            body.Items.Add(new LootItem
            {
                Kind = LootKind.Drug,
                Id = drug.Id,
                Name = drug.Name,
                Grams = grams,
                Purity = purity,
                Tag = drug.Amount(grams)
            });
        }

        // ---- taking it ----------------------------------------------------------

        /// <summary>
        /// Moves one thing off the body and onto you. False, with a reason, when it will not
        /// go -- and nothing is taken off the body when it does not.
        /// </summary>
        public bool Take(Body body, LootItem item, out string why)
        {
            why = "";

            if (body == null || item == null) { why = "nothing there"; return false; }

            var me = Game.Player.Character;

            if (me == null || !me.Exists()) { why = "not right now"; return false; }

            try
            {
                switch (item.Kind)
                {
                    case LootKind.Gun:
                        var hash = Function.Call<uint>(Hash.GET_HASH_KEY, item.Id);
                        if (hash == 0) { why = "no such gun"; return false; }

                        Function.Call(Hash.GIVE_WEAPON_TO_PED, me.Handle, hash,
                                      Math.Max(0, item.Count), false, false);
                        break;

                    case LootKind.Money:
                        if (Pay == null) { why = "no way to pay it in"; return false; }
                        Pay(item.Count);
                        break;

                    case LootKind.Food:
                        if (!Pantry.Present) { why = "nowhere to put it"; return false; }
                        if (!Pantry.Give(item.Id, Math.Max(1, item.Count)))
                        {
                            why = "your pockets are full";
                            return false;
                        }
                        break;

                    case LootKind.Drug:
                        if (State == null || State.Stash == null) { why = "nowhere to put it"; return false; }

                        var took = State.Stash.AddPackaged(item.Id, item.Grams, item.Purity);

                        if (took <= 0.005f) { why = "no room on you"; return false; }

                        // PART OF IT IS STILL A TAKE. A pocket with room for four grams of a
                        // six-gram bag takes four and leaves two, which is what a pocket does.
                        if (took < item.Grams - 0.005f)
                        {
                            item.Grams -= took;
                            item.Tag = Catalogue == null ? item.Grams.ToString("0.#") + "g"
                                                         : Named(item.Id, item.Grams);
                            return true;
                        }

                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Bodies: could not take " + item.Name + ": " + ex.Message);
                why = "it would not come";
                return false;
            }

            body.Items.Remove(item);
            return true;
        }

        private string Named(string drugId, float grams)
        {
            try
            {
                foreach (var drug in Catalogue.All)
                {
                    if (drug == null || !string.Equals(drug.Id, drugId, StringComparison.OrdinalIgnoreCase)) continue;

                    return drug.Amount(grams);
                }
            }
            catch
            {
                // Grams will do.
            }

            return grams.ToString("0.#") + "g";
        }
    }
}
