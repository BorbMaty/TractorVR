# CLAUDE.md — Tractor VR

VR tractor simulator for **Meta Quest 2**, built on a John Deere 6195M model.
University project (Sapientia Hungarian University of Transylvania, IRUM).
See `README.md` for controls, `docs/ROADMAP.md` for what's next and `docs/DEVLOG.md` for history and decisions.

## Stack
- Unity **6000.6.3f1** (installed at `C:\Program Files\Unity\Hub\Editor\6000.6.3f1`, Android module + SDK/NDK/JDK included)
- URP 17.6 (asset generated in `Assets/Generated/QuestURP.asset`), Linear color space
- OpenXR 1.18 + XR Management 4.7. Android: Meta Quest feature, Oculus Touch + Touch Plus profiles. Standalone: same profiles (Quest Link)
- Input System 1.20 (Active Input Handling = Input System only)
- Android: IL2CPP, ARM64, minSdk 32, Vulkan, ASTC. Package id `com.sapientia.tractorvr`
- Package versions in `Packages/manifest.json` are the ones bundled with this editor, so they resolve offline. Keep them unless you upgrade the editor.

## Key files
| File | Role |
|---|---|
| `Assets/Scripts/TractorInput.cs` | All input actions, created in code (Quest + keyboard/gamepad fallback) |
| `Assets/Scripts/TractorController.cs` | Kinematic bicycle model, Ackermann steering, wheel spin/steer animation |
| `Assets/Scripts/VRCameraRig.cs` | Cab / third-person views, recenter, seat calibration (PlayerPrefs) |
| `Assets/Scripts/Drivetrain.cs` | Engine RPM/load + 20F/20R gearbox (auto/manual), caps the controller's speed in manual |
| `Assets/Scripts/TractorAudio.cs` | Engine loops (pitch = RPM/1000, cross-fade by load, low-pass in cab), shift, beeper, horn |
| `Assets/Scripts/Dashboard.cs` + `Assets/Shaders/WorldText.shader` | Instrument cluster needles + gear/mode text (depth-tested TextMesh shader) |
| `Assets/Scripts/TractorHud.cs` | Rig-locked speed / gear / rpm readout (TextMesh) |
| `Assets/Scripts/ComfortVignette.cs` + `Assets/Shaders/ComfortVignette.shader` | Edge darkening while turning / changing speed; level on X (PlayerPrefs) |
| `Assets/Scripts/ControllerVisual.cs` | Animates the bones of the official Quest 2 controller models (stick, buttons, trigger, grip; hidden when untracked) |
| `Assets/Models/MetaControllers/` | Quest 2 Touch FBX + texture copied from the Meta XR Core SDK (Oculus SDK License, see README there) |
| `Assets/Scripts/Editor/ProjectSetup.cs` | **Generates everything**: player/XR/URP settings, model import, materials, scene, preview renders |
| `Assets/Scripts/Editor/BuildQuest.cs` | APK build → `Builds/TractorVR.apk` |
| `Assets/Scenes/TractorFlat.unity` | Generated scene (do not hand-edit, see below) |

## Workflow / commands
The scene is **generated**. Change `ProjectSetup.cs`, then re-run it. Hand edits to `TractorFlat.unity` are lost on the next run.

```powershell
$u = "C:\Program Files\Unity\Hub\Editor\6000.6.3f1\Editor\Unity.exe"
# Full setup (settings + scene + previews). The editor must be CLOSED: Unity locks the project to one instance.
& $u -batchmode -quit -projectPath . -buildTarget Android -executeMethod ProjectSetup.Run -logFile setup.log
# Scene only / previews only / APK
#   -executeMethod ProjectSetup.BuildSceneMenu | ProjectSetup.RenderPreviews | BuildQuest.Build
```
- Setup writes a report to `Logs/TractorSetupReport.txt`: bounds, wheel pivots, seat position, every node with its materials.
- Preview PNGs go to `Logs/Previews/` (cab, cab_down, third, side, front, dash = cluster close-up with the wheel turned 90° right, controller, vignette). Use them to check visual changes without a headset.
- In the editor, the same actions are under the **Tractor VR** menu.
- `-openfile` does NOT open a scene when launching the editor. Open `Assets/Scenes/TractorFlat.unity` from the Project window.

