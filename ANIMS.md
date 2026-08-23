# Animations

Every dictionary and clip pair below was checked against the game's own dump of what
is actually in this build -- **20,179 dictionaries, 269,414 clips** -- before it was written here.

That check is the entire point of this file. `TASK_PLAY_ANIM` returns void: a clip name
that is not in its dictionary is accepted and nothing moves, with no error and no log
line. This codebase shipped **25 such pairs in one file** for months -- the whole kitchen
prep animation, for every drug -- and six more scattered elsewhere, and nobody could tell
because the failure is silent by design.

So: **do not add a row here that you have not checked.** The dump is at
`https://raw.githubusercontent.com/DurtyFree/gta-v-data-dumps/master/animDictsCompact.json`
and checking a pair against it takes about four lines of Python.

---

## 1. Playing one

```csharp
Function.Call(Hash.TASK_PLAY_ANIM, ped.Handle, dict, clip,
              8f,      // blend in delta   -- 1/8 = 0.125s
              -8f,     // blend out delta  -- negative by convention
              -1,      // duration in ms, -1 = no limit
              flags,   // see the table
              0f,      // START PHASE 0-1. NOT playback rate.
              false,   // bPhaseControlled
              0,       // IkFlags -- an INT
              false);  // bAllowOverrideCloneUpdate, MP only
```

### The last three arguments are not position locks

Every community header calls them `lockX`, `lockY`, `lockZ` and leaves their descriptions
blank. Those names were invented and copied for a decade. ScriptHookVDotNet's own source
names them properly: **`bPhaseControlled`, `IkFlags` (an int, not a bool), and
`bAllowOverrideCloneUpdate`**.

Passing `true, true, true` -- which this codebase did -- means:

| slot | you passed | what it did |
|---|---|---|
| `bPhaseControlled` | `true` | the clip's phase is driven externally by nobody, so it sits on frame 0 |
| `IkFlags` | `true` -> `1` | `AIK_DISABLE_LEG_IK` -- feet stop planting to the floor |
| `bAllowOverrideCloneUpdate` | `true` | nothing, in single player |

Use `false, 0, false`. `IkFlags = 8192` is worth knowing: it makes the game look for
`<clipname>_facial` in the same dictionary and play it on the face automatically.

### Blend deltas are not speeds

Blend time in seconds is `1.0 / delta`. So `8` is 0.125s (normal), `1000` is instant,
`2` is half a second. Positive blends in, negative blends out.

### The flag bitfield

| bit | value | name | what it does |
|---|---|---|---|
| 0 | 1 | `AF_LOOPING` | repeat |
| 1 | 2 | `AF_HOLD_LAST_FRAME` | end in the pose and stay there |
| 2 | 4 | `AF_REPOSITION_WHEN_FINISHED` | move the capsule to match the visual |
| 3 | 8 | `AF_NOT_INTERRUPTABLE` | events cannot break it |
| 4 | 16 | `AF_UPPERBODY` | upper body only |
| 5 | 32 | `AF_SECONDARY` | **runs in the secondary task slot** |
| 7 | 128 | `AF_ABORT_ON_PED_MOVEMENT` | ends if the ped tries to move |
| 9 | 512 | `AF_TURN_OFF_COLLISION` | |
| 10 | 1024 | `AF_OVERRIDE_PHYSICS` | no physics forces |
| 12 | 4096 | `AF_EXTRACT_INITIAL_OFFSET` | required for multi-ped synced anims |
| 17 | 131072 | `AF_FORCE_START` | starts even while falling or ragdolling |
| 18 | 262144 | `AF_USE_KINEMATIC_PHYSICS` | ped pushes others, is not pushed |
| 19 | 524288 | `AF_USE_MOVER_EXTRACTION` | capsule follows the clip's root motion |
| 20 | 1048576 | `AF_HIDE_WEAPON` | |

**The one to remember is 51** = `1|2|16|32` -- loop, hold, upper body, secondary. That is
the walk-and-gesture flag, and the `AF_SECONDARY` bit is what makes it work: it puts the
clip in the *second* task slot so a movement task in the first keeps running underneath.
Without it the animation replaces the walk.

The corollary bites later: **anything started with 32 needs `CLEAR_PED_SECONDARY_TASK`**.
`CLEAR_PED_TASKS` clears the primary tree and leaves it running, which is the whole
explanation for "looping anims won't let go of my ped".

The mod uses this in [PortRun.cs](src/Hoodrich/Missions/PortRun.cs) for the dock loaders:
the walk task is issued FIRST and the box-carry goes on top as a secondary. The other way
round and the walk replaces the box.

### Loading, and why the wait loop is not optional

`REQUEST_ANIM_DICT` is asynchronous. `HAS_ANIM_DICT_LOADED` on the next line is false for
a dictionary that needs streaming and true for one already resident -- so never assume
either, always poll:

```csharp
if (!Function.Call<bool>(Hash.DOES_ANIM_DICT_EXIST, dict)) return false;   // FIRST
if (Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, dict)) return true;
Function.Call(Hash.REQUEST_ANIM_DICT, dict);
// ...then poll with a timeout
```

`DOES_ANIM_DICT_EXIST` first, because requesting a dictionary that is not there never
completes and the wait loop spins until its timeout every single time. Note it validates
the DICTIONARY only -- a bad clip name in a good dictionary still fails silently, which is
exactly the trap this file exists to close.

### Stopping one

1. `STOP_ANIM_TASK(ped, dict, clip, -8f)` -- surgical, blends out. **The exit speed must be
   a real float.** Passing `0.0` or an integer causes a documented animation lockout: that
   clip cannot be played again until the ped is killed.
2. `CLEAR_PED_SECONDARY_TASK` then `CLEAR_PED_TASKS` -- the belt-and-braces cancel.
3. `CLEAR_PED_TASKS_IMMEDIATELY` -- always works, always looks bad. It is the single most
   reported cause of the T-pose flash.

### Props

`boneIndex` is **not** `boneId`. Pass a raw id and the prop silently attaches to the
ped's centre -- the classic bottle-inside-the-pelvis bug. Always `GET_PED_BONE_INDEX`.

| id | bone | use |
|---|---|---|
| 28422 | `PH_R_Hand` | **right-hand prop bone.** Props sit at zero offset |
| 60309 | `PH_L_Hand` | left-hand prop bone |
| 57005 | `SKEL_R_Hand` | the wrist JOINT -- a prop here sits beside the hand, not in it |
| 36029 | `IK_L_Hand` | an IK effector. Not an attachment point |

