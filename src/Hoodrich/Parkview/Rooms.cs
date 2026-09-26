using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Parkview.Core;
using Hoodrich.UI;

namespace Hoodrich.Parkview
{
    /// <summary>
    /// The rooms behind the doors. Michael asked for it on 2026-09-22: walk up to one of the
    /// apartment doors, be offered it at $250 a week, and once it is yours walk in and be
    /// inside a shabby little room.
    ///
    /// SIX DOORS, ONE ROOM. The doors are real spots on the block -- two columns of three,
    /// read off the coordinate HUD -- and they all open into the same interior, moved to a
    /// different corner of it per door so two rooms are never the same corner. A room per
    /// door would mean six interiors, and the game has one shabby motel, not six.
    ///
    /// WHERE THE ROOM IS, is the one thing that could not be settled from this machine.
    /// Nothing here ships interior coordinates -- IPLList.xml is five lines and the Enable
    /// All Interiors ini is two colour settings -- so the spot below came off a motel script
    /// and is UNVERIFIED. So it is never trusted: the interior is asked for by name at the
    /// coordinate, and if the game does not answer with a real one the door says so and
    /// nobody is teleported into rock. Set it yourself with the set-room key and the guess is
    /// never used again.
    /// </summary>
    internal sealed class Rooms
    {
        /// <summary>A door on the block, and whether it is yours.</summary>
        private sealed class Door
        {
            public Vector3 At;
            public float Yaw;
            public string Name = "";

            public bool Rented;

            /// <summary>The day the next week's rent falls due. See Today.</summary>
            public int Due;

            /// <summary>Where in the room this one puts you, so six doors are not one corner.</summary>
            public int Corner;
        }

        /// <summary>
        /// A marker on the ground with a blip on the map, that takes you somewhere of its
        /// own. Unlike a door it is not rented and costs nothing -- it is a way in.
        /// </summary>
        private sealed class Place
        {
            public Vector3 At;
            public float Yaw;

            public Vector3 To;
            public float ToYaw;

            public int Sprite;
            public string Name = "";

            public int Blip;
            public int Interior;
            public bool Checked;
            public bool Good;
        }

        /// <summary>Something you can do standing somewhere. See data\spots.txt.</summary>
        private sealed class Act
        {
            public Vector3 At;
            public float Reach = 1.5f;
            public string Action = "";
            public string Label = "";
        }

        private readonly Settings _cfg;
        private readonly List<Door> _doors = new List<Door>();
        private readonly List<Place> _places = new List<Place>();
        private readonly List<Act> _acts = new List<Act>();

        /// <summary>The house on the map over the doors, and which place you walked in by.</summary>
        private int _houseBlip;
        private int _inPlace = -1;

        /// <summary>The room the doors open into, and whether it was set in game or shipped.</summary>
        private Vector3 _room;
        private float _roomYaw;
        private bool _roomIsOurs;

        /// <summary>Whether the game has confirmed there is really an interior there.</summary>
        private bool _roomChecked;
        private bool _roomGood;
        private int _room_id;

        /// <summary>Which door you walked in by, so leaving puts you back at it.</summary>
        private int _inside = -1;

        private int _dayAt;
        private int _lastDay = -1;

        /// <summary>
        /// The motel room this ships with, at the interior shell under the city where the
        /// game keeps them. Unverified -- see the note on the class. Overridden the moment
        /// anybody presses the set-room key.
        /// </summary>
        private static readonly Vector3 Shipped = new Vector3(151.4023f, -1007.8794f, -99.0f);

        /// <summary>How near the door you have to be stood to be offered it.</summary>
        private const float DoorReach = 1.8f;

        /// <summary>
        /// THE WAY OUT IS A SPOT. It used to be offered from anywhere within twelve metres
        /// of where you landed, which is the whole room and the corridor with it -- so the
        /// prompt sat on screen the entire time you were inside. Michael read the door off
        /// the coordinate HUD and this is it: you walk to the door to leave, the same as you
        /// walked to a door to come in.
        ///
        /// Overridden per place by the set-room key; this is the motel the doors share.
        /// </summary>
        private static readonly Vector3 RoomOut = new Vector3(151.339f, -1007.840f, -99.0f);
        private const float RoomOutYaw = 165.369f;

        /// <summary>How far out of the room he can get before we stop believing he is in it.</summary>
        private const float RoomReach = 25f;

        /// <summary>
        /// How often the game's date is asked for. The DOORS are looked at every frame --
        /// six distance checks is nothing, and the prompt has to be drawn every frame or it
        /// flashes. See Say.
        /// </summary>
        private const int DayEveryMs = 4000;

        /// <summary>How often the room is swept for anybody who wandered back into it.</summary>
        private const int SweepEveryMs = 500;
        private int _sweptAt;

        /// <summary>A week, in days on the game's clock.</summary>
        private const int Week = 7;

        /// <summary>
        /// The house over the apartments. 40 is radar_safehouse, which is the little house,
        /// and it is the number BLIPS.md and the FiveM list both give. A sprite the game does
        /// not have draws the default DOT rather than nothing, so a wrong number looks like a
        /// working blip -- which is why these are checked rather than remembered.
        /// </summary>
        private const int HouseSprite = 40;

        /// <summary>How near a place you have to be, and how big its ring on the ground is.</summary>
        private const float PlaceReach = 1.8f;

        /// <summary>
        /// How near the way out of the rented room you stand to be offered it. Its own number,
        /// and small: Michael asked on 2026-09-26 for the leave prompt only when he is at the door.
        /// </summary>
        private const float OutReach = 1.0f;
        private const float MarkerSize = 0.9f;

