# Changelog

## v0.2.0 (2026-11-10)

### New
- **Adjust mode.** A new "Adjust positions" action (in the right wheel under More..., and on the left wrist panel's More page). While it's on, the main wheel and both wrist panels stay visible. Grab one with your other hand's grip and move it. While holding it, push the stick up or down to resize it, and left or right to turn a wrist panel. Use Adjust positions again to save. Your grips don't reach the game while adjusting.
- **Laser pointers.** A beam and a dot show where a controller is aiming at the wheel, the VR keyboard or a wrist panel. Close to a wrist panel the beam hides and the touch ring takes over.
- **Touch ring on wrist panels.** A ring shows where your other controller will touch the panel. It shrinks as you get closer and turns into a solid dot on contact.
- **Controller choice on first start.** The editor asks which controllers you use and picks the buttons that open the wheels:
  - Quest, Rift and Pico: double-tap the thumbrest
  - Index, Pimax and Bigscreen: double-click the thumbstick
  - WMR, HP Reverb and Vive: hold the menu button
  You can change it later with the new **Controllers** button.
- **F1 to F12** on the VR keyboard.
- **Custom angle** for wrist panel text, set in the editor or by turning the panel in adjust mode. The editor also has a **Sideways** setting.

### Changed
- **The project is now called OpenXR Hand Menus.** Settings move from `%LOCALAPPDATA%\VRRadialOverlay` to `%LOCALAPPDATA%\OpenXRHandMenus`, and are copied across automatically on first start. Click **Install layer** once after updating.
- **New default placement:**
  - The wheel opens in front of your face, 0.70 m away.
  - Pick wheel items with the stick or the pointer.
  - The wrist panels are smaller and mirrored on both arms.
  - Wrist panels respond to both poking and pointing with the trigger.
- The editor reloads a profile when it changes on disk (for example after adjust mode). If you have unsaved changes, it asks first.
- If the editor can't find the layer files next to it, it now explains to extract the whole zip first.

### Fixed
- A double tap on a touch input no longer fires when the button is really pressed twice (for example, pressing B twice quickly to loot).
- The release zip now extracts correctly with every unzip tool.

## v0.1.0 (2026-11-10)
- First test build: radial wheels, wrist panels, VR keyboard, button combos, layers, macros and controller remaps, plus the profile editor with a guided tour.
