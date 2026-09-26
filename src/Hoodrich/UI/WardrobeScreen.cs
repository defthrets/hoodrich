using System;
using GTA;
using GTA.Math;
using GTA.Native;
using Hoodrich.Core;
using Control = GTA.Control;
using Hud = Hoodrich.UI.Draw;

namespace Hoodrich.UI
{
    /// <summary>
    /// What he is wearing, one slot a row, with him stood in front of the camera while you
    /// change it. Left and right walk the drawables in a slot, Enter walks the colours of the
    /// one on him, and every step goes onto him as you land on it, because he is the preview.
    /// Backspace keeps whatever he has on and writes it into the save.
    ///
    /// NO NAMES. The story's clothes carry no text the game will give up to a script, so a
    /// row says which of how many rather than what it is called -- which is what you see in
    /// the mirror anyway.
    /// </summary>
    internal sealed class WardrobeScreen
    {
        /// <summary>Set by Main: what he settled on is written down.</summary>
        public Action Done;

        /// <summary>
        /// Set by Main: the save, so the rail can hang outfits on it.
        ///
        /// A reference rather than a pile of callbacks because every one of the four things
        /// this screen does with an outfit is a call on Locations.Wardrobe that needs the
        /// state and nothing else. Null is a rail with no pegs on it, which is what a screen
        /// opened before Main has finished wiring should be.
        /// </summary>
        public State.PlayerState State;

        private readonly Curtain _curtain = new Curtain();

        /// <summary>What a peg row does when it is chosen.</summary>
        private enum Deed { None, Wear, Hang, Strip }

        /// <summary>The two rows that are neither clothes nor pegs.</summary>
        private enum Kind { None, Walk, Shoot }

        private sealed class Slot
        {
            public string Name;
            public bool Prop;
            public bool Body;
            public int Index;

            /// <summary>The row that picks WHICH peg. Left and right move along the rail.</summary>
            public bool Peg;

            /// <summary>What this row does to the peg the row above is showing.</summary>
            public Deed Act;

            /// <summary>The row that chooses how he moves rather than what he wears.</summary>
            public Kind Style;

            /// <summary>The heading over the list while this row is chosen.</summary>
            public string Group = "";

            /// <summary>Where the camera goes for this row. Body unless the row says otherwise.</summary>
            public Frame Look = Frame.Body;
        }

        /// <summary>Which peg the four rows at the top are pointed at.</summary>
        private int _peg;

        /// <summary>
        /// Every slot the game has, and the body they hang on.
        ///
        /// ALL TWELVE COMPONENTS AND ALL FIVE PROPS. Six of the twelve were listed before, on
        /// the reasoning that nobody changes their face at a wardrobe -- which is true right up
        /// until somebody wants the bag, the vest under the shirt, the badge on the chest or
        /// the earrings, and finds the closet does not have them. Nothing here is gated: what
        /// the model owns is what the rail offers, and a story character owns his whole
        /// wardrobe from the first frame whether or not the game's own shop ever sold it to him.
        ///
        /// SLOT 8 IS ALSO WHERE THE BALACLAVA GOES. It is on the rail anyway. The mask puts
        /// itself on and takes itself off around whatever is there, so the only way the two
        /// collide is choosing an undershirt while masked, and the answer to that is to take
        /// the mask off.
        /// </summary>
        /// <summary>
        /// Where the camera goes for a row.
        ///
        /// THE PICTURE FOLLOWS THE CHOICE. One fixed shot of his head and shoulders was fine
        /// for a hat and useless for shoes -- the feet were off the bottom of the screen and
        /// you changed them blind. Each row says which part of him it is about, and the camera
        /// glides there: close on the head for a face or a hat, low at the ground for shoes,
        /// the whole of him for anything that is really about the outfit.
        /// </summary>
        private enum Frame { Body, Head, Torso, Hands, Legs, Feet }

        private static readonly Slot[] Slots =
        {
            new Slot { Name = "Outfit", Group = "OUTFITS", Peg = true },
            new Slot { Name = "Wear it", Group = "OUTFITS", Act = Deed.Wear },
            new Slot { Name = "Hang this up", Group = "OUTFITS", Act = Deed.Hang },
            new Slot { Name = "Clear it", Group = "OUTFITS", Act = Deed.Strip },

            new Slot { Name = "Body", Group = "HIM", Body = true },
            new Slot { Name = "Walk", Group = "HIM", Style = Kind.Walk },
            new Slot { Name = "Shooting", Group = "HIM", Style = Kind.Shoot },

            // NAMED FOR THE MAN ON THE RAIL, NOT FOR THE ONLINE ONE. The slot numbers are
            // the game's and the same on every ped; what hangs in them is not. On Franklin,
            // slot one is his beard and, further along the same rail, the masks from the
            // stand on the beach -- so a row called "Mask" scrolled him through goatees
            // first. Slot five is his hands, not a bag. The labels say what actually
            // changes on him when you press right.
            new Slot { Name = "Face", Group = "HEAD", Index = 0, Look = Frame.Head },
            new Slot { Name = "Hair", Group = "HEAD", Index = 2, Look = Frame.Head },
            new Slot { Name = "Beard & mask", Group = "HEAD", Index = 1, Look = Frame.Head },
            new Slot { Name = "Hat", Group = "HEAD", Prop = true, Index = 0, Look = Frame.Head },
            new Slot { Name = "Glasses", Group = "HEAD", Prop = true, Index = 1, Look = Frame.Head },
            new Slot { Name = "Ears", Group = "HEAD", Prop = true, Index = 2, Look = Frame.Head },

            new Slot { Name = "Top", Group = "CLOTHES", Index = 11, Look = Frame.Torso },
            new Slot { Name = "Undershirt", Group = "CLOTHES", Index = 8, Look = Frame.Torso },
            new Slot { Name = "Vest", Group = "CLOTHES", Index = 9, Look = Frame.Torso },
            new Slot { Name = "Arms", Group = "CLOTHES", Index = 3, Look = Frame.Torso },
            new Slot { Name = "Hands", Group = "CLOTHES", Index = 5, Look = Frame.Hands },
            new Slot { Name = "Legs", Group = "CLOTHES", Index = 4, Look = Frame.Legs },
            new Slot { Name = "Shoes", Group = "CLOTHES", Index = 6, Look = Frame.Feet },

            new Slot { Name = "Chain", Group = "EXTRAS", Index = 7, Look = Frame.Torso },
            new Slot { Name = "Decal", Group = "EXTRAS", Index = 10, Look = Frame.Torso },
            new Slot { Name = "Watch", Group = "EXTRAS", Prop = true, Index = 6, Look = Frame.Hands },
            new Slot { Name = "Bracelet", Group = "EXTRAS", Prop = true, Index = 7, Look = Frame.Hands }
        };

