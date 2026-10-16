# Tractor VR — John Deere 6195M on Meta Quest 2

Unity **6000.6.3f1** · URP · OpenXR (Meta Quest feature) · Input System

![third person](docs/images/front.png)

Docs: [Roadmap](docs/ROADMAP.md) · [Development log](docs/DEVLOG.md) · [CLAUDE.md](CLAUDE.md) (project guide)

## Phase 1 (done)
- John Deere 6195M model (`Assets/Models/John_Deere_6195M.fbx`), real scale (wheelbase 2.80 m)
- Flat 2 km × 2 km field with a 10 m grid and marker posts every 25 m
- Two viewpoints: **cab** (driver's seat) and **third person** (behind and above)
- Driving with the Touch controller thumbsticks: Ackermann steering, wheel spin, front hubs steer
- Small speed readout (km/h, F/N/R, view)
- **Comfort**: edge vignette while turning / changing speed (off/low/high on X), gentle acceleration, yaw-rate and lateral-acceleration limits
- **Controllers visible**: official Quest 2 Touch models with moving stick, buttons, trigger and grip

## Controls

| Quest 2 controller | Keyboard (editor) | Action |
|---|---|---|
| Left stick ← → | A / D | Steering |
| Right stick ↑ | W | Drive forward (stick amount = target speed, max 40 km/h) |
| Right stick ↓ | S | Brake, then reverse when stopped (max 15 km/h) |
| Release stick | — | Coast to a stop |
| **A** | C | Switch view: cab ↔ third person |
| **B** | R | Recenter the view |
| **X** (left) | V | Comfort vignette: off / low / high (saved, default high) |
| **Hold left grip** + sticks | Left Shift | Seat adjust: left stick ↑↓ = height, right stick = forward/back/sideways (saved) |

## Project layout
```
Assets/Scripts/TractorInput.cs        input bindings (Quest + keyboard/gamepad fallback)
Assets/Scripts/TractorController.cs   driving model (kinematic bicycle model, Ackermann, wheel animation)
Assets/Scripts/VRCameraRig.cs         cab / third-person views, recenter, seat calibration
Assets/Scripts/TractorHud.cs          speed readout
Assets/Scripts/ComfortVignette.cs     comfort vignette (+ Assets/Shaders/ComfortVignette.shader)
Assets/Scripts/ControllerVisual.cs    animates the controller models
Assets/Models/MetaControllers/        Quest 2 controller models (Meta XR Core SDK, Oculus SDK License)
Assets/Scripts/Editor/ProjectSetup.cs settings + scene generator  (menu: Tractor VR > Run Full Setup)
Assets/Scripts/Editor/BuildQuest.cs   APK build                      (menu: Tractor VR > Build Quest APK)
Assets/Scenes/TractorFlat.unity       generated scene
```
`ProjectSetup` can be run as many times as you like. It regenerates the scene from the FBX, so
hand edits to `TractorFlat.unity` are overwritten. Change the generator instead, or save the
scene under a different name.

## Running on the Quest 2
1. Turn on Developer Mode on the headset (Meta Horizon phone app → Devices → Developer Mode).
2. Connect it over USB-C and accept "Allow USB debugging" inside the headset (tick *Always allow from this computer*).
3. In Unity: **File → Build Profiles → Android**, choose the Quest under *Run Device*, then **Build And Run**.
   Or install the ready APK: `adb install -r Builds/TractorVR.apk`
   (adb is at `C:\Program Files\Unity\Hub\Editor\6000.6.3f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools`).
4. On the headset: App Library → filter **Unknown Sources** → *Tractor VR*.

Testing in the editor without a headset: press Play and drive with the keyboard.
With Quest Link / Air Link, Play mode runs in the headset (Standalone OpenXR is enabled).

## Notes
- The FBX references external textures (`tex\*.tif`) that weren't included with the model.
  The setup script rebuilds the look from the material names instead (green paint, yellow rims, clear glass).
  If the original texture folder turns up, put it in `Assets/Models/tex/` and re-run the setup.
- Phase 1 physics is kinematic: there are no collisions, and the tractor drives on the flat plane.
- Turning is deliberately limited for comfort (max 25°/s yaw, 2.5 m/s² lateral). At speed the tractor turns wide, so slow down for tight turns.
- If you feel motion sick: set the vignette to high (X), prefer the cab view, keep sessions short.

## Next phase ideas
- Steering wheel in the cab turning with the steering input (it is part of the interior mesh and needs to be split out)
- Engine RPM, gearbox (PowrQuad / AutoQuad), engine and gear sounds
- Front loader / 3-point hitch / PTO with the hydraulic parts that are already separate in the model
  (`Rear_Holder_*`, `Rear_Forcer_*`, `Front_Forcer_*`)
- Terrain, physics (WheelColliders), implements, field work
