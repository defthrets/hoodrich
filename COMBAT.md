I have ground truth from the game binary itself. Writing up.

**EVIDENCE TIERS** — read this first, it determines how much you can trust each answer below.

- **TIER A (binary-verified)**: extracted from GTA V's own RAGE parser metadata dumped out of the shipped executable (build 3407), via `alexguirre/rage-parser-dumps`. Enum member names + their integer values come straight from the game's reflection data. This is not folklore — it is the game telling you the mapping. Local copies: `C:\Users\mmidd\AppData\Local\Temp\claude\C--\d0e87be3-ae62-4780-9819-589c97f1af92\scratchpad\b3407.json` and `...\scratchpad\dictionary.txt`.
- **TIER B (R\* script corpus)**: 736 decompiled Rockstar `.ysc` scripts (build 757) with resolved native names, cloned to `C:\Users\mmidd\AppData\Local\Temp\claude\C--\d0e87be3-ae62-4780-9819-589c97f1af92\scratchpad\br`. Real call sites, real orderings, ~3100 `SET_PED_COMBAT_ATTRIBUTES` calls. Behavioural inference, but from the people who wrote the AI.
- **TIER C (community docs)**: FiveM/citizenfx natives repo. Signatures generally reliable; prose comments frequently wrong.
- **TIER D (folklore)**: RAGE-MP wiki `BF_` index list and its copies. **Demonstrably wrong.** Details below.

---

## 1. SET_PED_COMBAT_MOVEMENT — your guess is CORRECT, the popular docs are WRONG

**TIER A.** Enum `CCombatData__Movement`, dumped from the exe:

```
0  CM_Stationary
1  CM_Defensive
2  CM_WillAdvance
3  CM_WillRetreat
```

`void SET_PED_COMBAT_MOVEMENT(Ped ped, int combatMovement)` — `0x4D9CA1009AFBD057`. Readback exists: `GET_PED_COMBAT_MOVEMENT` (`0xDEA92412FCAEB3F5`) returns the int, so you can assert your setting stuck.

**Confirm/correct:** `0=Stationary, 1=Defensive, 2=WillAdvance, 3=WillRetreat` is exactly right. The FiveM comment that everyone copies — "2 - Offensive (will charge but take cover), 3 - Suicidal Offensive (will try to flank enemy in a suicidal attack)" — is TIER D. Value 2 does advance so the gist is right, but **3 is WillRetreat, not a suicide-flank**. Don't reach for 3 expecting aggression.

**Which value holds ground and uses cover: `1` (CM_Defensive).** Corroborating TIER A: attribute 47 is named `BF_EnableTacticalPointsWhenDefensive`, and 44 is `BF_SwitchToDefensiveIfInCover` — both only make sense if Defensive is the cover-hugging mode. TIER B corroboration: R\*'s standard "make this guy stop holding back and charge" edit is `SET_PED_COMBAT_MOVEMENT(ped, 2)` immediately alongside removing the defensive area (`assassin_construction.c4:9908-9911`).

Corpus frequency (TIER B): `2` ×227, `1` ×185, `0` ×61, `3` ×15.

**Caveat worth knowing:** CM_Defensive alone does *not* pin a ped to a location. It changes *how* he fights, not *where*. Holding position is the defensive area's job (Q4). Use both.

---

## 2. SET_PED_COMBAT_RANGE

**TIER A.** Enum `CCombatData__Range`:

```
0  CR_Near
1  CR_Medium
2  CR_Far
3  CR_VeryFar
(4 = CR_NumRanges — count sentinel, not a usable value)
```

`void SET_PED_COMBAT_RANGE(Ped ped, int range)` — `0x3C606747B23E497B`.

**TIER C caveat:** the metre figures in the FiveM docs (near 5-15 m, medium 7-30 m, far 15-40 m, very far 22-45 m) are community-measured, not from the binary. Treat as approximate. The real driver is `AttackWindowDistanceForCover` / `OptimalCoverDistance` in `combatbehaviour.meta`, which vary per ped type.

