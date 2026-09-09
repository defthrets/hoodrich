# Blip sprites

Reference: <https://docs.fivem.net/docs/game-references/blips/>

The full list is about eight hundred entries. This is the part of it this mod has
a use for, plus what is already on the map, so a new blip can be picked without
going back to the site every time.

Set with `SET_BLIP_SPRITE(handle, id)`. SHVDN's `BlipSprite` enum covers a lot of
them by name -- prefer the name where one exists, because `BlipSprite.Weed` says
what it is and `496` does not.

## What the mod uses now

| id | name | where |
|---|---|---|
| 855 | `radar_ped_gang_leader` | Lamar, the fixer -- `Fixer.cs` |
| 556 | `radar_supplies` | Stretch at the counter -- `Armourer.cs` |
| 811 | `radar_van_keys` | a car you own -- `OwnedCars.cs` |
| 810 | `radar_vehicle_deluxo` | Hao and his lot -- `Hao.cs` |
| 923 | `radar_dog` | Trigger -- `Trigger.cs` |
| 885 | `radar_yankton` | a body you made, for fifty seconds -- `Graves.cs` |
| 161 | `radar_mp_noise` | a LUber on its way to you -- `Luber.cs` |
| 464 | `radar_spray_can` | tag spots -- `TagRun.cs` |

Set in Bare Minimum's `data/vendors.json` rather than here, because the shops are
its feature and the groups are its table:

| 827 | `radar_biker_bottle` | liquor stores |
| 889 | `radar_pizza_this` | fast food |
| 521 | `radar_laptop` | cafes |

The rest of that table moves faster than this file does -- read
`groups` in `vendors.json` for what every venue kind is wearing today.

| 952 | `radar_trashbag` | bad end of the reputation bar -- `PostUp.cs` |
| 621 | `radar_pickup_dtb_health` | good end of the reputation bar |
| 774 | `radar_sub_periscope` | inside the heat bar -- `PostUp.cs` |
| 835 | `radar_community_series` | inside the reputation bar |
| 855 | `radar_ped_gang_leader` | gang leaders by default -- `GangLeaders.cs` |
| 946 | `radar_four20` | Gerald, per-leader override in `leaders.json` |
| 58 | `radar_crim_wanted` | Oxycodone, everywhere it is drawn -- `Icons.cs` |
| -- | `BlipSprite.Weed` | drug markers |
| 497 | `radar_production_crack` | the pill press garage -- `Settings.cs` |
| -- | `BlipSprite.Package` | the plug's drop |
| -- | `BlipSprite.Safehouse` | the stash house |
| -- | `BlipSprite.Friend` | homies |
| 596 | `radar_nhp_wp2` | Tao Cheng on his way -- `Delivery.cs` |
| 225 | `radar_gang_vehicle` | a LUber driver bringing food in a car -- `LuberFood.cs` |
| 226 | `radar_gang_vehicle_bikers` | the same driver on the scooter -- `LuberFood.cs` |
| 362 | `radar_mask` | the mask shop on Vespucci Beach -- `MaskShop.cs` |
| -- | `BlipSprite.Standard` | anything unclassified |

## Drugs and production

| id | name |
|---|---|
| 51 | `radar_crim_drugs` |
| 496 | `radar_production_weed` |
| 497 | `radar_production_crack` |
| 498 | `radar_production_fake_id` |
| 499 | `radar_production_meth` |
| 500 | `radar_production_money` |
| 514 | `radar_drugs_package` |

Note there is no cocaine or heroin production sprite. Crack (497) is the closest
for either, and 51 is the generic.

## Weapons

| id | name |
|---|---|
| 110 | `radar_gun_shop` |
| 150 | `radar_weapon_assault_rifle` |
| 151 | `radar_weapon_bat` |
| 152 | `radar_weapon_grenade` |
| 154 | `radar_weapon_knife` |
| 155 | `radar_weapon_molotov` |
| 156 | `radar_weapon_pistol` |
| 157 | `radar_weapon_rocket` |
| 158 | `radar_weapon_shotgun` |
| 159 | `radar_weapon_smg` |
| 160 | `radar_weapon_sniper` |
| 173 | `radar_weapon_minigun` |
| 175 | `radar_weapon_armour` |

Useful for Grimes: one sprite per category on the rack, so a shotgun row and a
pistol row are told apart on the map and in a list before they are read.

## Gangs

| id | name |
|---|---|
| 128 | `radar_gang_cops` |
| 129 | `radar_gang_mexicans` |
| 130 | `radar_gang_bikers` |
| 225 | `radar_gang_vehicle` |
| 226 | `radar_gang_vehicle_bikers` |
| 227 | `radar_gang_vehicle_cops` |
| 228 | `radar_gang_vehicle_vagos` |

129 for the Vagos, the Aztecas and the Marabunta; 130 for the Lost; 225 for a
raiding carload of anybody.

## Money

| id | name |
|---|---|
| 272 | `radar_cash_pickup` |
| 276 | `radar_cash_lost` |
| 277 | `radar_cash_vagos` |
| 278 | `radar_cash_cops` |
| 500 | `radar_production_money` |

## Law

| id | name |
|---|---|
| 41 | `radar_police` |
| 42 | `radar_police_chase` |
| 43 | `radar_police_heli` |
| 60 | `radar_police_station` |
| 137 | `radar_police_station_blue` |

## Places

| id | name |
|---|---|
| 40 | `radar_safehouse` |
| 61 | `radar_hospital` |
| 71 | `radar_barber` |
| 72 | `radar_car_mod_shop` |
| 73 | `radar_clothes_store` |
| 93 | `radar_bar` |
| 121 | `radar_strip_club` |
| 135 | `radar_cinema` |
| 267 | `radar_property` |
| 350 | `radar_property_for_sale` |
| 357 | `radar_garage` |
| 369 | `radar_garage_for_sale` |
| 557 | `radar_property_bunker` |

## People and objectives

| id | name |
|---|---|
| 114 | `radar_random_female` |
| 115 | `radar_random_male` |
| 148 | `radar_mp_friend` |
| 274 | `radar_dead` |
| 310 | `radar_deathmatch` |
| 378 | `radar_team_deathmatch` |
| 380 | `radar_vehicle_deathmatch` |
| 280 | `radar_friend` |
| 855 | `radar_ped_gang_leader` |
| 143 | `radar_objective_blue` |
| 144 | `radar_objective_green` |
| 145 | `radar_objective_red` |
| 146 | `radar_objective_yellow` |

274 is the one for a body -- worth having on a homie who went down, and on the
spot a rival dropped you.

274 and 310 are both filed under death and they are not the same picture. `radar_dead`
draws a small X, which is a place where something happened; the skull everybody means
when they say "the skull blip" is `radar_deathmatch`, 310. Stretch is on 310 for that
reason -- 274 was tried first and reads as a map cross rather than as a man.

## Vehicles

| id | name |
|---|---|
| 68 | `radar_tow_truck` |
| 225 | `radar_gang_vehicle` |

---

Two things that are not on the sprite list and are worth remembering with it:

* Colour is separate -- `SET_BLIP_COLOUR`, and a gang blip should carry the
  gang's own colour rather than relying on the sprite to say whose it is.
* A sprite id the game does not have draws the default dot rather than nothing,
  so a wrong number looks like a working blip. Check it against the page above
  rather than against the map.
