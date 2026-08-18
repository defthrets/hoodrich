using System;
using System.Collections.Generic;
using Control = GTA.Control;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Gangs;
using Hoodrich.UI;

namespace Hoodrich.Locations
{
    /// <summary>One thing Big J will sell you.</summary>
    internal sealed class Piece
    {
        public readonly string Name;
        public readonly string Weapon;
        public readonly int Price;
        public readonly int Ammo;
        public readonly string Note;

        public Piece(string name, string weapon, int price, int ammo, string note)
        {
            Name = name;
            Weapon = weapon;
            Price = price;
            Ammo = ammo;
            Note = note;
        }

        public uint Hash => Function.Call<uint>(GTA.Native.Hash.GET_HASH_KEY, Weapon);
    }

    /// <summary>
    /// Big J, who sells guns out of the courtyard on Forum Drive.
    ///
    /// Deliberately not a shop. Ammu-Nation exists, it is on the map, and it wants your licence
    /// and your name -- so a man in a courtyard who does not is a different thing to have, not a
    /// cheaper version of the same thing. He is one of yours, he is always there, and what he
    /// has is what he has.
    ///
    /// Kept apart from Stretch and Lamar for the same reason those two are kept apart: three
    /// people you go to for three different things reads as a neighbourhood, and one NPC with
    /// everything bolted to him reads as a menu.
    /// </summary>
    internal sealed class Armourer
    {
        /// <summary>The courtyard, up the path from Denise's.</summary>
        private static readonly Vector3 Spot = new Vector3(-129.187f, -1461.375f, 33.823f);
        private const float Heading = 294.841f;

        private const float SpawnRange = 110f;
        private const float DespawnRange = 190f;
        private const float TalkRange = 3.0f;
        private const int UpdateIntervalMs = 700;

        /// <summary>Pistol blip, which is what he is.</summary>
        private const int Sprite = 110;

        /// <summary>
        /// Big by name. Tried in order, first one this install has wins -- the heavy Families
        /// models are the point of the name, so the slim ones are last resorts rather than
        /// equals.
        /// </summary>
        private static readonly string[] Models =
        {
            "g_m_y_famca_01", "a_m_m_famdd_01", "g_m_y_famfor_01",
            "a_m_m_soucent_01", "a_m_m_soucent_02", "g_m_y_famdnf_01"
        };

        /// <summary>
        /// What he has. Priced under Ammu-Nation, because none of it came with paperwork.
        ///
        /// Grouped the way he would lay it out on the table rather than by the game's own
        /// categories: handguns, then things that hold more, then rifles, then what you carry
        /// when you are not carrying, then what you throw.
        /// </summary>
        public static readonly Piece[] Handguns =
        {
            new Piece("WM 29 Pistol",         "WEAPON_PISTOL",         450,  60, "Does the job"),
            new Piece("SNS Pistol",           "WEAPON_SNSPISTOL",      300,  40, "Fits anywhere"),
            new Piece("Vintage Pistol",       "WEAPON_VINTAGEPISTOL",  650,  40, "Somebody's grandad's"),
            new Piece("Double Action",        "WEAPON_DOUBLEACTION",   900,  36, "Slow and mean"),
            new Piece("AP Pistol",            "WEAPON_APPISTOL",      1600, 120, "Goes through things"),
        };

        public static readonly Piece[] Automatics =
        {
            new Piece("Micro SMG",            "WEAPON_MICROSMG",      2200, 200, "Loud in a car"),
            new Piece("Compact Rifle",        "WEAPON_COMPACTRIFLE",  4500, 180, "The choppa"),
            new Piece("Carbine Rifle Mk II",  "WEAPON_CARBINERIFLE_MK2", 9500, 250, "Serious money"),
            new Piece("Assault Rifle Mk II",  "WEAPON_ASSAULTRIFLE_MK2", 8500, 250, "Serious money"),
        };