        /// <summary>One line of the list: a group's heading, or a slot.</summary>
        private struct Line
        {
            public int Slot;
            public string Header;
            public bool IsHeader => Header != null;
            public float Height => IsHeader ? HeaderH : RowH;
        }

        /// <summary>
        /// The list as it is drawn: every group announced by a heading of its own, so the
        /// closet reads as a closet with sections rather than one long column with the
        /// section's name changing somewhere up in the corner.
        /// </summary>
        private static readonly Line[] Lines = BuildLines();

        /// <summary>Which line each slot is on, for the scrolling.</summary>
        private static readonly int[] LineOf = BuildLineOf();

        private static Line[] BuildLines()
        {
            var lines = new System.Collections.Generic.List<Line>();

            for (var i = 0; i < Slots.Length; i++)
            {
                if (i == 0 || Slots[i].Group != Slots[i - 1].Group)
                {
                    lines.Add(new Line { Slot = -1, Header = Slots[i].Group });
                }

                lines.Add(new Line { Slot = i });
            }

            return lines.ToArray();
        }

        private static int[] BuildLineOf()
        {
            var of = new int[Slots.Length];

            for (var l = 0; l < Lines.Length; l++)
            {
                if (!Lines[l].IsHeader) of[Lines[l].Slot] = l;
            }

            return of;
        }

        /// <summary>
        /// The bodies the rail can put him in.
        ///
        /// THE ONLINE BODIES ARE BACK, AND HERE IS THE WHOLE ARGUMENT ON BOTH SIDES.
        ///
        /// There is no putting a multiplayer jacket on Franklin. A drawable number is an INDEX
        /// into one model's wardrobe -- it is not a garment, it is a row number -- so asking
        /// his model for the four-hundredth jacket does not give you a jacket that fits badly,
        /// it gives you nothing at all, because there is no four-hundredth row. Clipping is not
        /// the failure mode. Invisibility is. Being one of these bodies is the only way to
        /// reach those clothes from a script.
        ///
        /// They were pulled once because somebody used the row and could not get back, and a
        /// row that can strand you is not worth the clothes behind it. That was true. What has
        /// changed is that it can no longer strand anybody: the way home is on this rail AND on
        /// the settings screen, which is in the phone, which is in your pocket wherever you are
        /// standing. Being lost required both of those to be missing and only one of them ever
        /// was.
        ///
        /// KNOW WHAT YOU ARE PRESSING. This is not a costume, it is a different man -- his
        /// face, his voice, and whatever the story scripts make of him. Everything the mod
        /// keeps is filed per body, so his wardrobe, his outfits and his masks are all still
        /// there when he comes back.
        /// </summary>
        /// <summary>
        /// FRANKLIN, AND NOBODY ELSE. The online bodies were on this rail and are off it.
        ///
        /// They cost more than they gave. Everything the mod keeps about clothes is filed
        /// under the body it was worn on -- it has to be, because a drawable number is an
        /// index into ONE model's wardrobe and the thirty-first jacket on Franklin is a
        /// different object on a freemode ped -- so a wardrobe filled up on the online man
        /// looks empty the moment you are Franklin again, and an outfit hung on a peg is
        /// there but invisible. Reported as the wardrobe not saving, which is exactly what
        /// it looks like from the rail.
        ///
        /// And this is Franklin's mod. The story talks to him, the dialogue is his, his
        /// people know him; a freemode ped standing in his kitchen is a different game with
        /// the wrong voice.
        ///
        /// The row stays, because a body this rail has never heard of still needs a way home
        /// -- another mod can put him in anything, and the one useful thing this row does is
        /// hand him back. See Wear.
        /// </summary>
        private static readonly PedHash[] Bodies = { PedHash.Franklin };

        private static readonly string[] BodyNames = { "Franklin" };

        private int _row;
        private int _lastRow = -1;
        private int _pickedAt;
        private int _openedAt;
        private int _cam;

        private const float PanelWidthH = 0.46f;
        private const float PadH = 0.024f;
        private const int OpenGraceMs = 220;

        /// <summary>A slot row, a group heading, and how much of the panel the list gets.</summary>
        private const float RowH = 0.032f;
        private const float HeaderH = 0.023f;
        private const float ListH = 0.468f;

        /// <summary>The first line in the window. It scrolls a line at a time; see Scroll.</summary>
        private int _topLine;

        /// <summary>When the screen went up, for its entrance -- the same rise every screen in the mod makes.</summary>
        private int _shownAt;
        private const int EnterMs = 170;
        private const float EnterRise = 0.014f;

