using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;

namespace Hoodrich.UI
{
    /// <summary>
    /// A face for everybody, made out of the game rather than borrowed from it.
    ///
    /// THE PROBLEM WAS NEVER THAT THERE WERE NO PICTURES. It was that the only pictures
    /// available are the game's CHAR_ contact photos -- about forty of them, all of named story
    /// characters -- and there are a hundred and seventy people on this feed. Handing Simeon's
    /// face to a driverless taxi firm or Tanisha's to a tow truck driver is not a picture, it
    /// is the nearest wrong thing, and the mismatch is more distracting than the coloured
    /// letter it replaced.
    ///
    /// So they are MADE. The game will render a headshot of any ped into a texture -- it is
    /// how the phone's contact photos work and how Franklin's own mugshot on this screen is
    /// already done. Given that, a face for @kdot_ontheblock is: pick a ped model that suits
    /// who he is, stand one up out of sight, photograph him, delete him, and keep the picture.
    ///
    /// PERSISTENT BECAUSE IT IS DERIVED, not stored. The model is chosen by hashing the
    /// handle, so @kdot_ontheblock gets the same face this session, next session and on
    /// somebody else's machine -- with nothing written to the save and nothing to migrate.
    ///
    /// THE POOL IS SMALL AND THAT IS THE WHOLE DESIGN. The game will not hold a hundred and
    /// seventy of these; it holds a couple of dozen. So this is a cache with eviction, filled
    /// on demand for the people actually on screen, and the oldest unused face is handed back
    /// when a new one is wanted. A feed shows eight posts at a time and a cache of fourteen
    /// covers that with room to scroll.
    ///
    /// ONE AT A TIME. Registering a headshot is not instant -- the game renders it over several
    /// frames -- so this is a queue with a single job in flight and a timeout, rather than
    /// twenty peds stood in a field waiting to be photographed.
    /// </summary>
    internal static class Headshots
    {
        /// <summary>
        /// How many faces are kept. Well under whatever the engine's real ceiling is.
        ///
        /// The limit is not documented anywhere that can be checked and running into it does
        /// not throw -- REGISTER_PEDHEADSHOT simply starts returning nothing, which would look
        /// like the feature quietly not working for some people.
        ///
        /// EIGHT, DOWN FROM FOURTEEN. Each face costs more than a texture: the ped it was taken
        /// of is kept alive for as long as the picture is, which keeps that ped's model resident
        /// too. Fourteen of those is fourteen archetypes the game cannot stream out, on top of
        /// whatever else is going on -- and a takeover is forty people and fifteen cars of
        /// whatever else. Eight still covers a screenful of the feed with room over, and it
        /// halves the standing cost of the whole feature.
        /// </summary>
        /// <summary>
        /// How many faces are held at once. Eight was below what one screen needs.
        ///
        /// THAT IS WHY THE TOASTS FLASHED. The feed screen asks for a face for every post it
        /// draws and a page is about ten of them -- so opening it evicted the entire store,
        /// including whichever face was on the notification in the corner. The next time that
        /// author came round the feed asked again, the face came back, the one after evicted
        /// it again. A picture blinking in and out on a two-second cycle.
        ///
        /// Twelve. More than a page of the feed plus the two or three on screen, and it
        /// leaves the game slots to spare: it registers a fixed number of these for
        /// everybody at once, and twenty of ours plus the phone's own and whatever else is
        /// running was all of them -- the man you were talking to got none, and stood
        /// there with no face. The eviction is still there for a session that runs long
        /// enough to meet hundreds of people; it just is not firing every time somebody
        /// opens their phone.
        /// </summary>
        /// TWELVE CHURNED. The feed writes itself every few seconds, so at twelve the
        /// authors kept falling out and coming back -- the log had the same faces rendered
        /// four to six times each -- and every render is a post showing a letter in a square
        /// until the photograph lands, which read as the feed flickering. Sixteen holds a
        /// page and the next one; the talk's own retry covers the slot it used to be short.
        private const int Keep = 16;

        /// <summary>How long one is given to render before it is written off.</summary>
        private const int PatienceMs = 4000;

        /// <summary>And how long before a failed one is worth trying again.</summary>
        private const int RetryMs = 30000;

        private sealed class Face
        {
            public int Shot;
            public string Txd = "";
            public int Used;

            /// <summary>When it was made, whether the game has ever said it was ready, and whether its going away has been logged. See Ready.</summary>
            public int MadeAt;
            public bool WasReady;
            public bool Moaned;

