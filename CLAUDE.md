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
| `Assets/Scripts/TractorController.cs` | Physics driving: Rigidbody + 4 raycast wheels (suspension, tyre friction circle), comfort-limited speed command → drive force, Ackermann, wheel visuals |
| `Assets/Scripts/Hitch.cs` | Rear 3-point linkage (four-bar solved from model pivots → table θ/φ per position), top link + implement pitch (`SolvePitch`), front linkage + cylinders, drawbar, PTO, tipping input, attach/detach, mass/CoM update every frame |
| `Assets/Scripts/Tipper.cs` | Tipping trailer: rear-hinged body, telescopic ram stages, plumb tailgate, load that pours into runtime mound meshes, mass/CoM from load + angle |
| `Assets/Scripts/Implement.cs` | Implement: kinematic on the linkage mount / ball joint on the drawbar, depth from `TerrainGenerator.Height`, draft force, PTO spinners, TrailRenderer marks |
| `Assets/Scripts/SimpleWheels.cs` | Raycast wheels for the trailer (parking brake while uncoupled) |
| `Assets/Scripts/VRHands.cs` | Per side: tracked hand (XR Hands: palm, fist, index tip) or controller (grip pose, grip button, aim tip) |
| `Assets/Scripts/CabControls.cs` | Grab the wheel (rim angle in `SteeringColumn` space → `TractorController.HoldSteer`), the speed lever (→ `TractorInput.leverThrottle`), and `Swing`s: the 3 remote levers (→ `Hitch.MenuLift`) and the doors |
| `Assets/Scripts/CabPanel.cs` | Rig-locked touch panel (built at runtime from materials set by setup); also sets `CabPanel.Mph` |
| `Assets/Scripts/CommandCenter.cs` | Tractor-mounted display on the right corner post (fuel, coolant, power, linkage, PTO, hours) + hand throttle touch buttons; built at runtime |
| `Assets/Scripts/Editor/QuestManifest.cs` | Gradle post-process: HAND_TRACKING permission + handtracking feature in the Quest manifest |
| `Assets/Shaders/TriplanarLit.shader` | URP lit with triplanar detail + normal map in object space (metres), vertex-colour AO; UsePass Lit shadow/depth |
| `Assets/Scripts/Ground.cs` | `WorldLayout` (yard, barn, shed, fields, roads, mud spots) + `GroundMap` (surface per m²: grip, rolling, draft; control texture) |
| `Assets/Shaders/GroundLit.shader` | Terrain: grass / soil / mud blended by the control texture, world-XZ tiling, macro variation |
| `Assets/Scripts/TerrainGenerator.cs` | Hills built at runtime from `Height(x,z)` (8×8 chunks, 5 m grid, MeshColliders) |
| `Assets/Scripts/Editor/PlayModeTest.cs` | Real scene in editor play mode, 25 s scripted (drive, steer, STOP, every panel button, hitch); exits by itself, code 1 on runtime errors → `Logs/PlayModeTest.txt` |
| `Assets/Scripts/Editor/PhysicsTest.cs` | Offline driving test (scripted sticks, manual `Physics.Simulate`) → `Logs/PhysicsTest.txt` |
| `Assets/Scripts/VRCameraRig.cs` | Cab / third-person views, recenter, seat calibration (PlayerPrefs) |
| `Assets/Scripts/Drivetrain.cs` | Engine RPM/load, torque curve (`AvailableKW`), hand throttle, fuel + coolant, 20F/20R gearbox (auto with skip-shift / manual), caps the controller's speed in manual |
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
- Preview PNGs go to `Logs/Previews/` (cab, cab_down, third, side, front, dash = cluster close-up with the wheel turned 90° right, controller, vignette,
  remotes, commandcenter, doors_open / doors_side / doors_top, …). Use them to check visual changes without a headset.
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
- **Speed lever** = the shift lever in the H-gate at the front of the right armrest (interior piece ~19 cm tall near (0.31, 2.34, 0.64))
  **plus 4 knob pieces** (button block ~8×10×7 cm + buttons, selected by bounds centre within 8 cm of the knob; an all-vertices rule missed the block)
  **plus the stem** (thin rod into the gate, separate piece just below the stalk). The pivot is the bottom of the stem, inside the gate (0.403, 2.189, 0.696).
  Cut out like the wheel, hinge `Tractor/SpeedLever`, knob (0.257, 2.417, 0.606), tilts ±25° around X (+ = forward).
  The right console also has three **hydraulic remote levers**, each three pieces: knob (~2 cm tall), handle (~8 cm), stem (~10 cm), knobs at x 0.42/0.47/0.52, z 0.50.
  `SplitSteeringWheel` finds them by height in the box x 0.38–0.58, y 2.15–2.4, z 0.47–0.53, sorts each kind by x and zips them. Hinges
  `Tractor/RemoteLever_1..3` at the stem bottoms (y 2.155–2.199), swing around X like the speed lever.
