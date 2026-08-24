===============================================================================
  POSTED UP  0.2.0
  A drug-dealing and gang mod for Grand Theft Auto V
===============================================================================

You run with the Chamberlain Gangster Families. You get hold of some product,
you take it home and cut it, you stand somewhere busy and let the trade come to
you, and you try to be gone before the police or somebody else's people decide
you have been there long enough.

Everything runs off a phone that replaces the in-game one. Your weapon wheel is
left exactly as it was.


-------------------------------------------------------------------------------
  WHAT YOU NEED FIRST
-------------------------------------------------------------------------------

Two things, and NEITHER OF THEM IS IN THIS ZIP. Both are other people's work and
neither may be redistributed, so you install them yourself, once:

  1. ScriptHookV            http://www.dev-c.com/gtav/scripthookv/
     Alexander Blade's. Its licence does not allow anyone to repackage it, and
     it is version-locked to the game -- a copy bundled by a mod would be the
     wrong one within a week of the next patch anyway.

  2. ScriptHookVDotNet 3    https://github.com/scripthookvdotnet/scripthookvdotnet
     Not bundled on purpose. Every .NET mod you own loads the same copy of it,
     so the one in your scripts folder must be the newest of them -- and a zip
     that quietly overwrote yours with an older build would break the others.

     - GTA V LEGACY   : ScriptHookVDotNet 3.x
     - GTA V ENHANCED : the Enhanced-compatible build from the same project

  3. .NET Framework 4.8. Already on Windows 10 and 11. Nothing to do.

This mod itself has NO other dependencies. No LemonUI, no NativeUI, no
Newtonsoft, no Lua, no OpenIV, no asset replacements, no installer. One DLL,
some JSON and some PNGs -- which is also why it will not fight anything else in
your scripts folder.


-------------------------------------------------------------------------------
  INSTALL
-------------------------------------------------------------------------------

Copy the "scripts" folder from this zip into your GTA V folder and say yes to
merging. That is the whole of it.

Your GTA V folder is the one with GTA5.exe in it, normally:

  C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V

Afterwards you should have:

  ...\Grand Theft Auto V\scripts\Hoodrich.dll
  ...\Grand Theft Auto V\scripts\Hoodrich.ini
  ...\Grand Theft Auto V\scripts\Hoodrich\        (json + icons)

Load a save and press the phone button. Everything is in there.

TO UNINSTALL: delete those three things. Nothing else on your machine was
touched -- no game files are modified or replaced by this mod.


-------------------------------------------------------------------------------
  GETTING STARTED
-------------------------------------------------------------------------------

Gerald is the only marker on your map, and that is deliberate. Go and see him
at the flats on Grove. He puts the first lot of product in your hand for
nothing, so you do not need money to start.

Then open the phone, go to Dealing, and post up somewhere with people on it.
Buyers come to you.

The one thing nobody works out on their own: WEIGHT YOU BUY WILL NOT SELL until
it has been cut and bagged at the sink in Denise's kitchen. Anything you are
GIVEN is already bagged.

The intro screen on first run covers the rest. Settings has a switch to bring
it back if you want to read it again.


-------------------------------------------------------------------------------
  CONTROLS
-------------------------------------------------------------------------------

  Phone button       open the phone (everything lives here)
  Arrow keys         move          Enter  open          Backspace  back
  Right on the d-pad talk to somebody -- and, stood near a patrol car with no
                     wanted level, let them know how you feel about them

Every key is rebindable in Hoodrich.ini under [Phone].


-------------------------------------------------------------------------------
  CONFIGURING IT
-------------------------------------------------------------------------------

  Hoodrich.ini            every setting, commented. Delete any line to go back
                          to its default. 70 of them: prices, heat, difficulty,
                          what draws on screen, every key.

  Hoodrich\*.json         the content itself -- drugs, prices, gangs, turf,
                          dealers, missions, and several thousand lines of
                          social-media chatter. All editable, all reloaded at
                          startup. Break one and the mod says so in the log
                          and falls back to its built-in copy rather than
                          failing to load.

  Hoodrich\Hoodrich.log   what it did and why. Set LogLevel=Debug in the ini if
                          you are reporting a problem.

Your save lives in Hoodrich\save.json. Back it up before editing anything.


-------------------------------------------------------------------------------
  KNOWN AND DELIBERATE
-------------------------------------------------------------------------------

  * The Wei Cheng business at the port is built but switched off, and shows as
    "Coming soon". It is not finished and is not meant to be played yet.

  * The mod holds the front doors of Denise's house and Franklin's Vinewood
    Hills house unlocked, so you do not need Open All Interiors to reach the
    kitchen sink. If you already run that mod, nothing conflicts.

  * Works on both GTA V Legacy and GTA V Enhanced from the same DLL.

  * Single player only. Do not take this anywhere near GTA Online.


-------------------------------------------------------------------------------
  LICENCE
-------------------------------------------------------------------------------

See LICENCE.txt. Short version: do what you like with it, credit where it is
due, and it comes with no warranty of any kind.

Source: https://github.com/defthrets/hoodrich