        /// <summary>
        /// Where in the room each door stands you. ALONG THE ROOM, NOT ACROSS IT: the source
        /// the motel spot came from gives three points in that interior -- the way in at
        /// Y -1007.88 and two more at -1003.13 and -1001.33, all within half a metre of
        /// X 151.4 -- so it is a narrow room about six and a half metres long lying in Y.
        /// Offsets are taken inside that, and none across X, because a metre and a half
        /// sideways in a room that narrow is a wall.
        ///
        /// They were invented before that was read properly, which is how you end up stood
        /// in plaster.
        /// </summary>
        private static readonly Vector3[] Corners =
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(0f, 1.3f, 0f),
            new Vector3(0f, 2.6f, 0f),
            new Vector3(0f, 3.9f, 0f),
            new Vector3(0f, 5.0f, 0f),
            new Vector3(0f, 6.1f, 0f)
        };

        public Rooms(Settings cfg)
        {
            _cfg = cfg;
            _room = Shipped;
            _roomYaw = 0f;

            ReadDoors();
            ReadPlaces();
            ReadSpots();
            ReadSave();
            Blips();

            // Where he is stood, for the house: the cupboard opens from inside a rented room
            // and the cook screen stays up while he is at the table. See Core.Home.
            Home.InRoom = () => _inside >= 0;

            // AT A TABLE WHEREVER ONE STANDS. A spot is only reachable where it is, and since
            // 2026-09-26 there is one outside the motel -- the kitchen bench in Apartment E2.
            Home.AtTable = () => Near("cut", 1.2f);

            Log.Info("Rooms: " + _doors.Count + " door(s), " + Mine() + " rented. The room is " +
                     (_roomIsOurs ? "the one you set" : "the one it ships with") + " at " + Say(_room) + ".");

            // Asked for now rather than at the door, so it is built by the time anybody
            // walks up to one, and so the log says whether it is there at all on the line
            // after this one instead of the first time somebody tries.
            RoomReady();
        }

        private static string Say(Vector3 at)
        {
            return at.X.ToString("0.0", CultureInfo.InvariantCulture) + ", " +
                   at.Y.ToString("0.0", CultureInfo.InvariantCulture) + ", " +
                   at.Z.ToString("0.0", CultureInfo.InvariantCulture);
        }

        private int Mine()
        {
            var n = 0;
            foreach (var d in _doors) if (d.Rented) n++;
            return n;
        }

        // ---- the files ------------------------------------------------------------------

        private string SavePath => Path.Combine(Paths.ParkviewWritable, "rooms.sav");

