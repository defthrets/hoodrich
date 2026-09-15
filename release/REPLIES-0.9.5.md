# Replies — everything unanswered on the page

Copy and paste. 0.9.5 is out, so most of these are "that's fixed".

---

**gaspin12**

done in 0.9.5. two things were wrong — the count was being written down before the
mod had handed your guns back, so it saved what the base game had, which is nothing.
and a zero deleted the row from the save instead of saving a zero, so one autosave in
the first few seconds and the rounds were gone for good. cheers for the report.

---

**kocabac**

the ERR_MEM_EMBEDDEDALLOC and the gang war crash were the same thing, and you found
it yourself turning the rollers off. I had a world-is-full check that only ONE system
was actually asking — everything else just kept spawning people and cars. they all
ask now, and cars get counted as well as people. sorry it took me a while to join the
dots.

---

**Vorx4643**

fixed in 0.9.5. I was only hiding the game's phone, which isn't the same as closing
it, so if the real one was already up you got both. properly ended now.

---

**coltsbell87**

appreciated, seriously. the ammo one is fixed in 0.9.5. the weapon hold stance I
haven't managed to reproduce yet — which gun, and is it after a load or straight
away? I'll get it sorted.

---

**NordCyborg**

you had it right — Phone > Inventory while you're inside the house. problem is the
mod only told you that on your very first visit, which is no use later on. it says it
again now whenever you walk in carrying something. cheers for coming back with the
answer, that's what made it obvious.

---

**Zona Comics**

it's already in there. set `Language=PortugueseBR` in scripts\Hoodrich.ini, or change
it on the settings page in the phone. nine languages ship with it. dialogue and the
social feed stay in english by design — everything else translates.

---

**Felony83**

should be a lot better in 0.9.5. two causes — loading a model was stopping the whole
script for up to a second at a time, worst exactly where crews spawn like Gerald's,
and the spawners never checked whether the world was already full. both fixed. if it
still drops there send me the last lines of scripts\Hoodrich\Hoodrich.log.

---

**Godmordor**

yes please, I'd take that. the russian was machine-assisted and it shows. send it
over however suits — the file is scripts\Hoodrich\lang\ru.json, same keys, and I'll
ship it with credit.

---

**dtrail**

nightly moves its API around and the mod is built against ScriptHookVDotNet 3.9.0.0,
so a nightly with a changed signature throws MissingMethodException at runtime. stick
to the 3.9.0 release and it's fine. if you need nightly for something else, tell me
which build and I'll have a look at what changed.

---

**vultures**

still chasing this one. your log ends on a normal line with no exception, and
Franklin-only is odd given the whole mod is built round him. 0.9.5 has real crash
fixes in it so try that first, and if it still goes send me the last 30 lines of
scripts\Hoodrich\Hoodrich.log from just before it happens.

---

**Vipperr_2427**

that MissingMethodException is a ScriptHookVDotNet version mismatch — the mod is
built against 3.9.0.0 and something on your install is a different build, usually a
nightly. grab the 3.9.0 release, drop it in, and it should load. if it still throws,
paste the first ten lines of scripts\Hoodrich\Hoodrich.log and I'll tell you what's
missing.

---

**Zer0w**

cheers, that means a lot.