            /// <summary>
            /// The ped the picture was taken of, kept alive for as long as the picture is.
            ///
            /// THIS IS THE BIT THAT WAS WRONG. The ped was deleted the instant the texture
            /// came back, on the reasoning that the picture belongs to the registration rather
            /// than to the man -- which is half true and useless: the registration is what
            /// holds the texture, and the registration is against a ped. Delete him and the
            /// slot is a name pointing at nothing, which draws as nothing, which is exactly
            /// what the feed was showing.
            ///
            /// So he stays: frozen, invisible, no collision, thirty metres over the player's
            /// head, and deleted at the same moment the headshot is unregistered. Fourteen of
            /// those is the price of everybody having a face.
            /// </summary>
            public Ped Model;
        }

        private static readonly Dictionary<string, Face> Made =
            new Dictionary<string, Face>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Clear out any models left over from a previous load of the script.
        ///
        /// EVERYTHING ABOVE IS STATIC, AND STATICS DO NOT SURVIVE A RELOAD. Press Insert and
        /// this class comes back knowing about no faces at all -- while the peds it was
        /// keeping alive are still up there, frozen, thirty metres over wherever the player was
        /// stood. Nothing owns them and nothing will ever delete them, so every reload used to
        /// leave another eight behind.
        ///
        /// They are findable because of what makes them odd in the first place: ours, frozen,
        /// invisible, and a long way above your head. Nothing the game does on its own puts a
        /// ped there, so the test cannot take somebody else's.
        /// </summary>
        public static void Sweep()
        {
            try
            {
                var player = Game.Player.Character;
                if (player == null || !player.Exists()) return;

                var found = 0;

                foreach (var ped in World.GetNearbyPeds(player.Position, 90f))
                {
                    if (ped == null || !ped.Exists()) continue;
                    if (ped.Handle == player.Handle || ped.IsPlayer) continue;

                    // Ours, and well above head height. A ped twenty metres up that this mod
                    // owns is a leftover photograph and nothing else.
                    if (ped.Position.Z - player.Position.Z < 20f) continue;

                    if (!Function.Call<bool>(Hash.IS_ENTITY_A_MISSION_ENTITY, ped.Handle)) continue;
                    if (Function.Call<bool>(Hash.IS_ENTITY_VISIBLE, ped.Handle)) continue;

                    try { ped.Delete(); found++; }
                    catch { }
                }

                if (found > 0) Log.Info("Headshots: cleared " + found + " left over from a reload.");
            }
            catch (Exception ex)
            {
                Log.Debug("Headshots could not sweep: " + ex.Message);
            }
        }

        private static readonly Dictionary<string, int> Failed =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Who is waiting, oldest first. Bounded, so a scroll cannot pile up work.</summary>
        private static readonly List<string> Queue = new List<string>();

        /// <summary>
        /// What each waiting key should be photographed as, in order of preference.
        ///
        /// A LIST RATHER THAN A NAME, because the caller does not always know which of several
        /// models a given build has -- and a face that fails because one model is missing is a
        /// face that never comes back, since a failure is remembered for half a minute.
        /// </summary>
        private static readonly Dictionary<string, string[]> Wanted =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        private const int QueueMost = 12;

        // ---- the job in flight --------------------------------------------------

        private static string _doing = "";
        private static Ped _model;
        private static int _shot;
        private static int _startedAt;

        // ---- asking -------------------------------------------------------------

        /// <summary>
        /// The texture for somebody, or null while there is not one yet.
        ///
        /// Asking is also what keeps a face alive: the eviction below takes whichever has gone
        /// longest without being asked for, so a face on screen is never the one thrown away.
        /// </summary>
        public static string Txd(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;

            Face face;

            if (Made.TryGetValue(key, out face))
            {
                face.Used = Game.GameTime;
                return face.Txd;
            }

            return null;
        }