## Model facts (John_Deere_6195M.fbx)
- Imports already **metric and facing +Z**: measured wheelbase 2.809 m vs. spec 2.800 m (scale ×0.997 applied).
- Tractor root origin = **rear axle centre on the ground**, +Z forward. Front axle at z = 2.80.
- Wheels: `Front/Rear_Wheel_L/R` (rims) + `Front/Rear_Tire_L/R`. Radii: front 0.757 m, rear 0.955 m. Front track 1.89 m.
- `rotater_01..03_L/R` = front hubs/knuckles. They are parented to the steer pivot (they steer but don't spin).
- Generated pivots: `Wheel_XX` (steer, local Y) > `Spin` (local X) > meshes.
- The **steering wheel is baked into** `SM2_..._Cab_Interier_01`. `ProjectSetup.SplitSteeringWheel` cuts it out at setup time: the mesh is split into
  connected pieces (vertices welded by position), the rim is the largest centred piece 0.3–0.5 m wide, and its PCA plane gives the column axis.
  Pieces lying flat inside the rim (hub cap) go with it. Output: `Generated/CabInterior_NoWheel.asset` + `Generated/SteeringWheel.asset`,
  hierarchy `Tractor/SteeringColumn` (Z = axis, away from the driver) > `SteeringWheel` (spins on local Z) > `WheelMesh`.
  Wheel centre (0, 2.35, 1.09), Ø 0.397 m, rim 23° from horizontal. Right turn = negative local Z rotation.
- Instrument cluster = the glass piece of the interior nearest the wheel (0.257 × 0.109 m). The generated dial face sits 4 mm in front of it.
  From the seat, the hub hides the middle of the cluster and the column shroud hides its lower half, so the readouts live in the upper half of each dial.
- Separate hydraulic parts that can be animated later: `Rear_Holder_*`, `Rear_Forcer_*`, `Front_Forcer_*`, `Front_Small_Forcer_*`, `Front_Main_Forcer_01`, `Door_L/R`.
- **Textures are missing**: the FBX references `..\tex\*.tif` from the original TurboSquid package, which was not included.
  `ProjectSetup.ApplyMaterials` rebuilds the materials by name. `Body_paint` is shared by the body and the rims, so it is
  split by part (wheels → yellow, everything else → JD green). Glass uses URP Unlit, transparent, alpha 0.06.
- Driver eye point (`Tractor/DriverEye`) = interior floor + 1.20 m, rear window + 0.45 m → (0, ~2.75, ~0.54). It can be adjusted in VR (hold left grip).

## Conventions and gotchas
- Vehicle motion runs in **Update, not FixedUpdate**, and the rig follows in LateUpdate. This avoids camera judder in VR. The Rigidbody is kinematic and only exists for future triggers.
- The rig is yaw-only in third person (no pitch/roll), for comfort. Head tracking supplies the rest.
- Recenter = the `Camera Offset` transform cancels the head's local pose, so floor/device tracking origin doesn't matter. It also re-runs on the OS recenter (`trackingOriginUpdated`).
- In batch-mode previews, set `ShaderUtil.allowAsyncCompilation = false`. Otherwise uncompiled transparent variants render as grey placeholder haze, which looks like broken glass but isn't.
- **Comfort is a requirement**: the first headset test caused motion sickness. Keep acceleration ≤ ~1 m/s², the yaw-rate cap (`maxYawRate`) and lateral limit (`maxLateralAccel`), and don't add camera motion the head didn't make (no bob, shake, pitch/roll on the rig).
- The vignette is a head-locked quad 1 m in front of the camera (far enough that both eyes see it centred), ZTest Always, Overlay queue. Its shader needs the `UNITY_VERTEX_OUTPUT_STEREO` macros, or it renders in one eye only on Quest (multiview).
- Controllers are Meta's Quest 2 models (only the FBX/texture, the SDK is not installed). They are authored in Meta's **pointer frame** (origin on the face plate, +Z along the laser), so they're driven by the OpenXR **aim** pose (`pointerPosition/pointerRotation`, not `devicePosition`) and offset so the model's `*_laser_begin` node sits on that pose. Bones: `b_thumbstick`, `b_button_a/b` (`x/y` on the left), `b_trigger_front`, `b_trigger_grip`. Press ranges come from Meta's animation curves (stick ±22°, buttons 1.78 mm, trigger 20°).
- Built-in TextMesh font materials draw **on top of everything** (no depth test). That's fine for the HUD, but world-space text uses
  `TractorVR/WorldText`. Its font atlas is assigned at runtime (`Dashboard.Awake` + `Font.textureRebuilt`).
- Audio is **synthesised** in `ProjectSetup` (`EngineClips`, `ShiftClip`, `BeeperClip`, `HornClip`) → 16-bit WAVs in `Generated/Audio`, imported as PCM
  (seamless loops). The engine loops are exactly 10 engine cycles at 1000 rpm (1.2 s). Quest speakers are weak below ~150 Hz, so the
  "clatter" band (3.2 kHz) carries the diesel character.
- Drivetrain does not change how fast the tractor accelerates. The comfort-limited `TractorController` still owns speed, and the drivetrain
  derives gear/RPM/load from it (and caps the target speed in manual mode).
- The first render in a batch-mode session comes out grey, so `RenderPreviews` throws a warm-up shot away.
- If a preview PNG is open in a viewer, Windows locks it and `RenderPreviews` fails with `IOException ... 1224`. Delete or close the old images first.
- Quest blocks a launch via adb with a "controllers required" dialog while the headset sleeps. That isn't a crash.
- Killing the editor also kills its adb server. A fresh adb server must be re-authorized on the headset (tick *Always allow from this computer*).
- Avoid deprecated APIs (`FindObjectsOfType` etc.): use `FindObjectsByType` / serialized references.
- The FBX is ~58 MB and stored with Git LFS (`*.fbx`). The model is a licensed TurboSquid asset, so keep the repo private.
- `IRUMSim/` (outside this repo) is an older compiled Unity 2022.3 build of the original sim. It is reference only and has no sources.