        /// <summary>The shipped list of doors. See data\rooms.txt for the format.</summary>
        private void ReadDoors()
        {
            var path = Path.Combine(Paths.Parkview, "rooms.txt");

            if (!File.Exists(path))
            {
                Log.Info("Rooms: no rooms.txt in " + Paths.Parkview + "; there are no doors to rent.");
                return;
            }

            try
            {
                var corner = 0;

                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;

                    var bits = line.Split(',');
                    if (bits.Length < 5) continue;

                    float x, y, z, yaw;
                    var ci = CultureInfo.InvariantCulture;
                    var ns = NumberStyles.Float;

                    if (!float.TryParse(bits[0].Trim(), ns, ci, out x)) continue;
                    if (!float.TryParse(bits[1].Trim(), ns, ci, out y)) continue;
                    if (!float.TryParse(bits[2].Trim(), ns, ci, out z)) continue;
                    if (!float.TryParse(bits[3].Trim(), ns, ci, out yaw)) continue;

                    var name = string.Join(",", bits, 4, bits.Length - 4).Trim();

                    _doors.Add(new Door
                    {
                        At = new Vector3(x, y, z),
                        Yaw = yaw,
                        Name = name.Length == 0 ? "A room" : name,
                        Corner = corner++ % Corners.Length
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Error("Rooms: could not read rooms.txt.", ex);
            }
        }

        /// <summary>The markers that take you somewhere. See data\places.txt.</summary>
        private void ReadPlaces()
        {
            var path = Path.Combine(Paths.Parkview, "places.txt");
            if (!File.Exists(path)) return;

            try
            {
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;

                    var bars = line.Split('|');
                    if (bars.Length < 4) continue;

                    Vector3 at, to;
                    float yaw, toYaw;

                    if (!Four(bars[0], out at, out yaw)) continue;
                    if (!Four(bars[1], out to, out toYaw)) continue;

                    int sprite;
                    if (!int.TryParse(bars[2].Trim(), out sprite)) sprite = 0;

                    var name = string.Join("|", bars, 3, bars.Length - 3).Trim();

                    _places.Add(new Place
                    {
                        At = at,
                        Yaw = yaw,
                        To = to,
                        ToYaw = toYaw,
                        Sprite = sprite,
                        Name = name.Length == 0 ? "A way in" : name
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Error("Rooms: could not read places.txt.", ex);
            }
        }

        /// <summary>"X, Y, Z, heading" out of one group.</summary>
        private static bool Four(string s, out Vector3 at, out float yaw)
        {
            at = Vector3.Zero;
            yaw = 0f;

            var bits = s.Split(',');
            if (bits.Length < 4) return false;

            var ci = CultureInfo.InvariantCulture;
            var ns = NumberStyles.Float;

            float x, y, z;

            if (!float.TryParse(bits[0].Trim(), ns, ci, out x)) return false;
            if (!float.TryParse(bits[1].Trim(), ns, ci, out y)) return false;
            if (!float.TryParse(bits[2].Trim(), ns, ci, out z)) return false;
            if (!float.TryParse(bits[3].Trim(), ns, ci, out yaw)) return false;

            at = new Vector3(x, y, z);
            return true;
        }

        /// <summary>The things you can do standing somewhere. See data\spots.txt.</summary>
        private void ReadSpots()
        {
            var path = Path.Combine(Paths.Parkview, "spots.txt");
            if (!File.Exists(path)) return;

            try
            {
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;

                    var bars = line.Split('|');
                    if (bars.Length < 4) continue;

                    Vector3 at;
                    float yaw;
                    if (!Four(bars[0] + ",0", out at, out yaw)) continue;

                    float reach;
                    if (!float.TryParse(bars[1].Trim(), NumberStyles.Float,
                                        CultureInfo.InvariantCulture, out reach)) reach = 1.5f;

                    _acts.Add(new Act
                    {
                        At = at,
                        Reach = Math.Max(0.5f, Math.Min(8f, reach)),
                        Action = bars[2].Trim().ToLowerInvariant(),
                        Label = string.Join("|", bars, 3, bars.Length - 3).Trim()
                    });
                }

                Log.Info("Rooms: " + _acts.Count + " spot(s) to do something at.");
            }
            catch (Exception ex)
            {
                Log.Error("Rooms: could not read spots.txt.", ex);
            }
        }

        /// <summary>
        /// Whether Posted Up is here and awake.
        ///
        /// BY REFLECTION AND NOTHING ELSE. Parkview references no assembly but the BCL and
        /// ScriptHookVDotNet, and that is worth keeping: a hard reference would mean it will
        /// not load at all without Hoodrich sat next to it. So its public Api is looked up by
        /// name in whatever is loaded, once, and if it is not there the spots that need it
        /// say so and the bed still works.
        /// </summary>
        private static bool PostedUp()
        {
            // Posted Up is this dll now, so the only question is whether its side is wired
            // yet -- Api.Drugs says so, the same as it does for any other mod asking.
            try { return Api.Drugs.Ready; }
            catch { return false; }
        }

        /// <summary>How long you are out for, and what hour you wake at.</summary>
        private const int SleepHour = 7;

        /// <summary>
        /// Sleep until morning. Parkview's own -- no other mod is asked and none is needed,
        /// which is the point: without Posted Up the room is still a room you can sleep in.
        /// </summary>
        private void Sleep()
        {
            var me = Game.Player.Character;
            if (me == null || !me.Exists()) return;

            try
            {
                var was = Function.Call<int>(Hash.GET_CLOCK_HOURS);

                Fade(true);

                // Round the clock to the morning. Already morning, and it is tomorrow morning.
                var hours = SleepHour - was;
                if (hours <= 0) hours += 24;

                Function.Call(Hash.ADD_TO_CLOCK_TIME, hours, 0, 0);

                me.Health = me.MaxHealth;

                Script.Wait(900);
                Fade(false);

                Log.Info("Rooms: slept from " + was + ":00 to " + SleepHour + ":00.");
                Notify.Important("~g~Morning.~s~ " + hours + " hours.");
            }
            catch (Exception ex)
            {
                Log.Error("Rooms: could not sleep.", ex);
                try { Fade(false); } catch { }
            }
        }

        /// <summary>Whether he is stood within reach of a spot that does this, plus some slack.</summary>
        private bool Near(string action, float slack)
        {
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return false;

                foreach (var s in _acts)
                {
                    if (s.Action != action) continue;
                    if (me.Position.DistanceTo(s.At) <= s.Reach + slack) return true;
                }
            }
            catch
            {
                /* not near */
            }

            return false;
        }

        /// <summary>The spots, drawn and offered. Returns true when one took the prompt.</summary>
        private bool Acts(Ped me, int now)
        {
            if (_acts.Count == 0) return false;

            foreach (var s in _acts)
            {
                if (me.Position.DistanceTo(s.At) > s.Reach) continue;

                if (s.Action == "sleep")
                {
                    Say("Press ~INPUT_CONTEXT~ to sleep at " + s.Label + ".");
                    if (Tapped()) Sleep();
                    return true;
                }

                // THE CLOSET AND THE TABLE ARE THE HOUSE'S, reached through Core.Home: the
                // same wardrobe screen as the closet at Denise's, the same cook screen as her
                // kitchen counter with "The Table" over it, and the same cupboard behind
                // both. Until Main has put them on the shelf they say so and do nothing.
                if (s.Action == "wardrobe")
                {
                    if (Home.OpenWardrobe == null)
                    {
                        Say(s.Label + " -- Posted Up is not running.");
                        return true;
                    }

                    Say("Press ~INPUT_CONTEXT~ to change at " + s.Label + ".");

                    if (Tapped())
                    {
                        // TURNED TO PUT THE CLOSET AT HIS BACK. He walked up to it, so it is
                        // in front of him; the wardrobe camera stands in front of whichever
                        // way he faces, and a shot of a man against the inside of a closet
                        // door is the picture Denise's fixed heading was chosen to avoid.
                        // Round the other way, the closet is behind him and the room is
                        // behind the camera, which is that same picture in this room.
                        var away = me.Heading + 180f;
                        if (away >= 360f) away -= 360f;

                        Home.OpenWardrobe(away);
                    }

                    return true;
                }

                if (s.Action == "cut")
                {
                    if (Home.OpenTable == null)
                    {
                        Say(s.Label + " -- Posted Up is not running.");
                        return true;
                    }

                    // The clips warmed while he reads the prompt, the same as at the counter,
                    // so the batch does not open with a man stood still waiting for them.
                    Economy.PrepAnimation.Preload("");

                    Say("Press ~INPUT_CONTEXT~ to work the product.");
                    if (Tapped()) Home.OpenTable();
                    return true;
                }

                if (!PostedUp())
                {
                    Say(s.Label + " -- Posted Up is not running.");
                    return true;
                }

                Say(s.Label + ".");
                return true;
            }

            return false;
        }
        /// <summary>The house over the block, and one blip per place.</summary>
        private void Blips()
        {
            try
            {
                if (_doors.Count > 0)
                {
                    var mid = Vector3.Zero;
                    foreach (var d in _doors) mid += d.At;
                    mid *= 1f / _doors.Count;

                    _houseBlip = Blip(mid, HouseSprite, "Parkview rooms", true);
                    Tint();
                }

                foreach (var p in _places)
                {
                    if (p.Sprite > 0) p.Blip = Blip(p.At, p.Sprite, p.Name, true);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Rooms: could not put the blips up.", ex);
            }
        }

        /// <summary>
        /// The house white on the map until one of its rooms is yours, and green once one is --
        /// the same as every door you rent. Michael asked for the apartments that way on 2026-09-26.
        /// </summary>
        private void Tint()
        {
            try
            {
                if (_houseBlip != 0) Function.Call(Hash.SET_BLIP_COLOUR, _houseBlip, Mine() > 0 ? 2 : 0);
            }
            catch
            {
                // A blip is a nicety.
            }
        }

        private static int Blip(Vector3 at, int sprite, string name, bool shortRange)
        {
            var b = Function.Call<int>(Hash.ADD_BLIP_FOR_COORD, at.X, at.Y, at.Z);
            if (b == 0) return 0;

            Function.Call(Hash.SET_BLIP_SPRITE, b, sprite);
            Function.Call(Hash.SET_BLIP_AS_SHORT_RANGE, b, shortRange);
            Function.Call(Hash.SET_BLIP_SCALE, b, 0.85f);

            Function.Call(Hash.BEGIN_TEXT_COMMAND_SET_BLIP_NAME, "STRING");
            Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, name);
            Function.Call(Hash.END_TEXT_COMMAND_SET_BLIP_NAME, b);

            return b;
        }

        /// <summary>Taken down with the script, so a reload does not leave them stacked up.</summary>
        public void Down()
        {
            Home.UnwireRoom();

            try
            {
                if (_houseBlip != 0) Function.Call(Hash.REMOVE_BLIP, new OutputArgument(_houseBlip));
                foreach (var p in _places) if (p.Blip != 0) Function.Call(Hash.REMOVE_BLIP, new OutputArgument(p.Blip));
            }
            catch
            {
                /* teardown */
            }
        }
        /// <summary>
        /// What is yours and what the rent is up to, next to the log rather than in the data
        /// folder -- Program Files is not writable by the game. See Paths.ParkviewWritable.
        /// </summary>
        private void ReadSave()
        {
            var path = SavePath;
            if (!File.Exists(path)) return;

            try
            {
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;

                    var bits = line.Split('|');
                    var ci = CultureInfo.InvariantCulture;
                    var ns = NumberStyles.Float;

                    if (bits[0] == "room" && bits.Length >= 5)
                    {
                        float x, y, z, yaw;

                        if (float.TryParse(bits[1], ns, ci, out x) &&
                            float.TryParse(bits[2], ns, ci, out y) &&
                            float.TryParse(bits[3], ns, ci, out z) &&
                            float.TryParse(bits[4], ns, ci, out yaw))
                        {
                            _room = new Vector3(x, y, z);
                            _roomYaw = yaw;
                            _roomIsOurs = true;
                        }

                        continue;
                    }

                    if (bits[0] == "place" && bits.Length >= 6)
                    {
                        float x, y, z, yaw;

                        if (!float.TryParse(bits[2], ns, ci, out x) ||
                            !float.TryParse(bits[3], ns, ci, out y) ||
                            !float.TryParse(bits[4], ns, ci, out z) ||
                            !float.TryParse(bits[5], ns, ci, out yaw)) continue;

                        // By name, for the same reason as the doors.
                        var hit = false;

                        foreach (var p in _places)
                        {
                            if (!string.Equals(p.Name, bits[1], StringComparison.OrdinalIgnoreCase)) continue;

                            p.To = new Vector3(x, y, z);
                            p.ToYaw = yaw;
                            hit = true;
                            break;
                        }

                        int i;

                        if (!hit && int.TryParse(bits[1], out i) && i >= 0 && i < _places.Count)
                        {
                            _places[i].To = new Vector3(x, y, z);
                            _places[i].ToYaw = yaw;
                        }

                        continue;
                    }

                    if (bits[0] == "rented" && bits.Length >= 3)
                    {
                        int due;
                        if (!int.TryParse(bits[2], out due)) continue;

                        // BY NAME, NOT BY PLACE IN THE FILE. rooms.txt is edited by hand --
                        // five of the six doors are commented out and the # comes off when
                        // one is wanted back -- and every one of those edits renumbers the
                        // rest. Keyed by number, commenting out B1 handed B1's rent to B2,
                        // which is somebody else's room and somebody else's money.
                        var found = false;

                        foreach (var d in _doors)
                        {
                            if (!string.Equals(d.Name, bits[1], StringComparison.OrdinalIgnoreCase)) continue;

                            d.Rented = true;
                            d.Due = due;
                            found = true;
                            break;
                        }

                        // An old save wrote the number. Honoured once, then written back by
                        // name the next time anything changes.
                        int i;

                        if (!found && int.TryParse(bits[1], out i) && i >= 0 && i < _doors.Count)
                        {
                            _doors[i].Rented = true;
                            _doors[i].Due = due;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Rooms: could not read " + path + ".", ex);
            }
        }

        private void Write()
        {
            try
            {
                var ci = CultureInfo.InvariantCulture;
                var lines = new List<string>
                {
                    "# Parkview rooms. Written by the mod; delete it to start again.",
                };

                if (_roomIsOurs)
                {
                    lines.Add("room|" + _room.X.ToString("0.####", ci) + "|" + _room.Y.ToString("0.####", ci) +
                              "|" + _room.Z.ToString("0.####", ci) + "|" + _roomYaw.ToString("0.##", ci));
                }

                foreach (var d in _doors)
                {
                    if (d.Rented) lines.Add("rented|" + d.Name + "|" + d.Due);
                }

                foreach (var p in _places)
                {
                    lines.Add("place|" + p.Name + "|" + p.To.X.ToString("0.####", ci) + "|" +
                              p.To.Y.ToString("0.####", ci) + "|" + p.To.Z.ToString("0.####", ci) + "|" +
                              p.ToYaw.ToString("0.##", ci));
                }

                File.WriteAllLines(SavePath, lines.ToArray());
            }
            catch (Exception ex)
            {
                Log.Error("Rooms: could not write " + SavePath + ".", ex);
            }
        }

        // ---- the game's calendar --------------------------------------------------------

        /// <summary>
        /// The date as one number, so a week is arithmetic rather than a calendar. Months are
        /// taken as thirty-one because the rent does not care and a short month must never
        /// run the number backwards.
        /// </summary>
        private static int Today()
        {
            try
            {
                var d = Function.Call<int>(Hash.GET_CLOCK_DAY_OF_MONTH);
                var m = Function.Call<int>(Hash.GET_CLOCK_MONTH);
                var y = Function.Call<int>(Hash.GET_CLOCK_YEAR);
                return (y * 12 + m) * 31 + d;
            }
            catch
            {
                return 0;
            }
        }

        // ---- the interior ---------------------------------------------------------------

        /// <summary>
        /// Whether there is really an interior where the room is meant to be. Asked ONCE and
        /// remembered, because a shipped coordinate that came off somebody else's script is a
        /// guess until the game agrees with it, and a guess that is wrong teleports a man into
        /// rock. The interior is pinned so it is built before he arrives rather than after.
        /// </summary>
        private bool RoomReady()
        {
            if (_roomChecked) return _roomGood;

            _roomChecked = true;

            try
            {
                var id = Function.Call<int>(Hash.GET_INTERIOR_AT_COORDS, _room.X, _room.Y, _room.Z);

                if (id == 0 || !Function.Call<bool>(Hash.IS_VALID_INTERIOR, id))
                {
                    _roomGood = false;

                    Log.Warn("Rooms: there is no interior at " + Say(_room) + ", so no door can open. " +
                             (_roomIsOurs
                                 ? "That is the spot you set -- stand somewhere with a floor and set it again."
                                 : "That is the spot this ships with and it was never verified on this install. " +
                                   "Stand in a room you like and press the set-room key."));
                    return false;
                }

                Function.Call(Hash.PIN_INTERIOR_IN_MEMORY, id);
                _room_id = id;
                _roomGood = true;

                Log.Info("Rooms: the room is interior " + id + " at " + Say(_room) + ", pinned.");
                return true;
            }
            catch (Exception ex)
            {
                _roomGood = false;
                Log.Error("Rooms: could not ask about the interior.", ex);
                return false;
            }
        }

        /// <summary>Where a given door stands you in the room.</summary>
        private Vector3 Spot(Door d)
        {
            var c = Corners[d.Corner % Corners.Length];
            return new Vector3(_room.X + c.X, _room.Y + c.Y, _room.Z + c.Z);
        }

        // ---- the beat -------------------------------------------------------------------

        public void Update()
        {
            if (_cfg != null && !_cfg.ParkviewRooms) return;

            var now = Game.GameTime;

            if (now - _dayAt >= DayEveryMs)
            {
                _dayAt = now;
                Rent();
            }

            var me = Game.Player.Character;
            if (me == null || !me.Exists() || !me.IsAlive) { Say(null); return; }

            // THE HOUSE HAS THE SCREEN, OR THE HANDS. While the closet or the cook screen is
            // up, or a batch is running at the table, nothing here is offered and nothing
            // here listens for the key -- the prompt to leave the room over a man bagging
            // product is a prompt that either interrupts him or lies.
            if (Home.IsBusy) { Say(null); return; }

            // INSIDE. The way out is the same button as the way in, and it is offered from
            // anywhere in the room rather than off a mark, because a small room has no door
            // of ours in it and a man stood in the corner should not be stuck.
            if (_inside >= 0)
            {
                if (me.Position.DistanceTo(_room) <= RoomReach)
                {
                    // Held down: the game puts another one in as soon as it is allowed to,
                    // and the sweep runs on a beat rather than every frame because deleting
                    // is not free and one every half second is quicker than anybody walks in.
                    Function.Call(Hash.SET_PED_DENSITY_MULTIPLIER_THIS_FRAME, 0f);
                    Function.Call(Hash.SET_SCENARIO_PED_DENSITY_MULTIPLIER_THIS_FRAME, 0f, 0f);

                    if (now - _sweptAt >= SweepEveryMs)
                    {
                        _sweptAt = now;
                        Alone(_room);
                    }

                    if (me.Position.DistanceTo(RoomOut) <= OutReach)
                    {
                        Say("Press ~INPUT_CONTEXT~ to leave.");
                        if (Tapped()) Leave();
                    }
                    else if (!Acts(me, now))
                    {
                        Say(null);
                    }

                    return;
                }

                // He left some other way -- a teleport, a reload. Forget he was in.
                Unbox();
                _inside = -1;
                Say(null);
                return;
            }

            // THE PLACES. Their rings are drawn whether or not you are at one, because a
            // marker you can only see once you are stood on it is not a marker.
            Rings(me);

            if (_inPlace >= 0)
            {
                var p = _places[_inPlace];

                if (me.Position.DistanceTo(p.To) <= RoomReach)
                {
                    Function.Call(Hash.SET_PED_DENSITY_MULTIPLIER_THIS_FRAME, 0f);
                    Function.Call(Hash.SET_SCENARIO_PED_DENSITY_MULTIPLIER_THIS_FRAME, 0f, 0f);

                    if (now - _sweptAt >= SweepEveryMs)
                    {
                        _sweptAt = now;
                        Alone(p.To);
                    }

                    Say("Press ~INPUT_CONTEXT~ to leave " + p.Name + ".");
                    if (Tapped()) LeavePlace();
                    return;
                }

                Unbox();
                _inPlace = -1;
                Say(null);
                return;
            }

            // THE SPOTS IN HIS OTHER PLACES. A spot is only reachable where it stands, so one
            // somewhere that is not a Parkview room -- the bench and the couch in Apartment E2,
            // behind one of Hoodrich's doors -- is offered the same way the motel's are.
            if (Acts(me, now)) return;

            for (var i = 0; i < _places.Count; i++)
            {
                if (me.Position.DistanceTo(_places[i].At) > PlaceReach) continue;

                Say(_places[i].Name + " -- press ~INPUT_CONTEXT~ to go in.");
                if (Tapped()) EnterPlace(i);
                return;
            }

            var near = -1;
            var best = DoorReach;

            for (var i = 0; i < _doors.Count; i++)
            {
                var d = me.Position.DistanceTo(_doors[i].At);
                if (d <= best) { best = d; near = i; }
            }

            if (near < 0) { Say(null); return; }

            var door = _doors[near];

            if (!door.Rented)
            {
                Say(door.Name + " -- press ~INPUT_CONTEXT~ to rent it for $" + _cfg.ParkviewRent + " a week.");
                if (Tapped()) Rent(near);
                return;
            }

            Say(door.Name + " is yours. Press ~INPUT_CONTEXT~ to go in.");
            if (Tapped()) Enter(near);
        }

        /// <summary>What the help box is saying, so it is only started and stopped once.</summary>
        private string _saying;

        /// <summary>
        /// THE PROMPT, AND THE TWO WAYS IT GOES WRONG.
        ///
        /// It flashed, and it stuck on screen after you walked away. Both were the same
        /// mistake: the help box was being started afresh every two hundred milliseconds off
        /// the throttle this method used to sit behind. Every call restarts the box's own
        /// fade-in, which is the flashing; and the last call before you walked away was still
        /// running out its own time with nobody to read it, which is the sticking.
        ///
        /// So it is drawn EVERY FRAME while it should be up -- which is how the game's own
        /// scripts do it, and it holds steady -- and the moment it should not be, it is taken
        /// down once with CLEAR_ALL_HELP_MESSAGES. Null is the way to say nothing.
        /// </summary>
        private void Say(string text)
        {
            // NPC MIND FIRST. Its "press E to talk" and its conversation panel answer on the
            // same key as every prompt here, and one press must never do both -- so while it
            // wants the key, ours is down. Step past the man and the door is yours again.
            if (Mind.Busy) text = null;

            if (string.IsNullOrEmpty(text))
            {
                if (_saying == null) return;

                _saying = null;
                try { Function.Call(Hash.CLEAR_ALL_HELP_MESSAGES); } catch { }
                return;
            }

            // NO BEEP, EVER. It was meant to sound once when a prompt appeared, and it went
            // off constantly instead -- so whatever is re-triggering the box, the noise is
            // not worth chasing. A prompt you are stood next to should be silent.
            _saying = text;

            Help(text, false);
        }

        /// <summary>One frame of the game's own help box. See Say.</summary>
        private static void Help(string text, bool beep)
        {
            try
            {
                Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_HELP, "STRING");
                Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, text);
                Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_HELP, 0, false, beep, -1);
            }
            catch
            {
                /* no prompt this frame */
            }
        }

        /// <summary>E on the keyboard, the right button on a pad: the game's own context key.</summary>
        private static bool Tapped()
        {
            // The key is NPC Mind's while it wants it. See Say.
            if (Mind.Busy) return false;

            try { return Function.Call<bool>(Hash.IS_CONTROL_JUST_PRESSED, 0, (int)Control.Context); }
            catch { return false; }
        }

        // ---- renting --------------------------------------------------------------------

        private void Rent(int i)
        {
            var door = _doors[i];
            var rent = _cfg.ParkviewRent;

            if (Game.Player.Money < rent)
            {
                Notify.Problem("You cannot cover the first week. $" + rent + " up front.");
                return;
            }

            Game.Player.Money -= rent;

            door.Rented = true;
            door.Due = Today() + Week;

            Write();
            Tint();

            Log.Info("Rooms: rented " + door.Name + " for $" + rent + "; next due day " + door.Due + ".");
            Notify.Important("~g~" + door.Name + "~s~ is yours. $" + rent + " a week, and the week is up in seven days.");
        }

        /// <summary>
        /// The week's rent, on the game's own calendar. Short of it, the room goes -- there is
        /// no grace day, because a room you cannot pay for is the whole point of a room at
        /// $250 a week.
        /// </summary>
        private void Rent()
        {
            var today = Today();
            if (today == 0) return;

            if (_lastDay < 0) { _lastDay = today; return; }
            if (today == _lastDay) return;

            _lastDay = today;

            var changed = false;

            foreach (var door in _doors)
            {
                if (!door.Rented || today < door.Due) continue;

                var rent = _cfg.ParkviewRent;

                if (Game.Player.Money >= rent)
                {
                    Game.Player.Money -= rent;
                    door.Due = today + Week;
                    changed = true;

                    Notify.Important("Rent on ~g~" + door.Name + "~s~: $" + rent + ".");
                    Log.Info("Rooms: took $" + rent + " for " + door.Name + "; next due day " + door.Due + ".");
                }
                else
                {
                    door.Rented = false;
                    door.Due = 0;
                    changed = true;

                    if (_inside >= 0 && _doors[_inside] == door) Leave();

                    Notify.Problem(door.Name + " is not yours any more. The week's rent was $" + rent + ".");
                    Log.Info("Rooms: could not pay $" + rent + " for " + door.Name + "; it is let go.");
                }
            }

            if (changed)
            {
                Write();
                Tint();
            }
        }

        // ---- in and out -----------------------------------------------------------------

        /// <summary>A ring on the ground at every place within sight of him.</summary>
        private void Rings(Ped me)
        {
            if (_places.Count == 0) return;

            foreach (var p in _places) Ring(me, p.At);
        }

        /// <summary>One ring, drawn where he can see it.</summary>
        private static void Ring(Ped me, Vector3 at)
        {
            try
            {
                if (me.Position.DistanceTo(at) > 30f) return;

                Function.Call(Hash.DRAW_MARKER, 25,
                              at.X, at.Y, at.Z - 0.95f,
                              0f, 0f, 0f, 0f, 0f, 0f,
                              MarkerSize, MarkerSize, MarkerSize,
                              240, 200, 80, 90,
                              false, false, 2, false, 0, 0, false);
            }
            catch
            {
                /* no ring this frame */
            }
        }

        /// <summary>
        /// NOBODY ELSE IS IN THE ROOM. A motel room the game owns comes with the woman the
        /// game put in the chair, and CLEAR_AREA does not take her -- it clears the world,
        /// not the people. This is the one that does. Michael found her sat there on the
        /// first go in.
        /// </summary>
        /// <summary>How far round the room nobody else is allowed.</summary>
        private const float AloneReach = 22f;

        /// <summary>Whether the no-spawn box is up, so it is taken down again on the way out.</summary>
        private bool _boxed;

        private void Empty(Vector3 at)
        {
            try
            {
                // A BOX THE GAME MAY NOT SPAWN IN. Ped density is for AMBIENT people and does
                // nothing about a scenario ped, which is what the woman in the chair is: she
                // belongs to a scenario point in the motel and the game puts her back the
                // moment the interior is up, whatever the multiplier says.
                Function.Call(Hash.SET_PED_NON_CREATION_AREA,
                              at.X - AloneReach, at.Y - AloneReach, at.Z - AloneReach,
                              at.X + AloneReach, at.Y + AloneReach, at.Z + AloneReach);
                _boxed = true;

                Function.Call(Hash.CLEAR_AREA_OF_PEDS, at.X, at.Y, at.Z, AloneReach, false);
                Function.Call(Hash.CLEAR_AREA_OF_VEHICLES, at.X, at.Y, at.Z, AloneReach,
                              false, false, false, false, false);
            }
            catch
            {
                /* he shares it */
            }

            Alone(at);
        }

        /// <summary>
        /// Anybody else stood in the room, taken out by hand.
        ///
        /// CLEAR_AREA_OF_PEDS DOES NOT TAKE HER. It is the polite one -- it leaves anything
        /// the game considers busy, and a ped sat in a chair on a scenario is busy. So the
        /// ones actually standing there are deleted outright, and the no-spawn box above
        /// stops the next one arriving.
        ///
        /// Nothing of ours is ever in range: the scene is a thousand metres away and ninety
        /// nine metres up. The player is the only one spared.
        /// </summary>
        private static void Alone(Vector3 at)
        {
            try
            {
                var me = Game.Player.Character;

                foreach (var ped in World.GetAllPeds())
                {
                    if (ped == null || !ped.Exists()) continue;
                    if (me != null && ped.Handle == me.Handle) continue;
                    if (ped.Position.DistanceTo(at) > AloneReach) continue;

                    // NOBODY'S BUT THE GAME'S. A persistent ped was put there by a
                    // script -- ours or another mod's -- and deleting one is somebody
                    // else's people gone. The woman in the chair is ambient and is not
                    // persistent; that is the difference this leans on.
                    //
                    // It matters because the room is only the motel until somebody
                    // sets a place with Alt+F9, and that can be anywhere -- including
                    // the middle of Parkview, where this would otherwise quietly eat
                    // the scene.
                    if (ped.IsPersistent) continue;

                    try { ped.Delete(); } catch { /* the next sweep gets him */ }
                }
            }
            catch
            {
                /* he shares it */
            }
        }

        /// <summary>The box comes down when he is back outside, or nothing spawns out there.</summary>
        private void Unbox()
        {
            // Every way out of a room or a place comes through here, so this is where the
            // rest of the mod hears he is back on the street. See Core.Indoors.
            Hoodrich.Core.Indoors.Exit("Parkview room");
            Hoodrich.Core.Indoors.Exit("Parkview place");

            if (!_boxed) return;
            _boxed = false;

            try { Function.Call(Hash.CLEAR_PED_NON_CREATION_AREA); }
            catch { /* it lapses on its own */ }
        }

        /// <summary>Whether the game says there is really an interior where a place points.</summary>
        private bool PlaceReady(Place p)
        {
            if (p.Checked) return p.Good;
            p.Checked = true;

            try
            {
                var id = Function.Call<int>(Hash.GET_INTERIOR_AT_COORDS, p.To.X, p.To.Y, p.To.Z);

                if (id == 0 || !Function.Call<bool>(Hash.IS_VALID_INTERIOR, id))
                {
                    p.Good = false;
                    Log.Warn("Rooms: " + p.Name + " points at " + Say(p.To) + " and there is no interior there. " +
                             "Walk in by it, stand where you want to come out, and press the set-room key.");
                    return false;
                }

                Function.Call(Hash.PIN_INTERIOR_IN_MEMORY, id);
                p.Interior = id;
                p.Good = true;

                Log.Info("Rooms: " + p.Name + " is interior " + id + " at " + Say(p.To) + ", pinned.");
                return true;
            }
            catch (Exception ex)
            {
                p.Good = false;
                Log.Error("Rooms: could not ask about " + p.Name + ".", ex);
                return false;
            }
        }

        private void EnterPlace(int i)
        {
            var p = _places[i];

            if (!PlaceReady(p))
            {
                Notify.Problem("There is nothing in there yet. See the log -- you can set it yourself.");
                return;
            }

            var me = Game.Player.Character;
            if (me == null || !me.Exists()) return;

            var back = me.Position;
            var backYaw = me.Heading;

            Fade(true);

            Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, me.Handle, p.To.X, p.To.Y, p.To.Z, false, false, false);
            me.Heading = p.ToYaw;

            for (var n = 0; n < 60; n++)
            {
                if (p.Interior != 0 && Function.Call<bool>(Hash.IS_INTERIOR_READY, p.Interior)) break;
                Script.Wait(25);
            }

            Function.Call(Hash.CLEAR_AREA, p.To.X, p.To.Y, p.To.Z, 4f, false, false, false, false);
            Empty(p.To);

            float g;
            var floored = false;

            for (var n = 0; n < 24 && !floored; n++)
            {
                floored = Ground.Probe(new Vector3(p.To.X, p.To.Y, p.To.Z + 1.5f), out g);
                if (!floored) Script.Wait(25);
            }

            if (!floored)
            {
                Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, me.Handle, back.X, back.Y, back.Z, false, false, false);
                me.Heading = backYaw;
                Fade(false);

                Log.Warn("Rooms: nothing to stand on at " + Say(p.To) + " for " + p.Name + "; put back.");
                Notify.Problem("There is no floor in there. Set it yourself -- see the log.");
                return;
            }

