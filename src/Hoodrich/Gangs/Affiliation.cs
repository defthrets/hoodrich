using System;
using System.Collections.Generic;
using GTA;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Social;
using Hoodrich.Territory;
using Hoodrich.UI;

namespace Hoodrich.Gangs
{
    /// <summary>
    /// Who the player runs with, what that is worth, and what it costs.
    ///
    /// Relationship changes here are deliberately narrow. Affiliating makes YOUR gang respect
    /// you globally -- that is what "they help you on the street" means -- but it does NOT flip
    /// every rival to hate-on-sight, which would make the world unplayable. Rival aggression is
    /// situational and targeted, and lives in <see cref="Hoodrich.Territory.TurfWatch"/>.
    /// </summary>
    internal sealed class Affiliation
    {
        /// <summary>
        /// The SET_RELATIONSHIP_BETWEEN_GROUPS scale, in full.
        ///
        /// Only Respect is currently read. The rest are kept deliberately: the point of a
        /// numbered scale is being able to see where the number you are using sits on it, and a
        /// lone "= 1" with nothing around it is a magic number waiting to be got wrong.
        /// </summary>
        private const int RelCompanion = 0;
        private const int RelRespect = 1;
        private const int RelLike = 2;
        private const int RelNeutral = 3;
        private const int RelDislike = 4;
        private const int RelHate = 5;

        /// <summary>Relationships get reapplied on this cadence; other scripts can stomp them.</summary>
        private const int ReapplyIntervalMs = 15_000;

        /// <summary>Set by Main. Null-checked everywhere, so the feed is never load-bearing.</summary>
        public SocialFeed Social;

        /// <summary>
        /// Raised for every ped of somebody else's set that you drop, beef or no beef.
        ///
        /// Set by Main so the war system can watch for a provocation. Deliberately fired for
        /// gangs you are on good terms with too: walking into a quiet set's block and dropping
        /// three of them is the whole point of the mechanic, and gating it on existing beef
        /// would mean you could only start a war with somebody you were already fighting.
        /// </summary>
        public Action<GangDef> RivalDropped;


        private const float AllyScanRadius = 45f;
        private const int KillScanIntervalMs = 1200;

        /// <summary>Each nearby ally adds this to the sale price, up to <see cref="MaxLookoutBonus"/>.</summary>
        private const float LookoutBonusPerAlly = 0.04f;
        private const float MaxLookoutBonus = 0.20f;

        private readonly GangRegistry _gangs;
        private readonly Dictionary<string, GangStanding> _standings =
            new Dictionary<string, GangStanding>(StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<int> _countedKills = new HashSet<int>();

        private int _playerGroupHash;
        private int _lastReapply;
        private int _lastKillScan;
        private int _lastAllyCount;

        /// <summary>Groups we have altered, so they can be put back on unload.</summary>
        private readonly HashSet<int> _touchedGroups = new HashSet<int>();

        /// <summary>
        /// The set Franklin is from, whatever the join menu says.
        ///
        /// He grew up on it. Lamar is on it. There is no state of this mod in which a CGF
        /// soldier should be squaring up to him on his own street, and until now there was --
        /// the friendly relationship was only applied if you had gone through the join menu,
        /// so before that his own people challenged him like any stranger on a corner.
        /// </summary>
        private const string HomeSet = "families";

        public Affiliation(GangRegistry gangs)
        {
            _gangs = gangs;
            try
            {
                _playerGroupHash = Function.Call<int>(Hash.GET_HASH_KEY, "PLAYER");
            }
            catch (Exception ex)
            {
                Log.Error("Could not resolve the PLAYER relationship group.", ex);
            }

            RespectHome();
        }

        /// <summary>
        /// Puts the home set on good terms with the player and keeps it there.
        ///
        /// Cheap and idempotent, so it is safe to call from the same timer that re-applies the
        /// joined gang's relationship -- which it has to be, because the game resets ambient
        /// relationship groups on its own and a one-off call at startup quietly stops holding
        /// after the first time you leave the area and come back.
        /// </summary>
        /// <summary>
        /// Talks his own set down when one of them has started on him anyway.
        ///
        /// Companion decides whether somebody picks a fight. It has nothing to say once they
        /// have -- and they will, because a shot fired near a ped, or being walked into, raises
        /// an event that can put a man into combat regardless of how he feels about you. Once
        /// he is in it he stays in it, and the relationship that should have prevented it never
        /// gets consulted again.
        ///
        /// So anybody in the home set who is currently fighting the player is taken out of it.
        /// Their tasks are cleared and the relationship is re-asserted, which between them ends
        /// the fight and stops the next one starting for the same reason.
        ///
        /// Deliberately narrow: only the home set, only peds actually targeting the player, and
        /// only within earshot. It is not a general "nobody may fight Franklin" switch, and a
        /// rival who wants him still gets him.
        /// </summary>
        private void CalmHome()
        {
            if (_playerGroupHash == 0 || _gangs == null) return;

            var now = Game.GameTime;
            if (now - _lastCalm < CalmIntervalMs) return;
            _lastCalm = now;

            try
            {
                var home = _gangs.Get(HomeSet);
                if (home == null || home.GroupHash == 0) return;

                var player = Game.Player.Character;
                if (player == null || !player.Exists() || !player.IsAlive) return;

                foreach (var ped in World.GetNearbyPeds(player, CalmRadius))
                {
                    if (ped == null || !ped.Exists() || !ped.IsAlive) continue;
                    if (ped.Handle == player.Handle) continue;

                    var group = Function.Call<int>(Hash.GET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle);
                    if (group != home.GroupHash) continue;

                    // ---- said to EVERY one of ours near him, not just the ones swinging ----
                    //
                    // The relationship has been Companion both ways since the constructor and
                    // it was never the gap. What starts these fights is events: a round fired
                    // nearby, a shoulder, a car door. An event puts a man into combat on its
                    // own account and the relationship that should have prevented it is never
                    // consulted again -- so this used to arrive after the first punch, every
                    // time, and only tidied up.
                    //
                    // Now nobody in his own set is carrying the two attributes that make a man
                    // go looking, and none of them counts him as an enemy to begin with.
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 5, false);
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 46, false);
                    Function.Call(Hash.SET_PED_AS_ENEMY, ped.Handle, false);
                    Function.Call(Hash.SET_CAN_ATTACK_FRIENDLY, ped.Handle, false, false);

                    // AND THEY STOP TALKING TO HIM LIKE A STRANGER.
                    //
                    // "You're in the wrong neighbourhood" is the game's own gang challenge, and
                    // it runs off the ambient gang system rather than off any relationship --
                    // which is why being on Companion terms never silenced it. These are his
                    // people on his own block; there is no version of this where they warn him
                    // off it.
                    //
                    // Our own chatter is unaffected: BlockTalk and BlockLife lift this the
                    // instant before they speak, and a line already playing is not cut by it.
                    Function.Call(Hash.BLOCK_ALL_SPEECH_FROM_PED, ped.Handle, true, false);

                    var target = Function.Call<int>(Hash.GET_PED_TARGET_FROM_COMBAT_PED, ped.Handle, 0);
                    if (target != player.Handle) continue;

                    Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);
                    Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle, home.GroupHash);

