OpenXR Hand Menus v0.2.0 (test build)

Adds radial menus (wheels), wrist panels, button combos and a VR keyboard to any OpenXR game.
Works with Virtual Desktop (VDXR), SteamVR OpenXR, Meta Link and other OpenXR runtimes.


INSTALL
1. Unzip this folder somewhere it can stay (for example C:\Games\OpenXRHandMenus).
   The game loads the overlay from this folder, so don't move or delete it after installing.
   If you do move it, click Install layer again.
2. Run OpenXRHandMenusEditor.exe.
   The first time, it asks which controllers you use (this picks the buttons that open
   the wheels), then offers a short tour. The tour is worth doing: it takes about two minutes.
   Windows may warn that the program is unrecognised (it isn't code-signed yet):
   click "More info", then "Run anyway".
3. Click "Install layer" at the top. Windows asks for admin rights, which it needs to
   register the overlay with OpenXR.
4. Start an OpenXR game. A message at the bottom of your view saying
   "Hand menus: default.json loaded" means it's running.


DEFAULT CONTROLS
- Open a hand's wheel (depends on the controllers you picked):
    Quest / Rift / Pico: double-tap that hand's thumbrest
    Index / Pimax / Bigscreen: double-click that hand's thumbstick
    WMR / HP Reverb / Vive: hold that hand's menu button
  The wheel appears in front of you. Pick an item by pushing the stick toward it and letting
  it spring back, or by pointing at it and pulling the trigger (WMR/Vive: let go of menu).
  The "Controllers" button at the top of the editor changes the opening buttons later.
- Wrist panels: turn your wrist toward your face and look at it. Poke a button with your
  other hand's controller (a ring shows where it will touch), or point at it and pull
  the trigger.
- The VR keyboard is on the wheels and the left wrist panel.
- Move things to where you like them: right wheel > More... > Adjust positions. Grab the
  wheel or a wrist panel with your other hand's grip and move it; while holding, the stick
  resizes it (up/down) or turns a wrist panel (left/right). Adjust positions again saves.

Change anything in the editor and save (Ctrl+S). A running game picks it up within a second.


UNINSTALL
Open the editor and click "Uninstall layer", or run uninstall.ps1 as admin. Then delete the folder.
Your settings are in %LOCALAPPDATA%\OpenXRHandMenus\ if you want to remove those too.


REPORTING PROBLEMS
Please send:
- what you did and what happened
- your headset and runtime (Virtual Desktop, SteamVR, Link...) and the game
- the log: %LOCALAPPDATA%\OpenXRHandMenus\logs\<game>.log  (the editor's "Logs" button opens the folder)
- your profile: %LOCALAPPDATA%\OpenXRHandMenus\profiles\default.json

If a game misbehaves, you can turn the overlay off for just that game: in the editor, make a
profile for it and untick "Overlay is on" in Settings. Or uninstall the layer.
