# Replies — everything unanswered on the page, after 0.9.7

Post 0.9.7 first; every "fixed" below means fixed in it. One comment, everyone tagged,
or take them a line at a time.

---

0.9.7 is up. most of this one is your reports, so cheers.

@YOURLOCALSOUTHERN you don't need 3.9.0.0 any more. 0.9.7 is built against 3.6, so whatever scripthookvdotnet 3 you've got loads it. if it still crashes going into story mode, send me the last lines of scripts\Hoodrich\Hoodrich.log.

@Gta5gamer__ all three found. the blip riding off on random cars was a mark attached to the vehicle — the game hands the handle to the next thing it spawns when a car streams out, and the mark went with it. same thing on the bike. the lot was deleting a bought car when one of its own handles got recycled onto it, which is why only some cars. and the bike swap was grabbing any parked bagger, not just his. all fixed, and it loads on the nightlies now.

@kocabac thanks for the log, it told me exactly what i needed. the war was starting on a block that was already full — 217 peds when it opened, and the hold-offs only kicked in after. 0.9.7: a war won't start while the world's full, it uses two car models instead of nine (that error is the streaming pool, and variety is what fills it), and the limits are yours now — [Block] PedsBusy and CarsBusy in Hoodrich.ini, try 150 and 180. if it still goes, a gameconfig with bigger pools plus the heap adjuster is the usual answer to that error with any mod that spawns people.

@NordCyborg both fixed in 0.9.6 — he had no driver model, so every order cancelled itself, and the rapping loop was a job flag. 0.9.7 has it too.

@scubasteve94 vee's fixed, same as above. the bag: it drops on the ground under where you died now, or where you last stood if there's nothing under you, and there's a "bring my bag here" row in settings for one that's already gone.

@moesosa77 both in 0.9.7. Hoodrich.ini has FranklinsCar= and FranklinsBike= — leave them blank and his car and bike stay the game's own — and PhoneButton=false gives the up arrow back to the game so only your own key opens this phone (F2 by default, Phone > Key to change it). no discord at the moment, this page and github are where it lives.

@Bigb25 cheers. mp ped — no, it's built round franklin: his aunt's house, lamar, all of it. turfs on the map — not yet, it's on the list. other gangs — families only right now, the other eight are written and parked till they're finished. no discord for now.

@thaffdde that's where it's going. the other eight sets are written, just parked till they're done.

@VaporHaven cheers. identiswitch swaps the player out and this is built round franklin — his house, his aunt's sink, lamar — so other peds aren't supported yet. it's on the list with mp.

@JacksonDTM the bag's in already — twenty slots, and it takes food as well as product. on a fresh save it's on the floor at denise's; walk up and hold E.

@TruckerKingDee 0.9.7 adds BagProp= under [Dealing] in Hoodrich.ini — any prop name the game has, for the bag on the floor. the strap on his back is a clothing item, not a prop (vest slot, drawable 7), so that one's in the code. mp isn't supported.

@Bigsossa glad it's working. gerald at the flats on grove gives you the first two lots. after that it's the phone from denise's house — contacts, and they bring it to the door. weight you buy won't sell till it's cut at her sink.

@whatever5925 cheers, that means a lot. it's one product at a time while you're posted up for now — noted, it's a fair ask.

@Zona Comics cheers — send me the link when it's up.

@Vipperr_2427 good stuff.