                    Log.Debug("Talked one of ours out of fighting Franklin.");
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not calm the home set: " + ex.Message);
            }
        }

        /// <summary>
        /// Nobody of ours trades shots with the police.
        ///
        /// THEY ARE NOT AN ARMY AND THE POLICE ARE NOT A RIVAL SET. A corner man who lets off
        /// at a squad car turns a block into a siege, and every one of them is armed now, so
        /// one patrol rolling past a hangout was the whole night gone. What people on a corner
        /// actually do when the police come is leave.
        ///
        /// So: anybody wearing one of the mod's gang groups who has decided to fight an
        /// officer is taken out of it and sent away instead. Fleeing rather than merely
        /// stopping, because a man who stands still through a stop is a man being arrested,
        /// and the ones this covers are stood on a corner holding something.
        ///
        /// NOT THE HOMIES. The two men riding with you do not scatter the moment a siren goes
        /// past -- they are with you and they stay with you. Homies minds its own for the same
        /// rule with the other half of it: see Gangs.Minded and Homies.Lawful.
        ///
        /// The player is never touched by this. His own trouble is his own.
        /// </summary>
        private void Lawful()
        {
            if (_gangs == null) return;

            var now = Game.GameTime;
            if (now - _lastLawful < LawfulIntervalMs) return;
            _lastLawful = now;

            try
            {
                var player = Game.Player.Character;
                if (player == null || !player.Exists()) return;

                foreach (var ped in World.GetNearbyPeds(player, LawfulRange))
                {
                    if (ped == null || !ped.Exists() || !ped.IsAlive) continue;
                    if (ped.Handle == player.Handle) continue;
                    if (Gangs.Minded.Is(ped)) continue;

                    var group = Function.Call<int>(Hash.GET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle);
                    if (_gangs.ByGroupHash(group) == null) continue;

                    var target = Function.Call<int>(Hash.GET_PED_TARGET_FROM_COMBAT_PED, ped.Handle, 0);
                    if (target == 0) continue;

                    var him = Entity.FromHandle(target) as Ped;
                    if (him == null || !him.Exists() || !Law(him)) continue;

                    // HIT FIRST IS A DIFFERENT FIGHT, and this is the whole of the rule.
                    //
                    // Not starting one with the police is what somebody stood on a corner
                    // does. Standing there being shot at and running anyway is not -- it is a
                    // man walking away from a gun already pointed at him, which reads as the
                    // mod taking the fight off him rather than as him choosing not to have it.
                    //
                    // So the question is only ever asked about a fight he started. Once an
                    // officer has actually put a round in him he is off this rule for good and
                    // the game has him back.
                    if (Struck(ped, him)) continue;

                    Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);
                    Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, ped.Handle, 0, true);
                    Function.Call(Hash.TASK_SMART_FLEE_PED, ped.Handle, him.Handle, 120f, -1, false, false);
                    Function.Call(Hash.SET_PED_KEEP_TASK, ped.Handle, true);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not talk ours out of a shootout with the law: " + ex.Message);
            }
        }

        /// <summary>Whether this one is the law. Police, SWAT and the army all count.</summary>
        internal static bool Law(Ped who)
        {
            if (who == null || !who.Exists()) return false;

            try
            {
                var kind = Function.Call<int>(Hash.GET_PED_TYPE, who.Handle);
                return kind == 6 || kind == 27 || kind == 29;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Whether the law has actually hit him, and whether it ever did.
        ///
        /// REMEMBERED RATHER THAN ASKED FRESH. The game's own record is per PAIR -- has this
        /// ped been damaged by THAT one -- so a second officer arriving at a man already
        /// bleeding would read as unprovoked and send him running mid-gunfight. Once anybody
        /// wearing a badge has hit him, he is out of this rule entirely.
        ///
        /// Handles are reused by the game, so this is emptied with everything else on teardown
        /// -- see RestoreWorld. A stale handle only ever costs one man one flee.
        /// </summary>
        private readonly HashSet<int> _struck = new HashSet<int>();

        private bool Struck(Ped ped, Ped law)
        {
            if (ped == null || !ped.Exists()) return false;

            if (_struck.Contains(ped.Handle)) return true;

            try
            {
                if (!Function.Call<bool>(Hash.HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY,
                                         ped.Handle, law.Handle, true)) return false;
            }
            catch
            {
                return false;
            }

            _struck.Add(ped.Handle);

            return true;
        }

        private int _lastLawful;

        /// <summary>How often, and how far out. Cheaper than CalmHome because it matters less quickly.</summary>
        private const int LawfulIntervalMs = 1200;
        private const float LawfulRange = 90f;

        /// <summary>Often enough to end a fight before it lands, cheap enough to run always.</summary>
        private const int CalmIntervalMs = 900;

        /// <summary>Only people close enough to be fighting him.</summary>
        private const float CalmRadius = 40f;

        private int _lastCalm;

        private void RespectHome()
        {
            if (_playerGroupHash == 0 || _gangs == null) return;

            try
            {
                var home = _gangs.Get(HomeSet);
                if (home == null || home.GroupHash == 0) return;

                SetBoth(RelCompanion, home.GroupHash, _playerGroupHash);
                _touchedGroups.Add(home.GroupHash);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not set the home set relationship: " + ex.Message);
            }
        }

        /// <summary>The gang the player currently runs with, or null.</summary>
        public GangDef Current { get; private set; }

        /// <summary>The one loan the player can have running, or null.</summary>

        public bool IsAffiliated => Current != null;

        /// <summary>Allies seen within <see cref="AllyScanRadius"/> on the last scan.</summary>
        public int NearbyAllies => _lastAllyCount;

        /// <summary>Price uplift from having your own people watching your back, as a multiplier.</summary>
        public float LookoutMultiplier => 1f + Math.Min(MaxLookoutBonus, _lastAllyCount * LookoutBonusPerAlly);

        public GangStanding StandingFor(string gangId)
        {
            if (string.IsNullOrEmpty(gangId)) return null;
            if (!_standings.TryGetValue(gangId, out var s))
            {
                s = new GangStanding { GangId = gangId, Rep = SeedFor(gangId) };
                _standings[gangId] = s;
            }
            return s;
        }

        /// <summary>
        /// Where a gang starts before you have done anything to them.
        ///
        /// Nearly everybody starts at nothing -- they have no opinion, because you have given
        /// them no reason to have one. The Ballas and the Vagos do not: Franklin is from
        /// Chamberlain and those two have been at it with his set since long before he picked
        /// up a phone, so they start below the line and are at war from the first frame.
        ///
        /// Done by seeding a number rather than by a special case in the hostility check, so
        /// there is exactly one rule about who wants you dead. It also means those two can be
        /// worked back up, in principle, which is a more interesting world than a list that can
        /// never change.
        /// </summary>
        private static float SeedFor(string gangId)
        {
            switch ((gangId ?? "").ToLowerInvariant())
            {
                case "ballas":
                case "vagos":
                    return BeefAt - 20f;

                default:
                    return 0f;
            }
        }

        /// <summary>
        /// Standing at or below which a gang is at war with you.
        ///
        /// The whole hostility model. Above it they have no particular opinion; below it they
        /// come for you, they turn up in the raid list, and they are who a drive-by comes from.
        /// </summary>
        public const float BeefAt = -30f;

        /// <summary>
        /// Whether this lot want you dead.
        ///
        /// NEVER HIS OWN SET. Their number can still fall -- shooting up your own block has
        /// a cost on the feed and at the counter -- but it never crosses into beef, because
        /// beef is the rule every other system reads to decide who goes for Franklin, and
        /// there is no state of this mod in which that is the Families.
        /// </summary>
        public bool Beefing(string gangId)
        {
            if (string.Equals(gangId, HomeSet, StringComparison.OrdinalIgnoreCase)) return false;

            var s = StandingFor(gangId);
            return s != null && s.Rep <= BeefAt;
        }

        /// <summary>
        /// Everybody currently at war with you, worst first.
        ///
        /// Worst first because the one who hates you most is the one who should be raiding your
        /// blocks and pulling up on your corners, and every caller wants the same answer to
        /// "who is the problem right now".
        /// </summary>
        public List<GangDef> BeefingWith()
        {
            var list = new List<GangDef>();
            if (_gangs == null) return list;

            foreach (var gang in _gangs.All)
            {
                if (gang == null) continue;
                if (Current != null &&
                    string.Equals(gang.Id, Current.Id, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (Beefing(gang.Id)) list.Add(gang);
            }

            list.Sort((a, b) => StandingFor(a.Id).Rep.CompareTo(StandingFor(b.Id).Rep));
            return list;
        }

        /// <summary>
        /// You said something about them in public, and they read it.
        ///
        /// The only way to MAKE an enemy out of somebody who was not one. Enough of it and they
        /// cross the line on their own, without anybody declaring anything.
        /// </summary>
        public void Taunted(string gangId, float by = TauntCost)
        {
            var s = StandingFor(gangId);
            if (s == null) return;

            var before = s.Rep;
            s.Rep = Math.Max(-100f, s.Rep - by);
            s.HostileAt = Game.GameTime;

            Log.Info("Taunted " + gangId + ": " + before.ToString("0") + " -> " +
                     s.Rep.ToString("0") + (before > BeefAt && s.Rep <= BeefAt ? "  (that's beef)" : ""));

            if (before > BeefAt && s.Rep <= BeefAt)
            {
                var gang = _gangs == null ? null : _gangs.Get(gangId);
                Notify.Failure("that's beef with " + (gang == null ? gangId : gang.Name) + " now.");
            }
        }

        /// <summary>What one post about somebody costs you with them.</summary>
        public const float TauntCost = 12f;

        public GangStanding CurrentStanding => Current == null ? null : StandingFor(Current.Id);

        /// <summary>Registry lookup, exposed so callers do not need their own GangRegistry reference.</summary>
        public GangDef GangById(string gangId) => _gangs.Get(gangId);

        // ---- joining and leaving -----------------------------------------------

        /// <summary>Returns a player-facing refusal, or null on success.</summary>
        public string Join(GangDef gang, float playerRespect)
        {
            if (gang == null) return "No such gang.";
            if (Current != null && Current.Id == gang.Id) return "You already run with " + gang.Name + ".";

            // Checked here rather than only on the screen that offers it. LeaderTalk already
            // hides the option for everybody but the Families, but a rule that only exists in
            // the menu is a rule anything else calling this can walk straight through.
            if (!gang.Joinable) return gang.Name + " don't take people on.";

            var standing = StandingFor(gang.Id);
            if (standing.Rep <= -50f) return gang.Name + " want you dead, not on the payroll.";
            if (playerRespect < gang.JoinRespect)
            {
                return "Need " + gang.JoinRespect.ToString("F0") + " respect. You have " +
                       playerRespect.ToString("F0") + ".";
            }

            var previous = Current;
            if (previous != null) ClearRelationsFor(previous);

            Current = gang;
            ApplyRelations(gang);

            if (previous != null)
            {
                // Switching sides is not free: the crew you walked out on remembers.
                var old = StandingFor(previous.Id);
                old.Rep = Math.Max(-100f, old.Rep - 40f);
                Notify.Ticker("~o~You walked out on " + previous.Name + ".~s~ They ain't forgetting that.");
            }

            Notify.Important("~g~Running with " + gang.Name + ".~s~ " + gang.TurfHint);
            Log.Info("Affiliated with " + gang.Id + ".");

            if (Social != null) Social.On(SocialEvent.JoinedGang, gang.Name);
            return null;
        }

        /// <summary>
        /// Wipes everything the gangs know about you.
        ///
        /// Affiliation, every standing, every body counted, every dollar credited -- back to the
        /// day you arrived. Deliberately does NOT touch respect, rank, money or product: those
        /// are yours, not theirs, and somebody wanting a clean slate with the sets almost never
        /// means they also want to be broke.
        ///
        /// The relationship groups are put back first. Leaving those set while clearing the
        /// record is how you end up in a world where every gang still likes you and nothing in
        /// the readout explains why.
        /// </summary>
        public void ResetEverything()
        {
            var had = Current;

            if (had != null) ClearRelationsFor(had);

            foreach (var hash in _touchedGroups)
            {
                try
                {
                    ClearBoth(RelCompanion, hash);
                    ClearBoth(RelRespect, hash);
                }
                catch { /* teardown */ }
            }

            _touchedGroups.Clear();
            _standings.Clear();

            Current = null;
            WorkingACorner = false;

            Notify.Important("~o~You're nobody to any of them again.~s~ Standings wiped.");
            Log.Info("Gang standings and affiliation reset by the player.");

            if (Social != null && had != null) Social.On(SocialEvent.LeftGang, had.Name);
        }

        /// <summary>
        /// Walking out.
        ///
        /// Nothing offers this any more. There is one set in this mod -- the Families, and the
        /// data has said so all along -- so leaving would put you somewhere the game has no
        /// answer for: no corners to defend, nobody to fight beside, no rep to earn and no way
        /// back in except the leader who just watched you go.
        ///
        /// Kept rather than deleted because a save written while it was still reachable can
        /// load with no affiliation, and RestoreAffiliation needs a matching way to clear one.
        /// </summary>
        public void Leave()
        {
            if (Current == null) return;

            var gang = Current;
            ClearRelationsFor(gang);

            var standing = StandingFor(gang.Id);
            standing.Rep = Math.Max(-100f, standing.Rep - 25f);

            Current = null;
            Notify.Ticker("~o~You're on your own now.~s~");
            Log.Info("Left " + gang.Id + ".");

            if (Social != null) Social.On(SocialEvent.LeftGang, gang.Name);
        }

        /// <summary>Restores the affiliation loaded from a save without the join checks or messaging.</summary>
        public void RestoreAffiliation(string gangId)
        {
            var gang = _gangs.Get(gangId);
            if (gang == null) return;

            Current = gang;
            ApplyRelations(gang);
            Log.Info("Restored affiliation with " + gang.Id + ".");
        }

        private void ApplyRelations(GangDef gang)
        {
            if (gang.GroupHash == 0 || _playerGroupHash == 0) return;

            try
            {
                // COMPANION rather than respect.
                //
                // The street lines -- what you doing round here, why you here -- are what the
                // game has a gang say to somebody who is not one of them stood on their block.
                // Respect is good terms with an outsider and still gets them. Companion is the
                // level the game uses for people who are actually with you, and it is the only
                // thing that stops your own set challenging you outside your own house.
                SetBoth(RelCompanion, gang.GroupHash, _playerGroupHash);
                _touchedGroups.Add(gang.GroupHash);
                _lastReapply = Game.GameTime;
            }
            catch (Exception ex)
            {
                Log.Error("Could not apply relationships for " + gang.Id, ex);
            }
        }

        private void ClearRelationsFor(GangDef gang)
        {
            if (gang.GroupHash == 0 || _playerGroupHash == 0) return;

            try
            {
                // CLEAR takes the relationship you want REMOVED, not the one you want left
                // behind. Passing Neutral here cleared a relationship that was never set and
                // left our Respect in place, so every gang we had ever joined stayed friendly
                // for the rest of the session -- including after leaving them.
                // Both, because older saves set Respect and this now sets Companion -- and a
                // relationship left behind is a gang that likes you for reasons nothing in the
                // readout explains.
                ClearBoth(RelCompanion, gang.GroupHash);
                ClearBoth(RelRespect, gang.GroupHash);

                _touchedGroups.Remove(gang.GroupHash);
            }
            catch (Exception ex)
            {
                Log.Error("Could not clear relationships for " + gang.Id, ex);
            }
        }

        private static void SetBoth(int intensity, int a, int b)
        {
            Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, intensity, a, b);
            Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, intensity, b, a);
        }

        /// <summary>
        /// Every gang is a companion to ITSELF.
        ///
        /// The relationship a group has with its own hash is not something the game sets for
        /// you, and without it a set is merely neutral toward its own members -- which is fine
        /// until somebody clips one of them and the damage event has nothing standing in its
        /// way. Companion is the strongest tie there is and it costs one call per gang.
        ///
        /// Belt to SET_CAN_ATTACK_FRIENDLY's braces. That flag is per ped and has to be set on
        /// every one the mod spawns; this is per group and covers the ones it does not.
        /// </summary>
        public void MakeGangsWholeToThemselves()
        {
            foreach (var gang in _gangs.All)
            {
                if (gang == null) continue;

                try
                {
                    Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS,
                                  RelCompanion, gang.GroupHash, gang.GroupHash);
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not make " + gang.Id + " whole: " + ex.Message);
                }
            }
        }

        /// <summary>Removes the relationship we set, in both directions.</summary>
        private void ClearBoth(int intensity, int hash)
        {
            Function.Call(Hash.CLEAR_RELATIONSHIP_BETWEEN_GROUPS, intensity, hash, _playerGroupHash);
            Function.Call(Hash.CLEAR_RELATIONSHIP_BETWEEN_GROUPS, intensity, _playerGroupHash, hash);
        }

        /// <summary>Puts every relationship we changed back. Called on script unload.</summary>
        public void RestoreWorld()
        {
            foreach (var hash in _touchedGroups)
            {
                try
                {
                    ClearBoth(RelCompanion, hash);
                    ClearBoth(RelRespect, hash);
                }
                catch
                {
                    // Nothing useful to do during teardown.
                }
            }
            _touchedGroups.Clear();

            // Handles are reused, so who had been shot at goes with the session.
            _struck.Clear();
        }

        // ---- per-tick ----------------------------------------------------------

        public void Update()
        {
            var now = Game.GameTime;

            // Every tick, not every fifteen seconds. A relationship group stops somebody
            // deciding to fight you; it does nothing once they already have, and fifteen
            // seconds of being shot at by your own set is the whole complaint.
            CalmHome();
            Lawful();

            if (now - _lastReapply >= ReapplyIntervalMs)
            {
                // The home set first, and unconditionally. The game resets ambient relationship
                // groups on its own, so a single call at startup stops holding the first time
                // you leave the area and come back -- and then his own people start challenging
                // him on his own street again.
                RespectHome();

                if (Current != null) ApplyRelations(Current);
                else _lastReapply = now;
            }

            if (now - _lastKillScan >= KillScanIntervalMs)
            {
                _lastKillScan = now;

                ScanKills();
                TickPresence(Turf);
                ScanAllies();
                Repair(now);
            }
        }

        /// <summary>Which gang a ped belongs to, by relationship group. Null for civilians.</summary>
        public GangDef GangOf(Ped ped)
        {
            if (ped == null || !ped.Exists()) return null;
            try
            {
                var hash = Function.Call<int>(Hash.GET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle);
                return _gangs.ByGroupHash(hash);
            }
            catch
            {
                return null;
            }
        }

        public bool IsAlly(Ped ped)
        {
            if (Current == null) return false;
            var g = GangOf(ped);
            return g != null && g.Id == Current.Id;
        }

        public bool IsRival(Ped ped)
        {
            var g = GangOf(ped);
            if (g == null) return false;
            if (Current == null) return false;
            return g.Id != Current.Id && Beefing(g.Id);
        }

        private void ScanAllies()
        {
            _lastAllyCount = 0;
            if (Current == null) return;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            try
            {
                foreach (var ped in World.GetNearbyPeds(player, AllyScanRadius))
                {
                    if (ped == null || !ped.Exists() || !ped.IsAlive) continue;
                    if (ped.Handle == player.Handle) continue;
                    if (!IsAlly(ped)) continue;

                    _lastAllyCount++;
                    Friendly(ped);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Ally scan failed: " + ex.Message);
            }
        }

        /// <summary>
        /// One of ours, told plainly that Franklin is one of ours.
        ///
        /// The relationship between the two GROUPS is already set to companion, and it is not
        /// enough on its own: the game resets group relationships on an area reload, a
        /// cutscene and the end of a story mission, and a ped that was spawned angry stays
        /// angry whatever the group table says afterwards. So each man is told once, and the
        /// set means it is once per man rather than once a second forever.
        ///
        /// Combat attribute 46 is fight-armed-while-unarmed and 5 is always-fight. Both off,
        /// and not able to consider the player a threat at all -- because "he shot near me" is
        /// a thing that happens constantly on a block you are working, and a set that turns on
        /// you every time a round goes off is not a set you run with.
        /// </summary>
        private void Friendly(Ped ped)
        {
            if (ped == null || !ped.Exists()) return;
            if (_madeFriendly.Contains(ped.Handle)) return;

            _madeFriendly.Add(ped.Handle);

            try
            {
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 5, false);
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 46, false);
                Function.Call(Hash.SET_PED_CAN_BE_TARGETTED_BY_PLAYER, ped.Handle,
                              Game.Player.Handle, false);

                // And they speak to him rather than at him. One in six, so walking down a
                // street is people saying hello and not a receiving line.
                if (_rng.Next(6) == 0)
                {
                    Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, ped.Handle,
                                  Greetings[_rng.Next(Greetings.Length)], "SPEECH_PARAMS_FORCE");
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Could not settle one of ours: " + ex.Message);
            }

            // The set is a cache, not a record. Left to grow it would hold every handle the
            // game has ever reused.
            if (_madeFriendly.Count > 300) _madeFriendly.Clear();
        }

        private static readonly Random _rng = new Random();

        /// <summary>Who has already been told. Handles, so it costs nothing to ask.</summary>
        private readonly HashSet<int> _madeFriendly = new HashSet<int>();

        /// <summary>What one of ours says to him in passing.</summary>
        private static readonly string[] Greetings =
        {
            "GENERIC_HI", "GENERIC_HOWS_IT_GOING", "GENERIC_THANKS",
            "CHAT_STATE", "GENERIC_YES"
        };

        /// <summary>
        /// Credits kills of rival gang members. Polls nearby corpses rather than hooking a
        /// damage event, because SHVDN gives no kill callback.
        /// </summary>
        private void ScanKills()
        {
            if (Current == null) return;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            try
            {
                foreach (var ped in World.GetNearbyPeds(player, 90f))
                {
                    if (ped == null || !ped.Exists() || ped.IsAlive) continue;
                    if (_countedKills.Contains(ped.Handle)) continue;

                    var killer = Function.Call<int>(Hash.GET_PED_SOURCE_OF_DEATH, ped.Handle);
                    if (killer != player.Handle) continue;

                    _countedKills.Add(ped.Handle);

                    var gang = GangOf(ped);
                    if (gang == null) continue;

                    if (gang.Id == Current.Id)
                    {
                        // Killing your own costs you dearly.
                        var own = StandingFor(Current.Id);
                        own.Rep = Math.Max(-100f, own.Rep - 15f);
                        Notify.Ticker("~r~" + Current.Name + " saw that.~s~ -15 rep");
                        continue;
                    }

                    // Tallied before any of the gates below, because this is a count of what
                    // happened rather than a reward for it. Rep is conditional -- on beef, on
                    // whether you were working, on whose set it was -- and every one of those
                    // conditions is a reason a body might earn nothing. It is still a body,
                    // and the dossier is a history rather than a scoreboard.
                    StandingFor(gang.Id).TheirDead++;

                    // Reported BEFORE the beef gate, because starting a war is exactly how a
                    // set you had no problem with becomes a set you have a problem with. Three
                    // of these in five seconds on their own block is a declaration.
                    try { RivalDropped?.Invoke(gang); }
                    catch (Exception ex) { Log.Debug("Kill hook threw: " + ex.Message); }

                    // AND IT COSTS YOU WITH THEM, BEEF OR NO BEEF. A body is a body to the
                    // set that lost it; enough of them and they cross into beef on their
                    // own, the way enough disses do. It used to cost nothing unless you
                    // were already at war, which made the first few free.
                    Bodied(gang);

                    // Only counts for REP if you are actually at war with them. Shooting a man
                    // from a set nobody has a problem with is not a body for the block, it is
                    // a body.
                    if (!Beefing(gang.Id)) continue;

                    var standing = StandingFor(Current.Id);
                    standing.Kills++;

                    // Dropping a rival while you are working a corner is the thing they respect
                    // most: it is done for the block, in front of people, at real risk.
                    var earned = WorkingACorner ? KillWhileDealingRep : KillRep;
                    AddRep(earned, "for that one");

                    // The block hears about some of them and not others, which is the feed's
                    // own decision -- a neighbourhood that comments on every single one is a
                    // neighbourhood watching you rather than living in the same place as you.
                    if (Social != null)
                    {
                        Social.On(SocialEvent.RivalKilled, gang.Name);

                        // And he says something about it himself, sometimes. Seventy-five
                        // seconds between and a bit better than a coin toss -- a man who taunts
                        // every single body is not taunting, he is narrating.
                        Social.PostAsYouSometimes("YouDroppedOne", gang.Name, 75000, 55);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Kill scan failed: " + ex.Message);
            }

            if (_countedKills.Count > 400) _countedKills.Clear();
        }

        /// <summary>One of theirs, dead by your hand. Costs the same whether or not there was beef.</summary>
        private void Bodied(GangDef gang)
        {
            if (gang == null) return;

            var s = StandingFor(gang.Id);
            if (s == null) return;

            var before = s.Rep;
            s.Rep = Math.Max(-100f, s.Rep - BodyCost);
            s.HostileAt = Game.GameTime;

            if (before > BeefAt && s.Rep <= BeefAt)
            {
                Notify.Failure("that's beef with " + gang.Name + " now.");
                Log.Info("A body cost him " + gang.Id + ": " + before.ToString("0") + " -> " +
                         s.Rep.ToString("0") + "  (that's beef)");
            }
        }

        /// <summary>
        /// Standing heals when you leave a set alone.
        ///
        /// Six minutes without a body or a diss and it starts coming back, two a minute,
        /// until it is where it started -- nought for most of the city, and for the two
        /// sets Franklin has never been at peace with, only back to where they were seeded,
        /// which is still beef. Nothing here ever pushes a standing above neutral; that is
        /// earned with deeds, not with absence. The moment it crosses back over the beef
        /// line is said out loud, because it is the moment the visits stop.
        /// </summary>
        private void Repair(int now)
        {
            if (_lastRepair == 0) { _lastRepair = now; return; }

            var minutes = (now - _lastRepair) / 60000f;
            if (minutes <= 0f) return;
            _lastRepair = now;

            foreach (var s in _standings.Values)
            {
                if (s == null || string.Equals(s.GangId, HomeSet, StringComparison.OrdinalIgnoreCase)) continue;

                // Out of a save, or never touched: the clock starts now.
                if (s.HostileAt == 0) { s.HostileAt = now; continue; }
                if (now - s.HostileAt < RepairAfterMs) continue;

                var ceiling = SeedFor(s.GangId);
                if (s.Rep >= ceiling) continue;

                var before = s.Rep;
                s.Rep = Math.Min(ceiling, s.Rep + RepairPerMinute * minutes);

                if (before <= BeefAt && s.Rep > BeefAt)
                {
                    var gang = _gangs == null ? null : _gangs.Get(s.GangId);
                    var name = gang == null ? s.GangId : gang.Name;

                    Notify.Ticker("~g~" + name + " cooled off.~s~  Leave them alone and it stays that way.");
                    Log.Info("Cooled off with " + s.GangId + ": " + before.ToString("0") + " -> " + s.Rep.ToString("0") + ".");
                }
            }
        }

        private int _lastRepair;

        /// <summary>What a body costs you with the set that lost it, and what a track costs -- a post is TauntCost.</summary>
        private const float BodyCost = 6f;
        public const float TrackCost = 25f;

        /// <summary>How long they have to be left alone before it heals, and how fast.</summary>
        private const int RepairAfterMs = 6 * 60 * 1000;
        private const float RepairPerMinute = 2f;

        // ---- earning it --------------------------------------------------------

        /// <summary>Rep for a rival dropped in passing.</summary>
        private const float KillRep = 3f;

        /// <summary>Rep for a rival dropped while you are working a corner.</summary>
        private const float KillWhileDealingRep = 12f;

        /// <summary>Rep for one sale.</summary>
        private const float SaleRep = 0.8f;

        /// <summary>Rep for buying weight off your own people.</summary>
        private const float BuyRep = 1.2f;

        /// <summary>Rep per minute simply spent on your gang's blocks.</summary>
        private const float PresenceRepPerMinute = 0.5f;

        /// <summary>Set by PostUp, so a kill on the corner counts for more than one in a car.</summary>
        public bool WorkingACorner;

        private int _lastPresenceTick;

        /// <summary>
        /// Adds rep with the gang you run with, capped and announced.
        ///
        /// Everything that earns rep funnels through here so the amounts stay comparable to
        /// each other: standing on the block is a trickle, a sale is a nudge, a body is real.
        /// </summary>
        public void AddRep(float amount, string why = null)
        {
            if (!IsAffiliated || Math.Abs(amount) < 0.001f) return;

            var standing = StandingFor(Current.Id);
            var before = standing.Rep;

            standing.Rep = Math.Max(-100f, Math.Min(1000f, standing.Rep + amount));

            // Only worth telling them about when it is a lump, not a trickle.
            if (!string.IsNullOrEmpty(why) && standing.Rep - before >= 1f)
            {
                Notify.Ticker("~g~+" + (standing.Rep - before).ToString("0") + " rep~s~ " + why);
            }
        }

        /// <summary>Rep for a completed sale. Called by the dealing code.</summary>
        public void CreditSale() => AddRep(SaleRep);

        /// <summary>Rep for buying weight. Called when a purchase lands.</summary>
        public void CreditPurchase() => AddRep(BuyRep);

        /// <summary>
        /// A slow drip for simply being seen on your own blocks. Being around is how anyone
        /// becomes a face, so standing on the corner counts for something even on a day you
        /// sell nothing.
        /// </summary>
        private void TickPresence(TurfWatch turf)
        {
            if (!IsAffiliated || turf == null) return;
            if (turf.Status != TurfStatus.Home) { _lastPresenceTick = 0; return; }

            var now = Game.GameTime;
            if (_lastPresenceTick == 0) { _lastPresenceTick = now; return; }

            var minutes = (now - _lastPresenceTick) / 60000f;
            if (minutes < 0.25f) return;

            _lastPresenceTick = now;
            AddRep(PresenceRepPerMinute * minutes);
        }

        /// <summary>Where the presence drip is driven from, once turf is known.</summary>
        public TurfWatch Turf;

        /// <summary>Orders nearby allies onto whoever is attacking the player.</summary>
        public int CallBackup(Ped target)
        {
            if (Current == null || target == null || !target.Exists()) return 0;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return 0;

            var sent = 0;
            try
            {
                foreach (var ped in World.GetNearbyPeds(player, AllyScanRadius))
                {
                    if (ped == null || !ped.Exists() || !ped.IsAlive) continue;
                    if (ped.Handle == player.Handle || ped.Handle == target.Handle) continue;
                    if (!IsAlly(ped)) continue;
                    if (ped.IsInCombat) continue;

                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 46, true); // always fight
                    Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 5, true);  // will take on an armed man
                    Function.Call(Hash.TASK_COMBAT_PED, ped.Handle, target.Handle, 0, 16);
                    Function.Call(Hash.SET_PED_KEEP_TASK, ped.Handle, true);
                    sent++;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Backup dispatch failed: " + ex.Message);
            }

            return sent;
        }

        // ---- persistence -------------------------------------------------------

        public Json ToJson()
        {
            var obj = Json.Object();
            obj.Set("current", Current == null ? "" : Current.Id);

            var arr = Json.Array();
            foreach (var s in _standings.Values)
            {
                arr.Add(Json.Object()
                    .Set("gang", s.GangId)
                    .Set("rep", Math.Round(s.Rep, 2))
                    .Set("kills", s.Kills)
                    .Set("moneyEarned", s.MoneyEarned)
                    .Set("deals", s.Deals)
                    .Set("timeMs", s.TimeAffiliatedMs)
                    .Set("theirDead", s.TheirDead)
                    .Set("attacks", s.Attacks)
                    .Set("tweets", s.Tweets));
            }
            obj.Set("standings", arr);

            // Marks the save as one where beef is a number, so it is only ever seeded once.
            obj.Set("beefSeeded", true);
            return obj;
        }

        public void LoadFrom(Json node)
        {
            _standings.Clear();
            if (node == null || node.IsNull) return;

            foreach (var item in node["standings"].Items)
            {
                var id = item["gang"].AsString(null);
                if (string.IsNullOrEmpty(id)) continue;

                _standings[id] = new GangStanding
                {
                    GangId = id,
                    Rep = item["rep"].AsFloat(0f),
                    Kills = Math.Max(0, item["kills"].AsInt(0)),
                    MoneyEarned = Math.Max(0L, item["moneyEarned"].AsLong(0)),
                    Deals = Math.Max(0, item["deals"].AsInt(0)),
                    TimeAffiliatedMs = Math.Max(0L, item["timeMs"].AsLong(0)),

                    // Absent from every save written before these existed, which reads as zero
                    // -- the right answer for a tally nobody was keeping yet.
                    TheirDead = Math.Max(0, item["theirDead"].AsInt(0)),
                    Attacks = Math.Max(0, item["attacks"].AsInt(0)),
                    Tweets = Math.Max(0, item["tweets"].AsInt(0))
                };
            }

            // Saves made before beef was a number have the Ballas and the Vagos sitting at
            // zero, which now reads as "no problem with you" -- so loading an old save quietly
            // made peace with the two sets Franklin has never been at peace with.
            //
            // The flag is written from now on. Its absence means the save predates the change,
            // and the two of them are put back where they belong exactly once.
            if (!node["beefSeeded"].AsBool(false))
            {
                foreach (var id in new[] { "ballas", "vagos" })
                {
                    var s = StandingFor(id);
                    if (s != null && s.Rep > SeedFor(id)) s.Rep = SeedFor(id);
                }

                Log.Info("Old save: put the Ballas and the Vagos back on the wrong side of us.");
            }

            var current = node["current"].AsString("");
            if (!string.IsNullOrEmpty(current)) RestoreAffiliation(current);
        }
    }
}