        public static readonly Piece[] Shotguns =
        {
            new Piece("Sawn-Off Shotgun",     "WEAPON_SAWNOFFSHOTGUN", 1900,  40, "Close work"),
            new Piece("Double Barrel",        "WEAPON_DBSHOTGUN",      2400,  24, "Two and done"),
        };

        public static readonly Piece[] Melee =
        {
            new Piece("Switchblade",          "WEAPON_SWITCHBLADE",     150,   0, "Quiet"),
            new Piece("Knuckle Duster",       "WEAPON_KNUCKLE",         120,   0, "Quieter"),
            new Piece("Machete",              "WEAPON_MACHETE",         250,   0, "Not subtle"),
            new Piece("Baseball Bat",         "WEAPON_BAT",             100,   0, "Sporting equipment"),
            new Piece("Crowbar",              "WEAPON_CROWBAR",         100,   0, "A tool, officer"),
        };

        public static readonly Piece[] Throwables =
        {
            new Piece("Molotov",              "WEAPON_MOLOTOV",         350,   5, "Bottle and a rag"),
            new Piece("Pipe Bomb",            "WEAPON_PIPEBOMB",        900,   5, "Homemade"),
            new Piece("Sticky Bomb",          "WEAPON_STICKYBOMB",     2500,   5, "For doors"),
            new Piece("Flare",                "WEAPON_FLARE",            80,  10, "For seeing"),
        };

        private readonly Affiliation _crew;
        private readonly GangRegistry _gangs;

        private Ped _ped;
        private Blip _blip;
        private int _lastUpdate;
        private bool _held;
        private bool _talkHeld;

        public Armourer(Affiliation crew, GangRegistry gangs)
        {
            _crew = crew;
            _gangs = gangs;
        }

        public string Name => "Big J";

        public Vector3 Position => Spot;

        public Ped Ped => _ped != null && _ped.Exists() ? _ped : null;

        /// <summary>Set by Main: the conversation screen and what he has to say.</summary>
        public Conversation Talk;
        public Func<DialogueNode> TalkBuilder;

        public bool InReach
        {
            get
            {
                var player = Game.Player.Character;
                if (player == null || !player.Exists() || _ped == null || !_ped.Exists()) return false;

                var a = player.Position;
                var b = _ped.Position;
                var dx = a.X - b.X;
                var dy = a.Y - b.Y;

                return (float)Math.Sqrt(dx * dx + dy * dy) <= TalkRange;
            }
        }

        // ---- per-tick ----------------------------------------------------------

        public void Update()
        {
            var now = Game.GameTime;
            if (now - _lastUpdate < UpdateIntervalMs) return;
            _lastUpdate = now;

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsAlive) return;

            EnsureBlip();

            var away = player.Position.DistanceTo(Spot);

            if (_ped != null && _ped.Exists())
            {
                if (away > DespawnRange) Despawn();
                else if (!_held) Settle();

                return;
            }

            if (away <= SpawnRange) Spawn();
        }

        private void Spawn()
        {
            foreach (var name in Models)
            {
                try
                {
                    var model = new Model(name);
                    if (!model.IsValid || !model.IsInCdImage || !model.Request(1500)) continue;

                    // Probed from just above the authored height and only believed if it agrees.
                    // The courtyard has a first-floor walkway over it, and a probe from high up
                    // finds that instead of the path.
                    var spot = Spot;

                    try
                    {
                        if (World.GetGroundHeight(new Vector3(spot.X, spot.Y, spot.Z + 1.5f),
                                                  out var groundZ, GetGroundHeightMode.Normal) &&
                            groundZ > 0f && Math.Abs(groundZ - spot.Z) <= 3f)
                        {
                            spot.Z = groundZ;
                        }
                    }
                    catch
                    {
                        // Keep the authored height.
                    }

                    _ped = World.CreatePed(model, spot, Heading);
                    model.MarkAsNoLongerNeeded();

                    if (_ped == null || !_ped.Exists()) continue;

                    _ped.IsPersistent = true;
                    _ped.BlockPermanentEvents = true;

                    Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, _ped.Handle, true, true);
                    Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, _ped.Handle, false);
                    Function.Call(Hash.SET_AMBIENT_VOICE_NAME, _ped.Handle, "SHOP_CLOTHES_LS");

                    var gang = _gangs.Get("families");
                    if (gang != null)
                    {
                        Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, _ped.Handle, gang.GroupHash);
                    }