        /// <summary>The cursor frame that glides between rows. See UI.Glide.</summary>
        private readonly Glide _glide = new Glide();
        /// <summary>
        /// The six shots, as distance out, height above his feet, the bone to look at, how far
        /// above or below that bone, and the lens.
        ///
        /// Feet is the one that matters: low enough that the camera is at his shins and the
        /// shoes are the picture. Everything else frames the part the row is about.
        /// </summary>
        private static readonly float[,] Shots =
        {
            //  out     up      bone      offZ    fov
            { 3.30f,  0.30f,  Pelvis,    0.20f, 40f },   // Body
            { 1.30f,  0.62f,  Head,      0.00f, 34f },   // Head
            { 2.10f,  0.45f,  Spine3,    0.00f, 38f },   // Torso
            { 1.80f,  0.05f,  Pelvis,    0.05f, 36f },   // Hands
            { 2.60f, -0.10f,  Pelvis,   -0.35f, 38f },   // Legs
            { 2.20f, -0.55f,  LeftFoot,  0.10f, 34f }    // Feet
        };

        /// <summary>
        /// How far he sits to the left of centre. The panel takes the right of the screen, so
        /// a man framed dead centre is a man half behind a list of his own clothes.
        /// </summary>
        private const float Side = 0.34f;

        /// <summary>How fast the camera gets where it is going: per second, as a share of what is left.</summary>
        private const float Glide = 7.5f;

        /// <summary>How far the stick can lift or drop a shot, and how fast.</summary>
        private const float NudgeMost = 0.70f;
        private const float NudgeRate = 1.6f;

        private const int Pelvis = 11816;
        private const int Spine3 = 24818;
        private const int LeftFoot = 14201;

        private Vector3 _eye, _look;
        private float _fov = 40f;
        private float _nudge;
        private bool _snap;
        private const int Head = 31086;

        public bool IsOpen => _curtain.Showing;

        /// <summary>
        /// Which way he stands while the closet is open.
        ///
        /// The camera is placed in front of whatever way he happens to be facing, so the shot
        /// was different every time and half of them were of the inside of the wardrobe door.
        /// Turned to a fixed heading on the way in, the picture is the same one every time --
        /// him, against the open closet, with the room behind the camera.
        /// </summary>
        private const float FaceHeading = 222.743f;

        /// <param name="heading">Which way he stands. Denise's closet has its own; the room Parkview rents says.</param>
        public void Open(float? heading = null)
        {
            _row = 0;
            _lastRow = -1;
            _topLine = 0;
            _pickedAt = _openedAt = _shownAt = Game.GameTime;
            _glide.Reset();

            try
            {
                var me = Game.Player.Character;

                if (me != null && me.Exists())
                {
                    me.Heading = heading ?? FaceHeading;
                    Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, me.Handle);
                }
            }
            catch
            {
                // He is looked at whichever way he ended up.
            }

            _curtain.Open();
            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");

            Stock();
            Look();
        }

