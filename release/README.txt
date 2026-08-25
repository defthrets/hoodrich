===============================================================================
  POSTED UP  0.2.0
  A drug-dealing and gang mod for Grand Theft Auto V
===============================================================================

You run with the Chamberlain Gangster Families. Get hold of product, cut it at
the kitchen sink, stand somewhere busy and let the trade come to you -- and be
gone before the police or somebody else's people decide you have been there
long enough.

Everything runs off a phone that replaces the in-game one. Your weapon wheel is
left exactly as it was.


-------------------------------------------------------------------------------
  REQUIREMENTS  (install these first, they are not in this zip)
-------------------------------------------------------------------------------

  1. ScriptHookV
     http://www.dev-c.com/gtav/scripthookv/

  2. ScriptHookVDotNet 3
     https://github.com/scripthookvdotnet/scripthookvdotnet
     Legacy   -> ScriptHookVDotNet 3.x
     Enhanced -> the Enhanced-compatible build from the same project

  3. .NET Framework 4.8  -- already on Windows 10 and 11, nothing to do.

Neither of the first two may be repackaged by anyone, which is why they are not
bundled. Nothing else is needed: no LemonUI, no NativeUI, no OpenIV, no asset
replacements, no installer.


-------------------------------------------------------------------------------
  INSTALL
-------------------------------------------------------------------------------

Drag the "scripts" folder from this zip into your GTA V folder and say yes to
merging. That is the whole of it.

Your GTA V folder is the one with GTA5.exe in it, normally:

  C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V

You should end up with:

  ...\Grand Theft Auto V\scripts\Hoodrich.dll
  ...\Grand Theft Auto V\scripts\Hoodrich.ini
  ...\Grand Theft Auto V\scripts\Hoodrich\      (json + icons)

Load a save and press the phone button.


-------------------------------------------------------------------------------
  UNINSTALL
-------------------------------------------------------------------------------

Delete those three things. No game files are modified or replaced by this mod,
so there is nothing else to undo.


-------------------------------------------------------------------------------
  GETTING STARTED
-------------------------------------------------------------------------------

Gerald is the only marker on your map. Go and see him at the flats on Grove --
he puts the first lot of product in your hand for nothing, so you do not need
money to start.

Then open the phone, go to Dealing, and post up somewhere with people on it.

The one thing nobody works out on their own: WEIGHT YOU BUY WILL NOT SELL until
it has been cut and bagged at the sink in Denise's kitchen. Anything you are
GIVEN is already bagged.


-------------------------------------------------------------------------------
  CONTROLS
-------------------------------------------------------------------------------

  Phone button        open the phone (everything lives here)
  Arrow keys          move       Enter  choose       Backspace  back
  Right on the d-pad  talk to somebody

Every key is rebindable in Hoodrich.ini.


-------------------------------------------------------------------------------
  NOTES
-------------------------------------------------------------------------------

  * Works on both GTA V Legacy and GTA V Enhanced, same DLL.
  * Single player only. Do not take this anywhere near GTA Online.
  * Everything is configurable: Hoodrich.ini for settings and keys,
    Hoodrich\*.json for drugs, prices, gangs, missions and chatter.
  * Problems? Hoodrich\Hoodrich.log says what it did. Set LogLevel=Debug
    in the ini before reporting anything.
  * The Wei Cheng business at the port shows as "Coming soon" on purpose.
    It is not finished and is not meant to be played yet.


-------------------------------------------------------------------------------
  LICENCE
-------------------------------------------------------------------------------

See LICENCE.txt. Do what you like with it, credit where it is due, no warranty
of any kind.

Source: https://github.com/defthrets/hoodrich