        /// <summary>
        /// Whether somebody's picture can actually be drawn this frame.
        ///
        /// ASKED OF THE GAME, NOT OF THE TABLE. Txd says what the picture is called, and a
        /// name is not a texture: the game renders these into a pool of its own and can
        /// have one not ready again -- re-rendering it, or having let it go -- while the
        /// name is still good. A card that trusts the name draws nothing in that frame,
        /// over a square it has stopped putting a letter on, which is a blank square. So
        /// the card asks this before every draw and falls back to the letter on a no.
        ///
        /// The first no after a yes is logged, once a face, with how long the picture had
        /// been up: that line is the difference between the game dropping the picture and
        /// the game dropping the draw.
        /// </summary>
        public static bool Ready(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;

            Face face;
            if (!Made.TryGetValue(key, out face) || face.Shot == 0) return false;

            bool ready;

            try
            {
                ready = Function.Call<bool>(Hash.IS_PEDHEADSHOT_READY, face.Shot)
                        && Function.Call<bool>(Hash.IS_PEDHEADSHOT_VALID, face.Shot);
            }
            catch
            {
                ready = false;
            }

            if (ready)
            {
                face.WasReady = true;
            }
            else if (face.WasReady && !face.Moaned)
            {
                face.Moaned = true;
                Log.Info("Headshot for " + key + " (" + face.Txd + ") is not ready any more, " +
                         (Game.GameTime - face.MadeAt) + "ms after it was made. The letter stands in.");
            }

            return ready;
        }

        /// <summary>
        /// Whether a TEXTURE NAME is one of ours and is good this frame.
        ///
        /// Ready is asked by key -- the account, the person -- because everything that draws
        /// one of these knows whose face it wants. This is asked by the TXD instead, for the
        /// one caller that has been handed a name and no longer knows where it came from:
        /// Notify, which takes a portrait off whoever is sending the message and has to decide
        /// whether it is a CHAR_ dictionary or one of these before it can check it at all.
        ///
        /// A linear walk of a table that holds sixteen. It is called once per message.
        /// </summary>
        public static bool Holds(string txd)
        {
            if (string.IsNullOrEmpty(txd)) return false;

            foreach (var pair in Made)
            {
                if (!string.Equals(pair.Value.Txd, txd, StringComparison.OrdinalIgnoreCase)) continue;

                return Ready(pair.Key);
            }

            return false;
        }

        /// <summary>Asks for one. Does nothing if it exists, is queued, or recently failed.</summary>
        public static void Want(string key, string gang, string gender)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (Made.ContainsKey(key)) return;
            if (Wanted.ContainsKey(key)) return;
            if (string.Equals(key, _doing, StringComparison.OrdinalIgnoreCase)) return;

            int failedAt;

            if (Failed.TryGetValue(key, out failedAt) && Game.GameTime - failedAt < RetryMs) return;

            if (Queue.Count >= QueueMost) return;

            Queue.Add(key);
            Wanted[key] = new[] { Model(key, gang, gender) };
        }

        /// <summary>
        /// Asks for one photographed as a specific thing, rather than as whoever the handle
        /// hashes to.
        ///
        /// For the handful of accounts that are not people. A taxi firm has no face and no
        /// gender and hashing its handle into the street pool would give it somebody else's --
        /// so it names what it wants to look like and the factory does the rest.
        /// </summary>
        public static void WantAs(string key, params string[] models)
        {
            if (string.IsNullOrEmpty(key) || models == null || models.Length == 0) return;
            if (Made.ContainsKey(key)) return;
            if (Wanted.ContainsKey(key)) return;
            if (string.Equals(key, _doing, StringComparison.OrdinalIgnoreCase)) return;

            int failedAt;

            if (Failed.TryGetValue(key, out failedAt) && Game.GameTime - failedAt < RetryMs) return;
            if (Queue.Count >= QueueMost) return;

            Queue.Add(key);
            Wanted[key] = models;
        }

        // ---- the machine --------------------------------------------------------

        /// <summary>One step. Called every frame; does nothing at all when nobody is waiting.</summary>
        public static void Tick()
        {
            try
            {
                if (_doing.Length > 0) { Waiting(); return; }
                if (Queue.Count == 0) return;

                Start();
            }
            catch (Exception ex)
            {
                Log.Debug("Headshot step failed: " + ex.Message);
                Give(false);
            }
        }