        /// <summary>
        /// EVERY DRAWABLE AND EVERY TEXTURE THIS BODY OWNS, counted and written to the log.
        ///
        /// Because "does the closet have all the clothes in it" is a question nobody can answer
        /// by scrolling, and it is asked. The rail takes its counts from the game and filters
        /// nothing, so the answer is yes by construction -- but a construction argument is not
        /// evidence, and this is: twelve component slots and eight prop slots, the drawables in
        /// each and the textures across all of them, for whichever body is standing there.
        ///
        /// ALL EIGHT PROP SLOTS, INCLUDING THE THREE THE MENU HAS NO ROW FOR. Three, four and
        /// five are mouth and the two hands, and every body anybody has looked at has nothing
        /// in them -- so they are not worth a row that always says NOTHING ON. If this line
        /// ever prints a number against one of them, they are worth a row, and that is exactly
        /// the sort of thing that should be found out from the game rather than assumed.
        ///
        /// Once, when the closet opens. It is twenty native calls plus one per garment, on a
        /// frame where the camera is already moving and nobody is driving.
        /// </summary>
        private static void Stock()
        {
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return;

                var parts = 0;
                var shades = 0;
                var said = "";

                for (var slot = 0; slot < 12; slot++)
                {
                    var n = Function.Call<int>(Hash.GET_NUMBER_OF_PED_DRAWABLE_VARIATIONS, me.Handle, slot);
                    if (n <= 0) continue;

                    parts += n;
                    said += " c" + slot + ":" + n;

                    for (var d = 0; d < n; d++)
                    {
                        shades += Function.Call<int>(Hash.GET_NUMBER_OF_PED_TEXTURE_VARIATIONS,
                                                     me.Handle, slot, d);
                    }
                }

                for (var slot = 0; slot < 8; slot++)
                {
                    var n = Function.Call<int>(Hash.GET_NUMBER_OF_PED_PROP_DRAWABLE_VARIATIONS, me.Handle, slot);
                    if (n <= 0) continue;

                    parts += n;
                    said += " p" + slot + ":" + n;

                    for (var d = 0; d < n; d++)
                    {
                        shades += Function.Call<int>(Hash.GET_NUMBER_OF_PED_PROP_TEXTURE_VARIATIONS,
                                                     me.Handle, slot, d);
                    }
                }

                Log.Info("Wardrobe: " + me.Model.Hash.ToString() + " has " + parts +
                         " garment(s) in " + shades + " colours --" + said);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not count the wardrobe: " + ex.Message);
            }
        }

        public void Close()
        {
            Rest();

            if (!IsOpen) return;

            try { Done?.Invoke(); }
            catch (Exception ex) { Log.Debug("Could not write down what he wears: " + ex.Message); }

            Unlook();
            InputGuard.Swallow();
            _curtain.Close();
            Hud.PlaySound("BACK", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        // ---- the camera: in front of him, on him -------------------------------------

        private void Look()
        {
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return;

                _cam = Function.Call<int>(Hash.CREATE_CAM, "DEFAULT_SCRIPTED_CAMERA", true);
                if (_cam == 0) return;

                // Straight to the first shot; every one after it is a glide.
                _snap = true;
                _nudge = 0f;
                Steer(me);

                Function.Call(Hash.SET_CAM_ACTIVE, _cam, true);
                Function.Call(Hash.RENDER_SCRIPT_CAMS, true, true, 400, true, false);
            }
            catch (Exception ex)
            {
                Log.Debug("The wardrobe camera would not start: " + ex.Message);
                _cam = 0;
            }
        }

        /// <summary>
        /// One frame of the camera: where this row wants it, and some of the way there.
        ///
        /// POINTED AT A COORDINATE, NOT A BONE. Pointing at a bone snaps the instant the bone
        /// changes, and the whole point of the glide is that the picture flows from his head
        /// to his feet rather than cutting. The bone is read into a world point, and that is
        /// what is eased.
        /// </summary>
        private void Steer(Ped me)
        {
            if (_cam == 0 || me == null || !me.Exists()) return;

            try
            {
                var shot = (int)Slots[_row].Look;

                var rad = me.Heading * (float)Math.PI / 180f;
                var forward = new Vector3(-(float)Math.Sin(rad), (float)Math.Cos(rad), 0f);

                // Sideways, so he stands in the clear half of the screen. Eye and aim move by
                // the same amount, which shifts him in the frame rather than turning the camera.
                var aside = new Vector3(forward.Y, -forward.X, 0f) * Side;

                var eye = me.Position + forward * Shots[shot, 0]
                          + new Vector3(0f, 0f, Shots[shot, 1] + _nudge) + aside;

                var look = Function.Call<Vector3>(Hash.GET_PED_BONE_COORDS, me.Handle, (int)Shots[shot, 2],
                                                  0f, 0f, Shots[shot, 3])
                           + new Vector3(0f, 0f, _nudge * 0.6f) + aside;

                var fov = Shots[shot, 4];

                if (_snap)
                {
                    _eye = eye; _look = look; _fov = fov;
                    _snap = false;
                }
                else
                {
                    var k = Math.Min(1f, Glide * Game.LastFrameTime);
                    _eye += (eye - _eye) * k;
                    _look += (look - _look) * k;
                    _fov += (fov - _fov) * k;
                }

                Function.Call(Hash.SET_CAM_COORD, _cam, _eye.X, _eye.Y, _eye.Z);
                Function.Call(Hash.POINT_CAM_AT_COORD, _cam, _look.X, _look.Y, _look.Z);
                Function.Call(Hash.SET_CAM_FOV, _cam, _fov);
            }
            catch
            {
                // It stays where it was, which is somewhere on him.
            }
        }

        /// <summary>
        /// The stick or the mouse lifts and drops the shot, so any row can be seen from higher
        /// or lower than its own frame. Cleared when the row changes, because the next row
        /// brings its own.
        /// </summary>
        private void Nudge()
        {
            try
            {
                var v = Function.Call<float>(Hash.GET_DISABLED_CONTROL_NORMAL, 0, (int)Control.LookUpDown);
                if (Math.Abs(v) < 0.08f) return;

                _nudge = Math.Max(-NudgeMost, Math.Min(NudgeMost, _nudge - v * NudgeRate * Game.LastFrameTime));
            }
            catch
            {
            }
        }

        private void Unlook()
        {
            if (_cam == 0) return;

            var cam = _cam;
            _cam = 0;

            try
            {
                Function.Call(Hash.RENDER_SCRIPT_CAMS, false, true, 400, true, false);
                Function.Call(Hash.SET_CAM_ACTIVE, cam, false);
                Function.Call(Hash.DESTROY_CAM, cam, false);
            }
            catch
            {
            }
        }

        // ---- input --------------------------------------------------------------

        public void Update()
        {
            // WRITTEN DOWN AS HE CHOOSES, NOT ONLY ON THE WAY OUT.
            //
            // Done fires from Close, and Close is the cancel button -- so a session that
            // ended any other way, and every minute of one that had not ended yet, was an
            // hour of choosing that the save had never heard of. Which is what "the
            // wardrobe does not save" is: it saved, on one exit, and that was not the exit
            // anybody took.
            //
            // Two seconds apart, so it is a handful of reads a visit rather than sixty a
            // second, and Remember is only a dozen natives and a list.
            Keep();

            if (!IsOpen) return;

            LockControls();

            try
            {
                Nudge();
                Steer(Game.Player.Character);
            }
            catch
            {
            }

            try { Function.Call(Hash.HIDE_HUD_AND_RADAR_THIS_FRAME); }
            catch { }

            if (!_curtain.Taking) return;
            if (Game.GameTime - _openedAt < OpenGraceMs) return;

            if (Pressed(Control.PhoneCancel)) { Close(); return; }

            if (Pressed(Control.PhoneUp)) { Rest(); Move(-1); }
            else if (Pressed(Control.PhoneDown)) { Rest(); Move(1); }
            else if (Pressed(Control.PhoneLeft)) { Peg(-1); Grab(-1); }
            else if (Pressed(Control.PhoneRight)) { Peg(1); Grab(1); }
            else if (Pressed(Control.PhoneSelect) || Pressed(Control.Context)) { Rest(); Choose(); }
            else Running();
        }

        /// <summary>
        /// HOLD IT DOWN AND THE RAIL RUNS, and this is the difference between a wardrobe that
        /// has everything in it and one you can get everything out of.
        ///
        /// The closet has always offered every garment the body owns -- the counts come from
        /// the game, nothing is filtered, and the row says so out loud: "47 OF 312". What it
        /// did not have was any way to reach the three hundredth of anything. One press was one
        /// garment, so the far end of a rail was three hundred presses away, and a rail nobody
        /// can reach the end of is a short rail with a long number printed on it.
        ///
        /// SLOW FIRST, THEN QUICK. It starts at nine a second after a short hold, which is
        /// browsing, and winds up over a second and a half to thirty-five, which is travelling.
        /// A single press is still a single garment -- the delay before the first repeat is
        /// what protects that, and it is longer than any tap.
        ///
        /// ONLY THE RACKS. Holding right on "Wear it" is not a request for forty outfits, and
        /// holding it on the walks would flick him through every way of moving in the game
        /// several times a second. Those rows take a press each, as they should: there are six
        /// of them, not six hundred.
        /// </summary>
        private const int RepeatWaitMs = 380;
        private const int RepeatSlowMs = 110;
        private const int RepeatFastMs = 28;
        private const int RepeatWindUpMs = 1500;

        /// <summary>One click in this many steps while it is running. Thirty a second is a noise.</summary>
        private const int RepeatClickEvery = 5;

        private int _runDir;
        private int _runSince;
        private int _runNext;
        private int _runSteps;

        /// <summary>A direction has just been pressed: start the clock on it.</summary>
        private void Grab(int dir)
        {
            _runDir = dir;
            _runSince = Game.GameTime;
            _runNext = _runSince + RepeatWaitMs;
            _runSteps = 0;
        }

        /// <summary>Let go of it.</summary>
        private void Rest()
        {
            _runDir = 0;
            _runSteps = 0;
        }

        /// <summary>Still held down, and past due for another one.</summary>
        private void Running()
        {
            if (_runDir == 0) return;

            var control = _runDir < 0 ? Control.PhoneLeft : Control.PhoneRight;

            if (!Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)control))
            {
                Rest();
                return;
            }

            // Whatever row it started on. Moving up or down lets go of it anyway, so this can
            // only be the row the hold began on -- but it is checked rather than assumed,
            // because a body swap can change what a row is underneath a held finger.
            var s = Slots[_row];

            if (s.Peg || s.Body || s.Act != Deed.None || s.Style != Kind.None)
            {
                Rest();
                return;
            }

            var now = Game.GameTime;
            if (now < _runNext) return;

            _runQuiet = _runSteps % RepeatClickEvery != 0;
            Step(_runDir);
            _runQuiet = false;

            _runSteps++;
            _runNext = now + Pace(now);
        }

        /// <summary>The gap to the next one: slow at first, quick once it has wound up.</summary>
        private int Pace(int now)
        {
            var run = now - _runSince - RepeatWaitMs;

            if (run <= 0) return RepeatSlowMs;

            var t = run >= RepeatWindUpMs ? 1f : run / (float)RepeatWindUpMs;

            return (int)(RepeatSlowMs + (RepeatFastMs - RepeatSlowMs) * t);
        }

        /// <summary>Whether this step should keep its mouth shut. See RepeatClickEvery.</summary>
        private bool _runQuiet;

        /// <summary>Left and right: along the rail on a peg row, through the rack on any other.</summary>
        private void Peg(int by)
        {
            var s = Slots[_row];

            if (s.Style != Kind.None) { Style(s, by); return; }
            if (!s.Peg) { Step(by); return; }

            _peg = (_peg + by + Locations.Wardrobe.Pegs) % Locations.Wardrobe.Pegs;
            _pickedAt = Game.GameTime;
            Hud.PlaySound("NAV_LEFT_RIGHT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        /// <summary>
        /// Left and right on the two rows that are about how he moves.
        ///
        /// PUT ON THE INSTANT IT IS CHOSEN. A wardrobe where you scroll a list and then have
        /// to press something else to see it is a wardrobe you cannot judge -- the whole
        /// point of standing in front of a mirror is that the change is in front of you.
        /// </summary>
        private void Style(Slot s, int by)
        {
            if (State == null) return;

            if (s.Style == Kind.Walk)
            {
                var n = Locations.Wardrobe.Walks.Length;
                State.Walk = (State.Walk + by + n) % n;
            }
            else
            {
                var n = Locations.Wardrobe.Shoots.Length;
                State.Shoot = (State.Shoot + by + n) % n;
            }

            State.Touch();

            Locations.Wardrobe.Carry(State);

            _pickedAt = Game.GameTime;
            Hud.PlaySound("NAV_LEFT_RIGHT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        /// <summary>The button: the peg rows do their own thing, everything else takes a colour.</summary>
        private void Choose()
        {
            var s = Slots[_row];

            if (s.Peg || s.Act != Deed.None) { Do(s); return; }

            Colour();
        }

        /// <summary>
        /// What a peg row does.
        ///
        /// The peg row itself wears what is on it, because pressing the thing you are looking
        /// at should do the obvious thing rather than nothing -- the row below is the same
        /// action spelled out for anybody who did not guess.
        /// </summary>
        private void Do(Slot s)
        {
            if (State == null)
            {
                Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                return;
            }

            var deed = s.Peg ? Deed.Wear : s.Act;

            if (deed == Deed.Wear)
            {
                if (!Locations.Wardrobe.WearPeg(State, _peg))
                {
                    Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                    return;
                }

                Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                return;
            }

            if (deed == Deed.Strip)
            {
                if (!Locations.Wardrobe.Strip(State, _peg))
                {
                    Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                    return;
                }

                Hud.PlaySound("BACK", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                return;
            }

            if (deed != Deed.Hang) return;

            // NAMED WHEN IT IS HUNG, not on a separate row. Somebody who does not want to name
            // it presses escape and gets "Outfit 3", which is what the game's own wardrobe
            // calls them anyway -- and somebody who does gets the keyboard without having to
            // find a second row for it. A peg that already has something on it offers its own
            // name back, so this doubles as the rename.
            string typed;

            var already = Locations.Wardrobe.NameOn(State, _peg);

            try
            {
                typed = Game.GetUserInput(WindowTitle.EnterMessage20, already, 18);
            }
            catch (Exception ex)
            {
                Log.Debug("The keyboard would not open: " + ex.Message);
                typed = null;
            }

            // The keyboard's own Enter or Escape must not land on this panel as well.
            Core.InputGuard.Swallow();

            if (!Locations.Wardrobe.Hang(State, _peg, typed))
            {
                Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                return;
            }

            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        private void Move(int step)
        {
            _lastRow = _row;
            _row = (_row + step + Slots.Length) % Slots.Length;
            _pickedAt = Game.GameTime;
            _nudge = 0f;

            Scroll();
            Hud.PlaySound("NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        /// <summary>
        /// The window follows the row a line at a time, so it scrolls rather than pages, and
        /// the heading over a row comes into view with the row rather than a line later.
        /// </summary>
        private void Scroll()
        {
            var sel = LineOf[_row];
            var first = sel > 0 && Lines[sel - 1].IsHeader ? sel - 1 : sel;

            if (first < _topLine) _topLine = first;
            while (_topLine < sel && Span(_topLine, sel) > ListH) _topLine++;
            if (_row == 0) _topLine = 0;
        }

        /// <summary>How tall the lines from one to another are, both included.</summary>
        private static float Span(int from, int to)
        {
            var h = 0f;
            for (var l = from; l <= to && l < Lines.Length; l++) h += Lines[l].Height;
            return h;
        }

        // ---- what is on him -------------------------------------------------------------

        private static int Count(Ped me, Slot s)
        {
            return s.Prop
                ? Function.Call<int>(Hash.GET_NUMBER_OF_PED_PROP_DRAWABLE_VARIATIONS, me.Handle, s.Index)
                : Function.Call<int>(Hash.GET_NUMBER_OF_PED_DRAWABLE_VARIATIONS, me.Handle, s.Index);
        }

        private static int Colours(Ped me, Slot s, int drawable)
        {
            if (drawable < 0) return 0;
            return s.Prop
                ? Function.Call<int>(Hash.GET_NUMBER_OF_PED_PROP_TEXTURE_VARIATIONS, me.Handle, s.Index, drawable)
                : Function.Call<int>(Hash.GET_NUMBER_OF_PED_TEXTURE_VARIATIONS, me.Handle, s.Index, drawable);
        }

        private static int Drawable(Ped me, Slot s)
        {
            return s.Prop
                ? Function.Call<int>(Hash.GET_PED_PROP_INDEX, me.Handle, s.Index)
                : Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, me.Handle, s.Index);
        }

        private static int Texture(Ped me, Slot s)
        {
            return s.Prop
                ? Function.Call<int>(Hash.GET_PED_PROP_TEXTURE_INDEX, me.Handle, s.Index)
                : Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, me.Handle, s.Index);
        }

        private static void Put(Ped me, Slot s, int drawable, int texture)
        {
            if (s.Prop)
            {
                if (drawable < 0) Function.Call(Hash.CLEAR_PED_PROP, me.Handle, s.Index);
                else Function.Call(Hash.SET_PED_PROP_INDEX, me.Handle, s.Index, drawable, texture, true);
            }
            else
            {
                Function.Call(Hash.SET_PED_COMPONENT_VARIATION, me.Handle, s.Index, drawable, texture, 0);
            }
        }

        /// <summary>Which body he is wearing now, or -1 for one this rail does not know.</summary>
        private static int BodyNow(Ped me)
        {
            if (me == null || !me.Exists()) return -1;

            for (var i = 0; i < Bodies.Length; i++)
            {
                if ((uint)me.Model.Hash == (uint)Bodies[i]) return i;
            }

            return -1;
        }

        /// <summary>
        /// Become one of the other bodies.
        ///
        /// The blocking form of the model request on purpose: this is one deliberate keypress,
        /// not a spawner, and half a body arriving is worse than a frame. Everything after the
        /// swap has to be redone -- the ped is a NEW ped, so the camera is pointed at a handle
        /// that no longer exists until it is rebuilt.
        /// </summary>
        private void Wear(int by)
        {
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return;

                var now = BodyNow(me);

                // HOME, OR NOTHING. There is one body on this rail now -- see Bodies -- so
                // the only move this row has is putting back somebody another mod has
                // changed. Already Franklin is not a failure, it is the answer.
                if (now == 0)
                {
                    Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                    return;
                }

                var next = 0;

                var model = new Model(Bodies[next]);

                if (!model.IsValid || !model.IsInCdImage)
                {
                    Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                    return;
                }

                model.Request(3000);

                if (!model.IsLoaded)
                {
                    Log.Warn("The " + BodyNames[next] + " body would not load.");
                    Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                    return;
                }

                Unlook();

                Function.Call(Hash.SET_PLAYER_MODEL, Game.Player.Handle, model.Hash);
                model.MarkAsNoLongerNeeded();

                var him = Game.Player.Character;

                if (him != null && him.Exists())
                {
                    // A freemode model arrives with nothing on it at all. The game's own
                    // default set is a person in clothes rather than a mannequin, which is
                    // where somebody wants to start choosing from.
                    Function.Call(Hash.SET_PED_DEFAULT_COMPONENT_VARIATION, him.Handle);
                    him.Heading = FaceHeading;

                    // AND WHATEVER HE WORE LAST TIME HE WAS THIS BODY. Every outfit this mod
                    // records carries the model it was worn on, so coming back to a body comes
                    // back to the clothes -- otherwise every visit starts from the mannequin
                    // and an hour of choosing is worth one session.
                    try { Locations.Wardrobe.Apply(State); }
                    catch (Exception ex) { Log.Debug("Could not dress the new body: " + ex.Message); }
                }

                Look();

                _pickedAt = Game.GameTime;
                Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");

                Log.Info("The closet put him in the " + BodyNames[next] + " body.");
            }
            catch (Exception ex)
            {
                Log.Warn("Could not change body: " + ex.Message);
                Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET");
            }
        }

        /// <summary>The next or previous drawable in the slot. A prop can also be nothing, which sits before its first.</summary>
        private void Step(int by)
        {
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return;

                var s = Slots[_row];

                if (s.Body)
                {
                    Wear(by);
                    return;
                }
                var n = Count(me, s);
                if (n <= 0) { Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET"); return; }

                var now = Drawable(me, s);
                int next;

                if (s.Prop)
                {
                    // -1 .. n-1, with -1 being bare.
                    next = now + by;
                    if (next < -1) next = n - 1;
                    if (next >= n) next = -1;
                }
                else
                {
                    next = (now + by + n) % n;
                }

                Put(me, s, next, 0);
                _pickedAt = Game.GameTime;

                if (!_runQuiet) Hud.PlaySound("NAV_LEFT_RIGHT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
            }
            catch (Exception ex)
            {
                Log.Debug("Could not change a slot: " + ex.Message);
            }
        }

        /// <summary>The next colour of what is on him in the slot.</summary>
        private void Colour()
        {
            try
            {
                var me = Game.Player.Character;
                if (me == null || !me.Exists()) return;

                var s = Slots[_row];
                if (s.Body) { Wear(1); return; }

                var d = Drawable(me, s);
                var n = Colours(me, s, d);
                if (n <= 1) { Hud.PlaySound("ERROR", "HUD_FRONTEND_DEFAULT_SOUNDSET"); return; }

                Put(me, s, d, (Texture(me, s) + 1) % n);
                _pickedAt = Game.GameTime;
                Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
            }
            catch (Exception ex)
            {
                Log.Debug("Could not change a colour: " + ex.Message);
            }
        }

        private static bool Pressed(Control control)
        {
            return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, (int)control);
        }

        private static void LockControls()
        {
            Function.Call(Hash.DISABLE_ALL_CONTROL_ACTIONS, 0);

            foreach (var control in new[]
                     {
                         Control.PhoneUp, Control.PhoneDown, Control.PhoneLeft, Control.PhoneRight,
                         Control.PhoneSelect, Control.PhoneCancel, Control.LookUpDown
                     })
            {
                Function.Call(Hash.ENABLE_CONTROL_ACTION, 0, (int)control, true);
            }
        }

        // ---- drawing --------------------------------------------------------------

        /// <summary>
        /// The closet, drawn the way the kitchen and the pocket are drawn: the mark and the
        /// room in the house script over a rule, the list in sections with a heading over
        /// each, the plate and the gliding frame under whatever is chosen, and the keys as
        /// caps along the foot. It used to be a title, a column and a sentence of key names
        /// -- the one screen in the mod still wearing the first draft.
        /// </summary>
        public void Draw()
        {
            if (!IsOpen) return;

            var me = Game.Player.Character;
            if (me == null || !me.Exists()) return;

            var width = Hud.ToX(PanelWidthH);
            var pad = Hud.ToX(PadH);
            var height = UiKit.HeadH + ListH + UiKit.FootH + 0.004f;

            // Off to the right, so he stays in the clear half of the picture.
            var left = 0.96f - width;
            var top = 0.5f - height * 0.5f + _curtain.Lift;

            // Up and in, eased out so it slows as it lands.
            var age = Game.GameTime - _shownAt;
            var arrive = age >= EnterMs ? 1f : age / (float)EnterMs;
            arrive = 1f - (1f - arrive) * (1f - arrive);

            top += EnterRise * (1f - arrive);

            Theme.Panel(left, top, width, height, arrive);

            var x = left + pad;
            var right = left + width - pad;
            var wide = right - x;

            // ---- the letterhead ----
            Hud.BrandCentre(left + width * 0.5f, top + 0.021f, 0.019f,
                            Palette.Alpha(Palette.Text, (int)(225f * arrive)));

            Hud.Text("Wardrobe", x, top + 0.026f, 0.70f, Palette.Alpha(Palette.Text, (int)(255f * arrive)),
                     Hud.FontCursive, centre: false);

            Hud.TextRight(BodyNow(me) == 0 ? "FRANKLIN" : "SOMEBODY ELSE", right, top + 0.047f, 0.28f,
                          Palette.Alpha(Palette.TextDim, (int)(210f * arrive)), Hud.FontLabel);

            Theme.Rule(x, top + UiKit.HeadH - 0.006f, wide, arrive);

            // ---- the list ----
            var y = top + UiKit.HeadH;
            var bottom = y + ListH;

            var grown = Theme.Grown(_pickedAt);
            var barWide = wide + pad * 0.7f;

            _glide.Begin();

            for (var l = _topLine; l < Lines.Length; l++)
            {
                var line = Lines[l];
                if (y + line.Height > bottom + 0.0005f) break;

                if (line.IsHeader) Header(line.Header, x, y, wide, arrive);
                else Row(me, line.Slot, x, right, pad, barWide, y, grown, arrive);

                y += line.Height;
            }

            // ---- the keys ----
            var footY = top + height - UiKit.FootH + 0.006f;

            Theme.Rule(x, footY, wide, arrive);

            var ky = footY + 0.011f;

            UiKit.KeyRight(right, ky, UiKit.Back, "DONE", arrive);

            var kx = UiKit.Key(x, ky, null, "arrow_updown.png", "ROW", arrive);
            kx = UiKit.Key(kx, ky, null, "arrow_leftright.png", "CHANGE", arrive);
            kx = UiKit.Key(kx, ky, UiKit.Confirm, null, "COLOUR", arrive);
            UiKit.Key(kx, ky, Hud.OnPad ? "R-STICK" : "MOUSE", null, "LOOK", arrive);

            // Last, so it rides over the rows it is pointing at.
            _glide.Draw(arrive);
        }

        /// <summary>A group's name, small and dim, with the hairline running off it. One rectangle.</summary>
        private static void Header(string words, float x, float y, float wide, float arrive)
        {
            var ink = Palette.Alpha(Palette.TextDim, (int)(190f * arrive));

            Hud.Text(words, x, y + 0.004f, 0.22f, ink, Hud.FontLabel, centre: false);

            var w = Hud.MeasureText(words, 0.22f, Hud.FontLabel);

            Hud.RectFrom(x + w + 0.008f, y + 0.0125f, Math.Max(0f, wide - w - 0.008f), 0.0008f,
                         Palette.Alpha(Theme.Hairline, (int)(Theme.Hairline.A * arrive)));
        }

        /// <summary>
        /// One slot: its plate when chosen, its name, and on the right what is in it -- the
        /// count in words, a tag for the colour or the peg's name, and the arrows on a row
        /// that scrolls.
        /// </summary>
        private void Row(Ped me, int i, float x, float right, float pad, float barWide, float y,
                         float grown, float arrive)
        {
            var s = Slots[i];
            var here = i == _row;
            var lit = Theme.Lit(i, _row, _lastRow, grown) * arrive;

            Theme.Plate(x - pad * 0.35f, y, barWide, RowH, lit);
            Theme.Sheen(x - pad * 0.35f, y, barWide, RowH, lit);

            if (here) _glide.Target(x - pad * 0.35f, y, barWide, RowH);

            var ink = Theme.Ink(Palette.Alpha(here ? Palette.Text : Palette.TextDim, (int)(255f * arrive)), lit);
            var textY = y + 0.006f;

            Hud.Text(s.Name, x, textY, 0.30f, ink, Hud.FontBody, centre: false);

            string value, tag;
            bool scrolls;
            Describe(me, s, out value, out tag, out scrolls);

            // The arrows belong on a row you can scroll. An action row is pressed, not
            // scrolled, and putting arrows on it says otherwise.
            var shown = here && scrolls ? "<  " + value + "  >" : value;
            var valueInk = here ? ink : Palette.Alpha(Palette.TextDim, (int)(255f * arrive));

            Hud.TextRight(shown, right, textY + 0.002f, 0.24f, valueInk, Hud.FontLabel);

            if (string.IsNullOrEmpty(tag)) return;

            var edge = right - Hud.MeasureText(shown, 0.24f, Hud.FontLabel) - 0.010f;
            var tagW = Hud.MeasureText(tag, 0.19f, Hud.FontLabel) + 0.007f;

            UiKit.Tag(edge - tagW, textY + 0.0035f, tag, Palette.TextDim, arrive * (0.75f + 0.25f * lit));
        }

        /// <summary>
        /// What a row says on its right. WORDS, NOT A FRACTION: "4 OF 12" and a colour tag is
        /// a sentence; "4 / 12 / 2 / 3" is a sum to do. A slot with one thing in it says so,
        /// because an arrow on a row that cannot change is the closet lying.
        /// </summary>
        private void Describe(Ped me, Slot s, out string value, out string tag, out bool scrolls)
        {
            value = "";
            tag = null;
            scrolls = false;

            try
            {
                if (s.Style == Kind.Walk)
                {
                    var pick = State == null ? 0 : State.Walk;
                    var names = Locations.Wardrobe.WalkNames;

                    value = pick >= 0 && pick < names.Length ? names[pick] : "HIS OWN";
                    scrolls = true;
                }
                else if (s.Style == Kind.Shoot)
                {
                    var pick = State == null ? 0 : State.Shoot;
                    var names = Locations.Wardrobe.ShootNames;

                    value = pick >= 0 && pick < names.Length ? names[pick] : "HIS OWN";
                    scrolls = true;
                }
                else if (s.Peg)
                {
                    var name = State == null ? "" : Locations.Wardrobe.NameOn(State, _peg);

                    value = "PEG " + (_peg + 1) + " OF " + Locations.Wardrobe.Pegs;
                    tag = name.Length == 0 ? "EMPTY" : name.ToUpperInvariant();
                    scrolls = true;
                }
                else if (s.Act != Deed.None)
                {
                    var on = State != null && Locations.Wardrobe.Used(State, _peg);

                    value = s.Act == Deed.Hang
                                ? (on ? "OVER PEG " + (_peg + 1) : "ONTO PEG " + (_peg + 1))
                                : on ? "PEG " + (_peg + 1) : "NOTHING ON IT";
                }
                else if (s.Body)
                {
                    value = BodyNow(me) == 0 ? "FRANKLIN" : "PRESS TO GO BACK";
                }
                else
                {
                    var d = Drawable(me, s);
                    var n = Count(me, s);
                    var c = Colours(me, s, d);
                    var t = Texture(me, s);

                    if (n <= 1 && !s.Prop)
                    {
                        value = "JUST THE ONE";
                    }
                    else if (d < 0)
                    {
                        value = "NOTHING ON";
                        if (n > 0) tag = n + " TO PICK";
                        scrolls = n > 0;
                    }
                    else
                    {
                        value = (d + 1) + " OF " + n;
                        if (c > 1) tag = "COLOUR " + (t + 1) + " OF " + c;
                        scrolls = n > 1 || s.Prop;
                    }
                }
            }
            catch
            {
                value = "";
            }
        }

        private int _keptAt;
        private const int KeepEveryMs = 2000;

        /// <summary>What he has on, into the save, while he is still stood there.</summary>
        private void Keep()
        {
            if (State == null) return;

            var now = Game.GameTime;
            if (now - _keptAt < KeepEveryMs) return;
            _keptAt = now;

            try { Locations.Wardrobe.Remember(State); }
            catch { /* the way out still writes it */ }

            // AND HOW HE CARRIES HIMSELF, WHICH WAS APPLIED ONCE AND NEVER AGAIN.
            //
            // Main asks Wardrobe.Carry until it answers true and then stops asking, which is
            // right for dressing him on load and wrong for a man stood at the rail choosing:
            // the walk and the gun style went into the save and nothing put them on him until
            // the next session. "Changing the way you hold the weapon doesn't do nothing" is
            // exactly that, and it did nothing until the game was reloaded.
            try { Locations.Wardrobe.Carry(State); }
            catch { /* he carries on as he was */ }
        }

    }
}
