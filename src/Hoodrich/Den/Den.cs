using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Gangs;
using Hoodrich.Locations;
using Hoodrich.UI;

namespace Hoodrich.Den
{
    /// <summary>
    /// The gambling den: the floor behind the [GamblingDen] door.
    ///
    /// The room is whatever [GamblingDen] in the ini points at -- the two-car garage under
    /// Pillbox Hill since 2026-09-26 -- and the furniture is Michael's: the tables, the slots,
    /// the counter with the money on it are a Parkview scene he builds in Menyoo, and it stands
    /// up when the door puts you in there. This is what makes it a den rather than a room with
    /// tables in it: a woman of the set behind each table, the jukebox on Blonded, and the games
    /// -- roulette, three card poker and the slots -- played the way the Diamond plays them.
    ///
    /// THE DIAMOND'S OWN GAMES, AS NEAR AS THE STORY GAME ALLOWS. Michael asked on 2026-09-26 for
    /// "the actual gambling feature, same graphics, everything -- vanilla function". The casino's
    /// games are GTA Online scripts and do not run here, so each one is rebuilt out of the
    /// casino's own parts: the table's own chairs to sit in, its own cameras over the felt, its
    /// chips, its cards and its ball, its markers, its reels, its sounds, its dealer's voice and
    /// every one of its clips. See Roulette, Poker and Slots.
    ///
    /// NOTHING IN HERE KNOWS WHERE A TABLE IS. The tables are found by model near the way in,
    /// every time you come in, so he can move them in Menyoo and capture again and the dealers,
    /// the chairs and the games follow them. Where two tables of a kind stand in the same spot --
    /// the Casino Heist copy of a table under the Diamond's, left over from building the room in
    /// Menyoo -- the Diamond's is the one used, because it is the one its clips were made for.
    /// </summary>
    internal sealed class Floor
    {
        private readonly Settings _cfg;
        private readonly GangRegistry _gangs;
        private readonly InteriorDoor _door;
        private readonly Random _rng = new Random();

        private bool _in;
        private int _lookAt;
        private bool _tuned;
        private bool _saidTables;
        private bool _saidChairs;

        private Entity _roulette;
        private Entity _blackjack;
        private Entity _poker;
        private Entity _jukebox;
        private readonly List<Entity> _slots = new List<Entity>();

        /// <summary>Every machine's reels, by the machine's handle. See Reels.</summary>
        private readonly Dictionary<int, Reels> _reels = new Dictionary<int, Reels>();

        private Dealer _croupier;
        private Dealer _cardDealer;
        private Dealer _pokerDealer;

        private Roulette _atRoulette;
        private Blackjack _atBlackjack;
        private Poker _atPoker;
        private Slots _atSlot;

        /// <summary>The script's own emitter is tied to the jukebox prop. See Tune.</summary>
        private bool _linked;

        /// <summary>How far round the way in the furniture is looked for.</summary>
        private const float Reach = 40f;

        /// <summary>
        /// How near you stand to be offered a game: at one of a table's own chairs, or in front of
        /// a machine where its stool would be. Close, since 2026-09-26 -- Michael asked for the
        /// prompts only when he is right at them. A table with no chairs of its own is offered
        /// from TableReach round its middle.
        /// </summary>
        private const float ChairReach = 1.0f;
        private const float TableReach = 1.6f;
        private const float SlotReach = 0.8f;

        /// <summary>Where the stool is in front of a machine, in its own space.</summary>
        private static readonly Vector3 SlotStool = new Vector3(0f, -0.75f, 0f);

        /// <summary>How often the room is looked over for its furniture while it is missing.</summary>
        private const int LookEveryMs = 1000;

        /// <summary>
        /// The game's static emitters that are the jukebox, from [GamblingDen] Jukebox. The
        /// clubhouse had three; the garage has none that anybody has found, and a room with
        /// none is a quiet room.
        /// </summary>
        private string[] Jukebox
        {
            get
            {
                var raw = _cfg == null ? "" : _cfg.DenJukebox;
                var names = new List<string>();

                foreach (var one in (raw ?? "").Split(';'))
                {
                    var name = one.Trim();
                    if (name.Length > 0) names.Add(name);
                }

                return names.ToArray();
            }
        }

        /// <summary>The Diamond's tables first in each list: where two stand together, the first found here is used.</summary>
        private static readonly int[] RouletteModels =
        {
            Game.GenerateHash("vw_prop_casino_roulette_01"), Game.GenerateHash("vw_prop_casino_roulette_01b"),
            Game.GenerateHash("ch_prop_casino_roulette_01a"), Game.GenerateHash("ch_prop_casino_roulette_01b")
        };