- **UVs:** the cab plastics / dashboard have degenerate UVs (median 0.04–0.09 UV units per metre, many zero-area triangles: made for plain colours).
  Only seat cloth (4.6 UV/m), pedals and floor have real UVs. Hence `TractorVR/TriplanarLit` for detail (`ProjectSetup.ApplyDetailMaterials`,
  textures written as PNGs to `Generated/Textures`, normal maps imported as NormalMap). `_ao` material variants are used on meshes with baked AO.
- **Baked AO** (`BakeAmbientOcclusion`): 32 cosine-weighted rays per vertex up to 35 cm against temporary world-space mesh colliders at the scene root
  (the tractor is a non-kinematic Rigidbody, which can't own non-convex mesh colliders), glass excluded. Stored in the vertex colour (red). Mean AO 0.61.
- Instrument cluster = the glass piece of the interior nearest the wheel (0.257 × 0.109 m). The generated dial face sits 4 mm in front of it.
  From the seat, the hub hides the middle of the cluster and the column shroud hides its lower half, so the readouts live in the upper half of each dial.
- Linkage parts have their **pivots on the real hinge points** (verified with tinted renders): `Rear_Holder_Ruler_01` lift arms (rockshaft y 1.588 z −0.119),
  `Rear_Holder_Ruler_02_L/R` lift rods (pivot = arm tip), `Rear_Holder_02` lower links (both in one mesh, pivot y 0.67 z −0.099), `Rear_Holder_Ruler_03_L/R`
  stabilisers, `Rear_Forcer_01/02` lift cylinder barrels/rods, `Rear_Holder_01` top link (stowed upright, not animated), `Front_Holder_02_L/R` front lower
  links (stowed upright). The model is drawn with the rear hitch fully raised. Drawbar (pick-up hitch) at (0, 0.43, −0.78).
  `ProjectSetup.BuildLinkage` puts each moving part under a tractor-aligned `Hinge_*`. Still static: `Front_Small_Forcer_*` (front axle), `Front_Main_Forcer_01` (steering).
- **Top link** `Rear_Holder_01`: pivot = tractor bracket (0, 1.179, −0.511), stowed upright, 0.936 m. Coupled: `Hitch` turns `Hinge_TopLink` onto the
  implement's `topPin` and scales it along its axis (length set at coupling).
- **Front cylinders** `Front_Forcer_01_L/R` (barrels, pivot = frame pin at the top (1.162, 3.337)) and `Front_Forcer_02_L/R` (rods). The model's rod pivot
  sits on the lower link's pivot, so the rods ride on the link's lug below it instead (setup: centroid of link vertices > 4 cm below the pivot).
- **Doors** `Door_L/R`: their pivots sit on the hinge line at the rear edge (±0.744, 2.242, 0.589): rear-hinged like the real JD cab. `BuildDoors` puts them
  under `Hinge_Door_L/R`; + angle = open outward (left around −Y, right around +Y), max 65° (more swings the lower panel into the rear fender).
- **Textures are missing**: the FBX references `..\tex\*.tif` from the original TurboSquid package, which was not included.
  `ProjectSetup.ApplyMaterials` rebuilds the materials by name. `Body_paint` is shared by the body and the rims, so it is
  split by part (wheels → yellow, everything else → JD green). Glass uses URP Unlit, transparent, alpha 0.06.
- Driver eye point (`Tractor/DriverEye`) = interior floor + 1.20 m, rear window + 0.45 m → (0, ~2.75, ~0.54). It can be adjusted in VR (hold left grip).

## Conventions and gotchas
- Vehicle motion is **physics** (phase 2): forces in `FixedUpdate` at **90 Hz** (`TimeManager` Fixed Timestep; set via SerializedObject in setup,
  because `Time.fixedDeltaTime` set in the editor doesn't persist). The Rigidbody uses **interpolation**, and the rig follows in LateUpdate, so the camera is smooth.
  Steering + wheel visuals stay in `Update`. `SpeedMs` is measured, and `Acceleration` is smoothed (use that, not per-frame speed differences).
- Speed control: the phase 1 comfort rules make a speed *command*. The drive force tracks it, compensating slope + rolling resistance, limited by
  engine power (145 kW) and grip. The command is clamped to ±1.5 m/s of the real speed (anti wind-up). Parking brake = anchor at the stop position (no creep).
- Wheel rays ignore anything with a Rigidbody (`GroundRay`). Movable objects meet the body/wheel **box colliders** instead. A ray riding up a bale launched
  the tractor in the first test. The tractor colliders are on layer 2 (Ignore Raycast) with a low-friction material, because a grippy flat front climbed onto rolling bales.
- Implements: +Z of an implement points to the tractor. `hitchPoint` is its coupling point. Mounted ones go kinematic, parented to `RearMount`/`FrontMount`.
  The trailer stays dynamic, with a `ConfigurableJoint` at the drawbar eye. Collisions between a **mounted** implement and the tractor are ignored while
  coupled. The **trailer keeps colliding** (joint `enableCollision = true`). After detaching, collisions come back as soon as nothing overlaps (`OverlapsTractor`).
  Detaching a trailer is refused above 25° articulation (its jack would come down inside the tractor).
- Tractor colliders: a main block (full width, from z −0.55 forward), a narrow rear block (1 m wide, hitch/frame) and four wheel blocks.
  A single full-width box out to the hitch limited a coupled trailer to ~12° of swing. The trailer body starts 1.6 m behind its eye, so it swings ~50° before touching the tyres (joint limit 70°). Plough draft is fed forward into the drive force (a P-only speed controller left a 4 km/h error).
- Rear linkage reach: link ends 0.43–1.27 m, so 3-point implements need their lower-link pins at ~0.75 m when parked.
- Three-point implements with a `topPin` pitch with the hitch: `RearMount` is rotated by `Hitch.RearPitch`, and implements take the mount's rotation.
- `Implement.Depth` for an implement without ground points (front weight) = how far its coupling point is below its parked height, so the
  "lower it first" detach rule works for it too.
- Editor tests teleport the tractor: anything still coupled (especially the trailer on its joint) gets dragged and blows up the physics.
  Uncouple at the end of each scenario.
- Implement models are generated: primitives via `Part`, procedural meshes via `MeshPart` (`TyreMesh`, `MouldboardMesh`, `Tipper.Mound`), saved as `Generated/Impl_*.asset`.
- The **grips are for grabbing** (wheel, lever). Seat adjust moved to the cab panel (`TractorInput.seatAdjust`; Left Shift in the editor).
  Steering by hand: `HoldSteer(angle)` while held. After `ReleaseHeldSteer()` the angle is latched until |left stick| > 0.15. The comfort lock still clamps it.
- XR Hands: `XRHandSkeletonDriver.jointTransformReferences` is null on a component added from code, so set a new list before `FindJointsFromRoot`.
  Hand joints are in tracking space (children of `Camera Offset`). OpenXR feature `HandTracking` is enabled for Android + Standalone.
- **Ground**: physics asks `GroundMap.SurfaceAt(point)` (tyre grip and rolling resistance are per wheel; `grip` / `rollingResistance` on the
  controller and trailer are multipliers). Roads and the yard are visual ribbons/patches 3–7 cm above the terrain triangles
  (`TerrainGenerator.SurfaceHeight` follows the triangles, not the smooth `Height`). Hard surfaces have draft 0 = the plough can't enter.
- Outside play mode `Destroy` does nothing: components removed by game code that also runs in the editor tests need `DestroyImmediate` there
  (the trailer's joint survived detaching in `PhysicsTest` and dragged the trailer along with every teleport).
- Teleports (`ResetTractor`, the test's `Place`) call `ResetSuspension`: it clears the spring history and straightens a latched steering wheel.
- **Quest GPU budget** (13.9 ms at 72 Hz): the app is GPU-bound. Measure with `adb logcat -s VrApi` (look at `FPS=`, `App=` ms, `GPU%`) while the
  headset is worn (it only renders then). In place: fixed foveated rendering (`Foveation`, OpenXR Foveated Rendering feature without eye tracking),
  GroundLit samples only the layers present + Blinn-Phong, TriplanarLit projects from the dominant axis only, terrain casts no shadows,
  shadow map 1024, the tractor's shadow comes from a 416-triangle `ShadowProxy` (model renderers don't cast), FBX mesh LODs (625k → 355k / 233k / 151k),
  `QualitySettings.meshLodThreshold` 1.5. Keep new shaders cheap (few texture reads, Blinn-Phong) and new meshes from casting shadows unless needed.
- Run `PhysicsTest` after changing the vehicle, and `PlayModeTest` after changing anything that runs in the game
  (`Unity.exe -batchmode -projectPath . -executeMethod PlayModeTest.Run` without -quit). State survives the domain reload via SessionState. It has caught every physics bug so far, without a headset.
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
- The comfort-limited `TractorController` still owns speed; the drivetrain derives gear/RPM/load from it, caps the target speed in manual mode
  (hand throttle: at least the gear's speed at that rpm), and since batch 2 sets the **power limit** (`AvailableKW` = torque curve at the current RPM).
  When the force is power-limited the engine revs to peak torque (pulling away with a plough in the ground stalled at idle otherwise).
- Anything the editor tests step by hand (`FrameUpdate(dt)` / `PhysicsStep`) must use the `dt` it is given: no `Time.time`, and pass `dt` to
  `SmoothDamp` (its default is `Time.deltaTime`). The drivetrain's RPM sat at idle in `PhysicsTest` until this was fixed.
- Long-running accumulators (fuel, engine hours) are doubles: one 90 Hz step of idle fuel use is below float precision at ~300 L.
- Don't `CreatePrimitive` under the tractor at runtime: its MeshCollider lands on the dynamic Rigidbody until the deferred `Destroy` (physics error,
  caught by `PlayModeTest`). Use a GameObject + MeshFilter with `Resources.GetBuiltinResource<Mesh>("Quad.fbx")` (see `CommandCenter.Quad`).
- The first render in a batch-mode session comes out grey, so `RenderPreviews` throws a warm-up shot away.
- If a preview PNG is open in a viewer, Windows locks it and `RenderPreviews` fails with `IOException ... 1224`. Delete or close the old images first.
- Quest blocks a launch via adb with a "controllers required" dialog while the headset sleeps. That isn't a crash.
- Killing the editor also kills its adb server. A fresh adb server must be re-authorized on the headset (tick *Always allow from this computer*).
- Avoid deprecated APIs (`FindObjectsOfType` etc.): use `FindObjectsByType` / serialized references.
- The FBX is ~58 MB and stored with Git LFS (`*.fbx`). The model is a licensed TurboSquid asset, so keep the repo private.
- `IRUMSim/` (outside this repo) is an older compiled Unity 2022.3 build of the original sim. It is reference only and has no sources.
