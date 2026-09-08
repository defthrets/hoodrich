using System;
using Control = GTA.Control;
using GTA;
using GTA.Native;
using Hoodrich.Core;
using Hoodrich.Economy;
using Hoodrich.UI;
using Hud = Hoodrich.UI.Draw;

namespace Hoodrich.Locations
{
    /// <summary>
    /// Going through somebody's pockets.
    ///
    /// THE GAME'S OWN ANSWER IS A PICKUP AND THIS TURNS IT OFF. A dead man with a rifle drops
    /// a rifle on the pavement and you collect it by walking over it without looking down --
    /// which is the whole transaction, and it is worth nothing. Every ped near you is told not
    /// to drop what it is carrying (see Nobody), so the only way a gun changes hands is you
    /// kneeling down and taking it.
    ///
    /// TWO KEYS, AND THAT IS DELIBERATE. Standing over a body and pressing the button you
    /// press for everything else is how you loot a corpse by accident on the way past. So the
    /// ordinary context key only OFFERS it -- the line comes up saying which key actually does
    /// it -- and the search itself is a hold on the other one. On a keyboard both are E, so it
    /// reads as one long press; on a pad it is right to ask and left to do.
    ///
    /// AND HE KNEELS DOWN. The screen does not open over a man stood upright with his hands by
    /// his sides: he goes down on one knee first, and stays there for as long as the pockets
    /// are open.
    /// </summary>
    internal sealed class Search
    {
        /// <summary>How close you have to be to a body to be offered it.</summary>
        private const float Reach = 2.2f;

        /// <summary>And how far you can get before it shuts on its own.</summary>
        private const float Leave = 3.4f;

        /// <summary>How often the ground around you is looked at.</summary>
        private const int ScanMs = 400;

        /// <summary>How far the sweep reaches when it tells people not to drop things.</summary>
        private const float SweepRange = 90f;
        private const int SweepEveryMs = 2000;

        /// <summary>How long the key is held before he kneels. Long enough that a stray press is not a search.</summary>
        private const int HoldMs = 600;

        /// <summary>How long the offer stays up once you have stopped asking for it.</summary>
        private const int OfferMs = 2500;

        /// <summary>How long he is knelt over the body before the pockets come up.</summary>
        private const int KneelMs = 1100;

        private const string KneelDict = "amb@medic@standing@kneel@base";
        private const string KneelClip = "base";

        /// <summary>Looping, held until it is stopped. See TASK_PLAY_ANIM's flags.</summary>
        private const int LoopingAnim = 1;

        private readonly LootScreen _screen = new LootScreen();

        private Bodies _bodies;

        /// <summary>Set by Main: off while something louder owns the screen.</summary>
        public Func<bool> Busy;

        /// <summary>Set by Main: something came off a body.</summary>
        public Action<LootItem> Took;

        private Ped _near;
        private Ped _at;

        private int _nextScan;
        private int _sweptAt;

        /// <summary>When the offer was last asked for, when the hold started, and when he knelt.</summary>
        private int _offeredAt;
        private int _holdSince;
        private int _kneltAt;

        private bool _kneeling;

        public bool IsOpen => _screen.IsOpen || _kneltAt != 0;

        public Search(Bodies bodies)
        {
            _bodies = bodies;

            _screen.Took = item => { if (Took != null) Took(item); };
            _screen.Done = Stand;
        }

        public void Update(Ped player)
        {
            var now = Game.GameTime;

            if (_bodies != null) _bodies.Sweep(now);

            Nobody(player, now);

            if (_screen.IsOpen)
            {
                // He stays down while the pockets are open, and the body has to stay put.
                Kneel(player);

                if (_at == null || !_at.Exists() ||
                    (player != null && player.Exists() && player.Position.DistanceTo(_at.Position) > Leave))
                {
                    _screen.Close();
                    return;
                }

                _screen.Update();
                return;
            }

            // Knelt down, and the pockets have not come up yet.
            if (_kneltAt != 0)
            {
                Kneel(player);

                if (now - _kneltAt < KneelMs) return;

                Open(player);
                return;
            }

            if (player == null || !player.Exists() || !player.IsAlive) { _near = null; return; }
            if (player.IsInVehicle()) { _near = null; _holdSince = 0; return; }

            if (Busy != null && Busy()) { _near = null; _holdSince = 0; return; }

            if (now >= _nextScan)
            {
                _nextScan = now + ScanMs;
                Scan(player);
            }

            if (_near == null || !_near.Exists()) { _holdSince = 0; _offeredAt = 0; return; }

            // SOMEBODY ELSE'S MENU IS UP. The same courtesy the phone and the boot give.
            if (Menus.Owner() != null) { _holdSince = 0; return; }

            Offer(now);
        }

