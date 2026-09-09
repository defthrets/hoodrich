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
        ///
        /// IT MOVES IN STEPS, NOT SMOOTHLY, and that is worth knowing before nudging it. The
        /// fill count is an integer division, so most changes do nothing at all: with 0.070m
        /// marks on an ordinary sweep, everything from 0.30 to 0.38 gives the same 66 marks,
        /// and 0.40 is where it drops to 44. Tuning this by small amounts and watching for a
        /// difference is a way to conclude the setting does not work.
        /// </summary>
        public float Overlap = 0.40f;

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
        ///     0.5m and nearer  ->  0.025m across (the floor)
        ///     1m               ->  0.07m
        ///     1.5m             ->  0.13m
        ///     2m               ->  0.20m
        ///     2.5m             ->  0.28m, and it stops there
        ///
        /// A BLOOM RATHER THAN A CONE, and this is the one place the two tools genuinely
        /// disagree about physics. The extinguisher is a straight line because it is a jet
        /// under pressure. A can held near a wall lays a tight band and only opens out as you
        /// back off, so its power is 1.5 -- which halves the mark at a metre and leaves four
        /// metres exactly where it was.
        ///
        /// Solved for the far end deliberately, the same way each time it has moved: pick what
        /// the longest shot should be and derive k from it. Picking the near end and letting
        /// the far end fall where it may is how you end up re-tuning the whole thing every
        /// time one end of it feels wrong.
        ///
        /// Brought in from four metres to 2.5, and about a fifth finer at every distance with
        /// it. Four metres was a range you could paint a garage door from; this is a can you
        /// work close to a wall with, which is what a can is.
        /// </summary>
        public bool SprayCanLook = true;

        public float CanRange = 2f;

        /// <summary>
        /// The can's flare, FITTED TO THREE MARKS ON A WALL rather than guessed.
        ///
        /// Sprayed a line at hand height, one about half a metre above and below it, and one
        /// at the top and bottom of a standing reach, then solved the curve to pass through
        /// thin at the first and the ceiling at the last. Standing half a metre off, those are
        /// 0.50, 0.72 and 1.12 metres from his hand -- the offsets are vertical but the curve
        /// is of DISTANCE, so what matters is the hypotenuse, not the height.
        ///
        /// The old 0.07 and 1.5 gave 0.055 at the middle and 0.083 at the edge of his reach:
        /// a flare of one and a half times across the whole thing, which is why it read as a
        /// constant band. This gives five and a half.
        ///
        /// It is steep, and steep is the point. Anything shallower cannot both stay thin where
        /// his hand is and reach the ceiling anywhere he can still comfortably paint.
        /// </summary>
        public float CanSizeAtOneMetre = 0.237f;

        public float CanSpreadPower = 2.11f;
        /// <summary>
        /// The finest line the can can draw -- the thin cap's floor, which the other two
        /// multiply. Every cap moves when this does.
        ///
        /// WAS 0.025. The whole ladder moved up a step: the old stock cap is the thin one now,
        /// the old fat is the stock, and there is a new fat above both. Done here rather than
        /// by rewriting the multipliers in Caps, so those stay a plain doubling.
        /// </summary>
        public float CanMinSize = 0.055f;
        public float CanMaxSize = 0.30f;

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
        /// <summary>
        /// Which nozzle is on the can: 0 thin, 1 stock, 2 fat. See Caps.
        ///
        /// The can only. An extinguisher does not have caps.
        /// </summary>
        public int Cap;

        /// <summary>The nozzle's width multiplier, or 1 for anything that is not the can.</summary>
        private float Nozzle => SprayCanLook ? Caps.At(Cap).Width : 1f;

        /// <summary>
        /// THE FLOOR MOVES AND THE CEILING DOES NOT, which is the whole of what a cap is.
        ///
        /// A fat cap cannot draw a fine line however close you hold it -- the paint is already
        /// wide as it leaves the nozzle. It can still only manage what the distance allows at
        /// the far end, and so can a thin one, which is why the ceiling is shared.
        ///
        /// The visible consequence is that the caps converge as you step back: past the point
        /// where the distance curve has risen above the floor, they are the same can. That is
        /// correct rather than a simplification -- a metre off a wall the cap stops being the
        /// thing deciding how wide the band is.
        /// </summary>
        public float LiveMinSize => SprayCanLook ? CanMinSize * Nozzle : MinSize;
        public float LiveMaxSize => SprayCanLook ? CanMaxSize : MaxSize;

        /// <summary>
        /// How many times more paint the CAN lays than the extinguisher does.
        ///
        /// ONE DIAL RATHER THAN THREE, because density is not one number -- it comes out of
        /// three that have to move together or the result is uneven rather than denser:
        ///
        ///   Rate      marks a second, which is what you get standing still
        ///   Overlap   the spacing of the trail marks, which is what you get sweeping
        ///   MaxFill   the ceiling on that trail, which is what stops a fast sweep clipping
        ///
        /// Turn up the rate alone and a slow hand gets denser while a fast sweep does not.
        /// Tighten the overlap alone and it is the other way round. Halving the spacing without
        /// raising the ceiling means a fast sweep hits the cap and thins out exactly when you
        /// are moving quickest, which reads as the mod failing under speed.
        ///
        /// So all three move off this. At 2 the can lays twice the paint per stroke at any
        /// hand speed, and spends the decal budget twice as fast for it -- which is the whole
        /// of the trade and there is no version of this where it is not.
        ///
        /// The extinguisher is untouched. It already covers a garage door in a second and
        /// doubling that empties the game's pool faster than the sweep can recycle it.
        /// </summary>
        public float CanDensity = 3f;

        private float Denser => SprayCanLook ? (CanDensity < 0.25f ? 0.25f : CanDensity) : 1f;

        public float LiveRate => Rate * Denser;
        public float LiveOverlap => Overlap / Denser;
        public int LiveMaxFill => (int)(MaxFill * Denser);

        // ---- paint ---------------------------------------------------------------

        /// <summary>
        /// How solid a mark goes on WITH THE NOZZLE AT THE WALL. 1 is fully opaque.
        ///
        /// Was 0.92, which let the wall through every mark. Overlapping translucent marks do
        /// build up, but the FIRST pass over clean wall is the one you look at, and at 0.92
        /// that pass is visibly thin.
        ///
        /// It is now the NEAR end of a range rather than the only number. See OpacityFar.
        /// </summary>
        public float Opacity = 1f;

        /// <summary>
        /// And how solid at the far end of the can's reach.
        ///
        /// A CAN THAT LOSES WIDTH AND KEEPS DENSITY IS NOT A CAN. The width already opens out
        /// with distance -- that is what turns a stream of decals into a spray, and it is the
        /// oldest thing in this file. What went with it was nothing: a mark laid from the far
        /// end of the reach was as solid as one laid with the cap against the plaster. So
        /// backing off gave you a bigger stamp of exactly the same paint, and the halo of thin
        /// overspray that surrounds every real stroke had nowhere to come from.
        ///
        /// The same pressure explains both. A plume holds together while it has pressure and
        /// opens out as it loses it -- and the paint it is carrying is spread over that wider
        /// circle, so it arrives thinner. Wider and fainter are the same fact.
        ///
        /// SQUARED, LIKE THE WIDTH, AND FOR THE SAME REASON. Linear would start thinning the
        /// moment the cap leaves the wall, which is not what happens: close work is solid and
        /// stays solid, and the fade is the far half. At the middle of the reach a mark is a
        /// quarter of the way to this number, not half.
        ///
        /// Against LiveRange, so it is the tool's OWN reach: the extinguisher's four metres and
        /// the can's two and a half both fade across the whole of themselves rather than the
        /// can being faint everywhere because it was measured against a hose.
        /// </summary>
        public float OpacityFar = 0.6f;

        /// <summary>
        /// How solid a mark goes on from that far away.
        ///
        /// Nought is not a valid answer and never returned: a mark nobody can see is a decal
        /// slot spent on nothing, and the pool is the scarcest thing this mod has.
        /// </summary>
        public float InkAt(float away)
        {
            var near = Opacity;
            var far = OpacityFar;

            if (far <= 0f || far >= near) return near;

            var reach = LiveRange;
            if (reach <= 0.01f) return near;

            var t = away / reach;

            if (t <= 0f) return near;
            if (t >= 1f) return far;

            t = t * t;

            return near + (far - near) * t;
        }

        /// <summary>
        /// Whether police who SEE you tagging book you for it.
        ///
        /// One star, which in this game is the arrest: officers pursue and cuff, and only from
        /// two do they draw. Held at one for as long as a can is the worst thing in your hands,
        /// because a chase escalates on its own the moment you run and a tag should not become
        /// a firefight.
        ///
        /// Pull an actual gun in front of them and the cap comes off. What happens next is the
        /// game's business and it is not going to be an arrest.
        /// </summary>
        public bool CopsCare = true;

        /// <summary>
        /// Whether a swept stroke is drawn as STRETCHED decals rather than a chain of round
        /// ones. Reversible: false is exactly what the mod did before.
        ///
        /// THIS IS ABOUT THE POOL AND NOTHING ELSE. The game holds a few hundred to a couple of
        /// thousand decals in the whole world; a measured piece here came to 2,582 marks, which
        /// is three times the entire pool on a default Enhanced install. Nothing is lost when
        /// that happens -- the marks are saved and restored -- but only the nearest few hundred
        /// can be on a wall at once, so a big piece is always partly missing.
        ///
        /// The fix is not more slots, it is fewer marks for the same paint. ADD_DECAL takes a
        /// width AND a height, so one decal stretched along the path covers what a run of round
        /// ones did. The drips proved it: a run went from 42 decals to 1.
        ///
        /// The saving comes from each mark COVERING GROUND, so a streak has to replace the dabs
        /// rather than sit alongside them -- see Dab.
        /// </summary>
        public bool StrokeStreaks = true;

        /// <summary>
        /// How far the reticle must travel before a streak is laid, as a fraction of the mark.
        ///
        /// THE WHOLE TRADE IS HERE. Lower is more marks and a smoother line; higher is fewer
        /// marks and a longer stretch of the same texture. At 0.7 a stroke costs roughly a
        /// third of what it did.
        /// </summary>
        public float StreakStep = 0.7f;

        /// <summary>
        /// How long ONE streak may run, as a multiple of the mark, before it is cut.
        ///
        /// StreakStep above is now the SHORTEST a streak may be, not the length of every one.
        /// A stroke that stays straight keeps growing the same decal up to this, so a metre of
        /// clean line down the side of a shutter costs one mark instead of seven.
        ///
        /// FOUR IS A LOOK DECISION AS MUCH AS A BUDGET ONE. The texture is a splatter, and a
        /// splatter stretched four times reads as a drawn line; stretched twenty it reads as a
        /// smear. Push it in the ini if you want cheaper walls and can live with flatter paint.
        /// </summary>
        public float StreakLongest = 4f;

        /// <summary>
        /// How far the newest point may sit off the straight line before the streak is cut.
        ///
        /// A streak is a STRAIGHT quad, so a long one laid across a curve cuts the corner off
        /// it. This measures exactly that error -- the sideways distance from the line the quad
        /// would draw to where the hand actually is -- and cuts the streak when it grows past
        /// half a mark, which is inside the overlap the marks already have and so cannot show.
        ///
        /// It is why the length above can be generous: straight sweeps and long letter strokes
        /// take the whole of it, and the round of a letter gets cut short automatically.
        /// </summary>
        public float StreakBend = 0.45f;

        /// <summary>
        /// The longest a growing streak may hold paint back, whatever else is true.
        ///
        /// Nothing is drawn while a streak is still growing, so without this a slow hand on a
        /// long straight would see the paint arrive a second behind the reticle. A fifth of a
        /// second is under the threshold where a hand notices lag, and a fast sweep -- which is
        /// where the marks pile up -- still merges a dozen ticks into one decal.
        /// </summary>
        public int StreakHoldMs = 220;

        /// <summary>
        /// How long the spray may put nothing down before a plain round mark is laid anyway.
        ///
        /// Standing still, the reticle never travels, so nothing would ever meet the step above
        /// and holding the trigger on one spot would paint nothing at all. This is the floor
        /// that keeps a still hand working, and it is why the streaks cost nothing there.
        /// </summary>
        public int StreakIdleMs = 55;

        /// <summary>Which way round the stretch goes, if strokes come out square to the path.</summary>
        public bool StrokeSideways = false;

        /// <summary>
        /// Whether paint starts to run when you hold it on one spot.
        ///
        /// OFF. They were built, tried twice -- dotted, then solid as a single stretched
        /// decal -- and taken out. The machinery is all still here behind this one flag rather
        /// than deleted, because the stretched decal it taught us about is the interesting part
        /// and Marks.Streak now exists because of it.
        ///
        /// What they did, for whoever turns this back on: a second on one spot started a run,
        /// and a run was one decal 30mm by 220mm, taken down and put back longer ten times over
        /// so it visibly travelled. Never on anything within twelve degrees of level, because
        /// paint does not run down a pavement.
        /// </summary>
        public bool Drips = false;

        /// <summary>How long the spray has to stay on one spot before it starts to run.</summary>
        public int DripAfterMs = 1000;

        /// <summary>
        /// How far the reticle may wander and still count as the same spot.
        ///
        /// Generous rather than tight. A hand on a stick is never still, and a threshold that
        /// only a perfectly steady aim could hold is a feature nobody would ever see.
        /// </summary>
        public float DripArea = 0.28f;

        /// <summary>How often a running drip creeps further down.</summary>
        public int DripStepMs = 45;

        /// <summary>
        /// Which way round the stretch goes, for if the drip comes out lying on its side.
        ///
        /// ADD_DECAL is given a side vector and a width and a height, and which of the two the
        /// side vector governs is not written down anywhere R* left behind. False puts the long
        /// axis down the wall, which is the way round that should be right. If drips come out
        /// horizontal, this is the switch.
        /// </summary>
        public bool DripSideways = false;

        /// <summary>How long a run gets before it stops, and how many one spot will produce.</summary>
        public float DripLength = 0.22f;
        public int DripRuns = 3;

        /// <summary>
        /// How wide a drip is, against the spray mark that started it.
        ///
        /// Wider than it looks like it should be. A drip has to be solid, and solid costs a
        /// mark every half-width -- so a thin drip is not a cheaper drip, it is the same run
        /// drawn out of more, smaller marks.
        /// </summary>
        public float DripWidth = 0.55f;

        /// <summary>
        /// The decal type used when the thing hit is a VEHICLE, or 0 to use the same one as
        /// everything else.
        ///
        /// A CAR IS NOT A WALL as far as the decal system is concerned. splatters_paint goes on
        /// the map perfectly and appears to do nothing at all on a vehicle -- which is what got
        /// reported: paint that simply never showed up.
        ///
        /// The game clearly CAN mark a car, because bullet holes and blood land on one and stay
        /// there while it drives. Those are the weapImpact family, so that is what this tries.
        /// 4010 is weapImpact_metal, which is the one a car panel is.
        ///
        /// Unproven, which is why it is a setting and why the first one to land says so in the
        /// log either way. If none of them take, no decal type works on a vehicle and the
        /// honest fix is to stop probing them at all rather than to let the trigger do nothing.
        ///
        ///     4010  weapImpact_metal      a car panel
        ///     4020  weapImpact_concrete
        ///     4050  weapImpact_wood
        ///     1030  splatters_paint       what walls use, and what does not appear here
        /// </summary>
        public int VehicleDecal = 4010;

        /// <summary>
        /// Which decal texture the SPRAY CAN lays down. 0 means the same one the extinguisher
        /// uses, which is 1030 splatters_paint.
        ///
        /// The texture is what a mark actually looks like, and there is no way to author one
        /// from a script -- PATCH_DECAL_DIFFUSE_MAP exists but Rockstar never call it anywhere
        /// in their own scripts, so there is no valid dictionary and texture name to hand it.
        /// Choosing between the ones the game already ships is the whole of the control there
        /// is over the shape of a mark.
        ///
        /// Worth knowing before changing it: THE COLOUR ARGUMENTS MULTIPLY THE TEXTURE rather
        /// than replacing it. 1030 is authored pale, which is why an arbitrary colour comes out
        /// as that colour. Anything authored dark or strongly coloured tints everything toward
        /// itself -- mud is brown, so red over it is a rust and blue over it is a murk.
        ///
        ///     1030  splatters_paint   pale, speckled, takes colour honestly. The default.
        ///     1020  splatters_mud     bigger, wetter blobs. Fuller coverage, brown cast.
        ///     1040  splatters_water   faint. Barely marks a wall.
        ///     1010  splatters_blood   red, and it looks it.
        ///
        /// AND THE IMPACT FAMILY, which is what a bullet leaves in a wall.
        ///
        ///     4020  weapImpact_concrete   what a machine pistol writes on stucco
        ///     4010  weapImpact_metal      the same, on a panel
        ///     4050  weapImpact_wood
        ///
        /// These are worth knowing about for a reason that has nothing to do with guns. The
        /// splatter textures are SPLATTERS -- speckled, with holes in them -- and a wall covered
        /// in them still reads as a wall with something thrown at it. An impact mark is a small
        /// SOLID lump with a soft edge, which is very close to what a can actually leaves, and
        /// a hundred of them overlapping is a filled shape rather than a cloud of dots.
        ///
        /// THEY ARE NEARLY BLACK, and that is the whole cost. The colour arguments multiply, so
        /// a black texture stays black whatever you pass it -- an impact tag is a BLACK tag with
        /// a hint of your colour in the soft edge, not a green one. Which for an outline, a
        /// throw-up or anything that wants to read as solid is not a compromise; for a bright
        /// fill it is fatal. CanColourGain is the dial that might drag it back up, and might do
        /// nothing at all: see there.
        ///
        /// Marks remember which one they were placed with, so changing this leaves everything
        /// already on a wall alone.
        /// </summary>
        public int CanDecal = 1030;

        /// <summary>
        /// A SECOND texture, laid instead of the first every so often, and how often in a
        /// hundred marks.
        ///
        /// ONE TEXTURE REPEATED IS A PATTERN AND THE EYE FINDS IT. Every mark from a can is
        /// the same picture at a different size and angle, and across a whole wall that
        /// regularity is the thing that says "decal" -- the marks are varied but they are all
        /// varied in the same way.
        ///
        /// A second texture in the mix breaks it for almost nothing: nine marks in ten are the
        /// solid one and the tenth is a splatter, so the tag keeps the fill AND gets the
        /// speckle and the stray flecks that a can throws at the edge of a stroke. It is the
        /// same trick as the metallics -- variety between marks, not inside one.
        ///
        /// Zero for either turns it off, which is the default: the published look is one
        /// texture and this does not change it under anybody.
        /// </summary>
        public int MixDecal = 0;
        public int MixEvery = 0;

        /// <summary>
        /// How hard the can's colour is driven into its decal. 1 is a plain colour and is what
        /// everything has always passed.
        ///
        /// AN EXPERIMENT, AND HONESTLY LABELLED AS ONE. The colour arguments multiply the
        /// texture, so with a dark texture like mud every colour comes out dark and brown --
        /// multiply only goes downward and the texture is the ceiling. But the arguments are
        /// floats and nothing in the signature says they stop at 1. Per channel, a brown times
        /// (0, 2.6, 0) is a bright green, so IF the shader does not clamp, a dark texture can
        /// be driven back to colour.
        ///
        /// It may well clamp, in which case this does nothing at all and costs nothing to have
        /// found out. Rockstar never pass anything above 1 -- they pass 0.196, 0, 0 to darken
        /// blood -- so there is no example either way to read.
        ///
        /// Left at 1 by default because on the pale paint texture anything above 1 blows every
        /// colour out to white. Turn it up only alongside a dark CanDecal.
        /// </summary>
        public float CanColourGain = 1f;

        /// <summary>
        /// How hard the two metallics scatter, as a multiplier on their built-in sheen.
        ///
        /// 1 is as shipped. 0 turns chrome into flat grey paint and gold into flat mustard,
        /// which is what they would have been without any of this. Above about 1.6 the marks
        /// stop reading as one colour catching the light and start reading as somebody
        /// spraying at random, so that is roughly the useful ceiling.
        ///
        /// Only touches the two metallic swatches. Every other colour has a sheen of zero and
        /// nothing multiplied by zero cares what the multiplier is.
        /// </summary>
        public float MetallicShine = 1f;

        /// <summary>
        /// How much each mark varies in size, as a fraction either way.
        ///
        /// DENSITY WITHOUT MORE DECALS COMES FROM HERE. It was 0.15, so marks ranged from 85%
        /// to 115% of the size -- and a mark at 85% covers only 72% of the area of a full one,
        /// so roughly one in six was doing three-quarters of a job and leaving a thin patch
        /// where it landed.
        ///
        /// At 0.06 the coverage is even and the same number of decals reads noticeably fuller.
        /// It is not free of cost: variation is what stops a line looking stamped, so this is
        /// as low as it can go before the marks start looking identical.
        /// </summary>
        public float SizeJitter = 0.06f;

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

        /// <summary>
        /// The plume, widened with the cap.
        ///
        /// Gently -- a quarter of the cap's multiplier rather than all of it. The plume is what
        /// you aim by and a fat cap that fills the screen with mist is a fat cap you cannot see
        /// past. Enough that the tool in his hand looks like the tool that is painting.
        /// </summary>
        public float LiveJetScale => CanJetScale * (0.75f + 0.25f * Nozzle);
        public float CanJetScale = 0.42f;

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