        private static void Start()
        {
            var key = Queue[0];
            Queue.RemoveAt(0);

            string[] names;
            if (!Wanted.TryGetValue(key, out names)) return;

            Wanted.Remove(key);

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            // The first of them this build has. A name that is not here is a reason to try the
            // next one, not a reason for somebody to have no face.
            Model model = default(Model);
            var got = false;

            foreach (var name in names)
            {
                model = new Model(name);

                if (!model.IsValid || !model.IsInCdImage || !model.Request(1500)) continue;

                got = true;
                break;
            }

            if (!got)
            {
                Log.Info("Headshot for " + key + ": none of " + names.Length +
                         " model(s) would load. First was " + names[0] + ".");

                Failed[key] = Game.GameTime;
                return;
            }

            // ABOVE THE PLAYER, AND VISIBLE UNTIL HE HAS BEEN PHOTOGRAPHED.
            //
            // THIS IS WHAT WAS WRONG. He was created invisible, on the reasoning that nothing
            // should ever see him -- and the headshot camera is a thing that sees him. An
            // invisible ped photographs as an empty frame, so every texture came back blank
            // and every avatar fell through to the coloured letter. The log said the picture
            // had been made because it had; there was simply nothing in it.
            //
            // So he is visible for the half second it takes and hidden the moment it is done.
            // Thirty metres up, frozen and without collision: he cannot fall, cannot land on
            // anybody, and is a speck straight above your head in the fraction of a second
            // anybody would have to be looking up to catch him.
            var at = player.Position + new Vector3(0f, 0f, 30f);

            var handle = Function.Call<int>(Hash.CREATE_PED, 4, model.Hash,
                                            at.X, at.Y, at.Z, 0f, false, false);

            model.MarkAsNoLongerNeeded();

            if (handle == 0)
            {
                Log.Info("Headshot for " + key + ": the ped would not create.");
                Failed[key] = Game.GameTime;
                return;
            }

            _model = Entity.FromHandle(handle) as Ped;

            if (_model == null || !_model.Exists())
            {
                Failed[key] = Game.GameTime;
                return;
            }

            try
            {
                Function.Call(Hash.SET_ENTITY_COLLISION, _model.Handle, false, false);
                Function.Call(Hash.FREEZE_ENTITY_POSITION, _model.Handle, true);
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, _model.Handle, true);
                Function.Call(Hash.SET_ENTITY_INVINCIBLE, _model.Handle, true);
            }
            catch
            {
                // He is gone in a second either way.
            }

            _shot = Function.Call<int>(Hash.REGISTER_PEDHEADSHOT, _model.Handle);

            if (_shot == 0)
            {
                // The pool is full, or the game has decided not to. This is the one that
                // matters: it is how the engine says no, and it says it silently.
                Log.Info("Headshot for " + key + ": REGISTER_PEDHEADSHOT returned nothing. " +
                         Made.Count + " already held.");

                Failed[key] = Game.GameTime;
                Give(false);
                return;
            }