`PH_` bones are non-deforming helpers the animators put there specifically to hang props
on, which is why a prop attached to one needs no offset. This mod learned it the hard way:
the spray can was on `SKEL_R_Hand` with a hand-tuned offset and a -90 twist, which is also
why Rockstar's graffiti jet -- authored to come out of a can on `PH_R_Hand` -- sprayed
sideways.

### Failure modes

| symptom | cause |
|---|---|
| nothing happens, silently | dictionary not loaded, or the clip is not in it |
| nothing happens after a warp or a ragdoll | add `AF_FORCE_START` (131072) |
| T-pose flash | `CLEAR_PED_TASKS_IMMEDIATELY` |
| ped slides, root motion ignored | add `AF_USE_MOVER_EXTRACTION` (524288) |
| ped snaps back at the end | `AF_REPOSITION_WHEN_FINISHED` (4), if the clip has a mover node |
| feet float, legs wrong | `IkFlags` is 1 -- you passed `true` |
| frozen on frame 0 | `bPhaseControlled` is true and nothing is driving the phase |
| clip can never be played again | `STOP_ANIM_TASK` was given `0.0` or an int |
| prop inside the ped's body | raw bone id passed as bone index |

---

## 2. Scenarios

`TASK_START_SCENARIO_IN_PLACE(ped, name, timeToLeave, playEnterAnim)`.

**`timeToLeave`: 0 and -1 both mean forever.** A positive value is a duration in
milliseconds counted from the start of the main clip, not a number of repetitions.

**An unrecognised name does nothing, silently.** No exception, no log -- the ped just
stands there. Same failure shape as a bad clip name.

### The ones that never end on their own

This is the list worth having. These have **no exit clip in the game data**, so nothing
will ever transition them out -- they run until something kills the task. `WORLD_HUMAN_`
`CHEERING` is the worst of them: eight dictionaries, one clip each, called `base`. There
is literally nothing to transition into. It left Lamar clapping forever in this mod.

`WORLD_HUMAN_CHEERING`, `WORLD_HUMAN_YOGA` (no enter, exit or idle at all),
`WORLD_HUMAN_HUMAN_STATUE`, `WORLD_HUMAN_COP_IDLES`, `PROP_HUMAN_BUM_SHOPPING_CART`,
`CODE_HUMAN_CROSS_ROAD_WAIT`, `WORLD_HUMAN_AA_COFFEE`, `WORLD_HUMAN_AA_SMOKE`,
`WORLD_HUMAN_BUM_FREEWAY`, `WORLD_HUMAN_BUM_SLUMPED`, `WORLD_HUMAN_BUM_STANDING`,
`WORLD_HUMAN_BUM_WASH`, `WORLD_HUMAN_CAR_PARK_ATTENDANT`, `WORLD_HUMAN_CLIPBOARD`,
`WORLD_HUMAN_CONST_DRILL`, `WORLD_HUMAN_GARDENER_LEAF_BLOWER`, `WORLD_HUMAN_GOLF_PLAYER`,
`WORLD_HUMAN_HAMMERING`, `WORLD_HUMAN_HIKER_STANDING`, `WORLD_HUMAN_JANITOR`,
`WORLD_HUMAN_JOG`, `WORLD_HUMAN_JOG_STANDING`, `WORLD_HUMAN_MAID_CLEAN`,
`WORLD_HUMAN_MUSCLE_FLEX`, `WORLD_HUMAN_MUSCLE_FREE_WEIGHTS`, `WORLD_HUMAN_MUSICIAN`,
`WORLD_HUMAN_PARTYING`, `WORLD_HUMAN_POWER_WALKER`, `WORLD_HUMAN_SEAT_WALL_EATING`,
`WORLD_HUMAN_SMOKING_POT`, `WORLD_HUMAN_STAND_FIRE`, `WORLD_HUMAN_STAND_FISHING`,
`WORLD_HUMAN_STRIP_WATCH_STAND`, `WORLD_HUMAN_SUPERHERO`, `WORLD_HUMAN_TENNIS_PLAYER`,
`WORLD_HUMAN_TOURIST_MAP`, `WORLD_HUMAN_WELDING`, `PROP_HUMAN_SEAT_SEWING`,
`WORLD_HUMAN_INSPECT_STAND`, `WORLD_HUMAN_DRUG_FIELD_WORKERS_RAKE`,
`WORLD_HUMAN_DRUG_PROCESSORS_COKE`, `CODE_HUMAN_POLICE_CROWD_CONTROL`,
`CODE_HUMAN_POLICE_INVESTIGATE`

### Safe ones -- full enter and exit

`WORLD_HUMAN_SMOKING`, `WORLD_HUMAN_DRINKING`, `WORLD_HUMAN_LEANING`,
`WORLD_HUMAN_STAND_MOBILE`, `WORLD_HUMAN_STAND_IMPATIENT`, `WORLD_HUMAN_GUARD_STAND`,
`WORLD_HUMAN_GUARD_PATROL`, `WORLD_HUMAN_HANG_OUT_STREET`, `WORLD_HUMAN_BINOCULARS`,
`WORLD_HUMAN_SEAT_STEPS`, `WORLD_HUMAN_SEAT_WALL`, `WORLD_HUMAN_PICNIC`,
`WORLD_HUMAN_SUNBATHE`, `WORLD_HUMAN_PUSH_UPS`, `WORLD_HUMAN_SIT_UPS`,
`WORLD_HUMAN_PAPARAZZI`, `WORLD_HUMAN_SECURITY_SHINE_TORCH`, `WORLD_HUMAN_GARDENER_PLANT`,
`WORLD_HUMAN_TOURIST_MOBILE`, `WORLD_HUMAN_WINDOW_SHOP_BROWSE`,
`WORLD_HUMAN_MOBILE_FILM_SHOCKING`, `WORLD_HUMAN_DRUG_DEALER_HARD`,
`WORLD_HUMAN_PROSTITUTE_*`, `PROP_HUMAN_ATM`, `PROP_HUMAN_BBQ`, `PROP_HUMAN_BUM_BIN`,
`PROP_HUMAN_PARKING_METER`, `PROP_HUMAN_SEAT_CHAIR`, `PROP_HUMAN_SEAT_BAR`,
`CODE_HUMAN_COWER`