            Fade(false);

            _inPlace = i;
            Hoodrich.Core.Indoors.Enter(back, "Parkview place");
            Log.Debug("Rooms: into " + p.Name + ".");
        }

        private void LeavePlace()
        {
            var me = Game.Player.Character;

            if (me == null || !me.Exists() || _inPlace < 0 || _inPlace >= _places.Count)
            {
                _inPlace = -1;
                return;
            }

            var p = _places[_inPlace];

            Fade(true);
            Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, me.Handle, p.At.X, p.At.Y, p.At.Z, false, false, false);
            me.Heading = p.Yaw;
            Fade(false);

            Unbox();

            Log.Debug("Rooms: out of " + p.Name + ".");
            _inPlace = -1;
            Say(null);
        }
        private void Enter(int i)
        {
            if (!RoomReady())
            {
                Notify.Problem("The room is not there. See the log -- you can set it yourself with the set-room key.");
                return;
            }

            var me = Game.Player.Character;
            if (me == null || !me.Exists()) return;

            var to = Spot(_doors[i]);
            var back = me.Position;
            var backYaw = me.Heading;

            Fade(true);

            Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, me.Handle, to.X, to.Y, to.Z, false, false, false);
            me.Heading = _roomYaw;

            // THE ROOM HAS TO BE BUILT BEFORE HE IS IN IT. Pinning asks for it; it arrives a
            // moment later, and a man put down in an interior that has not arrived falls
            // through the floor of it. Waited for here rather than hoped for.
            for (var n = 0; n < 60; n++)
            {
                if (_room_id != 0 && Function.Call<bool>(Hash.IS_INTERIOR_READY, _room_id)) break;
                Script.Wait(25);
            }

            Function.Call(Hash.CLEAR_AREA, to.X, to.Y, to.Z, 4f, false, false, false, false);
            Empty(to);

            // AND HE HAS TO BE STOOD ON SOMETHING. The shipped spot is off somebody else's
            // script and has never been verified here, so the last word is the floor itself:
            // no ground under him and he goes straight back to the door rather than falling
            // out of the world while the screen is still black.
            float g;
            var floored = false;

            for (var n = 0; n < 24 && !floored; n++)
            {
                floored = Ground.Probe(new Vector3(to.X, to.Y, to.Z + 1.5f), out g);
                if (!floored) Script.Wait(25);
            }

            if (!floored)
            {
                Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, me.Handle, back.X, back.Y, back.Z, false, false, false);
                me.Heading = backYaw;
                Fade(false);

                Log.Warn("Rooms: nothing to stand on at " + Say(to) + " -- he is put back at the door. " +
                         "Stand in a room you like and press the set-room key.");
                Notify.Problem("There is no floor in there. Set the room yourself -- see the log.");
                return;
            }

            Fade(false);

            _inside = i;
            Hoodrich.Core.Indoors.Enter(back, "Parkview room");
            Log.Debug("Rooms: into " + _doors[i].Name + " at " + Say(to) + ".");

            // Said once a session, the first time he is in. The room has three spots and a
            // cupboard you cannot see, and nothing else in here says what any of them do.
            if (!_toldRoom)
            {
                _toldRoom = true;
                Notify.Ticker("~g~Your spot.~s~ The bed sleeps, the closet dresses you, the table works the product, " +
                              "and your pockets reach the cupboard from in here.");
            }
        }

        private bool _toldRoom;

        private void Leave()
        {
            var me = Game.Player.Character;
            if (me == null || !me.Exists()) { _inside = -1; return; }

            if (_inside < 0 || _inside >= _doors.Count) { _inside = -1; return; }

            var door = _doors[_inside];

            Fade(true);
            Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, me.Handle,
                          door.At.X, door.At.Y, door.At.Z, false, false, false);
            me.Heading = door.Yaw;
            Fade(false);

            Unbox();

            Log.Debug("Rooms: out of " + door.Name + ".");
            _inside = -1;
            Say(null);
        }

        /// <summary>
        /// Black, move, back. Blocking on purpose: a fade that returns before the man has
        /// moved shows him standing in the old place for a frame, and this is the one moment
        /// in the mod where a stutter is better than the truth.
        /// </summary>
        private static void Fade(bool out_)
        {
            try
            {
                if (out_)
                {
                    Function.Call(Hash.DO_SCREEN_FADE_OUT, 400);
                    for (var i = 0; i < 60 && !Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT); i++) Script.Wait(10);
                }
                else
                {
                    Script.Wait(120);
                    Function.Call(Hash.DO_SCREEN_FADE_IN, 400);
                }
            }
            catch
            {
                /* no fade; he still gets there */
            }
        }

        // ---- the authoring key ----------------------------------------------------------

        /// <summary>
        /// The room is where you are standing. The one thing in here that could not be worked
        /// out from this machine, so it is set the way the rest of the mod is authored: stand
        /// in it and press the key.
        /// </summary>
        public void SetRoomHere()
        {
            var me = Game.Player.Character;
            if (me == null || !me.Exists()) return;

            // WHATEVER YOU WALKED IN BY. Stood in a place you came in through a marker, the
            // key sets THAT place; anywhere else it sets the room the apartment doors share.
            // Trying rooms until one suits is the whole point of the key, and having to say
            // which one you meant would spoil it.
            if (_inPlace >= 0 && _inPlace < _places.Count)
            {
                var p = _places[_inPlace];

                p.To = me.Position;
                p.ToYaw = me.Heading;
                p.Checked = false;
                p.Good = false;

                Write();

                Log.Info("Rooms: " + p.Name + " now comes out at " + Say(p.To) + ".");
                Notify.Important("~g~" + p.Name + "~s~ comes out here now.");
                return;
            }

            var id = 0;
            try { id = Function.Call<int>(Hash.GET_INTERIOR_FROM_ENTITY, me.Handle); }
            catch { /* asked again below */ }

            _room = me.Position;
            _roomYaw = me.Heading;
            _roomIsOurs = true;
            _roomChecked = false;
            _roomGood = false;

            Write();

            var inside = id != 0;

            Log.Info("Rooms: the room is now " + Say(_room) + ", heading " +
                     _roomYaw.ToString("0.0", CultureInfo.InvariantCulture) +
                     (inside ? ", interior " + id + "." : ", and the game says that is not inside anything."));

            if (inside)
            {
                Notify.Important("~g~The room is here.~s~ Every door on the block opens into it.");
            }
            else
            {
                Notify.Problem("Set, but you are not stood in an interior -- the doors will put you out in the open.");
            }
        }
    }
}