Corpus frequency (TIER B): `0` ×113, `2` ×45, `1` ×31. **R\* overwhelmingly uses `0` (CR_Near) for peds that are supposed to hold a specific spot** — it stops them drifting away to find a long firing angle. This is what you want for fam holding a block.

---

## 3. SET_PED_COMBAT_ATTRIBUTES — full verified index table

`void SET_PED_COMBAT_ATTRIBUTES(Ped ped, int attributeIndex, BOOL enabled)` — `0x9F7794730795E019`.

**TIER A.** Enum `CCombatData__BehaviourFlags`, dumped from the exe. Valid indices are **0–90**; 91 is `MAX_COMBAT_FLAGS`.

### The indices you asked about

| Idx | Binary name (TIER A) | Meaning |
|----|----|----|
| 0 | `BF_CanUseCover` | **This is BF_CanUseCover.** Must be TRUE or the ped never takes cover. |
| 1 | `BF_CanUseVehicles` | Can use vehicles in combat. R\* sets FALSE ×280 to stop enemies jacking cars mid-fight. |
| 2 | `BF_CanDoDrivebys` | Drive-bys from a vehicle. |
| 3 | `BF_CanLeaveVehicle` | FALSE forces the ped to stay in his vehicle. |
| 5 | `BF_AlwaysFight` | **This is BF_AlwaysFight** — fights on threat response instead of fleeing. |
| 17 | `BF_AlwaysFlee` | Always flees on threat response. Exact inverse pair with 5. |
| 20 | `BF_CanTauntInVehicle` | Unarmed taunts from a vehicle. Cosmetic. |
| 26 | `BF_DisableEntryReactions` | Suppresses the flinch/turn-and-aim entry reaction into combat. |
| 46 | `BF_CanFightArmedPedsWhenNotArmed` | **NOT AlwaysFight.** Lets an unarmed ped engage armed ones. |
| 50 | `BF_CanCharge` | Ped is allowed to charge the enemy position. **Set FALSE for hold-the-block.** |
| 58 | `BF_DisableFleeFromCombat` | Stops non-mission peds bailing out of a fight. |

### ⚠ THE INDEX THAT WILL BITE YOU: 5 vs 46

The RAGE-MP wiki list — and the dozens of pages that copied it — says:

```
BF_CanFightArmedPedsWhenNotArmed = 5     ← WRONG
BF_AlwaysFight = 46                      ← WRONG
```

**These two are swapped.** TIER A says 5 = `BF_AlwaysFight`, 46 = `BF_CanFightArmedPedsWhenNotArmed`. Two independent TIER B confirmations:

- `agency_heist2.c4:809-811` turns a ped from fighter into runner: sets 5 → 0, then 17 → 1, then `SET_PED_FLEE_ATTRIBUTES`. Only coherent if 5 is AlwaysFight and 17 is AlwaysFlee.
- `chop.c4:5783-5786` — Chop the dog. Given `WEAPON_ANIMAL`, then 5 → 1, **0 → 0** (a dog can't take cover), **46 → 1** (an unarmed animal is allowed to attack armed peds). Nails 0, 5 and 46 simultaneously.

That same folklore list also contains `BF_FreezeMovement = 292` and `BF_PlayerCanUseFireingWeapons = 1424`. Both are **impossible** — max valid index is 90. If you see a list with those entries in it, discard the whole list.

### Answers to your specific sub-questions

- **BF_CanUseCover → index 0.** (Folklore agrees here.)
- **BF_AlwaysFight → index 5.** (Folklore says 46. Folklore is wrong.)
- **"Controls leaving a defensive area"** — there is no single flag. There are **five** flags that each independently destroy or escape your defensive area. This is the single most important thing in this report:

| Idx | Binary name | What it does to your defensive area |
|----|----|----|
| 37 | `BF_ClearAreaSetDefensiveIfDefensiveAreaReached` | On arrival, **wipes the area**, switches to Defensive movement |
| 45 | `BF_ClearPrimaryDefensiveAreaWhenReached` | On arrival, **wipes the primary area** |
| 51 | `BF_ClearAreaSetAdvanceIfDefensiveAreaReached` | On arrival, **wipes the area** and switches to **WillAdvance** — i.e. he reaches his post, then charges off |
| 62 | `BF_ClearAreaSetDefensiveIfDefensiveCannotBeReached` | If the area is unreachable, **wipes it** |
| 71 | `BF_PermitChargeBeyondDefensiveArea` | Lets him charge a target **outside** the area |

R\* enables 51 on 214 call sites and 37 on 73 — these are *on* for a lot of ped types by default via `combatbehaviour.meta`. **If your fam are wandering off, flag 51 or 37 is the likeliest culprit, not a missing defensive area.** Set all five to FALSE explicitly.

- **"Prevents chasing/pursuing on foot" → index 21, `BF_CanChaseTargetOnFoot`**, set to FALSE. TIER B note: R\* only ever *enables* it (10 sites, never disabled), implying it's off by default for most ped types — but defaults are per-model via `combatbehaviour.meta`, so set it explicitly.

### Other indices directly useful to you

| Idx | Binary name | Use |
|----|----|----|
| 11 | `BF_JustSeekCover` | Ped seeks cover only. Very passive — probably too passive for fam. |
| 12 | `BF_BlindFireWhenInCover` | Blind-fire from cover. TRUE looks good. |
| 13 | `BF_Aggressive` | "Ped may advance." Set FALSE for hold-ground. |
| 29 | `BF_MoveToLocationBeforeCoverSearch` | **Go to the defensive area first, then look for cover.** R\* enables ×83. Excellent for "get back to your post and dig in". |
| 43 | `BF_SwitchToAdvanceIfCantFindCover` | Set **FALSE** — otherwise no cover nearby = charge. |
| 44 | `BF_SwitchToDefensiveIfInCover` | TRUE reinforces staying put once in cover. |
| 47 | `BF_EnableTacticalPointsWhenDefensive` | Leave **FALSE** → when Defensive he uses cover only, not roaming tactical points. |
| 31 | `BF_MaintainMinDistanceToTarget` | Stops him closing to point-blank. |
| 42 | `BF_CanFlank` | Set FALSE for hold-ground. |

### Where citizenfx's list disagrees with the binary

The citizenfx `eCombatAttribute` enum is *mostly* right (and far better than the RAGE-MP one), but it **omits** indices 18, 19, 32, 33, 61, 63, 75, 76, 77. Binary names for those: 18 `BF_ForceInjuredOnGround`, 19 `BF_DisableInjuredOnGround`, 32 `BF_IgnoreHatedPedsInFastMovingVehicles`, 33 `BF_UseProximityAccuracy`, 61 `BF_NonMissionPedsFleeFromThisPedUnlessArmed`, 63 `BF_FleesFromInvincibleOpponents`, 75 `BF_DisableShoutTargetPosition`, 76 `BF_SetDisableShoutTargetPositionOnCombatStart`, 77 `BF_DisableRespondedToThreatBroadcast`. Indices 8, 10, 16 are `BF_Unused_3/1/2` — genuinely dead, writing them does nothing.

---

## 4. Defensive areas — and THE critical question

### Signatures

```c
void SET_PED_SPHERE_DEFENSIVE_AREA(Ped ped, float x, float y, float z, float radius, BOOL p5, BOOL p6);            // 0x9D3151A373974804
void SET_PED_ANGLED_DEFENSIVE_AREA(Ped ped, float x1,y1,z1, float x2,y2,z2, float width, BOOL p8, BOOL p9);
void SET_PED_DEFENSIVE_AREA_ATTACHED_TO_PED(Ped ped, Ped attachPed, float x1,y1,z1, float x2,y2,z2, float width, BOOL p9, BOOL p10);  // 0x4EF47FE21698A8B6
void SET_PED_DEFENSIVE_AREA_DIRECTION(Ped ped, float x, float y, float z, BOOL p4);                                 // 0x413C6C763A4AFFAD
void REMOVE_PED_DEFENSIVE_AREA(Ped ped, BOOL p1);                                                                   // 0x74D4E028107450A9
void TASK_CLEAR_DEFENSIVE_AREA(Ped ped);        // task-form clear, usable inside a sequence
```

**`SET_PED_DEFENSIVE_AREA_ATTACHED_TO_PED` argument meanings** (TIER B, derived from 28 call sites): the seven floats are an **angled box in the attach-ped's local space** — corner A `(x1,y1,z1)`, corner B `(x2,y2,z2)`, then **width**. R\*'s house pattern is corners `(5,0,5)` and `(-5,0,-5)` with the width varying to size the box: `(ped, attachPed, 5.0,0.0,5.0, -5.0,0.0,-5.0, W, 0, 0)` where W ∈ {5,8,10,15,20,25,30}. This box **follows the attached ped**. That is the bodyguard primitive.

**The two trailing BOOLs — honest uncertainty.** I could not determine `p5` on the sphere variant. TIER B distribution: `(0,0)` ×580, `(1,0)` ×312, `(1,1)`/`(0,1)` ×3 each. What I *can* say with evidence: Max Payne 3's RAGE native table (same command set, earlier engine) has `SET_PED_SPHERE_DEFENSIVE_AREA <INT,FLOAT,FLOAT,FLOAT,FLOAT>` and `REMOVE_PED_DEFENSIVE_AREA <INT>` — **no BOOLs at all**. GTA V added trailing BOOLs to *every* defensive-area setter *and* to `REMOVE`. Since peds have both a primary and a secondary defensive area (proven by the TIER A flag name `BF_ClearPrimaryDefensiveAreaWhenReached`), and since `finalec1.ysc` calls `REMOVE_PED_DEFENSIVE_AREA(ped, 0)` immediately followed by `REMOVE_PED_DEFENSIVE_AREA(ped, 1)` to fully clear a ped, **the last BOOL is almost certainly primary(0)/secondary(1) area select.** That's inference, not documentation — flagging it as such. `p5` on the sphere I genuinely don't know.

**Practical advice: pass `0, 0`.** It's R\*'s most common combination and it targets the primary area.

### ★ DOES A COMBAT TASK CLEAR A DEFENSIVE AREA? — **NO.**

This is the crux of your question and the evidence is strong and multi-stranded (TIER B):

1. **R\* explicitly removes the area before re-tasking combat, when they want the ped to leave.** From `assassin_construction.c4:9908-9911` — the "guards give up their posts and charge" trigger:
   ```
   REMOVE_PED_DEFENSIVE_AREA(ped, 0);
   SET_PED_COMBAT_MOVEMENT(ped, 2);
   TASK_CLEAR_DEFENSIVE_AREA(ped);
   TASK_COMBAT_PED(ped, target, 0, 16);
   ```
   If `TASK_COMBAT_PED` wiped the area, both the `REMOVE` and the `TASK_CLEAR` would be dead code. R\* wrote them because the area **survives** the combat task.

2. **The canonical R\* ordering is area-first, combat-last**, and it works. Measured across the corpus: area set **before** the combat task 512 times vs after 161. The repeated idiom, verbatim from `chinese1.c4` / `fbi5a.c4` / `fbi4.c4` / `bailbond2.c4`:
   ```
   SET_BLOCKING_OF_NON_TEMPORARY_EVENTS(ped, 0);
   SET_PED_SPHERE_DEFENSIVE_AREA(ped, GET_ENTITY_COORDS(ped, 1), radius, 0, 0);
   SET_PED_COMBAT_MOVEMENT(ped, 2);
   SET_PED_COMBAT_ATTRIBUTES(ped, 50, 1);
   TASK_COMBAT_HATED_TARGETS_AROUND_PED(ped, 200.0, 0);
   ```

3. **The existence of five separate "clear the area when reached / when unreachable" flags** (37/45/51/62, TIER A) only makes sense if the engine's default is that the area *persists*. You need an opt-in flag to destroy it.

4. The area is ped state (`CPedIntelligence`), not task state. `docks_heista.c4` sets a defensive area on a ped and separately re-tasks combat only `if (!IS_PED_IN_COMBAT(ped, 0))` — i.e. setting the area on an already-fighting ped is expected to take effect without re-tasking.

### Correct call order so a ped fights but does not leave the sphere

```
1. REMOVE_PED_DEFENSIVE_AREA(ped, false)                    // clear stale primary
   REMOVE_PED_DEFENSIVE_AREA(ped, true)                     // clear stale secondary
2. SET_PED_SPHERE_DEFENSIVE_AREA(ped, x, y, z, radius, false, false)
3. SET_PED_COMBAT_MOVEMENT(ped, 1)                          // CM_Defensive
   SET_PED_COMBAT_RANGE(ped, 0)                             // CR_Near
4. all SET_PED_COMBAT_ATTRIBUTES  (esp. 0=T, 29=T, 12=T, 44=T,
                                   13/21/37/42/43/45/50/51/62/71 = FALSE)
5. TASK_COMBAT_PED(ped, attacker, 0, 16)   — LAST
```

Steps 2–4 are ped state and order among themselves doesn't matter much; **the combat task must come last**, and steps 2–4 must not be re-issued in a way that resets them afterwards.

### The guard tasks

```c
void TASK_GUARD_CURRENT_POSITION(Ped ped, float p1, float p2, BOOL p3);          // 0x4A58A47A72E3FCB4
void TASK_GUARD_ASSIGNED_DEFENSIVE_AREA(Ped ped, float x, float y, float z, float heading, float p5, Any p6);
void TASK_GUARD_SPHERE_DEFENSIVE_AREA(Ped ped, float guardX, float guardY, float guardZ,
                                      float heading, float p5, Any timeout,
                                      float sphereX, float sphereY, float sphereZ, float radius);  // 0xC946FE14BE0EB5E2
```

`TASK_GUARD_SPHERE_DEFENSIVE_AREA` parameter mapping is TIER B — recovered from the only two call sites in the corpus (`fm_hideout_controler.c4:67785`, `fm_mission_controller.c4:240350`), both of which pass the *same* coordinate struct for the guard position and the sphere centre, with `-1` in the `p6` slot (indefinite) and matching floats in the `p5`/`radius` slots. This resolves FiveM's "p7,p8,p9 — XYZ again? p10 — maybe the size of sphere" guesses: **yes, second triple is the sphere centre, p10 is its radius.**

`TASK_GUARD_CURRENT_POSITION(ped, 35.0, 35.0, 1)` — used for stationary armed guards in `re_prisonvanbreak.c4`, and `(ped, 0.0, 3.0, 1)` in `re_cultshootout.c4`. The two floats read as distance thresholds (how far he'll stray / how far he'll pursue before returning). **TIER B inference; I do not have hard confirmation of which float is which.**

**Important:** these guard tasks are *tasks*, so unlike defensive areas they **will** be replaced by a subsequent `TASK_COMBAT_*`. R\* handles this by polling `GET_SCRIPT_TASK_STATUS(ped, 0x21e8d4e4)` and re-issuing the guard task when it's no longer running. For your use case you probably don't want the guard task at all — you want the defensive area, which is state and doesn't get replaced.

### SET_PED_PREFERRED_COVER_SET

```c
void SET_PED_PREFERRED_COVER_SET(Ped ped, Any itemSet);   // 0x8421EB4DA7E391B9
```

**TIER B, fully resolved.** The `itemSet` is an ItemSet of **scripted cover point handles**. Exact R\* usage (`finale_heist2b.c4:78732-78735`, `franklin1.c4`, `michael1.c4`, `prologue1.c4`):

```
handle   = ADD_COVER_POINT(x, y, z, direction, ...)
itemSet  = CREATE_ITEMSET(1)
ADD_TO_ITEMSET(handle, itemSet)
SET_PED_PREFERRED_COVER_SET(ped, itemSet)
DESTROY_ITEMSET(itemSet)          // destroyed immediately — the ped keeps the reference
```

Note that R\* destroys the itemset on the very next line. Survives combat tasks (ped state). Use this if you want fam to prefer *specific* cover spots you've authored on the block rather than whatever the navmesh offers.

---

## 5. Cover tasks — and do you even need them?

```c
void TASK_SEEK_COVER_FROM_PED(Ped ped, Ped target, int duration, BOOL p3);                          // 0x84D32B3BEC531324
void TASK_SEEK_COVER_TO_COORDS(Ped ped, float x1,y1,z1, float x2,y2,z2, Any p7, BOOL p8);           // 0x39246A6958EF072C
void TASK_PUT_PED_DIRECTLY_INTO_COVER(Ped ped, float x, float y, float z, Any timeout, BOOL p5,
                                      float p6, BOOL p7, BOOL p8, Any p9, BOOL p10);                // 0x4172393E6BE1FECE
void TASK_SEEK_COVER_TO_COVER_POINT(Ped ped, Any coverPoint, float x, float y, float z, int timeout, BOOL p6);
```

`duration` is milliseconds; **`-1` = indefinite** (confirmed across corpus call sites). R\* passes 3000/5000/10000 for brief scurries.

**Interaction with combat: these are all primary tasks and they REPLACE a combat task.** A ped executing `TASK_SEEK_COVER_FROM_PED` is not fighting. R\* only ever chains them via `OPEN_SEQUENCE_TASK` → seek cover → then `TASK_COMBAT_HATED_TARGETS_AROUND_PED` → `CLOSE_SEQUENCE_TASK`.

**Answer to your real question: NO, you should not explicitly task cover.** The combat AI finds cover on its own, provided:
- `SET_PED_COMBAT_MOVEMENT(ped, 1)` (CM_Defensive), **and**
- attribute `0` (`BF_CanUseCover`) is TRUE, **and**
- there is reachable cover inside the defensive area.

Explicit cover-tasking is what R\* reaches for only in tightly-authored setpieces where a specific ped must be behind a specific crate. The full pattern, from `armenian2.c4:34043-34047`:

```
coverPt = ADD_COVER_POINT(pos, direction, ...)
SET_PED_SPHERE_DEFENSIVE_AREA(ped, coverPos, 2.0, 0, 0)
SET_PED_COMBAT_ATTRIBUTES(ped, 29, 1)                    // move to location before cover search
TASK_SEEK_COVER_TO_COVER_POINT(ped, coverPt, x,y,z, 30000, 1)
TASK_COMBAT_HATED_TARGETS_AROUND_PED(ped, 50.0, 0)
```

For fam holding a block, hand-authoring cover points is the wrong tool — you'd have to author them per location. Use defensive area + CM_Defensive + attribute 0, and if the cover choices look bad, add attribute 29 (`BF_MoveToLocationBeforeCoverSearch`).

---

## 6. "Guard/defend this ped" — how R\* actually does bodyguards

There is **no** single "protect this ped" native. R\* composes it from two pieces:

**Piece 1 — leash the defender to the protected ped** via `SET_PED_DEFENSIVE_AREA_ATTACHED_TO_PED`. The box moves with the protected ped, so the defender fights *around* him and can't be lured away. `michael3.c4:66579-66583`, verbatim ordering:

```
CLEAR_PED_TASKS_IMMEDIATELY(ped);
REMOVE_PED_DEFENSIVE_AREA(ped, 0);
SET_PED_DEFENSIVE_AREA_ATTACHED_TO_PED(ped, PLAYER_PED_ID(), 5.0,0.0,5.0, -5.0,0.0,-5.0, 10.0, 0, 0);
SET_PED_COMBAT_ATTRIBUTES(ped, 50, 1);
TASK_COMBAT_PED(ped, PLAYER_PED_ID(), 0, 16);
```

`prologue1.c4:65913-65914` uses the same call to leash Michael and Trevor to a third ped (`(5,0,5)/(-5,0,-5)`, width 5) — **that is R\*'s literal "these two guys guard that guy" implementation.** `trevor1.c4:73768` uses it in reverse to leash an *enemy* to the player with width 25, so he can't wander off mid-fight.

**Piece 2 — point him at the right enemy.** `TASK_COMBAT_PED(defender, attacker, 0, 16)`. Note the flags: **p2 = 0, p3 = 16** — this exact pair appears in essentially every R\* call site and is what you should use.

For "defend whoever is under attack", the composition is:
```
SET_PED_DEFENSIVE_AREA_ATTACHED_TO_PED(defender, victim, 5,0,5, -5,0,-5, W, false, false);
TASK_COMBAT_PED(defender, attacker, 0, 16);
```
The defender fights the specific attacker, but is spatially bound to a box around the victim. That's precisely the behaviour you described wanting.

**`TASK_GUARD_ASSIGNED_DEFENSIVE_AREA`** does exist and means "guard whatever area is already assigned to me" — so `SET_PED_DEFENSIVE_AREA_ATTACHED_TO_PED` + `TASK_GUARD_ASSIGNED_DEFENSIVE_AREA` is a valid bodyguard idiom. But it's a *task* and will be replaced the moment combat starts, and it appears exactly **once** in 736 R\* scripts (inside a sequence in `fm_mission_controller.c4:240445`). R\* clearly does not rely on it. I'd skip it.

**Also note:** `TASK_COMBAT_HATED_TARGETS_AROUND_PED(ped, radius, 0)` (`0x7BF835BB9E2698C8`) despite its plural name attacks **only the single closest hated target** — TIER C, but consistent with corpus usage. If you want fam to converge on *the* attacker rather than each picking his own nearest enemy, use `TASK_COMBAT_PED` with an explicit target, not the hated-targets variant. `TASK_COMBAT_HATED_TARGETS_IN_AREA(ped, x,y,z, radius, p5)` is `0x4CF5F55DAC3280A0`, a different native.

---

## Recommended recipe for Hoodrich fam-under-attack

```
// once, per defender, when the block gets hit
REMOVE_PED_DEFENSIVE_AREA(fam, false);
REMOVE_PED_DEFENSIVE_AREA(fam, true);

// EITHER anchor to the post he was stationed at:
SET_PED_SPHERE_DEFENSIVE_AREA(fam, postX, postY, postZ, 12f, false, false);
// OR anchor to whoever is being attacked:
SET_PED_DEFENSIVE_AREA_ATTACHED_TO_PED(fam, victim, 5f,0f,5f, -5f,0f,-5f, 12f, false, false);

SET_PED_COMBAT_MOVEMENT(fam, 1);   // CM_Defensive
SET_PED_COMBAT_RANGE(fam, 0);      // CR_Near

SET_PED_COMBAT_ATTRIBUTES(fam,  0, true);   // BF_CanUseCover
SET_PED_COMBAT_ATTRIBUTES(fam, 12, true);   // BF_BlindFireWhenInCover
SET_PED_COMBAT_ATTRIBUTES(fam, 29, true);   // BF_MoveToLocationBeforeCoverSearch
SET_PED_COMBAT_ATTRIBUTES(fam, 44, true);   // BF_SwitchToDefensiveIfInCover
SET_PED_COMBAT_ATTRIBUTES(fam,  5, true);   // BF_AlwaysFight
SET_PED_COMBAT_ATTRIBUTES(fam, 58, true);   // BF_DisableFleeFromCombat

SET_PED_COMBAT_ATTRIBUTES(fam, 13, false);  // BF_Aggressive          (no advancing)
SET_PED_COMBAT_ATTRIBUTES(fam, 21, false);  // BF_CanChaseTargetOnFoot(no street chase)
SET_PED_COMBAT_ATTRIBUTES(fam, 42, false);  // BF_CanFlank
SET_PED_COMBAT_ATTRIBUTES(fam, 43, false);  // BF_SwitchToAdvanceIfCantFindCover
SET_PED_COMBAT_ATTRIBUTES(fam, 50, false);  // BF_CanCharge
SET_PED_COMBAT_ATTRIBUTES(fam,  1, false);  // BF_CanUseVehicles
// the five area-destroyers — ALL must be false:
SET_PED_COMBAT_ATTRIBUTES(fam, 37, false);
SET_PED_COMBAT_ATTRIBUTES(fam, 45, false);
SET_PED_COMBAT_ATTRIBUTES(fam, 51, false);
SET_PED_COMBAT_ATTRIBUTES(fam, 62, false);
SET_PED_COMBAT_ATTRIBUTES(fam, 71, false);

SET_BLOCKING_OF_NON_TEMPORARY_EVENTS(fam, false);
TASK_COMBAT_PED(fam, attacker, 0, 16);      // LAST
```

Debug assertion available: `GET_PED_COMBAT_MOVEMENT(fam)` should return `1`. There is no getter for combat attributes or defensive areas, so if behaviour is wrong, suspect flags 37/51 first — they are the silent killers, they're enabled by default on many ped types, and they wipe your sphere the instant the ped arrives at it.

**Highest-risk remaining unknowns**, honestly stated: (a) the `p5` BOOL on `SET_PED_SPHERE_DEFENSIVE_AREA` — meaning unknown, pass `false`; (b) the primary/secondary reading of the final BOOL is inference from Max Payne 3 signature deltas plus one R\* call site, not documentation; (c) the two floats on `TASK_GUARD_CURRENT_POSITION` are unconfirmed. Everything in sections 1, 2, 3 and the "combat tasks do not clear defensive areas" conclusion in section 4 is solid.

## Sources

- [rage-parser-dumps (GTA V b3407 parser metadata — TIER A)](https://github.com/alexguirre/rage-parser-dumps)
- [brendan-rius/gta-v-decompiled-scripts (R\* script corpus — TIER B)](https://github.com/brendan-rius/gta-v-decompiled-scripts)
- [citizenfx/natives — SetPedCombatAttributes](https://github.com/citizenfx/natives/blob/master/PED/SetPedCombatAttributes.md)
- [citizenfx/natives — SetPedCombatMovement](https://github.com/citizenfx/natives/blob/master/PED/SetPedCombatMovement.md)
- [FiveM native docs — SET_PED_COMBAT_RANGE](https://docs.fivem.net/natives/?_0x3C606747B23E497B)
- [FiveM native docs — TASK_COMBAT_PED](https://docs.fivem.net/natives/?_0x70FA2AFA=)
- [DottieDot/gta5-additional-nativedb-data (native usage dumps)](https://github.com/DottieDot/gta5-additional-nativedb-data)
- [ShinyWasabi/scrDbg — Max Payne 3 native table (signature deltas)](https://github.com/ShinyWasabi/scrDbg)
- [combatbehaviour.meta dump (BF_ flag names, CM_/CR_ values in shipped data)](https://pastebin.com/Jmcd6rFR)
- [RAGE-MP wiki — Player::setCombatAttributes (TIER D, contains the swapped 5/46 error)](https://wiki.rage.mp/wiki/Player%3A%3AsetCombatAttributes)