### Stopping one properly

```csharp
Function.Call(Hash.SET_PED_SHOULD_PLAY_NORMAL_SCENARIO_EXIT, ped.Handle);
ped.Task.ClearAll();          // the 'next script task' that triggers the exit
// wait ~500ms -- the exit clip takes several frames
if (Function.Call<bool>(Hash.IS_PED_USING_ANY_SCENARIO, ped.Handle))
    ped.Task.ClearAllImmediately();
```

`SET_PED_SHOULD_PLAY_NORMAL_SCENARIO_EXIT` arms the exit; the next script task fires it.
`CLEAR_PED_TASKS` on its own is unreliable for scenarios. There are also
`..._IMMEDIATE_SCENARIO_EXIT` and `..._FLEE_SCENARIO_EXIT`.

### Scenarios spawn props, and nothing cleans them up

`WORLD_HUMAN_HAMMERING` spawns `prop_tool_hammer`, smoking spawns a cigarette, and
**neither `ClearPedTasks` nor `ClearPedTasksImmediately` removes it.** Sweep for the model
near the ped and delete it yourself, including in the mod's `Aborted` handler, or players
find hammers and cigarettes littering the map after a reload.

### Blocking events does not make a scenario ped bulletproof

`SET_BLOCKING_OF_NON_TEMPORARY_EVENTS` stops them fleeing gunfire or starting combat. It
does **not** stop them reacting to being shot, or to **being walked into by the player** --
that last one is the one that breaks poses in practice.

### Model restrictions are real

These have no female clipset, so a female ped gets nothing or a fallback:
`WORLD_HUMAN_GUARD_STAND`, `WORLD_HUMAN_GUARD_PATROL`, `WORLD_HUMAN_HAMMERING`,
`WORLD_HUMAN_JANITOR`, `WORLD_HUMAN_MUSICIAN`, `WORLD_HUMAN_DRUG_DEALER_HARD`,
`WORLD_HUMAN_VEHICLE_MECHANIC`, `WORLD_HUMAN_WELDING`, `WORLD_HUMAN_CLIPBOARD`,
`WORLD_HUMAN_PUSH_UPS`, `WORLD_HUMAN_SIT_UPS`, `PROP_HUMAN_BBQ`,
`PROP_HUMAN_BUM_SHOPPING_CART`, and others. `PROP_HUMAN_SEAT_SEWING` is female-only.

The `_FACILITY`, `_CLUBHOUSE`, `_CASINO`, `_PRISON` and `_ARMY` variants have **no
animation dictionaries of their own** -- they are DLC-interior metadata over the base
scenario. Use the base name unless you are inside that interior.

---

## 3. Dictionaries by use

`LOOP` full body, ped rooted · `LOOP-UB` upper body, ped can still move ·
`ONCE` plays through · `ONCE-h` plays once and holds the pose

Source column: **named** a human labelled this pair in a curated config or a Rockstar
script · **rstar** traced to an actual Rockstar script call, so the context proves what it
depicts · **dict** read off the dictionary path, nobody labelled it -- the pair exists but
the description is inference.

### Idle and standing about

| dictionary | clip | what it looks like | loop | src |
|---|---|---|---|---|
| `amb@world_human_leaning@male@wall@back@foot_up@idle_a` | `idle_a` | Back to wall, one foot propped on it -- the corner lean | LOOP | named |
| `amb@world_human_leaning@male@wall@back@hands_together@idle_b` | `idle_e` | Back to wall, hands clasped in front | LOOP | named |
| `amb@world_human_leaning@female@wall@back@holding_elbow@idle_a` | `idle_a` | Wall lean holding own elbow | LOOP | named |
| `amb@world_human_leaning@male@wall@back@mobile@base` | `base` | Wall lean scrolling a phone | LOOP | named |
| `amb@world_human_leaning@male@wall@back@foot_up@base` | `base` | Wall lean with a foot up, base pose | LOOP | rstar |
| `amb@world_human_hang_out_street@female_arms_crossed@idle_a` | `idle_a` | Arms crossed, street loiter | LOOP-UB | named |
| `amb@world_human_hang_out_street@male_c@idle_a` | `idle_b` | Arms crossed, shifts weight | LOOP-UB | named |
| `amb@world_human_hang_out_street@male_a@base` | `base` | Street-corner hangout, base pose | LOOP | rstar |
| `amb@world_human_hang_out_street@male_b@base` | `base` | Street-corner hangout, second body | LOOP | rstar |
| `random@street_race` | `_car_b_lookout` | Arms crossed scanning around -- a lookout | LOOP-UB | named |
| `random@shop_gunstore` | `_idle` | Arms crossed behind a counter | LOOP-UB | named |
| `anim@heists@heist_corona@team_idles@male_a` | `idle` | Neutral standing idle, small shifts | LOOP | named |
| `anim@mp_corona_idles@male_c@idle_a` | `idle_a` | Relaxed standing idle | LOOP | named |
| `random@countrysiderobbery` | `idle_a` | Loose standing idle, shifty | LOOP | named |
| `amb@world_human_bum_standing@twitchy@idle_a` | `idle_c` | Twitchy, jittery -- a strung-out fiend | LOOP-UB | named |
| `mp_missheist_countrybank@nervous` | `nervous_idle` | Nervous shifting, looking about | LOOP-UB | named |
| `move_m@intimidation@cop@unarmed` | `idle` | Hands hovering at the belt, cop stance | LOOP-UB | named |
| `switch@trevor@scares_tramp` | `trev_scares_tramp_idle_tramp` | Slouched, chilled out | LOOP | named |
| `amb@world_human_drug_dealer_hard@male@base` | `base` | Street dealer posture, the hard variant | LOOP | dict |
| `amb@world_human_drug_dealer_hard@male@idle_a` | `idle_a` | Dealer fidget -- checking pockets, glancing | LOOP | dict |
| `amb@world_human_binoculars@male@idle_a` | `idle_a` | Scanning with binoculars | LOOP | dict |
| `amb@code_human_police_investigate@idle_a` | `idle_b` | Cop surveying a scene | LOOP | named |
| `oddjobs@taxi@gyn@cc@intro` | `f_impatient_b` | Impatient -- checks watch, sighs | LOOP | named |
| `missdocksshowoffcar@idle_a` | `idle_b_5` | Annoyed, arms out, exasperated | LOOP | named |

