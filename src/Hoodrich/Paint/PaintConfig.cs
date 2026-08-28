namespace Hoodrich.Paint
{
    /// <summary>
    /// Every knob the paint engine has, and nothing else.
    ///
    /// THIS EXISTS SO THE ENGINE CAN LIVE IN TWO MODS UNCHANGED. The standalone reads its
    /// values out of an ini and opens the picker on F3; Posted Up reads its own and opens the
    /// same thing as an app on the phone. Those are the only two differences there should ever
    /// be, and the way to keep it that way is to stop the engine knowing anything about either
    /// host -- so it depends on this, and each host fills it in.
    ///
    /// The alternative is what was here first: the engine taking the host's Settings class
    /// directly. That compiles fine in one mod and forces a fork the moment there are two,
    /// because the second host's settings are a different shape -- and a forked engine drifts
    /// silently, which is how you end up fixing the same bug twice and missing it once.
    ///
    /// Anything host-shaped -- which key opens the menu, where the ini lives, whether paint
    /// survives a reload -- deliberately stays OUT of here.
    /// </summary>
    internal sealed class PaintConfig
    {
        // ---- the extinguisher ---------------------------------------------------

        /// <summary>
        /// How far the extinguisher carries, in metres, measured from the MAN.
        ///
        /// Not from the camera, which is the mistake worth naming here because it cost a whole
        /// evening: the probe has to START at the camera or paint does not land under the
        /// reticle, but in third person the camera sits two to three metres behind him, so a
        /// range spent from there gets most of the way back to his own shoulder before it
        /// begins. Surface.InFront adds that distance back on.
        /// </summary>
        public float Range = 10f;

        /// <summary>
        /// Splatters per second while the trigger is down.
        ///
        /// RAISING THIS ALONE DOES NOT GIVE YOU A LINE, which is why it is only half of the
        /// answer. A rate is a number of marks per SECOND; whether they join up depends on how
        /// fast the reticle is travelling, so any fixed rate is beads when you sweep quickly
        /// and a pile of paint in one spot when you hold still. See Continuous.
        /// </summary>
        /// <remarks>
        /// BACK TO 22 AFTER A DETOUR. It was cut to 15 to fit more tags in the game's decal
        /// pool, and that bought about one extra tag at the cost of a line you could see
        /// through -- which is the wrong trade, because a patchy tag does not look like less
        /// paint, it looks broken.
        ///
        /// Worth knowing which knob does what: at ordinary sweep speeds the RATE decides the
        /// spacing and Overlap changes nothing, because the gap between dabs is already wider
        /// than the fill step. Overlap only bites when the reticle is moving fast -- which is
        /// exactly where the patchiness showed -- so it is set tighter than it ever was while
        /// costing nothing at all when you are working slowly.
        /// </remarks>
        public float Rate = 22f;

        /// <summary>
        /// Whether the gap between one splatter and the next is filled in.
        ///
        /// THIS IS WHAT MAKES A LINE. Each dab knows where the last one landed, so it can lay
        /// marks along the path between them -- as many as the distance needs and no more.
        /// Sweep fast and it fills; hold still and it costs nothing, because there is no gap.
        ///
        /// That is the whole difference between "more paint" and "a consistent line": the
        /// spacing follows the reticle rather than the clock.
        /// </summary>
        public bool Continuous = true;

        /// <summary>
        /// How far apart the filled marks sit, as a fraction of their own width.
        ///
        /// Below about 0.5 they overlap into a solid band. Higher reads as a dotted trail,
        /// which is a legitimate look but not the one this is for.
        /// </summary>
        public float Overlap = 0.30f;

        /// <summary>
        /// The most marks one dab may fill in.
        ///
        /// A cap rather than a budget, and it exists for the pathological case: whipping the
        /// reticle across a courtyard puts two consecutive hits forty metres apart, and
        /// without this that single frame would try to lay a thousand decals and empty the
        /// game's pool in one flick.
        /// </summary>
        public int MaxFill = 16;

        /// <summary>
        /// How wide the splatter is at one metre. Everything else follows.
        ///
        /// Width = SizeAtOneMetre x distance^SpreadPower. At 0.2 and a power of 1:
        ///
        ///      1m  ->  0.20m
        ///      5m  ->  1.00m
        ///     10m  ->  2.00m
        ///
        /// A straight line, so a real cone -- width proportional to distance is what a spray
        /// geometrically is. Above 1 it blooms toward the far end, like a plume losing pressure.
        /// </summary>
        public float SizeAtOneMetre = 0.2f;

        public float SpreadPower = 1f;

        public float MinSize = 0.08f;
        public float MaxSize = 3.50f;

        // ---- the spray can ------------------------------------------------------

        /// <summary>
        /// Whether he holds a can instead of an extinguisher.
        ///
        /// A LOOK, NOT A MODE -- but a look that carries its own reach and cone, because a can
        /// is worked a foot from the wall and an extinguisher is a pressure vessel you stand
        /// back from. Sharing one curve made one of the two wrong.
        ///
        ///     0.6m and nearer  ->  0.04m across (the floor)
        ///     1m               ->  0.09m
        ///     2m               ->  0.26m
        ///     3m               ->  0.47m
        ///     4m               ->  0.72m, and it stops there
        ///
        /// A BLOOM RATHER THAN A CONE, and this is the one place the two tools genuinely
        /// disagree about physics. The extinguisher is a straight line because it is a jet
        /// under pressure. A can held near a wall lays a tight band and only opens out as you
        /// back off, so its power is 1.5 -- which halves the mark at a metre and leaves four
        /// metres exactly where it was.
        ///
        /// Solved for the far end deliberately: k = 0.72 / 4^1.5. Picking the near end and
        /// letting the far end fall where it may is how you end up re-tuning the whole thing
        /// every time one end of it feels wrong.
        /// </summary>
        public bool SprayCanLook = true;

        public float CanRange = 4f;
        public float CanSizeAtOneMetre = 0.09f;
        public float CanSpreadPower = 1.5f;
        public float CanMinSize = 0.04f;
        public float CanMaxSize = 0.75f;

        // ---- whichever is in his hand -------------------------------------------

        /// <summary>
        /// The live numbers.
        ///
        /// The engine reads these and never the underlying pair, so exactly one place knows
        /// which tool is out. Without that the reach can come from one tool and the cone from
        /// the other, which is not hypothetical -- it happened, and it presented as the mod
        /// not painting at all.
        /// </summary>
        public float LiveRange => SprayCanLook ? CanRange : Range;
        public float LiveSizeAtOneMetre => SprayCanLook ? CanSizeAtOneMetre : SizeAtOneMetre;
        public float LiveSpreadPower => SprayCanLook ? CanSpreadPower : SpreadPower;
        public float LiveMinSize => SprayCanLook ? CanMinSize : MinSize;
        public float LiveMaxSize => SprayCanLook ? CanMaxSize : MaxSize;

        // ---- paint ---------------------------------------------------------------

        public float Opacity = 0.92f;

        /// <summary>
        /// How many marks are remembered.
        ///
        /// THIS IS THE RECORD, NOT WHAT IS ON THE WALL. The game's own decal pool is a few
        /// hundred across the entire world and refuses quietly once they are gone -- no
        /// setting here changes that, and nothing can.
        ///
        /// What this buys is everything you have EVER painted staying real. The list is the
        /// truth and the decals are a view of it: whatever is near you is on the wall, a
        /// refusal takes a slot back off something further away, and walking back to a piece
        /// you did an hour ago puts it up again. At fifty thousand that is a whole city's
        /// worth of paint you can return to, for a few megabytes of small structs.
        ///
        /// It is only affordable because the two loops that touch every mark were fixed to
        /// stop doing that -- see Marks.Recycle, which used to scan the entire list on every
        /// refused dab.
        /// </summary>
        public int MaxMarks = 50000;

        /// <summary>Whether the visible jet is tinted to the colour being sprayed.</summary>
        public bool ColourTheSmoke = true;

        /// <summary>
        /// How big the spray effect is out of each tool.
        ///
        /// SEPARATE NUMBERS BECAUSE THEY ARE SEPARATE TOOLS. An extinguisher discharge that
        /// fills half an alley is exactly right for an extinguisher. The same cloud out of a
        /// six-inch can is absurd, and worse, it sits between you and the wall you are trying
        /// to paint.
        ///
        /// The can also gets no cloud and no smoke fallback at all -- see Sprayer.CanJets.
        /// </summary>
        public float JetScale = 1f;
        public float CanJetScale = 0.30f;

        /// <summary>
        /// Whether the spray tilts with the camera.
        ///
        /// The PAINT never needed this: it comes off a ray from the camera and has always gone
        /// exactly where the reticle is. This is only the visible spray agreeing with it --
        /// which matters more than decoration, because a jet leaving at one angle while marks
        /// appear at another reads as the paint being broken.
        /// </summary>
        public bool JetFollowsAim = true;

        /// <summary>
        /// Where the can's jet leaves the can, in the CAN'S own space.
        ///
        /// Rockstar start this effect on the can at zero offset, which puts it at the model's
        /// origin -- and that is somewhere in the body of the tin, not at the tip. From behind
        /// him nobody notices; in first person the spray visibly leaves halfway down the can.
        ///
        /// Dials rather than numbers in the code, and all three axes, because which way is
        /// "up the can" in a prop's local space is not something that can be reasoned to. Nudge
        /// in steps of about 0.02. If one sends it the wrong way, negate it.
        /// </summary>
        public float CanJetUp = 0.09f;
        public float CanJetOut = 0f;
        public float CanJetSide = 0f;

        /// <summary>
        /// Which clip he plays while spraying in FIRST PERSON.
        ///
        /// The default is the ready pose, because the real spraying clip swings the arm across
        /// the body and takes the can out of frame from inside his head.
        ///
        /// It is a setting because the alternatives can only be judged by looking at them, and
        /// the dictionary has three. All come from
        /// anim@scripted@freemode@postertag@graffiti_spray@male@:
        ///
        ///     spray_can_idle_male     the ready pose. Still, can up. Default.
        ///     spray_can_var_01_male   a spray variation -- may move less than the main one
        ///     spray_can_var_02_male   the other variation
        ///     spray_can_male          the full clip. Correct finger, wrong arm in first person.
        ///
        /// A FINGER ON ITS OWN IS NOT AVAILABLE TO A SCRIPT. Peds are animation-driven and the
        /// game exposes only GET_ENTITY_BONE_ROTATION, never a setter -- so a trigger finger
        /// can only come from a clip that already has one, which is what this picks between.
        /// </summary>
        public string FirstPersonClip = "spray_can_idle_male";

        /// <summary>Whether it paints at all. There is no arming key; this is for turning it off.</summary>
        public bool PaintEnabled = true;

        /// <summary>
        /// Whether this mod's engine has been handed the tool.
        ///
        /// IT IS NOT ENOUGH TO ASK "IS AN EXTINGUISHER OUT". Two things go wrong the moment it
        /// is: any extinguisher picked up in a fire station silently turns into a spray can,
        /// and -- the one that was actually reported -- with both mods installed, taking an
        /// EXTINGUISHER in one of them had the other put a CAN in the same hand 66 milliseconds
        /// later, because both were watching the same weapon and only one of them had been
        /// asked for anything.
        ///
        /// So the engine waits to be given the job. The standalone defaults to true, because
        /// there an extinguisher paints and that is the entire mod. Posted Up sets it false at
        /// startup and its Graffiti app and tag runs turn it on, because there the app is the
        /// way in and an extinguisher is just an extinguisher until you ask.
        /// </summary>
        public bool Armed = true;

        /// <summary>Whether the can takes the nearest of the game's eight weapon tints.</summary>
        public bool TintTheCan = true;

        /// <summary>
        /// How far up out of his fist the can sits, in metres.
        ///
        /// A DIAL RATHER THAN A NUMBER IN THE CODE, because this is the one measurement here
        /// that cannot be reasoned to -- it is an offset in PH_R_Hand's own local space, and a
        /// bone's axes are whatever the animators made them. The only way to know it is right
        /// is to look at it, so it is somewhere you can nudge without a rebuild.
        ///
        /// The tag run seats it at 0.012, which puts the can in the middle of the grip. Raised
        /// by eye from there in three steps to 0.060: 0.012 buried the nozzle in his fist,
        /// 0.030 got it near his fingertip, 0.048 close, and this has the finger ON the tip.
        /// That is where a hand actually holds one -- you press the nozzle, so the finger has
        /// to reach it.
        ///
        /// THE SPRAY FOLLOWS THIS FOR FREE and needs no matching change. The jet is started ON
        /// the can rather than in the world, so its offset is in the can's own space -- move
        /// the can and the effect goes with it. CanJetUp positions the jet WITHIN the can, and
        /// is a separate question from where the can sits in his hand.
        ///
        /// If nudging this moves the can the WRONG way, the axis runs the other way on this
        /// build: use a negative number.
        /// </summary>
        public float CanSeat = 0.060f;

        // ---- how long it lasts ---------------------------------------------------

        /// <summary>How much longer a tank lasts than stock, when it empties at all.</summary>
        public float ExtinguisherLasts = 3f;

        /// <summary>Whether either tool ever empties. Neither does.</summary>
        public bool CanRunsOut = false;
        public bool ExtinguisherRunsOut = false;
    }
}
