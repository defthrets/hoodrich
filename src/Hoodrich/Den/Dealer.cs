using System;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Gangs;

namespace Hoodrich.Den
{
    /// <summary>
    /// The woman behind the table.
    ///
    /// One of the set -- Michael asked for Families women on the tables, 2026-09-25 -- stood
    /// where the casino's own animators put a dealer, doing what a dealer does between hands:
    /// idling, and now and then one of the idle variations so she is not a statue. Asked for an
    /// action -- spin the wheel, deal a card, rake the chips, a reaction to your luck -- she
    /// plays it once and goes back to idling on her own. See Scene for why there is no
    /// coordinate anywhere in here.
    ///
    /// She is somebody. Stamped for NPC Mind like everybody else the mod puts down, under a
    /// name that is the table rather than the session, so the same woman deals at the roulette
    /// every time you walk in.
    /// </summary>
    internal sealed class Dealer
    {
        public Ped Ped { get; private set; }

        /// <summary>What she is stood at, and which set of clips she idles in.</summary>
        private readonly Entity _table;
        private readonly string _idleDict;
        private readonly string[] _idles;
        private readonly string _who;

        private readonly Random _rng;

        private int _scene = -1;
        private bool _acting;
        private int _nextVariant;

        /// <summary>How long one idle runs before she tries another.</summary>
        private const int VariantMinMs = 14000;
        private const int VariantMaxMs = 30000;

        private const string Model = "g_f_y_families_01";

        public Dealer(Entity table, string idleDict, string[] idles, string who, Random rng)
        {
            _table = table;
            _idleDict = idleDict;
            _idles = idles;
            _who = who;
            _rng = rng;
        }

        public bool Exists => Ped != null && Ped.Exists() && Ped.IsAlive;

        /// <summary>She is in the middle of something asked of her; the game waits.</summary>
        public bool Busy => _acting && !Scene.Done(_scene);

        /// <summary>
        /// Stands her up beside the table. Only once the clips are in: her first pose is the
        /// idle, and a ped with no idle to play stands in the road for a second.
        /// </summary>
        public bool Spawn(GangDef gang)
        {
            if (Exists) return true;
            if (_table == null || !_table.Exists()) return false;
            if (!Scene.Loaded(_idleDict)) return false;
            if (Core.Crowded.Busy) { Core.Crowded.HeldOff("Den"); return false; }

            try
            {
                var model = new Model(Model);
                if (!model.IsValid || !model.IsInCdImage || !Models.Ready(model)) return false;

                // Beside the table and not in it. The scene moves her to her mark on the
                // first frame; this is only where she exists for that frame.
                var at = _table.Position + _table.ForwardVector * 1.5f;
                var ped = World.CreatePed(model, at, _table.Heading + 180f);
                model.MarkAsNoLongerNeeded();

                if (ped == null || !ped.Exists()) return false;

                ped.IsPersistent = true;
                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, ped.Handle, true, true);
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, true);
                Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, ped.Handle, false);
                Function.Call(Hash.SET_PED_CAN_RAGDOLL, ped.Handle, false);
                Function.Call(Hash.SET_ENTITY_INVINCIBLE, ped.Handle, true);

                if (gang != null) Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle, gang.GroupHash);

                Core.Helmets.Off(ped);
                Folk.Stamp(ped, "den:dealer:" + _who);

                Ped = ped;
                Idle();

                Log.Info("Den: the dealer is stood at the " + _who + ".");
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Den: could not stand a dealer at the " + _who + ": " + ex.Message);
                return false;
            }
        }

        /// <summary>Between hands: her idle, looped, in a scene on the table.</summary>
        public void Idle()
        {
            if (!Exists) return;

            _acting = false;
            _scene = Scene.At(_table);

            var clip = _pinned ?? _idles[_rng.Next(_idles.Length)];
            Scene.Ped(_scene, Ped, _idleDict, clip, true);

            _nextVariant = Game.GameTime + _rng.Next(VariantMinMs, VariantMaxMs);
        }

        /// <summary>
        /// One idle and no other, until told otherwise: the blackjack dealer watching the one
        /// player at her table rather than the room. Null pins nothing.
        /// </summary>
        public void Pin(string clip)
        {
            if (_pinned == clip) return;

            _pinned = clip;
            if (!_acting) Idle();
        }

        private string _pinned;

        /// <summary>Something asked of her, once. Idle again when it is done.</summary>
        public void Act(string dict, string clip, bool holdLast = false)
        {
            if (!Exists) return;

            if (!Scene.Loaded(dict))
            {
                Log.Debug("Den: " + dict + " is not loaded, so the dealer skips " + clip + ".");
                return;
            }

            _acting = true;
            _scene = Scene.At(_table);
            Scene.Ped(_scene, Ped, dict, clip, false, holdLast);
        }

        /// <summary>A scene the game wants her in with other things -- the wheel, the ball.</summary>
        public int Begin(string dict, string clip)
        {
            if (!Exists) return -1;

            _acting = true;
            _scene = Scene.At(_table);
            Scene.Ped(_scene, Ped, dict, clip, false);
            return _scene;
        }

        /// <summary>Her reaction to how it went for you, from the shared set.</summary>
        public void React(bool good)
        {
            var n = 1 + _rng.Next(3);
            Act(Scene.SharedDealer, (good ? "female_dealer_reaction_good_var0" : "female_dealer_reaction_bad_var0") + n);
        }

        public void Update()
        {
            if (!Exists) return;

            // An action that has run its course: back to the table.
            if (_acting)
            {
                if (Scene.Done(_scene)) Idle();
                return;
            }

            // Or an idle she has held long enough. A new scene each time -- the old one is let
            // go the moment she is tasked out of it.
            if (Game.GameTime >= _nextVariant || !Scene.Running(_scene)) Idle();
        }

        public void Remove()
        {
            try
            {
                if (Ped != null && Ped.Exists()) Ped.Delete();
            }
            catch { }

            Ped = null;
            _scene = -1;
            _acting = false;
        }
    }
}