### Talking and conversation

| dictionary | clip | what it looks like | loop | src |
|---|---|---|---|---|
| `gestures@m@standing@casual` | `gesture_hello` | Casual greeting nod and hand | ONCE-h | named |
| `gestures@m@standing@casual` | `gesture_damn` | "Damn" -- a hand flick of disbelief | ONCE-h | named |
| `gestures@m@standing@casual` | `gesture_no_way` | "No way" -- both hands refuse | ONCE-h | named |
| `gestures@m@standing@casual` | `gesture_shrug_hard` | Big shrug, palms up | ONCE-h | named |
| `gestures@m@standing@casual` | `gesture_nod_yes_hard` | Emphatic agreeing nod | ONCE-h | dict |
| `gestures@m@standing@casual` | `gesture_come_here_hard` | Beckons somebody over, aggressively | ONCE-h | dict |
| `gestures@m@standing@casual` | `gesture_you_hard` | Points hard at the other person | ONCE-h | dict |
| `gestures@m@standing@casual` | `gesture_easy_now` | "Easy, easy" -- both palms down | ONCE-h | dict |
| `gestures@m@standing@casual` | `gesture_displeased` | Dismissive, unimpressed | ONCE-h | dict |
| `gestures@f@standing@casual` | `gesture_point` | Points ahead while talking | ONCE-h | named |
| `gestures@f@standing@casual` | `gesture_me_hard` | Jabs a thumb at own chest -- "me" | ONCE-h | named |
| `mp_player_int_upper_nod` | `mp_player_int_nod_no` | Slow "nah" head shake | LOOP-UB | named |
| `misscarsteal4@actor` | `actor_berating_loop` | Berating somebody, jabbing a finger | LOOP-UB | named |
| `anim@amb@casino@brawl@fights@argue@` | `arguement_loop_mp_m_brawler_01` | Full angry argument, chest out | LOOP-UB | named |
| `anim@amb@casino@brawl@fights@argue@` | `arguement_loop_mp_m_brawler_02` | Angry argument, second variant | LOOP-UB | named |
| `mini@prostitutestalk` | `street_argue_f_a` | Street-side argument | LOOP-UB | named |
| `mini@hookers_sp` | `idle_reject` | Waves somebody off, rejects them | LOOP-UB | named |
| `mini@triathlon` | `want_some_of_this` | "Come at me" -- arms spread wide | LOOP-UB | named |
| `mp_ped_interaction` | `handshake_guy_a` | Handshake, initiator half | LOOP-UB | named |
| `mp_ped_interaction` | `handshake_guy_b` | Handshake, receiver half | LOOP-UB | named |
| `mp_ped_interaction` | `hugs_guy_a` | Bro-hug / dap-and-pull-in, initiator | ONCE | named |
| `mp_ped_interaction` | `hugs_guy_b` | Bro-hug, receiver | ONCE | named |
| `mp_player_int_uppergang_sign_a` | `mp_player_int_gang_sign_a` | Throws a gang sign | LOOP-UB | named |
| `mp_player_int_uppergang_sign_b` | `mp_player_int_gang_sign_b` | Second gang sign | LOOP-UB | named |
| `cellphone@` | `cellphone_call_listen_base` | Phone to ear, listening | LOOP-UB | named |
| `cellphone@` | `cellphone_text_read_base` | Looking down at a phone screen | LOOP-UB | named |
| `random@kidnap_girl` | `ig_1_girl_on_phone_loop` | Animated phone call, pacing | LOOP-UB | named |
| `amb@world_human_stand_mobile@male@text@exit` | `exit` | Puts the phone away into a pocket | ONCE-h | named |
| `random@arrests` | `generic_radio_chatter` | Talks into a shoulder radio | LOOP-UB | named |
| `friends@fra@ig_1` | `over_here_idle_a` | Waves somebody over | LOOP-UB | named |
| `misslamar1ig_20` | `Lamar_Call_Hurry_A` | Lamar waving you on, hurrying you up | ONCE | rstar |
| `missarmenian2lamar_idles` | `idle_a` | Lamar standing idle | LOOP | rstar |
| `missarmenian2` | `lamar_texting` | Lamar texting on his phone | LOOP-UB | rstar |

### Deals and hand-offs

| dictionary | clip | what it looks like | loop | src |
|---|---|---|---|---|
| `mp_common` | `givetake1_a` | Hands something over -- initiator half | LOOP-UB | named |
| `mp_common` | `givetake1_b` | Receives something -- pair with the above | LOOP-UB | named |
| `mp_common` | `givetake2_a` | Second give/take pair, different posture | LOOP-UB | dict |
| `mp_common` | `givetake2_b` | Second give/take, receiver | LOOP-UB | dict |
| `random@mugging5` | `ig_1_guy_handoff` | Handing something over under duress | ONCE | rstar |
| `random@mugging5` | `ig_2_guy_handoff` | Hand-off, second actor | ONCE | rstar |
| `anim@amb@business@cfm@cfm_counting_notes@` | `note_counting_binmoney` | Counting a stack of notes, head down | LOOP | dict |
| `anim@amb@business@cfm@cfm_counting_notes@` | `lookaround_counting_binmoney` | Counting cash while glancing around | LOOP | dict |
| `anim@mp_player_intupperraining_cash` | `idle_a` | Throwing cash in the air | LOOP-UB | named |
| `anim@heists@ornate_bank@grab_cash` | `grab` | Grabs and stuffs cash | ONCE | dict |
| `anim@heists@narcotics@trash` | `pickup` | Bends and picks a bag off the ground | ONCE | dict |
| `anim@heists@narcotics@trash` | `drop` | Puts a bag down -- a dead drop | ONCE | dict |
| `anim@heists@narcotics@trash` | `idle` | Standing holding a bag at the side | LOOP | named |
| `anim@heists@narcotics@trash` | `walk` | Walking while holding a bag | LOOP | dict |
| `random@domestic` | `pickup_low` | Picks something up from low down | ONCE | named |
| `anim@heists@humane_labs@finale@keycards` | `ped_a_enter_loop` | Holds a small item up, inspecting it | LOOP-UB | named |
| `move_weapon@jerrycan@generic` | `idle` | Carries a bag or case one-handed at the side | LOOP-UB | named |
| `anim@heists@box_carry@` | `idle` | Carries a box two-handed at chest height | LOOP-UB | named |
| `anim@heists@box_carry@` | `walk` | Walking while carrying the box | LOOP | dict |
| `missheistdockssetup1clipboard@base` | `base` | Holds a clipboard, checking it | LOOP-UB | named |
| `missfam5_yoga` | `a2_pose` | Arms out, being frisked | LOOP-UB | named |

