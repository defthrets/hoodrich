using System;
using GTA;
using GTA.Native;

namespace Hoodrich.Core
{
    /// <summary>
    /// A face covering, on or off.
    ///
    /// COMPONENT 1 ON FRANKLIN HOLDS FIVE DRAWABLES AND THEY ARE ALL BEARDS. That is measured,
    /// not assumed -- the first build of this logged "slot has 5" and put a goatee on him,
    /// which is the same thing BikeRide's MaskUp did before it was deleted. On a freemode ped
    /// component 1 is the mask slot; on a story ped it is "berd", the beard slot, and Franklin
    /// has four beards and no mask in it. There was never an index in there that could work.
    ///
    /// SO THE SLOT IS A SETTING TOO, AND IT CAN BE A PROP. A bandana or a mask on a story
    /// character lives somewhere else -- another component, or one of the eight PROP slots,
    /// which are a different set of natives entirely and the reason nothing found it. Which
    /// one differs between Legacy and Enhanced and between clothing packs, so this asks the
    /// game rather than believing a wiki: Probe logs the size of every component and every
    /// prop slot on whoever is standing there, and Settings > Mask walks all of it live.
    ///
    /// WHAT WAS THERE IS REMEMBERED. Taking it off puts back what the slot held, not zero,
    /// because zero is clean-shaven or bare-headed and a man who had a beard before he covered
    /// his face should have one after.
    ///
    /// It does not fight anybody. A cutscene, a mission outfit or another mod that changes the
    /// slot wins: Update notices it no longer holds ours and stops claiming it is on.
    /// </summary>
    internal static class Mask
    {
        /// <summary>How many of each the game has.</summary>
        public const int Components = 12;
        public const int Props = 8;

        /// <summary>What the slot held before, or -1 for not known. Props use -1 for nothing on.</summary>
        private static int _wore = -1;
        private static int _woreTexture = -1;

        private static bool _on;
        private static int _nextLook;
        private static bool _probed;

        private const int LookEveryMs = 500;

        /// <summary>Whether it is on right now.</summary>
        public static bool Wearing => _on;

        // ---- what this install actually has ------------------------------------

        /// <summary>
        /// Writes the whole wardrobe to the log: every component and every prop slot, with how
        /// many drawables each holds.
        ///
        /// THE ONLY WAY TO KNOW. There is no native that says "this drawable is a mask" and no
        /// list anywhere that is right for both Legacy and Enhanced. What there is, is a count
        /// per slot -- and a slot with five things in it is beards, while a slot with thirty
        /// is the one worth scrolling. Once per session unless something asks again.
        /// </summary>
        public static void Probe(bool force = false)
        {
            if (_probed && !force) return;

            try
            {
                var me = Game.Player?.Character;
                if (me == null || !me.Exists()) return;

                _probed = true;

                var line = "Mask: this character's wardrobe -- components";

                for (var i = 0; i < Components; i++)
                {
                    var n = Function.Call<int>(Hash.GET_NUMBER_OF_PED_DRAWABLE_VARIATIONS, me.Handle, i);
                    line += " " + i + ":" + n;
                }

                line += "  props";

                for (var i = 0; i < Props; i++)
                {
                    var n = Function.Call<int>(Hash.GET_NUMBER_OF_PED_PROP_DRAWABLE_VARIATIONS, me.Handle, i);
                    line += " " + i + ":" + n;
                }

                Log.Info(line + ".");
            }
            catch (Exception ex)
            {
                Log.Debug("Mask: could not read the wardrobe: " + ex.Message);
            }
        }

        // ---- learning it from his own wardrobe ---------------------------------
        //
        // THE GAME ALREADY KNOWS WHERE THE MASK IS. It is in his wardrobe -- you can walk into
        // his house and put it on -- so the index exists and is correct and the only thing
        // missing is which slot it went into. Every attempt to answer that from the outside is
        // a guess, and the guesses have now cost two builds: the beard, and component 1 having
        // five things in it.
        //
        // So this stops answering it. Remember how he looks, go and put the mask on the way
        // the game intends, come back, and the mod DIFFS the two and writes down the slot, the
        // drawable and the texture it finds. It cannot be wrong about a thing it measured, and
        // it works for a prop as readily as for clothing because it walks both.

        private static readonly int[,] BareComp = new int[Components, 2];
        private static readonly int[,] BareProp = new int[Props, 2];

        private static bool _based;

        /// <summary>Whether there is a look on file to compare against.</summary>
        public static bool HasBaseline => _based;