        /// <summary>The Diamond's three card poker tables, and the heist and island ones that look like them.</summary>
        private static readonly int[] PokerModels =
        {
            Game.GenerateHash("vw_prop_casino_3cardpoker_01"), Game.GenerateHash("vw_prop_casino_3cardpoker_01b"),
            Game.GenerateHash("ch_prop_casino_poker_01a"), Game.GenerateHash("ch_prop_casino_poker_01b"),
            Game.GenerateHash("h4_prop_casino_3cardpoker_01a"), Game.GenerateHash("h4_prop_casino_3cardpoker_01b"),
            Game.GenerateHash("h4_prop_casino_3cardpoker_01c"), Game.GenerateHash("h4_prop_casino_3cardpoker_01d"),
            Game.GenerateHash("h4_prop_casino_3cardpoker_01e")
        };

        /// <summary>Anything in the room that is a jukebox. See Tune.</summary>
        private static readonly int[] JukeboxModels =
        {
            Game.GenerateHash("bkr_prop_clubhouse_jukebox_01a"), Game.GenerateHash("bkr_prop_clubhouse_jukebox_01b"),
            Game.GenerateHash("bkr_prop_clubhouse_jukebox_02a"), Game.GenerateHash("prop_jukebox_01"),
            Game.GenerateHash("prop_jukebox_02")
        };

        private static readonly int[] BlackjackModels =
        {
            Game.GenerateHash("vw_prop_casino_blckjack_01"), Game.GenerateHash("vw_prop_casino_blckjack_01b"),
            Game.GenerateHash("ch_prop_casino_blackjack_01a"), Game.GenerateHash("ch_prop_casino_blackjack_01b"),
            Game.GenerateHash("h4_prop_casino_blckjack_01a"), Game.GenerateHash("h4_prop_casino_blckjack_01b")
        };

        private static readonly string[] RouletteIdles =
        {
            "idle", "idle_var01", "idle_var02", "idle_var03", "idle_var04", "idle_var05", "idle_var06"
        };

        /// <summary>The blackjack dealer between hands: the shared dealer idle, played where she stands. See Dealer.</summary>
        private static readonly string[] BlackjackIdles =
        {
            "female_idle", "female_idle", "female_idle_var_01", "female_idle_var_02", "female_idle_var_03",
            "female_idle_var_04", "female_idle_var_05", "female_idle_var_06", "female_idle_var_07", "female_idle_var_08"
        };

        /// <summary>
        /// Her mark at a blackjack table, in the table's own space, and her turn from its heading:
        /// behind the middle of it and facing across. Where the Diamond stands its blackjack
        /// dealers, worked back from DiamondBlackjack's four tables -- they agree to a centimetre.
        /// </summary>
        private static readonly Vector3 BlackjackMark = new Vector3(0f, 0.79f, 1f);
        private const float BlackjackTurn = 180.7f;

        /// <summary>
        /// The poker dealer between hands: the casino's shared dealer idle, hands on the felt. The
        /// deck is on the table now (see Poker), so the idle holding it is only for the deal.
        /// </summary>
        private static readonly string[] PokerIdles =
        {
            "female_idle", "female_idle", "female_idle_var_01", "female_idle_var_02", "female_idle_var_03",
            "female_idle_var_04", "female_idle_var_05", "female_idle_var_06", "female_idle_var_07", "female_idle_var_08"
        };

        /// <summary>Who deals where: strippers, since 2026-09-26. See Dealer.</summary>
        private const string RouletteDealerModel = "s_f_y_stripper_01";
        private const string PokerDealerModel = "s_f_y_stripper_02";
        private const string BlackjackDealerModel = "s_f_y_stripper_02";

        /// <summary>And whose voice: three of the Diamond's croupiers.</summary>
        private const string RouletteVoice = "S_F_Y_Casino_01_ASIAN_01";
        private const string PokerVoice = "S_F_Y_Casino_01_LATINA_01";
        private const string BlackjackVoice = "S_F_Y_Casino_01_LATINA_01";

        /// <summary>The emitter the game keeps for a radio a script has put down. See Tune.</summary>
        private const string PropEmitter = "SE_Script_Placed_Prop_Emitter_Boombox";

        public Floor(Settings cfg, GangRegistry gangs, InteriorDoor door)
        {
            _cfg = cfg;
            _gangs = gangs;
            _door = door;
        }

        /// <summary>A game is up: it has the keys and the bottom of the screen.</summary>
        public bool IsPlaying => _atRoulette != null || _atBlackjack != null || _atPoker != null || _atSlot != null;