### Smoking, drinking, using

| dictionary | clip | what it looks like | loop | src |
|---|---|---|---|---|
| `amb@world_human_smoking@male@male_a@enter` | `enter` | Raises hand to mouth and takes a drag | LOOP-UB | named |
| `amb@world_human_smoking@male@male_a@idle_a` | `idle_a` | Standing smoking idle | LOOP | dict |
| `amb@world_human_aa_smoke@male@idle_a` | `idle_c` | Smoking, arm across the body | LOOP-UB | named |
| `amb@world_human_aa_smoke@male@idle_a` | `idle_b` | Smoking, other variant | LOOP-UB | named |
| `amb@world_human_smoking@female@idle_a` | `idle_b` | Female standing smoking | LOOP-UB | named |
| `anim@amb@carmeet@checkout_car@` | `smoke_male_a_idle_b` | Smoking while eyeing a car up | LOOP-UB | named |
| `amb@world_human_smoking@male@male_b@base` | `base` | Smoking or vaping, base pose | LOOP-UB | named |
| `timetable@gardener@smoking_joint` | `smoke_idle` | Smoking a joint, relaxed | LOOP | dict |
| `timetable@gardener@smoking_joint` | `idle_cough` | Coughs after a hit | LOOP-UB | named |
| `anim@safehouse@bong` | `bong_stage3` | A bong hit | LOOP-UB | named |
| `amb@world_human_drinking@beer@male@idle_a` | `idle_a` | Swigs from a bottle | LOOP-UB | named |
| `amb@world_human_drinking@beer@male@idle_a` | `idle_c` | Drinks from a bottle, other variant | LOOP-UB | named |
| `amb@world_human_drinking@beer@male@idle_a` | `idle_b` | Tips a bottle out -- pour one out | LOOP-UB | named |
| `amb@world_human_drinking@coffee@male@idle_a` | `idle_c` | Drinks from a cup or can | LOOP-UB | named |
| `amb@code_human_wander_drinking@male@base` | `static` | Walks around drinking from a cup | LOOP-UB | named |
| `mp_player_intdrink` | `loop_bottle` | Drinks from a bottle | LOOP-UB | named |
| `mini@drinking` | `shots_barman_a` | Pours a shot | LOOP-UB | named |
| `random@drunk_driver_1` | `drunk_driver_stand_loop_dd1` | Drunk swaying stand | LOOP | named |
| `random@drunk_driver_1` | `drunk_driver_stand_loop_dd2` | Drunk sway, second variant | LOOP | named |
| `missarmenian2` | `standing_idle_loop_drunk` | Heavily drunk standing sway | LOOP | rstar |
| `missarmenian2` | `drunk_loop` | Passed out on the ground | LOOP | rstar |
| `random@drunk_driver_1` | `drunk_fall_over` | Falls over drunk | ONCE | named |
| `timetable@trevor@smoking_meth@base` | `base` | Smoking meth | LOOP | dict |
| `switch@trevor@trev_smoking_meth` | `trev_smoking_meth_loop` | Trevor smoking meth, the switch scene | LOOP | rstar |

### Production -- cooking, cutting, bagging

| dictionary | clip | what it looks like | loop | src |
|---|---|---|---|---|
| `anim@amb@business@coc@coc_unpack_cut@` | `fullcut_cycle_v1_cokecutter` | Cutting coke at a table -- the human track is cokecutter | LOOP | dict |
| `anim@amb@business@coc@coc_unpack_cut@` | `fullcut_cycle_v1_cokepacker` | Bagging it once cut | LOOP | dict |
| `anim@amb@business@coc@coc_packing_hi@` | `full_cycle_v1_pressoperator` | Pressing bricks -- the human track is pressoperator, NOT packer | LOOP | dict |
| `anim@amb@business@coc@coc_packing@` | `base_pressoperator` | At the press, base pose | LOOP | dict |
| `anim@amb@business@meth@meth_smash_weight_check@` | `break_weigh_char01` | Breaking meth up and weighing it | LOOP | dict |
| `anim@amb@business@meth@meth_monitoring_cooking@cooking@` | `chemical_pour_long_cooker` | Pouring chemicals into the cooker | LOOP | dict |
| `anim@amb@business@meth@meth_monitoring_cooking@monitoring@` | `base_idle_guage_monitor` | Watching the gauges | LOOP | dict |
| `anim@amb@business@weed@weed_sorting_seated@` | `base_sorter_left_sorter01` | Seated sorting buds -- human track is sorter01 | LOOP | dict |
| `anim@amb@business@weed@weed_inspecting_lo_med_hi@` | `weed_stand_base_inspector` | Standing inspecting a crop | LOOP | dict |
| `anim@amb@business@weed@weed_inspecting_high_dry@` | `weed_inspecting_high_base_inspector` | Inspecting drying weed up a ladder | LOOP | dict |
| `anim@amb@clubhouse@tutorial@bkr_tut_ig3@` | `machinic_loop_mechandplayer` | Bench work | LOOP | dict |
| `anim@amb@business@meth@meth_monitoring_cooking@cooking@` | `base_idle_tank_cooker` | Standing over the cooker | LOOP | dict |
| `anim@amb@business@meth@meth_smash_weight_check@` | `break_weigh_hammer` | Breaking up meth with a hammer | LOOP | dict |
| `anim@amb@business@meth@meth_smash_weight_check@` | `break_weigh_scale` | The scale, during the weigh | LOOP | dict |
| `anim@amb@drug_processors@coke@female_a@idles` | `idle_a` | Cutting coke at a processing table | LOOP | dict |
| `anim@amb@drug_processors@weed@male_a@idles` | `idle_a` | Weed processing table worker | LOOP | dict |
| `anim@amb@drug_processors@weed@male_a@react_shock` | `shock_front` | Directional shock -- reacts to a raid | ONCE | dict |
| `anim@amb@drug_processors@weed@male_a@react_cower` | `cower_front_enter` | Cowers as the door goes in | ONCE | dict |
| `anim@amb@drug_field_workers@weeding@male_a@base` | `base` | Tending a crop, crouched | LOOP | dict |
| `anim@amb@drug_field_workers@rake@male_a@base` | `base` | Raking, two-handed | LOOP | named |

