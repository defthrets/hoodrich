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
    /// The room is the old clubhouse under the sea and the furniture is Michael's -- the
    /// tables, the slots, the counter with the money on it are a Parkview scene that stands up
    /// when the door puts you in there (data\parkview\scenery\gambling-den.xml). This is what
    /// makes it a den rather than a room with tables in it: a woman of the set behind each
    /// table, the jukebox on Blonded, and the games -- roulette, blackjack and the slots --
    /// played the way the Diamond plays them, with the casino's own animations on the casino's
    /// own props. Michael asked for it on 2026-09-25.
    ///
    /// NOTHING IN HERE KNOWS WHERE A TABLE IS. The tables are found by model near the way in,
    /// every time you come in, so he can move them in Menyoo and capture again and the dealers
    /// and the games follow them. The animations are anchored on the props themselves -- see
    /// Scene -- so there is no offset to get wrong either.
    ///
    /// STILL TO COME, and honest about it: the machine's own reels turning (they are separate
    /// props whose seat in the cabinet nobody on this machine can look up), cards you can read
    /// on the felt (no native for a card's face in ScriptHookVDotNet 3.6), and a chair at the
    /// roulette. What is here works end to end: sit, bet, play, get paid or not.
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

        private Entity _roulette;
        private Entity _blackjack;
        private readonly List<Entity> _slots = new List<Entity>();

        private Dealer _croupier;
        private Dealer _cardDealer;

        private Roulette _atRoulette;
        private Blackjack _atBlackjack;
        private Slots _atSlot;

        /// <summary>How far round the way in the furniture is looked for.</summary>
        private const float Reach = 40f;

        /// <summary>How near a table or a machine you stand to be offered it.</summary>
        private const float TableReach = 2.6f;
        private const float SlotReach = 1.6f;

        /// <summary>How often the room is looked over for its furniture while it is missing.</summary>
        private const int LookEveryMs = 1000;

        /// <summary>
        /// The game's static emitters that are the jukebox, from [GamblingDen] Jukebox. The
        /// clubhouse had three; a story room has whatever somebody finds for it, or none.
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

        private static readonly int[] RouletteModels =
        {
            Game.GenerateHash("vw_prop_casino_roulette_01"), Game.GenerateHash("vw_prop_casino_roulette_01b")
        };

        private static readonly int[] BlackjackModels =
        {
            Game.GenerateHash("vw_prop_casino_blckjack_01"), Game.GenerateHash("vw_prop_casino_blckjack_01b")
        };

        private static readonly int[] SlotModels =
        {
            Game.GenerateHash("vw_prop_casino_slot_01a"), Game.GenerateHash("vw_prop_casino_slot_02a"),
            Game.GenerateHash("vw_prop_casino_slot_03a"), Game.GenerateHash("vw_prop_casino_slot_04a"),
            Game.GenerateHash("vw_prop_casino_slot_05a"), Game.GenerateHash("vw_prop_casino_slot_06a"),
            Game.GenerateHash("vw_prop_casino_slot_07a"), Game.GenerateHash("vw_prop_casino_slot_08a")
        };

        private static readonly string[] RouletteIdles =
        {
            "idle", "idle_var01", "idle_var02", "idle_var03", "idle_var04", "idle_var05", "idle_var06"
        };

        private static readonly string[] BlackjackIdles = { "idle", "dealer_idle" };

        public Floor(Settings cfg, GangRegistry gangs, InteriorDoor door)
        {
            _cfg = cfg;
            _gangs = gangs;
            _door = door;
        }

        /// <summary>A game is up: it has the keys and the bottom of the screen.</summary>
        public bool IsPlaying => _atRoulette != null || _atBlackjack != null || _atSlot != null;

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
            Furniture();
            Dealers();

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
            _lookAt = 0;

            foreach (var d in Scene.All) Scene.Request(d);

            Log.Info("Den: in. Asking for the casino's clips and looking for the tables.");
        }

        private void Leave()
        {
            _in = false;

            Quit();

            if (_croupier != null) _croupier.Remove();
            if (_cardDealer != null) _cardDealer.Remove();
            _croupier = null;
            _cardDealer = null;

            _roulette = null;
            _blackjack = null;
            _slots.Clear();

            Scene.Forget();

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
        /// behind the door.
        /// </summary>
        private void Furniture()
        {
            var now = Game.GameTime;
            if (now < _lookAt) return;
            _lookAt = now + LookEveryMs;

            if (Alive(_roulette) && Alive(_blackjack) && _slots.Count > 0) return;

            try
            {
                var around = _door.Landing;
                _slots.Clear();

                foreach (var prop in World.GetNearbyProps(around, Reach))
                {
                    if (prop == null || !prop.Exists()) continue;

                    var hash = prop.Model.Hash;

                    if (Is(hash, RouletteModels)) _roulette = prop;
                    else if (Is(hash, BlackjackModels)) _blackjack = prop;
                    else if (Is(hash, SlotModels)) _slots.Add(prop);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Den: could not look for the tables: " + ex.Message);
            }

            if (!_saidTables && (Alive(_roulette) || Alive(_blackjack) || _slots.Count > 0))
            {
                _saidTables = true;
                Log.Info("Den: the room has " + (Alive(_roulette) ? "the roulette, " : "no roulette, ") +
                         (Alive(_blackjack) ? "the blackjack, " : "no blackjack, ") +
                         _slots.Count + " slot machine(s).");
            }
        }

        private void Tune()
        {
            if (_tuned) return;
            _tuned = true;

            var station = Radio.Blonded;
            var names = Jukebox;

            if (names.Length == 0)
            {
                Log.Info("Den: no jukebox in this room -- [GamblingDen] Jukebox names none.");
                return;
            }

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
        }

        private void Dealers()
        {
            // The jukebox waits for nothing; it is tuned the moment we are in.
            Tune();

            var gang = _gangs == null ? null : _gangs.Get("families");

            if (Alive(_roulette))
            {
                if (_croupier == null) _croupier = new Dealer(_roulette, Scene.RouletteDealer, RouletteIdles, "roulette", _rng);
                if (!_croupier.Exists) _croupier.Spawn(gang);
                _croupier.Update();
            }

            if (Alive(_blackjack))
            {
                if (_cardDealer == null) _cardDealer = new Dealer(_blackjack, Scene.BlackjackDealer, BlackjackIdles, "blackjack", _rng);
                if (!_cardDealer.Exists) _cardDealer.Spawn(gang);
                _cardDealer.Update();
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

            if (Alive(_roulette) && Flat(at, _roulette.Position) <= TableReach && _croupier != null && _croupier.Exists)
            {
                Help.ShowThisFrame("Press ~INPUT_CONTEXT~ to play roulette. $" + MinBet.ToString("N0") + " minimum.");

                if (Game.IsControlJustPressed(Control.Context))
                {
                    _atRoulette = new Roulette(_roulette, _croupier, MinBet, MaxBet, _rng);
                    InputGuard.Swallow();
                }

                return;
            }

            if (Alive(_blackjack) && Flat(at, _blackjack.Position) <= TableReach && _cardDealer != null && _cardDealer.Exists)
            {
                Help.ShowThisFrame("Press ~INPUT_CONTEXT~ to play blackjack. $" + MinBet.ToString("N0") + " minimum.");

                if (Game.IsControlJustPressed(Control.Context))
                {
                    _atBlackjack = new Blackjack(_cardDealer, MinBet, MaxBet, _rng);
                    InputGuard.Swallow();
                }

                return;
            }

            Entity nearest = null;
            var best = SlotReach;

            foreach (var slot in _slots)
            {
                if (!Alive(slot)) continue;

                var d = Flat(at, slot.Position);
                if (d > best) continue;

                best = d;
                nearest = slot;
            }

            if (nearest == null) return;

            Help.ShowThisFrame("Press ~INPUT_CONTEXT~ to play the slots. $" + MinBet.ToString("N0") + " a pull.");

            if (Game.IsControlJustPressed(Control.Context))
            {
                _atSlot = new Slots(nearest, me, MinBet, MaxBet, _rng);
                InputGuard.Swallow();
            }
        }

        /// <summary>Whatever is being played, stopped where it is. For leaving and for teardown.</summary>
        private void Quit()
        {
            try
            {
                if (_atRoulette != null) { Scene.Stop(_roulette); }
                if (_atSlot != null)
                {
                    var me = Game.Player.Character;
                    if (me != null && me.Exists()) Function.Call(Hash.CLEAR_PED_TASKS, me.Handle);
                }
            }
            catch { }

            _atRoulette = null;
            _atBlackjack = null;
            _atSlot = null;
        }

        private static bool Alive(Entity e)
        {
            return e != null && e.Exists();
        }

        private static bool Is(int hash, int[] models)
        {
            foreach (var m in models)
            {
                if (m == hash) return true;
            }

            return false;
        }

        private static float Flat(Vector3 a, Vector3 b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
