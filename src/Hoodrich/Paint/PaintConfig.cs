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

        /// <summary>Splatters per second while the trigger is down.</summary>
        public float Rate = 9f;

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
        ///     0.45m and nearer ->  0.08m across (the floor)
        ///     1m               ->  0.18m
        ///     2m               ->  0.36m
        ///     4m               ->  0.72m, and it stops there
        ///
        /// Brought down about a quarter from where it started. A can lays a narrow band you
        /// draw with, not a patch you cover with -- and the wider it was, the less the marks
        /// read as strokes and the more they read as one blob growing.
        /// </summary>
        public bool SprayCanLook = true;

        public float CanRange = 4f;
        public float CanSizeAtOneMetre = 0.18f;
        public float CanSpreadPower = 1f;
        public float CanMinSize = 0.08f;
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
        /// The GAME'S pool is the real ceiling -- a few hundred across the whole world, and it
        /// refuses quietly once they are gone. This number only means something because a
        /// refusal takes the slot back off whichever mark is furthest away. See Marks.Recycle.
        /// </summary>
        public int MaxMarks = 1500;

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

        /// <summary>Whether it paints at all. There is no arming key; this is for turning it off.</summary>
        public bool PaintEnabled = true;

        /// <summary>Whether the can takes the nearest of the game's eight weapon tints.</summary>
        public bool TintTheCan = true;

        // ---- how long it lasts ---------------------------------------------------

        /// <summary>How much longer a tank lasts than stock, when it empties at all.</summary>
        public float ExtinguisherLasts = 3f;

        /// <summary>Whether either tool ever empties. Neither does.</summary>
        public bool CanRunsOut = false;
        public bool ExtinguisherRunsOut = false;
    }
}