### Robbery and stick-ups

| dictionary | clip | what it looks like | loop | src |
|---|---|---|---|---|
| `oddjobs@shop_robbery@rob_till` | `enter` | Reaching into a shop till -- the approach | ONCE | rstar |
| `oddjobs@shop_robbery@rob_till` | `loop` | Emptying the till | LOOP | rstar |
| `oddjobs@shop_robbery@rob_till` | `exit` | Stepping back from the till | ONCE | rstar |
| `mp_am_hold_up` | `handsup_enter` | Shopkeeper putting hands up | ONCE | rstar |
| `mp_am_hold_up` | `handsup_base` | Hands up, held | LOOP | rstar |
| `mp_am_hold_up` | `handsup_exit` | Lowering hands again | ONCE | rstar |
| `mp_am_hold_up` | `cower_intro` | A customer starting to cower | ONCE | rstar |
| `mp_am_hold_up` | `cower_loop` | Customer cowering | LOOP | rstar |
| `mp_am_hold_up` | `wary_loop` | A wary shopkeeper watching you | LOOP | rstar |
| `random@shop_robbery` | `robbery_intro_loop_a` | Robbery random event, actor A intro | LOOP | rstar |
| `random@robbery` | `f_cower_01` | Bystander cowering | LOOP | rstar |
| `random@robbery` | `f_distressed_loop` | Distressed, hands to face | LOOP-UB | named |
| `random@countryside_gang_fight` | `biker_02_stickup_loop` | Holding somebody at gunpoint | LOOP-UB | named |
| `random@countryside_gang_fight` | `gangmember_stickup_loop` | Gang member holding a stick-up | LOOP-UB | rstar |
| `random@atmrobberygen` | `b_atm_mugging` | Mugging somebody at an ATM | LOOP-UB | named |

### Police, arrest, surrender

| dictionary | clip | what it looks like | loop | src |
|---|---|---|---|---|
| `random@arrests` | `idle_2_hands_up` | Raising hands from idle | ONCE | rstar |
| `random@arrests` | `kneeling_arrest_idle` | Kneeling, hands behind the head | LOOP | rstar |
| `random@arrests` | `kneeling_arrest_get_up` | Getting up from the kneeling arrest | ONCE | named |
| `random@arrests` | `kneeling_arrest_escape` | Breaking away from the arrest and running | ONCE | dict |
| `random@arrests@busted` | `idle_a` | Busted -- hands behind head, defeated | LOOP | named |
| `mp_arresting` | `idle` | Cuffed behind the back, can still walk | LOOP-UB | named |
| `mp_arresting` | `a_uncuff` | Breaking free of cuffs | ONCE-h | named |
| `anim@move_m@prisoner_cuffed` | `idle` | Cuffed at the front, can still walk | LOOP-UB | named |
| `missminuteman_1ig_2` | `handsup_base` | Hands up in surrender | LOOP-UB | named |
| `anim@mp_player_intuppersurrender` | `idle_a_fp` | Hands up, interaction-menu version | LOOP-UB | named |
| `mp_bank_heist_1` | `m_cower_01` | Cowering hostage | LOOP | named |
| `amb@code_human_police_crowd_control@idle_a` | `idle_a` | Cop holding a line back | LOOP | rstar |
| `amb@world_human_cop_idles@male@base` | `base` | Officer standing around | LOOP | rstar |
| `stungun@standing` | `damage` | Tased -- convulsing while upright | LOOP | named |

### Reactions

| dictionary | clip | what it looks like | loop | src |
|---|---|---|---|---|
| `random@dealgonewrong` | `idle_a` | The aftermath of a deal gone wrong | LOOP | rstar |
| `misscarsteal2car_stolen` | `chad_car_stolen_reaction` | Devastated -- hands on head, disbelief | ONCE | named |
| `random@car_thief@agitated@idle_a` | `agitated_idle_a` | Agitated facepalm and head-rub | LOOP-UB | named |
| `anim@mp_player_intupperface_palm` | `idle_a` | A clean facepalm | LOOP-UB | named |
| `anim@arena@celeb@flat@paired@no_props@` | `laugh_a_player_b` | Laughing at somebody, doubled over | LOOP | named |
| `anim@mp_player_intcelebrationfemale@slow_clap` | `slow_clap` | Sarcastic slow clap | LOOP-UB | named |
| `anim@mp_player_intcelebrationmale@slow_clap` | `slow_clap` | Slow clap | LOOP-UB | named |
| `anim@arena@celeb@flat@solo@no_props@` | `angry_clap_a_player_a` | Angry, mocking clap | LOOP | named |
| `rcmfanatic1celebrate` | `celebrate` | Celebrating -- fists pumping | LOOP | named |
| `anim@mp_player_intcelebrationmale@respect` | `respect` | "Respect" -- fist to chest | LOOP-UB | named |
| `anim@mp_player_intcelebrationmale@cut_throat` | `cut_throat` | Throat-slit gesture -- a death threat | ONCE | named |
| `anim@mp_player_intcelebrationfemale@bang_bang` | `bang_bang` | Finger-gun "bang bang" | ONCE | named |
| `anim@mp_player_intcelebrationfemale@knuckle_crunch` | `knuckle_crunch` | Cracks knuckles menacingly | LOOP-UB | named |
| `taxi_hail` | `hail_taxi` | Whistles and waves an arm | LOOP-UB | named |
| `rcmnigel1c` | `hailing_whistle_waive_a` | Two-finger whistle | LOOP-UB | named |
| `random@dealgonewrong` | `base` | Standing at the scene of it | LOOP | rstar |
| `misscarsteal4@actor` | `stumble` | Stumbling, disoriented | LOOP | named |

### Fighting and intimidation