        private int MinBet => _cfg == null ? 100 : _cfg.DenMinBet;
        private int MaxBet => _cfg == null ? 10000 : _cfg.DenMaxBet;

        public void Update()
        {
            if (_door == null) return;

            if (!_door.IsInside)
            {
                if (_in) Leave();
                return;
            }

            if (!_in) Enter();

            var me = Game.Player.Character;
            if (me == null || !me.Exists()) return;

            Scene.AllLoaded();
            Sfx.Banks();
            Furniture();
            Dealers();

            foreach (var r in _reels.Values) r.Update();

            if (IsPlaying)
            {
                Keys.HoldTheGame();
                Play();
                return;
            }

            Offer(me);
        }

        // ---- in and out ---------------------------------------------------------------

        private void Enter()
        {
            _in = true;
            _tuned = false;
            _saidTables = false;
            _saidChairs = false;
            _lookAt = 0;

            foreach (var d in Scene.All) Scene.Request(d);
            Chips.Ask();

            Log.Info("Den: in. Asking for the casino's clips and looking for the tables.");
        }

        private void Leave()
        {
            _in = false;

            Quit();

            if (_croupier != null) _croupier.Remove();
            if (_cardDealer != null) _cardDealer.Remove();
            if (_pokerDealer != null) _pokerDealer.Remove();
            _croupier = null;
            _cardDealer = null;
            _pokerDealer = null;

            foreach (var r in _reels.Values) r.Remove();
            _reels.Clear();

            // The jukebox's emitter back off, or it plays Blonded to an empty garage all night.
            if (_linked)
            {
                try { Function.Call(Hash.SET_STATIC_EMITTER_ENABLED, PropEmitter, false); }
                catch { /* it is only sound */ }

                _linked = false;
            }

            _roulette = null;
            _blackjack = null;
            _poker = null;
            _jukebox = null;
            _slots.Clear();

            Scene.Forget();
            Buttons.Drop();
            Sfx.Release();

            Log.Info("Den: out.");
        }

        public void RestoreWorld()
        {
            Leave();
        }

        // ---- the room -----------------------------------------------------------------

