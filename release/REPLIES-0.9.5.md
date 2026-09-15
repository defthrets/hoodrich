# Replies — one comment, everyone tagged

Copy the whole block below as a single comment on the page. Or take them a line at
a time if you'd rather answer people individually.

---

0.9.5 is up and it's mostly your reports, so — cheers, all of you.

@gaspin12 ammo's fixed. it was saving the count before the mod handed your guns back, and a zero wiped the row instead of storing it.

@kocabac both crashes were the same thing. only one system was checking whether the world was full, the rest just kept spawning. they all check now, cars as well as people.

@Vorx4643 fixed. I was hiding the game's phone instead of closing it, so you got both. properly ended now.

@NordCyborg you had it right — Phone > Inventory inside the house. it only told you that on your first visit, which was no use later. says it every time you walk in carrying something now.

@Felony83 much better in 0.9.5. loading a model was stalling the whole script, and the spawners never checked if the block was already full. both fixed. if it still drops there send me the end of scripts\Hoodrich\Hoodrich.log.

@Zona Comics it's already in there. set Language=PortugueseBR in scripts\Hoodrich.ini, or change it on the phone's settings page. nine languages ship with it.

@Godmordor yes please. the file is scripts\Hoodrich\lang\ru.json, same keys — send it however suits and I'll ship it with credit.

@dtrail @Vipperr_2427 that MissingMethodException is a ScriptHookVDotNet mismatch. the mod's built against 3.9.0.0 and nightlies move the API about. grab the 3.9.0 release and it'll load.

@coltsbell87 ammo's sorted. the weapon hold stance I can't reproduce yet — which gun, and is it after a load or straight away?

@vultures still on this one. your log ends clean with no exception in it. 0.9.5 has real crash fixes so try that first, and if it still goes send me the last 30 lines of scripts\Hoodrich\Hoodrich.log.

@Zer0w cheers, means a lot.
