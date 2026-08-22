using System;
using System.Collections.Generic;
using System.IO;
using Hoodrich.Core;

namespace Hoodrich.Missions
{
    /// <summary>What kind of work it is, which decides how it plays out.</summary>
    internal enum MissionKind
    {
        /// <summary>Ride out with the homies and put hands on somebody. Fists, both sides.</summary>
        RideOut,

        /// <summary>Same trip, but everybody is carrying and the corner shoots back.</summary>
        Hit,

        /// <summary>Take a car past a rival corner and shoot it up.</summary>
        DriveBy,

        /// <summary>
        /// A drive-by you have to clean up after.
        ///
        /// Shoot up the corner, and it is done the moment one of them notices you -- it is a
        /// message, not a body count. Then the law wants you, and the car you did it in is
        /// evidence: drive it somewhere quiet, get out, and burn it.
        /// </summary>
        TorchJob,

        /// <summary>
        /// The push-bike job: ride out together, a straightener at the courts, a drink
        /// afterwards, and ride back. Scripted end to end rather than assembled from a site
        /// and a target count, because the shape of it IS the job.
        /// </summary>
        BikeRide,

        /// <summary>
        /// Going round rival blocks putting your set over theirs. Targets is how many walls.
        /// </summary>
        Tags
    }

    /// <summary>One job Lamar can put your way.</summary>
    internal sealed class MissionDef
    {
        public string Id = "";
        public MissionKind Kind = MissionKind.RideOut;

        /// <summary>Short name, shown as the choice in his dialogue.</summary>
        public string Name = "";

        /// <summary>What he says when he explains it.</summary>
        public string Brief = "";

        /// <summary>
        /// The rest of it, one beat at a time.
        ///
        /// A job with five stages cannot be explained in one paragraph -- and the why has to
        /// come before the what, or it is a fetch quest with an accent on it. Each entry is
        /// another thing he says, and you press on through them.
        /// </summary>
        public readonly List<string> BriefMore = new List<string>();

        /// <summary>Line when it lands.</summary>
        public string Done = "";

        /// <summary>Gang whose people you are going after.</summary>
        public string TargetGang = "";

        /// <summary>
        /// Zone the job happens in. A zone rather than a coordinate on purpose: the mod already
        /// knows where every zone is, and a job that finds its own patch of pavement cannot be
        /// authored into a wall.
        /// </summary>
        public string Zone = "";

        /// <summary>
        /// Exact spot, when one is given. Zero falls back to the zone centre.
        ///
        /// A zone centre is the middle of a neighbourhood, which is not the same as the corner
        /// somebody actually stands on -- and for a big zone it can be a block away from the
        /// gang whose block it is meant to be.
        /// </summary>
        public float X;
        public float Y;
        public float Z;

        public int Targets = 3;

        /// <summary>
        /// Where the car and the people who ride in it are left, for the jobs that need one.
        ///
        /// Its own coordinate rather than "near the player", because a drive-by starts with
        /// walking round the back to where the car is, and that walk is part of the job.
        /// </summary>
        public float CarX;
        public float CarY;
        public float CarZ;
        public float CarHeading;

        /// <summary>
        /// Whether the law turns up on the way home.
        ///
        /// Put on the job rather than derived from the kind, because whether a piece of work
        /// draws police is a property of the work, not of how you did it -- and it is what
        /// makes the trip back to Lamar a part of the job instead of a walk.
        /// </summary>
        public bool EscapeHeat;

        /// <summary>Stars applied when the work is done, if EscapeHeat is set.</summary>
        public int HeatStars = 2;
        public int PayMin = 600;
        public int PayMax = 1200;
        public float Rep = 40f;
        public int MinRank;

        /// <summary>
        /// The hours this job can be taken in, or -1 for any.
        ///
        /// On the definition rather than in the code because it is a fact about ONE job: the
        /// bike ride finishes inside a shop, and a shop with the shutter down is a mission that
        /// cannot be completed no matter how well it is played. Every other job happens in the
        /// street and the street is open all night.
        ///
        /// The window may wrap past midnight -- open 6, shut 2 is eight hours of the night on
        /// the far side of the day boundary -- so it is read as a window and not as a range.
        /// </summary>
        public int OpensHour = -1;
        public int ClosesHour = -1;

        /// <summary>Whether the clock is inside that window right now.</summary>
        public bool OpenNow(int hour)
        {
            if (OpensHour < 0 || ClosesHour < 0) return true;
            if (OpensHour == ClosesHour) return true;

            return ClosesHour > OpensHour
                ? hour >= OpensHour && hour < ClosesHour
                : hour >= OpensHour || hour < ClosesHour;
        }

