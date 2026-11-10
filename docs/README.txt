VR Radial Overlay - test build 2026-11-10

Adds radial menus (wheels), wrist panels, button combos and a VR keyboard to any OpenXR game.
Works with Virtual Desktop (VDXR), SteamVR OpenXR, Meta Link and other OpenXR runtimes.


INSTALL
1. Unzip this folder somewhere it can stay (for example C:\Games\VRRadialOverlay).
   The game loads the overlay from this folder, so don't move or delete it after installing.
   If you do move it, click Install layer again.
2. Run VRRadialOverlayEditor.exe.
   The first time, it offers a short tour. It's worth doing: it takes about two minutes.
3. Click "Install layer" at the top. Windows asks for admin rights, which it needs to
   register the overlay with OpenXR.
4. Start an OpenXR game. A message at the bottom of your view saying
   "Radial overlay: default.json loaded" means it's running.


DEFAULT CONTROLS
- Double-tap the right thumbrest: right-hand wheel. Push the stick toward an item and let
  it spring back to pick it.
- Double-tap the left thumbrest: left-hand wheel.
- Wrist panels: turn your wrist toward your face and look at it. Poke a button with your
  other hand's finger, or point at it and pull the trigger.
- The VR keyboard is on the wheels and the left wrist panel.

Change anything in the editor and save (Ctrl+S). A running game picks it up within a second.


UNINSTALL
Open the editor and click "Uninstall layer", or run uninstall.ps1 as admin. Then delete the folder.
Your settings are in %LOCALAPPDATA%\VRRadialOverlay\ if you want to remove those too.


REPORTING PROBLEMS
Please send:
- what you did and what happened
- your headset and runtime (Virtual Desktop, SteamVR, Link...) and the game
- the log: %LOCALAPPDATA%\VRRadialOverlay\logs\<game>.log  (the editor's "Logs" button opens the folder)
- your profile: %LOCALAPPDATA%\VRRadialOverlay\profiles\default.json

If a game misbehaves, you can turn the overlay off for just that game: in the editor, make a
profile for it and untick "Overlay is on" in Settings. Or uninstall the layer.