        /// <summary>
        /// The offer, and then the hold.
        ///
        /// The ordinary context key puts the line up and does nothing else; the line names the
        /// key that does the work, and holding THAT fills the bar under the words. On a
        /// keyboard they are the same key, which is why the offer is drawn from the first
        /// frame either of them is down.
        /// </summary>
        private void Offer(int now)
        {
            var asking = Down(Control.Context) || Down(Control.ContextSecondary);
            var doing = Down(Control.Context) || Down(Control.ScriptPadLeft);

            if (asking || doing) _offeredAt = now;

            if (!doing) _holdSince = 0;
            else if (_holdSince == 0) _holdSince = now;

            var held = doing ? (now - _holdSince) / (float)HoldMs : -1f;

            // Up while either key is down, and for a moment after -- long enough on a pad to
            // let go of one and find the other.
            if (_offeredAt != 0 && now - _offeredAt < OfferMs)
            {
                var cap = Hud.OnPad ? "HOLD D-PAD LEFT" : "HOLD E";

                UiKit.Prompt(cap, "Search the body", 1f, held);
            }

            if (held < 1f) return;

            _holdSince = 0;
            Begin(now);
        }

        /// <summary>Down on one knee, and the pockets a beat later.</summary>
        private void Begin(int now)
        {
            _at = _near;
            _kneltAt = now;
            _kneeling = false;

            var player = Game.Player.Character;

            Kneel(player);

            Hud.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        /// <summary>
        /// Puts him on one knee and keeps him there.
        ///
        /// Asked for every tick rather than once, because a looping animation can be knocked
        /// off by anything the game decides is more important -- and a screen that says he is
        /// knelt over a body while he stands there is worse than no animation at all.
        /// </summary>
        private void Kneel(Ped player)
        {
            if (player == null || !player.Exists()) return;

            try
            {
                if (!_kneeling)
                {
                    Function.Call(Hash.REQUEST_ANIM_DICT, KneelDict);

                    if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, KneelDict)) return;

                    _kneeling = true;
                }

                if (Function.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM, player.Handle,
                                        KneelDict, KneelClip, 3)) return;

                Function.Call(Hash.TASK_PLAY_ANIM, player.Handle, KneelDict, KneelClip,
                              4f, -2f, -1, LoopingAnim, 0f, false, false, false);
            }
            catch
            {
                // He searches it stood up, then.
            }
        }

        /// <summary>Back on his feet.</summary>
        private void Stand()
        {
            var player = Game.Player.Character;

            try
            {
                if (player != null && player.Exists())
                {
                    Function.Call(Hash.STOP_ANIM_TASK, player.Handle, KneelDict, KneelClip, -4f);
                }
            }
            catch
            {
                // He gets up on his own.
            }

            _kneeling = false;
            _kneltAt = 0;
            _at = null;
            _near = null;
            _holdSince = 0;
            _offeredAt = 0;
            _nextScan = Game.GameTime + 600;
        }

        private void Open(Ped player)
        {
            if (_bodies == null || _at == null || !_at.Exists()) { Stand(); return; }

            var body = _bodies.For(_at);

            if (body == null) { Stand(); return; }

            Log.Info("Search: " + body.Name + ", " + body.Affiliation + ", " +
                     body.Items.Count + " thing(s) on him.");

            _screen.Open(_bodies, body, _at);
        }

        public void Draw()
        {
            if (_screen.IsOpen) _screen.Draw();
        }

        /// <summary>
        /// The nearest body worth kneeling over.
        ///
        /// EMPTIED ONES ARE SKIPPED, which is what stops a searched body offering itself for
        /// the rest of the night. One you left something on still asks.
        /// </summary>
        private void Scan(Ped player)
        {
            _near = null;

            try
            {
                var closest = Reach;

                foreach (var ped in World.GetNearbyPeds(player, Reach + 1.5f))
                {
                    if (ped == null || !ped.Exists() || ped.IsAlive) continue;
                    if (ped.Handle == player.Handle) continue;

                    if (_bodies != null && _bodies.Done(ped)) continue;

                    var gap = player.Position.DistanceTo(ped.Position);
                    if (gap > closest) continue;

                    closest = gap;
                    _near = ped;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Search: could not look at the ground: " + ex.Message);
            }
        }

        /// <summary>
        /// Nobody drops anything any more.
        ///
        /// SET BEFORE THEY DIE, WHICH IS WHY IT IS A SWEEP. The flag decides what happens at
        /// the moment of death, so setting it on a corpse is too late -- the gun is already on
        /// the pavement. Everybody within ninety metres is told, every couple of seconds,
        /// which is one native call each on a few dozen people and is not worth optimising.
        /// </summary>
        private void Nobody(Ped player, int now)
        {
            if (player == null || !player.Exists()) return;
            if (now - _sweptAt < SweepEveryMs) return;

            _sweptAt = now;

            try
            {
                foreach (var ped in World.GetNearbyPeds(player, SweepRange))
                {
                    if (ped == null || !ped.Exists() || !ped.IsAlive) continue;
                    if (ped.Handle == player.Handle) continue;

                    Function.Call(Hash.SET_PED_DROPS_WEAPONS_WHEN_DEAD, ped.Handle, false);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Search: could not stop the drops: " + ex.Message);
            }
        }

        private static bool Down(Control control)
        {
            try
            {
                return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)control) ||
                       Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, (int)control);
            }
            catch
            {
                return false;
            }
        }

        public void RestoreWorld()
        {
            try
            {
                if (_screen.IsOpen) _screen.Close();
                else Stand();
            }
            catch
            {
                // Teardown.
            }
        }
    }
}