| dictionary | clip | what it looks like | loop | src |
|---|---|---|---|---|
| `anim@deathmatch_intros@unarmed` | `intro_male_unarmed_c` | Squares up, fists raised | ONCE | named |
| `anim@deathmatch_intros@unarmed` | `intro_male_unarmed_e` | Squares up, second variant | ONCE | named |
| `melee@unarmed@streamed_core_fps` | `idle` | Boxing guard stance, held | LOOP | named |
| `rcmextreme2` | `loop_punching` | Repeatedly punching somebody | LOOP-UB | named |
| `melee@unarmed@streamed_variations` | `plyr_takedown_front_slap` | Slaps somebody -- attacker half | LOOP-UB | named |
| `melee@unarmed@streamed_variations` | `victim_takedown_front_slap` | Gets slapped -- victim half | ONCE | named |
| `melee@unarmed@streamed_variations` | `plyr_takedown_front_headbutt` | Headbutts somebody | ONCE | named |
| `melee@unarmed@streamed_variations` | `plyr_takedown_rear_lefthook` | Sucker-punch from behind | ONCE | named |
| `missfinale_c2ig_11` | `pushcar_offcliff_m` | Shoving forward with both hands | LOOP | named |
| `missheistdockssetup1ig_13@kick_idle` | `guard_beatup_kickidle_guard1` | Kicking somebody on the ground | LOOP | named |
| `mp_player_int_upperfinger` | `mp_player_int_finger_01` | Flips the bird | LOOP-UB | named |
| `switch@franklin@gang_taunt_p1` | `gang_taunt_loop_franklin` | Franklin being taunted -- his half | LOOP | rstar |
| `switch@franklin@gang_taunt_p1` | `gang_taunt_loop_thug_01` | The thug doing the taunting | LOOP | rstar |
| `switch@franklin@gang_taunt_p1` | `gang_taunt_loop_thug_02` | Second thug | LOOP | rstar |

### Work and manual

| dictionary | clip | what it looks like | loop | src |
|---|---|---|---|---|
| `mini@repair` | `fixing_a_ped` | Leaning into an engine bay, wrenching | LOOP | named |
| `amb@world_human_vehicle_mechanic@male@base` | `base` | Working under a bonnet | LOOP | named |
| `amb@world_human_hammering@male@base` | `base` | Hammering | LOOP-UB | named |
| `anim@heists@fleeca_bank@drilling` | `drill_straight_end` | Drilling into something in front | LOOP-UB | named |
| `random@burial` | `a_burial` | Digging with a shovel | LOOP | named |
| `timetable@floyd@clean_kitchen@base` | `base` | Wiping down a surface | LOOP-UB | named |
| `anim@move_f@waitress` | `idle` | Carries a tray flat on one hand, can walk | LOOP-UB | named |
| `weapons@misc@jerrycan@` | `fire` | Pouring from a jerrycan | LOOP-UB | named |
| `missheistfbi3b_ig7` | `lift_fibagent_loop` | Lifting something heavy | LOOP | named |
| `anim@veh@van@mule5@rds` | `lean_back_idle` | Crouched, reaching into a van | LOOP | named |
| `amb@code_human_wander_clipboard@male@base` | `static` | Walking while checking a clipboard | LOOP-UB | dict |
| `anim@amb@carmeet@checkout_engine@male_a@idles` | `idle_a` | Leaning over an open bonnet | LOOP | rstar |

### In and around cars

| dictionary | clip | what it looks like | loop | src |
|---|---|---|---|---|
| `switch@michael@sitting_on_car_bonnet` | `sitting_on_car_bonnet_loop` | Sitting on a car bonnet | LOOP | rstar |
| `switch@michael@stuckintraffic` | `StuckInTraffic_HitHorn` | Punching the horn in traffic | ONCE | rstar |
| `switch@franklin@cleaning_car` | `001946_01_GC_FRAS_V2_IG_5_BASE` | Franklin washing his car | LOOP | rstar |
| `missarmenian2` | `car_react_gang_ds` | Reacting to a gang from the driver's seat | LOOP-UB | rstar |
| `missarmenian2` | `car_react_gang_ps` | Same, passenger seat | LOOP-UB | rstar |
| `random@hitch_lift` | `carjack_mainloop_female` | A carjacking in progress | LOOP | rstar |
| `random@car_thief@waiting_ig_4` | `idle_a` | Car thief loitering, waiting | LOOP | rstar |

### Tagging

| dictionary | clip | what it looks like | loop | src |
|---|---|---|---|---|
| `anim@scripted@freemode@postertag@graffiti_spray@male@` | `intro_male` | Steps up to the wall | ONCE | named |
| `anim@scripted@freemode@postertag@graffiti_spray@male@` | `shake_can_male` | Shakes the can | ONCE | named |
| `anim@scripted@freemode@postertag@graffiti_spray@male@` | `shake_can_idle_male` | Shaking, held | LOOP | named |
| `anim@scripted@freemode@postertag@graffiti_spray@male@` | `spray_can_male` | Spraying the wall | LOOP | named |
| `anim@scripted@freemode@postertag@graffiti_spray@male@` | `spray_can_idle_male` | Spraying, held | LOOP | named |
| `anim@scripted@freemode@postertag@graffiti_spray@male@` | `exit_male` | Steps back off it | ONCE | named |
| `anim@scripted@freemode@postertag@graffiti_spray@male@` | `quick_exit_male` | Breaks off in a hurry | ONCE | named |
| `anim@scripted@freemode@tagcoll_ig_postertag@male@` | `postertag` | The Cayo poster tag, one shot | ONCE | rstar |
| `switch@franklin@lamar_tagging_wall` | `lamar_tagging_wall_loop_lamar` | The man painting | LOOP | rstar |
| `switch@franklin@lamar_tagging_wall` | `lamar_tagging_wall_exit_lamar` | Stepping back off the wall | ONCE | rstar |
| `switch@franklin@lamar_tagging_wall` | `lamar_tagging_wall_loop_franklin` | The man watching him paint | LOOP | rstar |

---

## 4. What Hoodrich plays today

Pulled out of the source, not remembered. 40 dictionaries.

