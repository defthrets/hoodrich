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
        /// like the feature quietly not working for some people. Fourteen is comfortably below
        /// every figure anybody quotes and is twice what a screenful needs.
        /// </summary>
        private const int Keep = 14;

        /// <summary>How long one is given to render before it is written off.</summary>
        private const int PatienceMs = 4000;

        /// <summary>And how long before a failed one is worth trying again.</summary>
        private const int RetryMs = 30000;

        private sealed class Face
        {
            public int Shot;
            public string Txd = "";
            public int Used;
        }

        private static readonly Dictionary<string, Face> Made =
            new Dictionary<string, Face>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, int> Failed =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Who is waiting, oldest first. Bounded, so a scroll cannot pile up work.</summary>
        private static readonly List<string> Queue = new List<string>();

        private static readonly Dictionary<string, string> Wanted =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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
            Wanted[key] = Model(key, gang, gender);
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

            string modelName;
            if (!Wanted.TryGetValue(key, out modelName)) return;

            Wanted.Remove(key);

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            var model = new Model(modelName);

            if (!model.IsValid || !model.IsInCdImage || !model.Request(1500))
            {
                Failed[key] = Game.GameTime;
                return;
            }

            // ABOVE THE PLAYER AND OUT OF EVERYTHING. He exists for about a second and the
            // only thing that must be true of him is that the game is willing to render him:
            // frozen so he cannot fall, no collision so he cannot land on anybody, and
            // invisible so that if any of the above fails he is still not a man standing in
            // the sky over Chamberlain Hills.
            var at = player.Position + new Vector3(0f, 0f, 30f);

            var handle = Function.Call<int>(Hash.CREATE_PED, 4, model.Hash,
                                            at.X, at.Y, at.Z, 0f, false, false);

            model.MarkAsNoLongerNeeded();

            if (handle == 0)
            {
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
                Function.Call(Hash.SET_ENTITY_VISIBLE, _model.Handle, false, false);
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
                Log.Debug("Headshot for " + _doing + " never rendered.");
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

            Made[_doing] = new Face { Shot = _shot, Txd = txd, Used = Game.GameTime };

            // The ped goes and the PICTURE STAYS. That is the whole trick -- the texture
            // belongs to the registration, not to the man, and unregistering is what frees it.
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

                try { Function.Call(Hash.UNREGISTER_PEDHEADSHOT, Made[oldest].Shot); }
                catch { /* it goes either way */ }

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
        /// So the pool is chosen by the set, and the sets in this game are drawn along lines
        /// the models follow: the Families and the Ballas are South Central, the Vagos and the
        /// Aztecas are Latino, the Triads and the Kkangpae are East Asian, the Armenians are
        /// eastern European, the Lost are bikers. Those are the game's own model families and
        /// picking from them is the difference between a face and a photograph of a stranger.
        ///
        /// Everybody with no set draws from the street, because that is what they are.
        /// </summary>
        private static readonly string[] SouthCentralMen =
        {
            "g_m_y_famca_01", "g_m_y_famdnf_01", "g_m_y_famfor_01",
            "g_m_y_ballaeast_01", "g_m_y_ballaorig_01", "g_m_y_ballasout_01",
            "a_m_y_soucent_01", "a_m_y_soucent_02", "a_m_y_soucent_03", "a_m_y_soucent_04",
            "a_m_m_soucent_01", "a_m_m_soucent_02", "a_m_m_soucent_03", "a_m_m_soucent_04",
            "a_m_o_soucent_01", "a_m_o_soucent_02", "a_m_o_soucent_03"
        };

        private static readonly string[] SouthCentralWomen =
        {
            "g_f_y_families_01", "g_f_y_ballas_01",
            "a_f_y_soucent_01", "a_f_y_soucent_02", "a_f_y_soucent_03",
            "a_f_m_soucent_01", "a_f_m_soucent_02", "a_f_o_soucent_01"
        };

        private static readonly string[] LatinoMen =
        {
            "g_m_y_mexgoon_01", "g_m_y_mexgoon_02", "g_m_y_mexgoon_03",
            "g_m_y_azteca_01", "g_m_m_mexboss_01", "g_m_m_mexboss_02",
            "a_m_y_mexthug_01", "a_m_y_latino_01", "a_m_m_mexlabor_01",
            "a_m_o_soucent_02", "a_m_y_gencaspat_01"
        };

        private static readonly string[] LatinoWomen =
        {
            "a_f_y_latino_01", "a_f_m_ktown_02", "a_f_y_eastsa_01",
            "a_f_m_eastsa_01", "a_f_y_soucent_02"
        };

        private static readonly string[] AsianMen =
        {
            "g_m_m_chiboss_01", "g_m_m_chicold_01", "g_m_m_chigoon_01", "g_m_m_chigoon_02",
            "g_m_y_korean_01", "g_m_y_korean_02", "g_m_y_korlieut_01",
            "a_m_y_ktown_01", "a_m_y_ktown_02", "a_m_m_ktown_01", "a_m_o_ktown_01"
        };

        private static readonly string[] AsianWomen =
        {
            "a_f_y_ktown_01", "a_f_m_ktown_01", "a_f_m_ktown_02", "a_f_y_eastsa_02"
        };

        private static readonly string[] ArmenianMen =
        {
            "g_m_m_armboss_01", "g_m_m_armgoon_01", "g_m_m_armlieut_01", "g_m_y_armgoon_02",
            "a_m_m_eastsa_01", "a_m_m_eastsa_02", "a_m_y_business_01"
        };

        private static readonly string[] BikerMen =
        {
            "g_m_y_lost_01", "g_m_y_lost_02", "g_m_y_lost_03",
            "a_m_m_hillbilly_01", "a_m_m_hillbilly_02", "a_m_y_methhead_01",
            "a_m_m_trampbeac_01", "a_m_y_motox_01"
        };

        private static readonly string[] BikerWomen =
        {
            "g_f_y_lost_01", "a_f_y_hipster_04", "a_f_m_fatwhite_01", "a_f_y_rurmeth_01"
        };

        private static readonly string[] AnyMen =
        {
            "a_m_y_hipster_01", "a_m_y_hipster_02", "a_m_y_business_01", "a_m_m_business_01",
            "a_m_y_ktown_01", "a_m_y_latino_01", "a_m_m_eastsa_01", "a_m_y_stwhi_01",
            "a_m_y_downtown_01", "a_m_m_hillbilly_01", "a_m_y_vinewood_01", "a_m_y_beach_01",
            "a_m_y_soucent_01", "a_m_m_soucent_02"
        };

        private static readonly string[] AnyWomen =
        {
            "a_f_y_hipster_02", "a_f_y_business_01", "a_f_m_business_02", "a_f_y_latino_01",
            "a_f_y_vinewood_01", "a_f_y_eastsa_01", "a_f_m_eastsa_01", "a_f_y_beach_01",
            "a_f_y_soucent_01", "a_f_m_soucent_02"
        };

        /// <summary>The pool a given set draws from.</summary>
        private static string[] Pool(string gang, bool she)
        {
            switch ((gang ?? "").ToLowerInvariant())
            {
                case "families":
                case "ballas":
                    return she ? SouthCentralWomen : SouthCentralMen;

                case "vagos":
                case "aztecas":
                case "marabunta":
                    return she ? LatinoWomen : LatinoMen;

                case "triads":
                case "koreans":
                    return she ? AsianWomen : AsianMen;

                // No women's models in the Armenian family, so theirs fall through to the
                // street rather than to somebody who plainly is not one of them.
                case "armenians":
                    return she ? AnyWomen : ArmenianMen;

                case "lost":
                    return she ? BikerWomen : BikerMen;

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
                try { Function.Call(Hash.UNREGISTER_PEDHEADSHOT, pair.Value.Shot); }
                catch { /* teardown */ }
            }

            Made.Clear();
            Failed.Clear();
            Queue.Clear();
            Wanted.Clear();
        }
    }
}