            _doing = key;
            _startedAt = Game.GameTime;
        }

        private static void Waiting()
        {
            if (Game.GameTime - _startedAt > PatienceMs)
            {
                Log.Info("Headshot for " + _doing + " never rendered in " + PatienceMs + "ms.");
                Failed[_doing] = Game.GameTime;
                Give(false);
                return;
            }

            if (!Function.Call<bool>(Hash.IS_PEDHEADSHOT_READY, _shot)) return;
            if (!Function.Call<bool>(Hash.IS_PEDHEADSHOT_VALID, _shot))
            {
                Failed[_doing] = Game.GameTime;
                Give(false);
                return;
            }

            var txd = Function.Call<string>(Hash.GET_PEDHEADSHOT_TXD_STRING, _shot);

            if (string.IsNullOrEmpty(txd))
            {
                Failed[_doing] = Game.GameTime;
                Give(false);
                return;
            }

            Room();

            Log.Info("Headshot for " + _doing + " -> " + txd + ".");

            // Photographed, so he can stop being visible now. He stays alive because the
            // registration is against him -- see Face.Model -- but there is no reason for
            // anybody to be able to see him from here on.
            try { Function.Call(Hash.SET_ENTITY_VISIBLE, _model.Handle, false, false); }
            catch { /* he is thirty metres up either way */ }

            Made[_doing] = new Face
            {
                Shot = _shot,
                Txd = txd,
                Used = Game.GameTime,
                MadeAt = Game.GameTime,
                WasReady = true,
                Model = _model
            };

            // Handed over rather than deleted. See Face.Model.
            _model = null;

            Give(true);
        }

        /// <summary>Ends the job in flight. The registration is kept only on success.</summary>
        private static void Give(bool keep)
        {
            try
            {
                if (_model != null && _model.Exists()) _model.Delete();
            }
            catch
            {
                // Already gone.
            }

            if (!keep && _shot != 0)
            {
                try { Function.Call(Hash.UNREGISTER_PEDHEADSHOT, _shot); }
                catch { /* nothing else to do */ }
            }

            _model = null;
            _shot = 0;
            _doing = "";
        }

        /// <summary>Hands back the face nobody has looked at for the longest.</summary>
        private static void Room()
        {
            while (Made.Count >= Keep)
            {
                var oldest = "";
                var when = int.MaxValue;

                foreach (var pair in Made)
                {
                    if (pair.Value.Used >= when) continue;

                    when = pair.Value.Used;
                    oldest = pair.Key;
                }

                if (oldest.Length == 0) return;

                try
                {
                    Function.Call(Hash.UNREGISTER_PEDHEADSHOT, Made[oldest].Shot);

                    // And the man in the picture goes with it, which is the only moment he can
                    // safely go.
                    if (Made[oldest].Model != null && Made[oldest].Model.Exists())
                    {
                        Made[oldest].Model.Delete();
                    }
                }
                catch
                {
                    // It goes either way.
                }

                Made.Remove(oldest);
            }
        }

        // ---- who looks like what ------------------------------------------------

        /// <summary>
        /// Who looks like whom.
        ///
        /// THE WHOLE POINT OF MAKING A FACE IS THAT IT FITS THE NAME ON IT. A Vagos handle
        /// coming back as a man from Vinewood is the same failure as handing him Simeon's
        /// contact photo -- it is the nearest wrong thing, and the coloured letter it replaced
        /// was at least honest about knowing nothing.
        ///
        /// A SET GETS ITS OWN MEMBERS AND NOBODY ELSE, which it did not before. The pools
        /// used to be one per ETHNICITY with the gang models at the top and a dozen civilians
        /// underneath -- so two thirds of every Families or Ballas post came out wearing the
        /// face of a South Central passer-by, and three of those are pensioners. A message
        /// about who runs Grove Street, from a photograph of somebody's nan.
        ///
        /// Now each set draws from the models the game built for that set: Families from the
        /// three Families models, Ballas from the three Ballas ones, the Vagos and the Aztecas
        /// and the Marabunta from their own, the Triads and the Kkangpae from theirs, the
        /// Armenians from theirs, the Lost from theirs. Nobody in a set can wear a face from
        /// another set, and no gang post can come from a civilian.
        ///
        /// WHERE THE GAME HAS NO WOMAN FOR A SET, and it usually has not, the fallback is the
        /// nearest thing by neighbourhood rather than the street at large -- Koreatown for the
        /// two East Asian sets, East Los Santos for the Armenians. One model is one model:
        /// every woman in the Families wears the same face, because there is one of her.
        ///
        /// Everybody with no set draws from the street, because that is what they are.
        /// </summary>
        private static readonly string[] Families =
        {
            "g_m_y_famca_01", "g_m_y_famdnf_01", "g_m_y_famfor_01"
        };

        private static readonly string[] FamiliesWomen = { "g_f_y_families_01" };

        private static readonly string[] Ballas =
        {
            "g_m_y_ballaeast_01", "g_m_y_ballaorig_01", "g_m_y_ballasout_01"
        };

        private static readonly string[] BallasWomen = { "g_f_y_ballas_01" };

        private static readonly string[] Vagos =
        {
            "g_m_y_mexgoon_01", "g_m_y_mexgoon_02", "g_m_y_mexgoon_03",
            "g_m_y_mexgang_01", "g_m_m_mexboss_01", "g_m_m_mexboss_02"
        };

        private static readonly string[] Aztecas =
        {
            "g_m_y_azteca_01", "g_m_y_pologoon_01", "g_m_y_pologoon_02"
        };

        private static readonly string[] Marabunta =
        {
            "g_m_y_salvaboss_01", "g_m_y_salvagoon_01", "g_m_y_salvagoon_02",
            "g_m_y_salvagoon_03", "g_m_m_maragrande_01"
        };

        /// <summary>One model between the three Latino sets, because the game shipped one.</summary>
        private static readonly string[] LatinaWomen = { "g_f_y_vagos_01" };

        private static readonly string[] Triads =
        {
            "g_m_m_chiboss_01", "g_m_m_chicold_01", "g_m_m_chigoon_01", "g_m_m_chigoon_02"
        };

        private static readonly string[] Koreans =
        {
            "g_m_m_korboss_01", "g_m_y_korean_01", "g_m_y_korean_02", "g_m_y_korlieut_01"
        };

        /// <summary>No gang women in either East Asian set, so Koreatown rather than the street.</summary>
        private static readonly string[] AsianWomen = { "a_f_m_ktown_01", "a_f_m_ktown_02" };

        private static readonly string[] Armenians =
        {
            "g_m_m_armboss_01", "g_m_m_armgoon_01", "g_m_m_armlieut_01", "g_m_y_armgoon_02"
        };

        /// <summary>None of theirs either. East Los Santos, which is the nearest the game has.</summary>
        private static readonly string[] ArmenianWomen = { "a_f_m_eastsa_01", "a_f_y_eastsa_01" };

        /// <summary>
        /// The Lost, and the county they ride in.
        ///
        /// The two hillbillies are deliberate and are the only civilians left in a gang pool:
        /// the Lost are not a street set with a colour, they are a club in Blaine County, and
        /// a man in a trucker cap belongs in that photograph in a way a South Central civilian
        /// never belonged in the Families'.
        /// </summary>
        private static readonly string[] Lost =
        {
            "g_m_y_lost_01", "g_m_y_lost_02", "g_m_y_lost_03",
            "a_m_m_hillbilly_01", "a_m_m_hillbilly_02"
        };

        private static readonly string[] LostWomen = { "g_f_y_lost_01", "a_f_y_rurmeth_01" };

        /// <summary>
        /// Everybody with no set: the street, and the whole street.
        ///
        /// The one place a passer-by belongs, because a neighbour with an opinion about the
        /// price of chicken IS a passer-by. a_f_y_latino_01 came out of here on the way past:
        /// there is no such model on this build, so every woman who hashed onto it had no
        /// face at all and the log said so once a session.
        /// </summary>
        private static readonly string[] AnyMen =
        {
            "a_m_y_hipster_01", "a_m_y_hipster_02", "a_m_y_business_01", "a_m_m_business_01",
            "a_m_y_ktown_01", "a_m_m_eastsa_01", "a_m_y_stwhi_01", "a_m_y_downtown_01",
            "a_m_m_hillbilly_01", "a_m_y_vinewood_01", "a_m_y_beach_01",
            "a_m_y_soucent_01", "a_m_m_soucent_02"
        };

        private static readonly string[] AnyWomen =
        {
            "a_f_y_hipster_02", "a_f_y_business_01", "a_f_m_business_02",
            "a_f_y_vinewood_01", "a_f_y_eastsa_01", "a_f_m_eastsa_01", "a_f_y_beach_01",
            "a_f_y_soucent_01", "a_f_m_soucent_02"
        };

        /// <summary>The pool a given set draws from.</summary>
        private static string[] Pool(string gang, bool she)
        {
            switch ((gang ?? "").ToLowerInvariant())
            {
                case "families": return she ? FamiliesWomen : Families;
                case "ballas": return she ? BallasWomen : Ballas;

                case "vagos": return she ? LatinaWomen : Vagos;
                case "aztecas": return she ? LatinaWomen : Aztecas;
                case "marabunta": return she ? LatinaWomen : Marabunta;

                case "triads": return she ? AsianWomen : Triads;
                case "koreans": return she ? AsianWomen : Koreans;

                case "armenians": return she ? ArmenianWomen : Armenians;

                case "lost": return she ? LostWomen : Lost;

                default:
                    return she ? AnyWomen : AnyMen;
            }
        }

        /// <summary>
        /// Which model this handle wears, forever.
        ///
        /// Hashed rather than random and hashed rather than stored. The same handle lands on
        /// the same model every time it is asked, on any machine, with nothing written down --
        /// so a face is persistent without ever being saved, and adding a person to the feed
        /// does not shuffle everybody else's.
        /// </summary>
        private static string Model(string key, string gang, string gender)
        {
            var she = string.Equals(gender, "female", StringComparison.OrdinalIgnoreCase);
            var pool = Pool(gang, she);

            return pool[(int)(Hash32(key) % (uint)pool.Length)];
        }

        /// <summary>FNV-1a. Any stable hash would do; this one is short and has no surprises.</summary>
        private static uint Hash32(string s)
        {
            var h = 2166136261u;

            foreach (var c in s ?? "")
            {
                h ^= char.ToLowerInvariant(c);
                h *= 16777619u;
            }

            return h;
        }

        // ---- teardown -----------------------------------------------------------

        public static void Clear()
        {
            Give(false);

            foreach (var pair in Made)
            {
                try
                {
                    Function.Call(Hash.UNREGISTER_PEDHEADSHOT, pair.Value.Shot);

                    if (pair.Value.Model != null && pair.Value.Model.Exists())
                    {
                        pair.Value.Model.Delete();
                    }
                }
                catch
                {
                    // Teardown.
                }
            }

            Made.Clear();
            Failed.Clear();
            Queue.Clear();
            Wanted.Clear();
        }
    }
}