| dictionary | exists? | first use |
|---|---|---|
| `amb@code_human_police_investigate@idle_a` | yes | src/Hoodrich/Territory/StopSearch.cs:36 |
| `amb@code_human_police_investigate@idle_b` | yes | src/Hoodrich/Territory/StopSearch.cs:37 |
| `amb@world_human_hammering@male@base` | yes | src/Hoodrich/Economy/PrepAnimation.cs:88 |
| `amb@world_human_janitor@male@idle_a` | yes | src/Hoodrich/Missions/TagRun.cs:142 |
| `amb@world_human_window_shop@male@idle_a` | yes | src/Hoodrich/Missions/TagRun.cs:143 |
| `anim@amb@business@coc@coc_packing@` | yes | src/Hoodrich/Economy/PrepAnimation.cs:78 |
| `anim@amb@business@coc@coc_packing_hi@` | yes | src/Hoodrich/Economy/PrepAnimation.cs:77 |
| `anim@amb@business@coc@coc_unpack_cut@` | yes | src/Hoodrich/Economy/PrepAnimation.cs:73 |
| `anim@amb@business@meth@meth_monitoring_cooking@cooking@` | yes | src/Hoodrich/Economy/PrepAnimation.cs:126 |
| `anim@amb@business@meth@meth_monitoring_cooking@monitoring@` | yes | src/Hoodrich/Economy/PrepAnimation.cs:132 |
| `anim@amb@business@meth@meth_smash_weight_check@` | yes | src/Hoodrich/Economy/PrepAnimation.cs:81 |
| `anim@amb@business@weed@weed_inspecting_high_dry@` | yes | src/Hoodrich/Economy/PrepAnimation.cs:114 |
| `anim@amb@business@weed@weed_inspecting_lo_med_hi@` | yes | src/Hoodrich/Economy/PrepAnimation.cs:113 |
| `anim@amb@business@weed@weed_sorting_seated@` | yes | src/Hoodrich/Economy/PrepAnimation.cs:84 |
| `anim@amb@clubhouse@tutorial@bkr_tut_ig3@` | yes | src/Hoodrich/Economy/PrepAnimation.cs:87 |
| `anim@amb@nightclub@dancers@crowddance_facedj_11_amy@` | **NO** | src/Hoodrich/Main.cs:223 |
| `anim@amb@nightclub@dancers@crowddance_groups@hi_intensity@` | **NO** | src/Hoodrich/Main.cs:238 |
| `anim@amb@nightclub@djs@black_madonna@` | yes | src/Hoodrich/Main.cs:246 |
| `anim@amb@nightclub@djs@dixon@` | yes | src/Hoodrich/Main.cs:245 |
| `anim@amb@nightclub@djs@solomun@` | yes | src/Hoodrich/Main.cs:247 |
| `anim@amb@nightclub@djs@tale_of_us@` | yes | src/Hoodrich/Main.cs:248 |
| `anim@amb@nightclub@mini@dance@dance_solo@female@var_a@` | yes | src/Hoodrich/Main.cs:221 |
| `anim@amb@nightclub@mini@dance@dance_solo@female@var_b@` | yes | src/Hoodrich/Main.cs:222 |
| `anim@amb@nightclub@mini@dance@dance_solo@male@var_a@` | yes | src/Hoodrich/Main.cs:236 |
| `anim@amb@nightclub@mini@dance@dance_solo@male@var_b@` | yes | src/Hoodrich/Main.cs:237 |
| `anim@heists@box_carry@` | yes | src/Hoodrich/Missions/PortRun.cs:1774 |
| `anim@heists@narcotics@trash` | yes | src/Hoodrich/Supply/Delivery.cs:148 |
| `anim@heists@prison_heiststation@cop_reactions` | yes | src/Hoodrich/Economy/PrepAnimation.cs:157 |
| `anim@scripted@freemode@postertag@graffiti_spray@heeled@` | yes | src/Hoodrich/Missions/TagRun.cs:138 |
| `anim@scripted@freemode@postertag@graffiti_spray@male@` | yes | src/Hoodrich/Missions/TagRun.cs:174 |
| `cellphone@` | yes | src/Hoodrich/Supply/Delivery.cs:543 |
| `mini@strip_club@idle_dance@idle_a` | **NO** | src/Hoodrich/Main.cs:224 |
| `mini@strip_club@private_dance@part1` | yes | src/Hoodrich/Main.cs:293 |
| `mini@strip_club@private_dance@part2` | yes | src/Hoodrich/Main.cs:294 |
| `mini@strip_club@private_dance@part3` | yes | src/Hoodrich/Main.cs:295 |
| `move_m@drunk@moderatedrunk` | yes | src/Hoodrich/Supply/Delivery.cs:1747 |
| `random@arrests` | yes | src/Hoodrich/Territory/StopSearch.cs:35 |
| `switch@franklin@lamar_tagging_wall` | yes | src/Hoodrich/Missions/TagRun.cs:164 |
| `timetable@floyd@clean_kitchen@base` | yes | src/Hoodrich/Economy/PrepAnimation.cs:97 |
| `timetable@maid@ig_2@` | yes | src/Hoodrich/Economy/PrepAnimation.cs:91 |

### Scenarios in use

| scenario | uses | ends on its own? |
|---|---|---|
| `WORLD_HUMAN_DRINKING` | 15 | yes |
| `WORLD_HUMAN_SMOKING` | 13 | yes |
| `WORLD_HUMAN_STAND_MOBILE` | 11 | yes |
| `WORLD_HUMAN_GUARD_STAND` | 10 | yes |
| `WORLD_HUMAN_STAND_IMPATIENT` | 10 | yes |
| `WORLD_HUMAN_SMOKING_POT` | 5 | no -- needs clearing |
| `WORLD_HUMAN_PARTYING` | 5 | no -- needs clearing |
| `WORLD_HUMAN_LEANING` | 2 | yes |
| `WORLD_HUMAN_MUSICIAN` | 2 | no -- needs clearing |
| `WORLD_HUMAN_HANG_OUT_STREET` | 2 | yes |
| `WORLD_HUMAN_PROSTITUTE_HIGH_CLASS` | 1 | yes |
| `WORLD_HUMAN_BUM_STANDING` | 1 | no -- needs clearing |
| `WORLD_HUMAN_DRUG_DEALER` | 1 | yes |
| `WORLD_HUMAN_STAND_MOBILE_UPRIGHT` | 1 | yes |
| `WORLD_HUMAN_AA_SMOKE` | 1 | no -- needs clearing |
| `WORLD_HUMAN_GUARD_PATROL` | 1 | yes |
| `WORLD_HUMAN_HAMMERING` | 1 | no -- needs clearing |
| `CODE_HUMAN_POLICE_INVESTIGATE` | 1 | no -- needs clearing |