        /// <summary>An hour of the clock as somebody would say it out loud.</summary>
        public static string Clock(int hour)
        {
            hour = ((hour % 24) + 24) % 24;

            if (hour == 0) return "midnight";
            if (hour == 12) return "midday";

            return hour < 12
                ? hour.ToString() + " in the mornin"
                : (hour - 12).ToString() + " at night";
        }

        /// <summary>Homies who ride with you.</summary>
        public int Homies = 2;

        /// <summary>
        /// Where the car gets left, for a <see cref="MissionKind.TorchJob"/>.
        ///
        /// Somewhere you would actually leave a car nobody should find, which in practice means
        /// off the road and out of sight rather than a marker in a street.
        /// </summary>
        public float DumpX;
        public float DumpY;
        public float DumpZ;

        /// <summary>
        /// What you do the job in.
        ///
        /// Named per mission rather than picked from a pool, because on a job that ends with
        /// the car on fire it matters that it looked disposable from the moment you got in. A
        /// clean car nobody minds burning is a different story from a rusty one.
        /// </summary>
        public string CarModel = "";

        public override string ToString() => Id;
    }

    /// <summary>The work Lamar has going, loaded from missions.json.</summary>
    internal sealed class MissionBook
    {
        private readonly List<MissionDef> _all = new List<MissionDef>();

        public IReadOnlyList<MissionDef> All => _all;

        public MissionDef Get(string id)
        {
            return _all.Find(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public static MissionBook Load()
        {
            var missions = new MissionBook();

            var doc = JsonFile.Read(Path.Combine(Paths.Data, "missions.json"));
            if (doc == null)
            {
                Log.Warn("No missions.json; Lamar will have nothing to offer.");
                return missions;
            }

            foreach (var node in doc["missions"].Items)
            {
                var id = node["id"].AsString("");
                if (string.IsNullOrEmpty(id)) continue;

                var def = new MissionDef
                {
                    Id = id,
                    Name = node["name"].AsString(id),
                    Brief = node["brief"].AsString(""),
                    Done = node["done"].AsString(""),
                    TargetGang = node["targetGang"].AsString(""),
                    Zone = node["zone"].AsString(""),
                    X = node["x"].AsFloat(),
                    Y = node["y"].AsFloat(),
                    Z = node["z"].AsFloat(),
                    Targets = Math.Max(1, node["targets"].AsInt(3)),
                    DumpX = node["dumpX"].AsFloat(),
                    DumpY = node["dumpY"].AsFloat(),
                    DumpZ = node["dumpZ"].AsFloat(),
                    CarModel = node["carModel"].AsString(""),

                    CarX = node["carX"].AsFloat(),
                    CarY = node["carY"].AsFloat(),
                    CarZ = node["carZ"].AsFloat(),
                    CarHeading = node["carHeading"].AsFloat(),
                    EscapeHeat = node["escapeHeat"].AsBool(false),
                    HeatStars = Math.Max(1, Math.Min(5, node["heatStars"].AsInt(2))),
                    PayMin = Math.Max(0, node["payMin"].AsInt(600)),
                    PayMax = Math.Max(0, node["payMax"].AsInt(1200)),
                    Rep = Math.Max(0f, node["rep"].AsFloat(40f)),
                    MinRank = Math.Max(0, node["minRank"].AsInt(0)),
                    OpensHour = node["opensHour"].AsInt(-1),
                    ClosesHour = node["closesHour"].AsInt(-1),
                    Homies = Math.Max(0, node["homies"].AsInt(2))
                };

                var kind = node["kind"].AsString(def.Kind.ToString());
                try { def.Kind = (MissionKind)Enum.Parse(typeof(MissionKind), kind, true); }
                catch { Log.Warn("Unknown mission kind '" + kind + "' on " + id + "."); }

                var more = node["briefMore"];
                if (more.Kind == JsonKind.Array)
                {
                    foreach (var line in more.AsStringList()) def.BriefMore.Add(line);
                }

                if (def.PayMax < def.PayMin) def.PayMax = def.PayMin;

                _allAdd(missions, def);
            }

            Log.Info("Missions loaded: " + missions._all.Count + ".");
            return missions;
        }

        private static void _allAdd(MissionBook m, MissionDef def) => m._all.Add(def);
    }
}