                    Settle();

                    Log.Info("Big J is out at " + spot + ".");
                    return;
                }
                catch
                {
                    // Try the next model.
                }
            }

            Log.Warn("No model would load for Big J.");
        }

        /// <summary>Puts him back on his spot, facing the right way.</summary>
        private void Settle()
        {
            try
            {
                if (_ped.Position.DistanceTo(Spot) > 2.5f)
                {
                    _ped.Position = Spot;
                }

                if (!Function.Call<bool>(Hash.IS_PED_USING_SCENARIO, _ped.Handle, "WORLD_HUMAN_SMOKING"))
                {
                    _ped.Task.ClearAll();
                    Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, _ped.Handle,
                                  "WORLD_HUMAN_SMOKING", 0, true);
                    _ped.Heading = Heading;
                }
            }
            catch
            {
                // He will settle on his own.
            }
        }

        private void EnsureBlip()
        {
            if (_blip != null && _blip.Exists()) return;

            try
            {
                _blip = World.CreateBlip(Spot);
                if (_blip == null || !_blip.Exists()) return;

                Function.Call(Hash.SET_BLIP_SPRITE, _blip.Handle, Sprite);
                _blip.Color = BlipColor.Green;
                _blip.Scale = 0.8f;
                _blip.IsShortRange = true;
                _blip.Name = "Big J -- guns";
            }
            catch (Exception ex)
            {
                Log.Debug("Could not blip Big J: " + ex.Message);
            }
        }

        // ---- talking -----------------------------------------------------------

        public void UpdatePrompt()
        {
            if (Talk == null || Talk.IsOpen || !InReach) return;

            Help.ShowThisFrame("Press ~INPUT_CELLPHONE_RIGHT~ to see what Big J has.");

            if (!WantsToTalk()) return;

            var root = TalkBuilder == null ? null : TalkBuilder();
            if (root == null) return;

            HoldForTalk();

            Talk.Speaker = _ped;
            Talk.Open(root, this);
        }

        private bool WantsToTalk()
        {
            var down = false;

            try
            {
                down = Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, (int)Control.PhoneRight)
                    || Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)Control.PhoneRight)
                    || Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, (int)Control.Context)
                    || Game.IsKeyPressed(System.Windows.Forms.Keys.Right)
                    || Game.IsKeyPressed(System.Windows.Forms.Keys.E);
            }
            catch
            {
                // Unreadable control is simply not pressed.
            }

            var pressed = down && !_talkHeld;
            _talkHeld = down;
            return pressed;
        }

        public void HoldForTalk()
        {
            if (_ped == null || !_ped.Exists() || _held) return;

            _held = true;

            try
            {
                var player = Game.Player.Character;

                _ped.Task.ClearAll();

                if (player != null && player.Exists())
                {
                    Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, _ped.Handle, player.Handle, -1);
                }
            }
            catch
            {
                // He will still talk.
            }
        }

        public void ReleaseFromTalk()
        {
            if (!_held || _ped == null || !_ped.Exists()) return;

            _held = false;
            Settle();
        }

        private void Despawn()
        {
            try
            {
                if (_ped != null && _ped.Exists())
                {
                    _ped.MarkAsNoLongerNeeded();
                    _ped.Delete();
                }
            }
            catch { /* teardown */ }

            _ped = null;
            _held = false;
        }

        public void RestoreWorld()
        {
            Despawn();

            try { if (_blip != null && _blip.Exists()) _blip.Delete(); }
            catch { /* teardown */ }

            _blip = null;
        }
    }
}