        /// <summary>
        /// Writes down every slot on him as he stands. Taken automatically once, shortly after
        /// load, while nothing of ours is on him.
        /// </summary>
        public static string Baseline()
        {
            try
            {
                var me = Game.Player?.Character;
                if (me == null || !me.Exists()) return "Nobody there.";

                for (var i = 0; i < Components; i++)
                {
                    BareComp[i, 0] = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, me.Handle, i);
                    BareComp[i, 1] = Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, me.Handle, i);
                }

                for (var i = 0; i < Props; i++)
                {
                    BareProp[i, 0] = Function.Call<int>(Hash.GET_PED_PROP_INDEX, me.Handle, i);
                    BareProp[i, 1] = Function.Call<int>(Hash.GET_PED_PROP_TEXTURE_INDEX, me.Handle, i);
                }

                _based = true;

                Log.Info("Mask: remembered how he looks. Put the mask on in his wardrobe, then " +
                         "come back and find what changed.");

                return "";
            }
            catch (Exception ex)
            {
                Log.Debug("Mask: could not remember the look: " + ex.Message);
                return "Couldn't read what he's wearing.";
            }
        }

        /// <summary>
        /// Compares him now against the remembered look and adopts whatever moved.
        ///
        /// The FIRST difference wins and the rest go to the log. Change only the mask between
        /// the two snapshots and there is exactly one; change a whole outfit and the log says
        /// what else moved so it is obvious what happened.
        /// </summary>
        public static string Learn(Settings cfg)
        {
            try
            {
                var me = Game.Player?.Character;
                if (me == null || !me.Exists()) return "Nobody there.";

                if (!_based) return "Nothing to compare against yet.";

                if (_on) return "Take ours off first, or it finds itself.";

                var found = false;
                var also = "";
                var moved = 0;

                for (var i = 0; i < Components; i++)
                {
                    var d = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, me.Handle, i);
                    var t = Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, me.Handle, i);

                    if (d == BareComp[i, 0] && t == BareComp[i, 1]) continue;

                    moved++;

                    if (!found && Plausible(i, d))
                    {
                        found = true;

                        cfg.MaskAsProp = false;
                        cfg.MaskSlot = i;
                        cfg.MaskDrawable = d;
                        cfg.MaskTexture = t;
                    }
                    else
                    {
                        also += " component " + i + ":" + d + "/" + t;
                    }
                }

                for (var i = 0; i < Props; i++)
                {
                    var d = Function.Call<int>(Hash.GET_PED_PROP_INDEX, me.Handle, i);
                    var t = Function.Call<int>(Hash.GET_PED_PROP_TEXTURE_INDEX, me.Handle, i);

                    if (d == BareProp[i, 0] && t == BareProp[i, 1]) continue;

                    moved++;

                    // A prop that has been taken OFF is not the thing that was put on.
                    if (!found && d >= 0)
                    {
                        found = true;

                        cfg.MaskAsProp = true;
                        cfg.MaskSlot = i;
                        cfg.MaskDrawable = d;
                        cfg.MaskTexture = t;
                    }
                    else
                    {
                        also += " prop " + i + ":" + d + "/" + t;
                    }
                }

                if (!found)
                {
                    Log.Info("Mask: nothing changed since the look was remembered.");
                    return "He looks the same as before. Put the mask on first.";
                }

                // The look he is wearing IS the mask, so this is the state -- and what was
                // under it is what the baseline says, which is exactly what Off wants.
                _on = true;
                _wore = cfg.MaskAsProp ? BareProp[cfg.MaskSlot, 0] : BareComp[cfg.MaskSlot, 0];
                _woreTexture = cfg.MaskAsProp ? BareProp[cfg.MaskSlot, 1] : BareComp[cfg.MaskSlot, 1];

                Log.Info("Mask: learned it -- " + Where(cfg) + " drawable " + cfg.MaskDrawable +
                         " texture " + cfg.MaskTexture + ", over " + _wore + "/" + _woreTexture +
                         (string.IsNullOrEmpty(also) ? "." : ". Also changed:" + also + "."));

                // A WHOLE OUTFIT MOVED, SO THIS IS A GUESS AMONG SEVERAL. It happened: a
                // trainer used to put the mask on reset his face, hair, torso, legs, shoes and
                // both props at the same time, eight slots changed, and the first plausible one
                // is not necessarily the right one. Worth saying out loud rather than letting
                // somebody wonder why they are wearing trousers on their head.
                if (moved > 2)
                {
                    return "Found " + Where(cfg) + " " + cfg.MaskDrawable + ", but " + moved +
                           " things changed. Change only the mask if it is wrong.";
                }

                return "";
            }
            catch (Exception ex)
            {
                Log.Debug("Mask: could not work out what changed: " + ex.Message);
                return "Couldn't work out what changed.";
            }
        }

        // ---- counting ----------------------------------------------------------

        /// <summary>How many drawables the configured slot holds, or 0.</summary>
        public static int Count(Settings cfg)
        {
            try
            {
                var me = Game.Player?.Character;
                if (me == null || !me.Exists()) return 0;

                return cfg.MaskAsProp
                    ? Function.Call<int>(Hash.GET_NUMBER_OF_PED_PROP_DRAWABLE_VARIATIONS,
                                         me.Handle, cfg.MaskSlot)
                    : Function.Call<int>(Hash.GET_NUMBER_OF_PED_DRAWABLE_VARIATIONS,
                                         me.Handle, cfg.MaskSlot);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>How many textures the configured drawable has, or 0.</summary>
        public static int Textures(Settings cfg)
        {
            try
            {
                var me = Game.Player?.Character;
                if (me == null || !me.Exists()) return 0;

                return cfg.MaskAsProp
                    ? Function.Call<int>(Hash.GET_NUMBER_OF_PED_PROP_TEXTURE_VARIATIONS,
                                         me.Handle, cfg.MaskSlot, cfg.MaskDrawable)
                    : Function.Call<int>(Hash.GET_NUMBER_OF_PED_TEXTURE_VARIATIONS,
                                         me.Handle, cfg.MaskSlot, cfg.MaskDrawable);
            }
            catch
            {
                return 0;
            }
        }

        // ---- on and off --------------------------------------------------------

        /// <summary>On if it is off, off if it is on. Returns why not, or empty for done.</summary>
        public static string Toggle(Settings cfg)
        {
            return _on ? Off(cfg) : On(cfg);
        }

        public static string On(Settings cfg)
        {
            try
            {
                var me = Game.Player?.Character;
                if (me == null || !me.Exists()) return "Nobody to mask.";

                Probe();

                var slot = cfg.MaskSlot;
                var many = Count(cfg);

                if (many <= 0)
                {
                    Log.Warn("Mask: " + Where(cfg) + " is empty on this character.");
                    return "Nothing in that slot. Settings > Mask.";
                }

                var drawable = cfg.MaskDrawable;

                if (drawable < 0 || drawable >= many)
                {
                    Log.Warn("Mask: " + Where(cfg) + " has " + many +
                             ", so " + drawable + " is not in it. Settings > Mask.");
                    return "That one isn't in the slot. Settings > Mask.";
                }

                var textures = Textures(cfg);
                var texture = cfg.MaskTexture;

                if (texture < 0 || texture >= Math.Max(1, textures)) texture = 0;

                // Remembered ONCE, on the way from bare to covered. Refresh comes through here
                // too, and remembering again then would remember the mask as what was under it.
                if (!_on) Keep(me, cfg);

                if (cfg.MaskAsProp)
                {
                    Function.Call(Hash.SET_PED_PROP_INDEX, me.Handle, slot, drawable, texture, true);
                }
                else
                {
                    if (!Function.Call<bool>(Hash.IS_PED_COMPONENT_VARIATION_VALID,
                                             me.Handle, slot, drawable, texture))
                    {
                        Log.Warn("Mask: " + Where(cfg) + " drawable " + drawable + " texture " +
                                 texture + " is not valid on this character.");
                        return "Not valid on him. Settings > Mask.";
                    }

                    var palette = Function.Call<int>(Hash.GET_PED_PALETTE_VARIATION, me.Handle, slot);

                    Function.Call(Hash.SET_PED_COMPONENT_VARIATION, me.Handle, slot,
                                  drawable, texture, palette);
                }

                _on = true;

                Log.Info("Mask on: " + Where(cfg) + " drawable " + drawable + " texture " +
                         texture + " (slot has " + many + ", this one has " + textures +
                         " texture(s)); was " + _wore + "/" + _woreTexture + ".");

                return "";
            }
            catch (Exception ex)
            {
                Log.Debug("Mask: could not put it on: " + ex.Message);
                return "Couldn't get it on.";
            }
        }

        public static string Off(Settings cfg)
        {
            try
            {
                var me = Game.Player?.Character;
                if (me == null || !me.Exists()) return "Nobody to unmask.";

                var slot = cfg.MaskSlot;

                if (cfg.MaskAsProp)
                {
                    // Nothing there before is the normal case for a hat slot, and clearing is
                    // the only way to express it -- a prop has no "drawable zero means none".
                    if (_wore < 0)
                    {
                        Function.Call(Hash.CLEAR_PED_PROP, me.Handle, slot);
                    }
                    else
                    {
                        Function.Call(Hash.SET_PED_PROP_INDEX, me.Handle, slot,
                                      _wore, Math.Max(0, _woreTexture), true);
                    }
                }
                else
                {
                    // Not known is a reload: the mask survived it and the memory did not. Zero
                    // is the honest fallback and the log says it happened.
                    if (_wore < 0) Log.Info("Mask off: what was under it is not known, using 0.");

                    var back = _wore < 0 ? 0 : _wore;
                    var backTexture = _woreTexture < 0 ? 0 : _woreTexture;

                    var palette = Function.Call<int>(Hash.GET_PED_PALETTE_VARIATION, me.Handle, slot);

                    Function.Call(Hash.SET_PED_COMPONENT_VARIATION, me.Handle, slot,
                                  back, backTexture, palette);
                }

                _on = false;
                _wore = -1;
                _woreTexture = -1;

                return "";
            }
            catch (Exception ex)
            {
                Log.Debug("Mask: could not take it off: " + ex.Message);
                return "Couldn't get it off.";
            }
        }

        /// <summary>
        /// Re-applies what is configured, if it is on. This is what the Settings sliders call
        /// as they move, so the right slot and index are found by watching his head.
        /// </summary>
        public static void Refresh(Settings cfg)
        {
            if (!_on) return;

            var why = On(cfg);

            // A bad number mid-scroll is not a reason to strip him: the last good one is still
            // on, and the next notch may be fine.
            if (!string.IsNullOrEmpty(why)) Log.Debug("Mask: refresh skipped -- " + why);
        }

        /// <summary>
        /// Puts the mask on the OTHER slot when the sliders move between them.
        ///
        /// Changing the slot while something is on would otherwise leave the old slot wearing
        /// it for ever -- nothing would ever put that one back, because Off only knows about
        /// whatever is configured NOW.
        /// </summary>
        public static void MoveTo(Settings cfg, Action change)
        {
            var was = _on;

            if (was) Off(cfg);

            change();

            if (was) On(cfg);
        }

        /// <summary>Keeps Wearing honest against whatever else touches him. Cheap, on a clock.</summary>
        // ---- what the police have on him ----------------------------------------
        //
        // THE MASK IS AN IDENTIFIER, OR THE LACK OF ONE. The police are looking for whoever
        // they last had eyes on: a man in a balaclava, or a bare face. Change that while they
        // are not looking -- stars grey, round a corner, in a doorway -- and the man they are
        // looking for no longer exists, the same way a different car or a bush loses them.
        // Change it in front of them and they have simply watched you do it. Nothing here
        // touches the wanted level itself: the search carries on and runs out the way a
        // search always does; they just cannot spot him while it does.

        private static int _wanted;

        /// <summary>How he looked the last time they had eyes on him: -1 never, 0 bare, 1 masked.</summary>
        private static int _seen = -1;

        /// <summary>He changed his look out of their sight; they are ignoring him until the search runs out.</summary>
        private static bool _incognito;

        private static bool _wokeIgnore;

        /// <summary>Every tick: what they know now.</summary>
        private static void Police()
        {
            try
            {
                var player = Game.Player;
                if (player == null) return;

                // Whatever a crash left set is put back, once.
                if (!_wokeIgnore)
                {
                    _wokeIgnore = true;
                    Function.Call(Hash.SET_POLICE_IGNORE_PLAYER, player.Handle, false);
                }

                var wanted = player.Wanted.WantedLevel;

                if (wanted == 0)
                {
                    if (_wanted > 0) Forget(player);
                    _wanted = 0;
                    return;
                }

                // A new crime seen while they were meant to be ignoring him: they have him again.
                if (_incognito && wanted > _wanted)
                {
                    Function.Call(Hash.SET_POLICE_IGNORE_PLAYER, player.Handle, false);
                    _incognito = false;
                    UI.Notify.Important("~r~They've got you again.");
                }

                // While they can see him, they know what he looks like now.
                if (!Function.Call<bool>(Hash.ARE_PLAYER_STARS_GREYED_OUT, player.Handle))
                {
                    _seen = _on ? 1 : 0;
                }

                _wanted = wanted;
            }
            catch
            {
            }
        }

        private static void Forget(Player player)
        {
            if (_incognito)
            {
                try { Function.Call(Hash.SET_POLICE_IGNORE_PLAYER, player.Handle, false); }
                catch { }
            }

            _incognito = false;
            _seen = -1;
        }

        /// <summary>
        /// After the mask went on or came off: what that did to the search, as a line for the
        /// screen, or null when there is no search or nothing changed. Lost is true when it
        /// worked -- they are after a man who no longer exists.
        /// </summary>
        public static string Changed(out bool lost)
        {
            lost = false;

            try
            {
                var player = Game.Player;
                if (player == null || player.Wanted.WantedLevel == 0) return null;

                var look = _on ? 1 : 0;

                // In front of them, it is just a man taking a mask off.
                if (!Function.Call<bool>(Hash.ARE_PLAYER_STARS_GREYED_OUT, player.Handle))
                {
                    _seen = look;
                    return _on ? "~r~Too late for that. They can see you." : "~r~They watched you take it off.";
                }

                // They never had a look at him -- a report, not a sighting -- so there is nothing to shed.
                if (_seen == -1) return null;

                // Back to the look they are after, while they were ignoring him: that is the man again.
                if (look == _seen)
                {
                    if (!_incognito) return null;

                    Function.Call(Hash.SET_POLICE_IGNORE_PLAYER, player.Handle, false);
                    _incognito = false;
                    return "~r~That's the face they're after.";
                }

                if (!_incognito)
                {
                    Function.Call(Hash.SET_POLICE_IGNORE_PLAYER, player.Handle, true);
                    _incognito = true;
                }

                lost = true;
                Log.Info("Mask: the police are after a " + (_seen == 1 ? "masked man" : "bare face") + ". Not him.");

                return _on ? "~g~They're after a bare face. That's not you." : "~g~They're after a man in a mask. That's not you.";
            }
            catch
            {
                return null;
            }
        }

        public static void Update(Settings cfg)
        {
            Police();

            var now = Game.GameTime;
            if (now < _nextLook) return;
            _nextLook = now + LookEveryMs;

            // The first look at a bare-faced man is the one worth writing down, and it costs
            // twenty natives once. Without it the learner needs a button pressed before the
            // wardrobe rather than after, which is the step everybody forgets.
            if (!_based && !_on) Baseline();

            if (!_on) return;

            try
            {
                var me = Game.Player?.Character;
                if (me == null || !me.Exists()) return;

                var has = cfg.MaskAsProp
                    ? Function.Call<int>(Hash.GET_PED_PROP_INDEX, me.Handle, cfg.MaskSlot)
                    : Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, me.Handle, cfg.MaskSlot);

                if (has == cfg.MaskDrawable) return;

                // Somebody else changed it -- a cutscene, an outfit, another mod. They win, and
                // the memory of what was under it is no longer true either.
                _on = false;
                _wore = -1;
                _woreTexture = -1;

                Log.Info("Mask: the slot changed under it; no longer counted as on.");
            }
            catch
            {
                // Next look.
            }
        }

        /// <summary>For a teardown. Off if it is on; quiet if it is not.</summary>
        public static void RestoreWorld(Settings cfg)
        {
            try { if (Game.Player != null) Forget(Game.Player); } catch { }
            if (_on) Off(cfg);
        }

        // ---- bits --------------------------------------------------------------

        private static void Keep(Ped me, Settings cfg)
        {
            if (cfg.MaskAsProp)
            {
                _wore = Function.Call<int>(Hash.GET_PED_PROP_INDEX, me.Handle, cfg.MaskSlot);
                _woreTexture = Function.Call<int>(Hash.GET_PED_PROP_TEXTURE_INDEX, me.Handle, cfg.MaskSlot);
            }
            else
            {
                _wore = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, me.Handle, cfg.MaskSlot);
                _woreTexture = Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, me.Handle, cfg.MaskSlot);
            }
        }

        /// <summary>
        /// Whether a component that moved could be the thing somebody just put on.
        ///
        /// TWO SLOTS ALWAYS MOVE AND NEITHER IS EVER THE MASK. Component 0 is his face, which
        /// a trainer resets as a side effect of touching anything; component 2 is his hair,
        /// which a mask HIDES -- so putting one on sets the hair to none every single time and
        /// it is the change most likely to be seen first. Between them they cost this the
        /// right answer once already: the log picked "component 0 drawable 0" out of eight
        /// slots that had moved, while component 8 drawable 4 -- the balaclava -- sat further
        /// down the same line.
        ///
        /// A slot cleared to nothing is out for the same reason: a thing taken OFF is not the
        /// thing that was put on.
        /// </summary>
        private static bool Plausible(int component, int drawable)
        {
            if (component == 0 || component == 2) return false;

            return drawable > 0;
        }

        /// <summary>"prop slot 0" or "component 1", for a log line somebody has to act on.</summary>
        public static string Where(Settings cfg)
        {
            return (cfg.MaskAsProp ? "prop slot " : "component ") + cfg.MaskSlot;
        }
    }
}