        /// <summary>
        /// The tables and the machines, by model, near the way in. Looked for again every
        /// second until they are all there -- the scene that stands them up runs a tick or two
        /// behind the door -- and the machines' reels kept in their windows.
        /// </summary>
        private void Furniture()
        {
            var now = Game.GameTime;
            if (now < _lookAt) return;
            _lookAt = now + LookEveryMs;

            Machines();

            if ((Alive(_roulette) || Alive(_poker)) && _slots.Count > 0 && Alive(_jukebox)) return;

            try
            {
                var around = _door.Landing;
                var found = new List<Prop>();

                foreach (var prop in World.GetNearbyProps(around, Reach))
                {
                    if (prop != null && prop.Exists()) found.Add(prop);
                }

                _roulette = Pick(found, RouletteModels) ?? _roulette;
                _blackjack = Pick(found, BlackjackModels) ?? _blackjack;
                _poker = Pick(found, PokerModels) ?? _poker;
                _jukebox = Pick(found, JukeboxModels) ?? _jukebox;

                _slots.Clear();

                foreach (var prop in found)
                {
                    if (Reels.KindOf(prop) == null) continue;

                    // One machine a spot: the Diamond's, where a heist copy stands in it too.
                    var twin = -1;

                    for (var i = 0; i < _slots.Count; i++)
                    {
                        if (_slots[i].Position.DistanceTo(prop.Position) < 0.3f) { twin = i; break; }
                    }

                    if (twin < 0) _slots.Add(prop);
                    else if (Rank(prop.Model.Hash) < Rank(_slots[twin].Model.Hash)) _slots[twin] = prop;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Den: could not look for the tables: " + ex.Message);
            }

            if (!_saidTables && (Alive(_roulette) || Alive(_blackjack) || Alive(_poker) || _slots.Count > 0))
            {
                _saidTables = true;
                Log.Info("Den: the room has " + (Alive(_roulette) ? "the roulette, " : "no roulette, ") +
                         (Alive(_blackjack) ? "the blackjack, " : "no blackjack, ") +
                         (Alive(_poker) ? "the poker, " : "no poker, ") +
                         _slots.Count + " slot machine(s), " + (Alive(_jukebox) ? "a jukebox." : "no jukebox."));
            }

            if (!_saidChairs && (Alive(_roulette) || Alive(_poker) || Alive(_blackjack)))
            {
                _saidChairs = true;
                Log.Info("Den: chairs -- roulette " + (Alive(_roulette) ? (Seat.Has(_roulette) ? "has its own" : "has none") : "absent") +
                         ", blackjack " + (Alive(_blackjack) ? (Seat.Has(_blackjack) ? "has its own" : "has none") : "absent") +
                         ", poker " + (Alive(_poker) ? (Seat.Has(_poker) ? "has its own" : "has none") : "absent") + ".");
            }
        }

        /// <summary>The first of the models, in the order they are listed, that stands in the room.</summary>
        private static Entity Pick(List<Prop> found, int[] models)
        {
            foreach (var model in models)
            {
                foreach (var prop in found)
                {
                    if (prop.Model.Hash == model) return prop;
                }
            }

            return null;
        }

        /// <summary>A Diamond machine before a heist copy of one.</summary>
        private static int Rank(int hash)
        {
            for (var n = 1; n <= 8; n++)
            {
                if (hash == Game.GenerateHash("vw_prop_casino_slot_0" + n + "a")) return 0;
            }

            return 1;
        }

        /// <summary>Every machine's reels in its window; the reels of one gone from the room, gone too.</summary>
        private void Machines()
        {
            var keep = new HashSet<int>();

            foreach (var machine in _slots)
            {
                if (!Alive(machine)) continue;

                keep.Add(machine.Handle);

                Reels reels;

                if (!_reels.TryGetValue(machine.Handle, out reels))
                {
                    var kind = Reels.KindOf(machine);
                    if (kind == null) continue;

                    reels = new Reels(machine, kind, _rng);
                    _reels[machine.Handle] = reels;
                }

                if (!reels.Built) reels.Build();
            }

            var gone = new List<int>();

            foreach (var pair in _reels)
            {
                if (!keep.Contains(pair.Key)) gone.Add(pair.Key);
            }

            foreach (var handle in gone)
            {
                _reels[handle].Remove();
                _reels.Remove(handle);
            }
        }

        /// <summary>
        /// The jukebox on Blonded. The ini's static emitters if it names any -- the clubhouse had
        /// three of its own -- and otherwise the jukebox Michael put in the room: the game keeps
        /// an emitter for a radio a script has put down, and it is tied to the prop, so the music
        /// comes out of the box. Asked for until the room has stood its jukebox up.
        /// </summary>
        private void Tune()
        {
            if (_tuned) return;

            var station = Radio.Blonded;
            var names = Jukebox;

            if (names.Length > 0)
            {
                _tuned = true;

                foreach (var name in names)
                {
                    try
                    {
                        Function.Call(Hash.SET_STATIC_EMITTER_ENABLED, name, true);
                        Function.Call(Hash.SET_EMITTER_RADIO_STATION, name, station);
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("Den: could not tune " + name + ": " + ex.Message);
                    }
                }

                Log.Info("Den: the jukebox is on " + station + ".");
                return;
            }

            if (!Alive(_jukebox)) return;

            _tuned = true;

            try
            {
                Function.Call(Hash.LINK_STATIC_EMITTER_TO_ENTITY, PropEmitter, _jukebox.Handle);
                Function.Call(Hash.SET_STATIC_EMITTER_ENABLED, PropEmitter, true);
                Function.Call(Hash.SET_EMITTER_RADIO_STATION, PropEmitter, station);
                _linked = true;

                Log.Info("Den: the jukebox is on " + station + ", out of the box in the room.");
            }
            catch (Exception ex)
            {
                Log.Info("Den: could not put the jukebox on: " + ex.Message);
            }
        }

        private void Dealers()
        {
            // The jukebox waits for nothing; it is tuned the moment we are in.
            Tune();

            var gang = _gangs == null ? null : _gangs.Get("families");

            if (Alive(_roulette))
            {
                if (_croupier == null)
                    _croupier = new Dealer(_roulette, Scene.RouletteDealer, RouletteIdles, "roulette", _rng, RouletteDealerModel, RouletteVoice);
                if (!_croupier.Exists) _croupier.Spawn(gang);
                _croupier.Update();
            }

            if (Alive(_blackjack))
            {
                if (_cardDealer == null)
                    _cardDealer = new Dealer(_blackjack, Scene.SharedDealer, BlackjackIdles, "blackjack", _rng, BlackjackDealerModel, BlackjackVoice,
                                             BlackjackMark, BlackjackTurn);
                if (!_cardDealer.Exists) _cardDealer.Spawn(gang);
                _cardDealer.Update();
            }

            if (Alive(_poker))
            {
                if (_pokerDealer == null)
                    _pokerDealer = new Dealer(_poker, Scene.SharedDealer, PokerIdles, "poker", _rng, PokerDealerModel, PokerVoice);
                if (!_pokerDealer.Exists) _pokerDealer.Spawn(gang);
                _pokerDealer.Update();
            }
        }

        // ---- the games ----------------------------------------------------------------

        private void Play()
        {
            if (_atRoulette != null)
            {
                _atRoulette.Update();
                if (_atRoulette.Finished) _atRoulette = null;
            }
            else if (_atBlackjack != null)
            {
                _atBlackjack.Update();
                if (_atBlackjack.Finished) _atBlackjack = null;
            }
            else if (_atPoker != null)
            {
                _atPoker.Update();
                if (_atPoker.Finished) _atPoker = null;
            }
            else if (_atSlot != null)
            {
                _atSlot.Update();
                if (_atSlot.Finished) _atSlot = null;
            }
        }

        /// <summary>Stood by something you can play: the prompt, and the game on the button.</summary>
        private void Offer(Ped me)
        {
            if (Mind.Busy) return;

            var at = me.Position;

            Seat seat;

            if (Alive(_roulette) && _croupier != null && _croupier.Exists && At(_roulette, at, out seat))
            {
                Help.ShowThisFrame("Press ~INPUT_CONTEXT~ to sit at the roulette. $" + MinBet.ToString("N0") + " a chip.");

                if (Game.IsControlJustPressed(Control.Context))
                {
                    _atRoulette = new Roulette(_roulette, _croupier, me, seat, MinBet, MaxBet, _rng);
                    InputGuard.Swallow();
                }

                return;
            }

            if (Alive(_blackjack) && _cardDealer != null && _cardDealer.Exists && At(_blackjack, at, out seat))
            {
                Help.ShowThisFrame("Press ~INPUT_CONTEXT~ to sit at blackjack. $" + MinBet.ToString("N0") + " minimum.");

                if (Game.IsControlJustPressed(Control.Context))
                {
                    _atBlackjack = new Blackjack(_blackjack, _cardDealer, me, seat, MinBet, MaxBet, _rng);
                    InputGuard.Swallow();
                }

                return;
            }

            if (Alive(_poker) && _pokerDealer != null && _pokerDealer.Exists && At(_poker, at, out seat))
            {
                Help.ShowThisFrame("Press ~INPUT_CONTEXT~ to sit at three card poker. $" + MinBet.ToString("N0") + " ante.");

                if (Game.IsControlJustPressed(Control.Context))
                {
                    _atPoker = new Poker(_poker, _pokerDealer, me, seat, MinBet, MaxBet, _rng);
                    InputGuard.Swallow();
                }

                return;
            }

            Entity nearest = null;
            var best = SlotReach;

            foreach (var slot in _slots)
            {
                if (!Alive(slot)) continue;

                var d = Flat(at, slot.GetOffsetPosition(SlotStool));
                if (d > best) continue;

                best = d;
                nearest = slot;
            }

            if (nearest == null) return;

            Reels reels;
            _reels.TryGetValue(nearest.Handle, out reels);

            Help.ShowThisFrame("Press ~INPUT_CONTEXT~ to play " + Slots.Title(Reels.KindOf(nearest)) + ". $" + MinBet.ToString("N0") + " a spin.");

            if (Game.IsControlJustPressed(Control.Context))
            {
                _atSlot = new Slots(nearest, reels, me, MinBet, MaxBet, _rng);
                InputGuard.Swallow();
            }
        }

        /// <summary>
        /// Whether you are at a table: by one of its own chairs if it has them, and then that is
        /// the chair you sit in; round its middle if it has none, and you play stood up.
        /// </summary>
        private static bool At(Entity table, Vector3 at, out Seat seat)
        {
            seat = null;

            if (Seat.Has(table))
            {
                seat = Seat.Nearest(table, at, ChairReach);
                return seat != null;
            }

            return Flat(at, table.Position) <= TableReach;
        }

        /// <summary>Whatever is being played, stopped where it is. For leaving and for teardown.</summary>
        private void Quit()
        {
            try
            {
                if (_atRoulette != null) _atRoulette.Abandon();
                if (_atBlackjack != null) _atBlackjack.Abandon();
                if (_atPoker != null) _atPoker.Abandon();
                if (_atSlot != null) _atSlot.LetGo();
            }
            catch { }

            _atRoulette = null;
            _atBlackjack = null;
            _atPoker = null;
            _atSlot = null;
        }

        private static bool Alive(Entity e)
        {
            return e != null && e.Exists();
        }

        private static float Flat(Vector3 a, Vector3 b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
