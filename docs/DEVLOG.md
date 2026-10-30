# Development log

## 2026-10-16: Phase 1 setup

**Starting point.** The folder `Irum_proj/IRUMSim/` contained a *compiled* Unity 2022.3.4f1 Windows build
("IRUMSim", Sapientia Hungarian University of Transylvania), with no sources, plus the model
`John_Deere_6195M.FBX`. A new Unity project was created: `Irum_proj/TractorVR/`.

**Engine choice.** Unity 6000.6.3f1 was already installed with Android support, which suits the Quest 2.
URP (recommended for Quest) + OpenXR with the Meta Quest feature (the vendor-neutral standard) + Input System.
All package versions are the ones bundled with the editor, so the project resolves offline.

**Automation.** All project configuration and scene construction lives in `Assets/Scripts/Editor/ProjectSetup.cs`
and runs in batch mode. That makes the setup reproducible and reviewable: the scene can be regenerated at any time.

### Model analysis
- Binary FBX 7.x, 65 model nodes, 31 materials, 34 texture references.
- Already metric and +Z forward in Unity. Measured wheelbase 2.809 m vs. spec 2.800 m, so scale ×0.997.
- Wheels and tires are separate objects. `rotater_*` nodes sit exactly at the front wheel centres (hubs/knuckles).
- The steering wheel is baked into the cab interior mesh (not separately animatable yet).
- **Textures are missing**: the references point to `..\tex\*.tif` / `D:\Turbosquid_2017\...`. They are not embedded and not on this PC.
  Fix: materials are rebuilt from their names (`Body_paint` → JD green, and yellow on wheel parts; glass → near-clear unlit).

### Driving model (phase 1)
- Kinematic bicycle model around the rear axle (the tractor origin is placed there), Ackermann angles per front wheel,
  wheel spin from distance / radius, steering lock reduced at speed.
- Speed governor: stick position = target speed. Moving the stick against the motion brakes; releasing it coasts.
- Runs in `Update` so the VR camera riding on the tractor never judders (no physics interpolation issues).
- No collisions yet. That is fine for a flat world, and the move to real physics is planned for phase 2.

### VR camera
- Rig > Camera Offset > Main Camera (TrackedPoseDriver). Recentering moves the Camera Offset so the eye lands exactly on
  the anchor (seat or third-person point), independent of the tracking origin.
- Cab view: the rig is locked to `Tractor/DriverEye`. Third person: 9.5 m behind, 4.2 m up, yaw-only with smoothing (comfort).
- The seat position was estimated from the cab geometry and checked with preview renders. It can be fine-tuned in the headset.

### Issues hit
| Problem | Cause | Fix |
|---|---|---|
| Tractor rendered white | FBX textures missing | Name-based URP materials |
| Cab view looked milky/grey | Batch-mode async shader compilation drew placeholder shaders | `ShaderUtil.allowAsyncCompilation = false` for previews (not an issue in builds) |
| Editor opened an empty scene | `-openfile` doesn't load scenes | Open the scene from the Project window |
| Couldn't open the editor during the batch build | Unity locks a project to one instance | Stopped the batch build; build from the editor menu |

### Preview renders

| Cab | Cab (looking down) |
|---|---|
| ![cab](images/cab.png) | ![cab down](images/cab_down.png) |

| Third person | Side | Front |
|---|---|---|
| ![third](images/third.png) | ![side](images/side.png) | ![front](images/front.png) |

## 2026-10-16 — First deploy to Quest 2
- Built `Builds/TractorVR.apk` in batch mode (`BuildQuest.Build`, 57.8 MB, 0 errors) and installed it over USB with the
  adb bundled in Unity's Android SDK (`...\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe`). The app launches; it is under *Unknown Sources*.
- Closing the editor also kills its adb server. The fresh server had to be re-authorized on the headset ("Allow USB debugging").
- Logcat shows `ClassNotFoundException: com.google.android.play.core.assetpacks.AssetPackManager`. This is harmless: Unity probes for
  Play Asset Delivery, which Quest doesn't have.
- Headset test: everything works (controls, views, seat position, wheel spin/steer look great). The tester got **motion sickness**,
  so comfort (vignette, gentler acceleration and steering, a comfort setting) is now the next item on the ROADMAP.

## 2026-10-16: Comfort pass + visible controllers
The first headset session caused motion sickness, so this pass reduces the motion the inner ear notices.

**Gentler motion** (`TractorController`)
- Acceleration 1.8 → 1.0 m/s², braking 5 → 2.5, coast 1.0 → 0.8, steer rate 70 → 35°/s.
- Squared stick response for steering: fine control near centre.
- New steering limit instead of the old `steerAtMaxSpeed` factor: the lock angle is capped so the yaw rate stays ≤ 25°/s
  and v·ω ≤ 2.5 m/s². At 10 km/h this allows ~24° (radius ~6 m), at 40 km/h ~3° (radius ~50 m), which is also more realistic
  than before (the old model allowed ~74°/s yaw and ~14 m/s² lateral at full speed).

**Comfort vignette** (`ComfortVignette.cs`, `TractorVR/ComfortVignette` shader)
- Darkens the edge of the view in proportion to the rig's yaw rate and the tractor's speed change. It fades in fast and out slowly.
- Head-locked quad 1 m ahead (an eye offset of 32 mm then shifts it by only ~2°; at 0.1 m it would be off-centre by ~18° per eye).
- Shader works in view angles: clear inside `_InnerDeg`, black beyond `_OuterDeg`. Low: 38°/52°, High: 24°/40° at full strength.
- **X** cycles Off / Low / High (PlayerPrefs, default High). The HUD shows the level for 2.5 s.

**Controllers** (`ControllerVisual.cs`, `ProjectSetup.BuildController`)
- Stand-in Quest Touch models from primitives + a generated torus ring, under `Camera Offset`, posed with TrackedPoseDriver on the
  grip pose. The stick tilts, buttons sink and turn yellow when pressed, and the trigger and grip move. Hidden while not tracked.
- The official Meta controller models would need the Meta XR SDK. That can come later with hands/grab interaction.

**Previews**: `controller.png` and `vignette_high.png` were added. The first render of a batch session came out grey, so a warm-up
shot is now thrown away.

| Controller (stand-in) | Vignette, High, full strength |
|---|---|
| ![controller](images/controller.png) | ![vignette](images/vignette_high.png) |

## 2026-10-16: Real Quest 2 controller models
The stand-in primitive controllers looked wrong in the headset (the tracking ring sat between the buttons). They are replaced by
the official **Quest 2 Touch models**, copied from the Meta XR Core SDK 207.0.1 (downloaded from Meta's npm registry; only
the two FBX files and the texture were taken, the SDK itself is not installed). They're under the Oculus SDK License, which is fine for a Quest app in a private repo.

- **Pose:** the bone positions showed that the models are authored in Meta's pointer frame (face plate at the origin, stick
  and buttons at y≈0, the laser along +Z), not the OpenXR grip frame. So the TrackedPoseDriver now uses the **aim pose**
  (`pointerPosition/pointerRotation`), and the model is offset so its `*_laser_begin` node coincides with that pose.
- **Animation:** the FBX is rigged. `ControllerVisual` drives the bones directly with the ranges read from Meta's animation curves:
  stick ±22°, A/B (X/Y) 1.78 mm, trigger 20°, grip (1.3°, 7.4°, 4.1°). No Animator is needed.
- Imported with Animation Type None and no materials. One URP Lit material uses the albedo texture.

| Controllers, front | Side |
|---|---|
| ![controllers](images/controller.png) | ![side](images/controller_side.png) |

## 2026-10-16: Headset check
- Real controller models, comfort vignette and the gentler driving were tested on the Quest 2: all good.

## 2026-10-16: Session summary
What happened today, in order:
1. First APK build (batch mode) and install over adb. The app ran first time, and the wheels and steering looked right.
2. Headset test: controls, views and seat were fine, but the tester got **motion sick**.
3. Comfort pass: gentler acceleration/braking, yaw-rate and lateral-acceleration limits, squared steering response, edge vignette with
   off/low/high on X.
4. Visible controllers: first primitive stand-ins (wrong ring placement), then the official Quest 2 models from the Meta XR Core SDK on the aim pose.
5. Headset re-test: all good.

Workflow lessons (also in CLAUDE.md): the editor must be closed for batch builds; closing it resets adb authorization unless
*Always allow* is ticked; a launch via adb is blocked while the headset sleeps; preview PNGs open in a viewer get locked by Windows.

## 2026-10-17: Phase 1 closed, phase 2 started (steering wheel + drivetrain)
Phase 1 was accepted after the headset tests. For phase 2 the first block chosen was **steering wheel + drivetrain**, because those
make the biggest difference to how the tractor feels in the headset.

**Steering wheel, without Blender** (`ProjectSetup.SplitSteeringWheel`)
- The wheel is baked into `Cab_Interier_01` (37 177 vertices, 11 submeshes). A temporary analysis split that mesh into connected pieces
  (union-find over triangles, with vertices welded by position so UV seams don't break pieces apart). The largest piece turned out to be the rim:
  centred at x = 0, 0.40 m across.
- The setup script now does this automatically. Rim = largest centred piece 0.3–0.5 m wide. Its best-fit plane (PCA via power iteration)
  gives the column axis (rim 23° from horizontal). Every piece lying flat inside the rim (the hub cap) goes with it. The interior is rebuilt
  without those triangles, and the wheel becomes its own mesh in a pivot frame (`SteeringColumn` > `SteeringWheel` > `WheelMesh`).
- `TractorController` turns it by `SteerAngle × 13` (40° lock → ±520°, ~2.9 turns lock to lock). Right turn = clockwise as seen by the driver.

**Instrument cluster** (`Dashboard.cs`)
- Placed on the glass piece nearest the wheel (0.257 × 0.109 m): a generated face texture with two tick rings (0–3000 rpm with a red zone
  above 2250, 0–60 km/h), needles, scale numbers, direction + gear, AUTO/MAN.
- The built-in TextMesh font shader ignores depth, so the text showed through the wheel hub. A small `TractorVR/WorldText` shader fixes it
  (with stereo macros for Quest multiview).
- From the seat the hub hides the middle of the cluster and the column shroud hides the lower half, so the readouts were moved to the upper half of the dials.
  The HUD also shows speed, direction, gear and rpm now.

**Drivetrain** (`Drivetrain.cs`), loosely after the 6195M's AutoQuad Plus 20/20
- 20 forward / 20 reverse gears, named A1–E4 (5 ranges × 4 powershift steps), geometric from 2.4 to 50 km/h at the rated 2100 rpm.
  Engine idle 850, high idle 2250 rpm.
- **Auto**: pulls away in B2, shifts up above 1950 rpm and down below 1300 rpm, at least 0.5 s between shifts.
  **Manual**: right stick flick → / ← shifts. The gear caps the speed (stick = share of the gear's top speed), and downshifts that would over-rev are refused.
- RPM is locked to the wheels through the gear, with clutch slip (idle + up to 250 rpm) when pulling away. Load (for sound) = accelerating share + rolling resistance.
- Design decision: the drivetrain does **not** change the acceleration. The comfort limits from phase 1 stay in charge, and the drivetrain is derived from the motion.
  Top speed is raised from 40 to the real 50 km/h. The lateral/yaw limits still keep turns at speed gentle.

**Sounds** (`TractorAudio.cs`, all synthesised by `ProjectSetup`, no third-party samples)
- Engine: two loops at 1000 rpm (light/heavy load). Six-cylinder firing at 50 Hz excites exhaust/body/block resonances (85/240/700 Hz), plus a short
  3.2 kHz clatter burst and some breath noise per firing. The cylinders differ slightly in strength for the uneven diesel beat. Each loop is exactly 10 engine cycles
  (1.2 s), so it repeats seamlessly. The loops are pitched by RPM/1000 and cross-faded by load (equal power), and low-passed at 2.2 kHz in the cab view.
- Spectrum check of the first version: 93 % of the energy was below 300 Hz and only 1 % in the clatter band. Quest speakers barely reproduce that,
  so the clatter was boosted and the loops normalised separately.
- Shift clunk (thump + click + hydraulic hiss), 1 kHz reverse beeper (whenever the shuttle is in R), two-tone 400/500 Hz horn on the right trigger.

**New controls**: right stick flick → / ← = shift (switches to manual), Y = auto/manual (saved), right trigger = horn.
Keyboard: X / Z, T, H.

**Build**: setup + APK build in batch mode without errors (59.3 MB), installed over adb. The launch check was blocked by the
"controllers required" dialog (headset asleep), so a **headset test is still to do**.

| Cab | Cluster close-up (wheel turned 90° right) |
|---|---|
| ![cab](images/cab.png) | ![dash](images/dash.png) |

## 2026-10-17: Phase 2 headset check
- Steering wheel (direction and ratio), instrument cluster, gearbox (auto/manual, shifting), engine/shift/beeper/horn sounds were tested on the Quest 2: all OK, no tuning needed.

## 2026-10-17: Phase 2 block 1, physics + terrain
Order for the rest of phase 2, agreed today: **physics + terrain → hydraulics + implements → VR interaction**.

**Driving physics** (`TractorController`, rewritten)
- Rigidbody, 7.5 t, centre of mass (0, 1.05, 1.12): 40 % on the front axle, like the unballasted 6195M.
- **Raycast suspension** instead of WheelColliders (more control, cheaper on Quest). Per wheel, a ray runs from 0.15 m above the wheel centre.
  A preloaded spring-damper makes the body sit exactly at the model's height at rest (front 300 kN/m for axle suspension + tyre, rear 500 kN/m tyre only,
  damping ratio 0.45). Wheel meshes move with the spring.
- **Tyres**: the drive force is shared by wheel load (MFWD). The lateral force cancels the wheel's sideways sliding. Both are limited by a friction circle (μ 0.8 × load).
- **Comfort kept**: the phase 1 rules (1 m/s² acceleration, yaw ≤ 25°/s, lateral ≤ 2.5 m/s²) now make a speed *command*. A controller turns it into a
  drive force, compensating slope and rolling resistance, limited by engine power (145 kW) and grip.
  On a steep climb the tractor runs out of power and slows down. The command waits for it (anti wind-up).
- **Parking brake**: at standstill the tractor is anchored to the spot where it stopped, both along and across.
- Physics at **90 Hz** with interpolation, and the camera rig follows in LateUpdate. The cab view now pitches and rolls with the tractor on slopes.
  The comfort vignette now reacts to all view rotation (not just yaw) and uses a smoothed acceleration.
- The drivetrain's load (engine sound) is now the real share of engine power in use, so the engine labours on climbs.
- Rolled over or fallen off the world: it resets to the start after 3 s.

**Terrain** (`TerrainGenerator`): 2 × 2 km built at startup from a height function (two octaves of Perlin noise), 8 × 8 chunks with a 5 m grid,
mesh colliders, and normals from the height function. Flat within 60 m of the start, blending into the hills by 160 m. Nothing is stored in the repo.
The first version (±9 m, max 5.7°) looked flat in the renders, so the hills were raised (max 10.6°), the sun lowered to 35° and light haze added (250–1300 m).
Six round hay bales (350 kg) near the start can be pushed.

**Offline physics test** (`Tractor VR → Run Physics Test`, `PhysicsTest.cs`): steps the controller and `Physics.Simulate` by hand in batch mode with scripted sticks.
It found three bugs before any headset test:

| Found | Cause | Fix |
|---|---|---|
| Physics ran at 50 Hz | `Time.fixedDeltaTime` set from the editor isn't saved | Write `TimeManager` Fixed Timestep (rational count/rate) via SerializedObject |
| Driving into a bale flipped the tractor (pitch −56°, roll 49°) | Wheel rays hit the bale, and the bump stop launched the front | Wheel rays ignore rigidbodies, wheel box colliders push obstacles, load capped at 6× static |
| Then the front climbed onto the bale and got stuck | Friction between the flat front and the rolling bale surface pulled the nose up | Low-friction physics material on the tractor colliders |
| Parked tractor crept ~2 cm / 5 s | Velocity-only brake, and sideways only velocity damping | Hold anchored to the stop position, along and across |

Final test results:

| Scenario | Result |
|---|---|
| Settle | Rest height −3 mm, all springs at their static point |
| Full throttle | 1.00 m/s² up to 50 km/h at 14 s, pitch −0.3° |
| Full lock at 16 km/h | Yaw 24.7°/s (cap 25), lateral 1.95 m/s² |
| Full lock at 29 km/h | Yaw 17.4°/s, lateral 2.48 m/s² (cap 2.5) |
| Steepest slope 10.6° | Power-limited at ~28 km/h (0.23 m/s²), pitch −11° → +4° over the crest at ~1°/s |
| Park on 4.9° | 0.0 cm creep in 5 s |
| Hay bale at 6.5 km/h | Bale pushed 9 m aside, speed dip < 0.3 km/h, pitch unchanged |

**Build**: setup + APK without errors (59.6 MB), installed. The launch was blocked by the "controllers required" dialog (headset asleep), so it is **not headset-tested yet**.
Main open question: whether the cab pitching/rolling on the hills is comfortable.

| Hills (start area is flat on purpose) | Cab |
|---|---|
| ![hills](images/hills.png) | ![cab](images/cab.png) |

## 2026-10-17: Physics + terrain headset check
- Hills, cab pitch/roll on slopes, driving feel and pushing bales were tested on the Quest 2: all good, no comfort problems, no tuning needed.

## 2026-10-17: Phase 2 block 2, hydraulics + implements
**Linkage.** A temporary script tinted each `Rear_*`/`Front_*` part and logged its pivot. The pivots sit on the real hinge points
(rockshaft, lower link pins, arm tips), so the parts can be turned in place. Each moving part gets a tractor-aligned hinge.
- Rear three-point hitch = four-bar linkage. The lift arm angle θ moves the arm tip, and the lower link angle φ is solved (bisection) so the lift rod keeps its length.
  The rod's bottom end and the link ends come from the mesh (farthest vertex from the pivot). A θ → φ table is built at start-up.
  The lift cylinders follow: the barrel turns on its base, and the rod rides on the arm. Range: link ends 0.43 m (lowest the geometry closes) to 1.27 m (the model is drawn fully raised).
- Front linkage: the lower links fold from upright (stowed, as modelled) to forward-down (tips 0.42 m).
- Drawbar found automatically as the rearmost low centre point of the rear frame: (0, 0.43, −0.78).

**Implements** (no models came with the tractor, so they're built from primitives by the setup script):

| Implement | Coupling | Behaviour |
|---|---|---|
| 3-furrow plough, 1.2 t | rear hitch | Depth = shares below the ground (terrain height function). Draft 30 kN at 25 cm, + 3 %/km/h. Depth control lifts it back to 25 cm and the lever can't push it deeper. Brown furrow trail |
| 3 m front disc mower, 0.9 t | front linkage | 6 discs spin with the PTO, 30 kW while mowing, floats on the ground, light green mown strip |
| Tipping trailer, 2.5 t | drawbar | Stays a physics body: ball joint (yaw ±70°, roll ±25°), its own raycast wheels, parking brake and jack while uncoupled |

- Attach: back up or drive up so the coupling points are within 1.5 m and 30°, at about the right height (±0.35 m), at standstill, then press attach.
  The HUD shows the nearest implement's distance while you line up, and why an attach was refused. The implement slides onto the coupling over 0.8 s.
- Mounted implements change the tractor's mass and centre of mass. With the plough on, the front axle load drops from 14.7 to ~8 kN per wheel.
- PTO: 1000 rpm at 1900 engine rpm. With the PTO on, AUTO shifts to keep the engine near PTO speed, and the PTO's power is taken off the drive power and added to the engine sound load.

**Offline test** (new scenarios I–L):

| Scenario | Result |
|---|---|
| I. Hitch sweep | Link ends 0.43 → 1.27 m, arms −60°/links −42° from the modelled pose |
| J. Plough | Couples, lowers to 0.27 m (depth control), **7.1 km/h at 30.4 kN draft, 48 % engine power**, raises, parks upright |
| K. Mower | Couples on the lowered front linkage, PTO on, floats at 0.01–0.02 m, mows at 9.2 km/h, CoM moves forward 0.38 m |
| L. Trailer | Coupling gap 0.0 cm throughout, 30 km/h, 8.6° articulation at full lock, brakes, reverses |

Bugs the test found:

| Found | Fix |
|---|---|
| Plough couldn't couple: its pins (0.55 m) were below the linkage's lowest point plus tolerance | Pins at 0.75 m (taller headstock) |
| Ploughing only reached 2.7 km/h of 7: proportional speed control leaves a fixed error under a steady 26 kN pull | Draft force fed forward into the drive force |
| The plough test ran into the parked mower | Mower parked out of the way |

| Hitch low | Hitch high |
|---|---|
| ![low](images/hitch_low.png) | ![high](images/hitch_high.png) |

| Plough at work | Parked implements |
|---|---|
| ![plough](images/plough_attached.png) | ![implements](images/implements.png) |

**Build**: setup + APK without errors, installed. The launch was blocked by the "controllers required" dialog, so it is **not headset-tested yet**.

## 2026-10-17: Block 2 headset check + trailer collision fix
Headset test of hydraulics + implements: linkage controls fine, attaching fine ("takes practice"), ploughing / mowing / towing fine.
**Bug: the tractor could drive into the trailer.**

Cause: while coupled, all collisions between implement and tractor were switched off (`IgnoreCollision`, so the two wouldn't fight at the
coupling), and the trailer's ball joint also had `enableCollision = false`. After detaching, collisions only came back at 8 m apart.

Fix:
- The trailer keeps colliding while coupled. After detaching (any implement), collisions come back as soon as nothing overlaps.
- New test scenario **M** (jackknife in reverse, then reverse into the parked trailer) measures the overlap with `Physics.ComputePenetration`.
  It found two more problems:
  1. The trailer could only swing ~12°: the tractor had **one full-width box out to the hitch**, and the trailer body started only 0.6 m behind its eye.
     Now the tractor has a main block + a narrow rear block + wheel blocks, and the trailer has a realistic 1.6 m drawbar. It now swings to the joint limit (70°) with no overlap.
  2. Detaching at 70° brought the jack down inside the tractor, and the push-out **rolled the tractor 48°**. Detaching now needs the trailer within 25°
     ("STRAIGHTEN UP TO DETACH"), and collisions stay off until nothing overlaps.
- Final scenario M: jackknife deepest overlap 0.0 cm. Reversing into the parked trailer pushes it 1 m, deepest overlap 0.0 cm. All other scenarios unchanged.
- Headset re-check of the fix: the tractor and trailer collide as intended (jackknife and reversing into it).

## 2026-10-17: Phase 2 block 3, VR interaction
**Finding the controls.** A temporary script split the right console of the cab interior into colour-coded pieces. Found: the shift lever in an
H-gate at the front of the armrest (one piece), three hydraulic remote levers, and a small lever at the right rear.
The shift lever becomes a **speed lever** (like an IVT tractor's): cut out of the mesh with the steering wheel code, pivot at the bottom of the stalk.

**Hands** (`VRHands`): each side is either a tracked hand or a controller, whichever is active.
- Controller: grab point = OpenXR *grip* pose (new trackers, separate from the aim pose the controller models use), grabbing = grip button, poke tip = front of the controller.
- Hand tracking (XR Hands 1.9.0, bundled with the editor, works offline): grab point = palm, grabbing = a fist (average index/middle/ring tip–palm distance
  < 6.5 cm, open again > 8.5 cm), poke tip = index fingertip. The hand meshes come from the XR Hands *HandVisualizer* sample, driven by `XRHandSkeletonDriver` (all 26 joints mapped).
- The Quest needs `com.oculus.permission.HAND_TRACKING` + `oculus.software.handtracking` in the manifest. The OpenXR package only adds them for
  its Meta hand-mesh feature, so `QuestManifest` (Gradle post-process) adds them. Checked in the built APK with `aapt dump permissions`.

**Cab controls** (`CabControls`)
- Steering wheel: grab within 7 cm of the rim. The hand's angle around the column (in `SteeringColumn` space) turns the wheel, averaged over both hands,
  each hand tracked separately so hand over hand works. Wheel angle / 13 = steering angle, still clamped by the speed-dependent comfort lock (the wheel slips in the hand beyond it).
  Let go: the wheel stays (like real hydrostatic steering) until the left stick is moved.
- Speed lever: grab the knob, tilt ±25° → the same speed command as the right stick. A neutral detent of ±8 %. It stays where it was let go (cruise), so both hands
  can be on the wheel. Moving the right stick puts it back to neutral.

**Cab panel** (`CabPanel`): 14 touch buttons at the right front of the cab (where the 6M's CommandCenter display is), riding with the rig.
They cover view, recenter, comfort, gearbox, hitch up/down, front up/down, PTO, attach/detach, seat adjust on/off, seat reset, km/h ↔ mph, and reset tractor.
Open with the left menu button (M) or the MENU tab. Buttons highlight while touched and click. Seat adjust moved here because the grips now grab.

**Offline check** (new `PhysicsTest` scenario N, simulated hands):

| Check | Result |
|---|---|
| Grab the rim at 12 o'clock, turn 60° clockwise | steer +4.62° (expected 60/13 = 4.62) |
| Let go, wait 1 s | stays at 4.62° |
| Use the stick | back to stick control (0.00°) |
| Grab the lever knob, push 15° forward | lever throttle 0.60 (expected 0.60), knob 4.5 cm forward |
| Let go | stays at 0.60 |

All earlier scenarios unchanged. **Build**: setup + APK without errors, installed. **Not headset-tested yet** (controllers and hand tracking).

![panel](images/panel.png)

## 2026-10-17: Block 3 headset check + hand-tracking grab fix
Headset test: controllers on the wheel and lever fine, cab panel fine (controllers and fingertips). **With hand tracking the wheel and the lever
shook (looked doubled) and the grab kept dropping.**

Cause: a fist hides the fingers from the Quest's cameras. The palm position then jitters by millimetres every frame, the fist measurement
flickers between closed and open, and tracking drops out for a few frames. The first version used all three raw: every jitter moved the wheel,
and every flicker or dropout counted as letting go.

Fix (`VRHands.HandSignal`, hand tracking only; controllers unchanged):
- Grab point = midpoint of palm and middle knuckle (steadier in a fist), through a **one-euro filter** (1 Hz cutoff while still, +4 Hz per m/s of hand speed).
- **Pinch** (thumb to index tip, < 2.5 cm / > 4.5 cm) grabs too, as well as the fist. It is much more reliable on Quest.
- **Release debounce**: let go only after 0.2 s open. The point is frozen meanwhile, so opening the hand doesn't drag the wheel or lever.
- **Tracking grace**: a dropout under 0.4 s while holding keeps the grab.

Replay test (`PhysicsTest` N: 60° wheel turn with 4 mm noise, 12 % grab flicker, two 5-frame dropouts):

| | Grab drops | Wheel shake (2nd difference, °/frame²) | Final steer (expected 4.62°) |
|---|---|---|---|
| Raw (before) | 24 | 1.54 | 3.62° |
| Filtered (after) | **0** | **0.28** | **4.68°** |

APK built and installed. ⏳ Re-check in the headset.

## 2026-10-17: Speed lever knob fix
Headset: with the steadier hand tracking, it was visible that **only the stalk and the knob shell moved, while the knob's button block stayed in place**.
The knob is several pieces in the cab mesh. The first rule ("every vertex within 7.5 cm of the knob") took two tiny buttons but missed the
8 × 10 × 7 cm button block, whose corners reach further. The setup now logs every piece still left near the knob. With "bounds centre within 8 cm"
it takes 4 knob pieces (801 vertices with the stalk), and nothing is left near the knob. New preview `lever.png` (lever fully forward) shows the
whole handle tilting together. Tests unchanged, APK installed, ⏳ re-check.

![lever](images/lever.png)

## 2026-10-18: Speed lever stem fix + how the hand controls work
- Headset: the lever **looked like it broke off its stem**. The thin rod that goes down into the gate is another separate piece, just below the stalk.
  The lever pivoted at the top of that rod, so the stalk bent away from a rod that stayed put. The rod is now part of the lever, and the pivot is its
  bottom, inside the gate (6 cm lower). Side renders at rest / fully forward (`lever_side_rest.png`, `lever_side_fwd.png`) show the whole lever
  swinging in the gate slot. With the longer lever, 15° moves the knob 6.2 cm (was 4.5 cm).
- The user asked how the hand controls work exactly. Wheel: palm on the rim (within 7 cm), fist or pinch, move the hand *around* the wheel's centre
  (wrist twist and in/out movement do nothing); open the hand to let go, and the wheel stays. Lever: palm on the knob, fist or pinch, move forward/back; it stays set.
- Still open: hand-tracking grabs feel fussy. Proposed: (1) highlight the rim/knob when in reach and while held, (2) gear the lever down so ~20 cm of
  hand travel covers the full range, (3) snap the visible hand to the rim/knob while holding.

| Lever at rest | Fully forward |
|---|---|
| ![rest](images/lever_side_rest.png) | ![forward](images/lever_side_fwd.png) |
- Neutral with hand controls: only by bringing the speed lever back into its ±8 % neutral zone (about ±2°, under 1 cm at the knob), which is fiddly with
  hand tracking. A wider notchy neutral and a NEUTRAL/STOP panel button were added to the ROADMAP as possible improvements, together with the three grab proposals.

## 2026-10-18: Interior textures + baked ambient occlusion
The model's textures are missing, so the cab was plain grey. A UV check per material showed why simple tiling textures can't fix that:
the plastics and dashboard (most of the cab) have **degenerate UVs** (median 0.04–0.09 UV units per metre, thousands of zero-area triangles). They were
made for plain colours. Only the seat cloth, pedals and floor are properly mapped.

- **`TractorVR/TriplanarLit`** (new shader): URP lighting (`UniversalFragmentPBR`, main-light shadows, SH ambient, fog, stereo) with a detail texture and
  normal map projected from three directions in the object's own space, scaled to metres, so nothing slides when the tractor moves.
  Normal blending: whiteout. Shadow and depth passes are reused from URP Lit (`UsePass`).
- **Generated tiling textures** (512², PNG + normal map, by the setup script): fine leather-like plastic grain (6 cm tile, kept subtle after a first
  version looked like plaster), plain-weave seat fabric (40 threads per 6 cm), ribbed rubber floor mat (16 ribs per 24 cm, floor darkened),
  pedal tread blocks. 12 materials (plastics, dashboard, rubber, seat cloth, floor, pedals; `_ao` variants inside the cab).
- **Baked ambient occlusion** on the cab interior, steering wheel and speed lever (37,177 vertices): 32 rays per vertex, up to 35 cm, against the tractor's
  opaque geometry (glass left out), stored in the vertex colours. Corners, gaps, the cup holder, the footwell and under the dash now have depth. It costs nothing at runtime.
- APK 61.4 MB (+2 MB). All `PhysicsTest` scenarios unchanged. ⏳ Headset check.

| Interior | Console and lever |
|---|---|
| ![interior](images/interior.png) | ![lever](images/lever.png) |

## 2026-10-18: Interior checked, roadmap tidy-up
- Interior textures + AO approved in the headset. ROADMAP: everything built so far ticked off. Mirrors moved to a new "Future ideas / reference" section
  with the plan and its expected cost. Next focus chosen: more physics, the terrain, and working buttons in the cab.
- Phase 3 scope chosen: soil + wheel slip, brakes + diff lock + 4WD; fields + crop rows, roads + farmyard, varied ground; hydraulic levers,
  pressable switches, lights, keep the panel. Order: ground + layout → traction → fields + crops → cab buttons.

## 2026-10-18: Phase 3 step 1, ground + layout
**World layout** (`WorldLayout`): the start became a farmyard. A 52 × 64 m concrete yard has a barn (14 × 24 m, gable roof, doors to the yard) on the west side
and an open machine shed along the south side. Gravel tracks lead to three fields on the hills (north field = crop, south-east and west = bare soil).
An asphalt road runs north–south at x = 80. There's mud at every field gate and in hollows of the pasture (noise). The phase 1 grid floor and marker posts are gone.

**Ground map** (`GroundMap`): one surface type per square metre (2000 × 2000 bytes), built at start-up from the layout + noise
(≈ 90 % grass, 6 % fields, 4 % mud). Each surface has a tyre grip, a rolling resistance and a plough draft factor:

| Surface | Grip | Rolling | Plough draft |
|---|---|---|---|
| Asphalt | 0.90 | 0.015 | hard (can't plough) |
| Concrete | 0.85 | 0.015 | hard |
| Gravel | 0.65 | 0.030 | hard |
| Grass | 0.70 | 0.050 | × 1.25 (sod) |
| Soil / crop | 0.65 | 0.070 | × 1.0 |
| Ploughed | 0.50 | 0.120 | × 0.6 |
| Mud | 0.35 | 0.150 | × 0.8 |

**Look**: `TractorVR/GroundLit` blends grass / soil / mud textures from a 1024² control texture (2 m per texel, soft edges), with a 64 m
variation texture against visible tiling. Roads and the yard are thin meshes 3–7 cm above the actual terrain triangles (a new `SurfaceHeight`
follows the 5 m triangles, which differ from the smooth height function by up to ~4 cm), drawn with `TriplanarLit`. All ground textures are generated
(grass blades and dry patches, soil clods, wet mud, gravel stones, asphalt aggregate, concrete slabs with joints). First version: the yard looked like a bright quilt and the mud
like grey water, so both were toned down.

**Physics**: every wheel takes its grip from the surface under it, rolling resistance is load-weighted over the wheels (tractor and trailer), the plough's draft
depends on the soil, the plough can't go into concrete, asphalt or gravel (it rests on top, no draft), and ploughing turns the ground to "ploughed".

**Test findings** (new scenario O + fixes):
- The plough test had been ploughing the yard concrete (98 kN); now it's refused on hard ground, and the test carries the plough to the field (7.1 km/h at 30.4 kN).
- **The trailer stayed coupled in the editor tests**: `Destroy` does nothing outside play mode, so its joint survived detaching and dragged the trailer along with
  every teleport (35° pitch, flying tractor). It's now `DestroyImmediate` in edit mode. The game itself was never affected.
- Teleports now reset the suspension history and straighten a hand-latched steering wheel (it had sent the test tractor in circles).
- Preview rendering skips a PNG that is open in an image viewer instead of aborting the setup.

Scenario O (6.5 km/h, drive force minus the slope share = rolling resistance):

| Surface | Measured | Expected (73.6 kN × coefficient) |
|---|---|---|
| Asphalt (uphill) | 1.1 kN | 1.1 |
| Concrete | 1.1 kN | 1.1 |
| Gravel | 2.2 kN | 2.2 |
| Grass | 3.7 kN | 3.7 |
| Field soil | 5.1 kN | 5.2 |
| Mud | 11.0 kN | 11.0 |

APK 62.2 MB, installed. ⏳ Headset check.

| Farmyard | Field gate (mud, gravel, soil) |
|---|---|
| ![farmyard](images/farmyard.png) | ![gate](images/field_gate.png) |

| Main road | Hills |
|---|---|
| ![road](images/road.png) | ![hills](images/hills.png) |

## 2026-10-18: Step 1 checked, README gallery
- Ground + layout approved in the headset.
- README reworked into a gallery (world, driving, implements, cab). New showcase renders from the setup script (`ShowcaseShots`: the tractor posed
  out in the world with the implements coupled): `hero.png`, `ploughing.png`, `trailer.png`, `mower.png`. All `docs/images` refreshed from the latest renders.

## 2026-10-18: Finishing phases 1 and 2, batch 1 (hand controls + testing)
Decision: finish every open item of phases 1 and 2 before continuing phase 3. Items already covered elsewhere were ticked or moved
(soil types / roads / farmyard done in phase 3 step 1; wheel slip → phase 3 step 2; crop rows → phase 3 step 3). The rest is split into three batches.

**Batch 1**
- **Grab feedback**: the steering wheel rim and the speed lever knob glow softly when a hand (or controller) is in reach, brighter while held
  (`_Highlight` in `TriplanarLit`, set per renderer with a MaterialPropertyBlock).
- **Speed lever geared down**: hand travel along the tractor's forward axis, 20 cm from neutral to full, relative to where it was grabbed. A **notchy neutral**
  (±20 %) snaps it upright with a click.
- **Hand snap**: a tracked hand that holds the wheel is drawn on the rim (at its own angle), and on the knob for the lever
  (`XRHandSkeletonDriver.ApplyRootPoseOffset`). Tracking jitter no longer shows on the hand.
- **STOP / NEUTRAL** panel button: lever to neutral and brake at 2.5 m/s² to a standstill (coasting would take 3× longer), then hold. **FRAME TIME** button:
  fps, frame time and the worst frame of the last second on the HUD (for the frame-rate check in the headset).
- **Play-mode test** (phase 1's open "play test in the editor"): `PlayModeTest` plays the real scene for 25 s with scripted input (drive, steer, STOP,
  every panel button twice, hitch down/up) and fails on any runtime error. It runs in batch mode and exits by itself. First run: OK
  (21.6 km/h on the yard concrete in gear D4, stopped from 22.8 km/h, 15 buttons).
- PhysicsTest: lever notch (3 cm → 0.00) and gearing (12 cm → 0.60) checked; new scenario P: STOP from 20.1 km/h → standstill in 2.2 s (expected 2.2), then holds.

APK installed, ⏳ headset check.

## 2026-10-18: Batch 1 headset check: frame rate + hand-control fixes
Headset: **41–43 fps in the cab, 53 in third person** (target 72), and the hand controls jittered, the lever only went forward, and the steering
wheel couldn't be grabbed. The Quest's own log (`adb logcat -s VrApi`) showed `FPS=41/72, App=18.2 ms, GPU%=0.99, CPU%=0.13`: completely
**GPU-bound** (budget 13.9 ms). The low frame rate also makes everything (hands included) judder through reprojection.

GPU savings:
- **Fixed foveated rendering** (OpenXR Foveated Rendering feature, SRP foveation API, no eye tracking, subsampled layout): new `Foveation` component,
  FOVEATION Off / Low / Medium / High on the cab panel (default High).
- **Terrain shader**: samples only the layers present (most pixels are pure grass: ~4 texture reads instead of 8), one normal map (the dominant layer),
  Blinn-Phong instead of PBR.
- **Interior shader**: projects from the dominant axis only (2 texture reads instead of 6), Blinn-Phong.
- Terrain no longer casts shadows (gentle hills shadow nothing within the 40 m shadow distance); shadow map 2048 → 1024.

Hand-control fixes:
- Lever travel 20 → 12 cm from neutral to full: reverse needed the hand ~24 cm back, behind the body. The neutral notch is ±25 % (3 cm).
- A pinch now grabs at the pinch point (between thumb and index tips), not the palm, which is 5–8 cm from a pinched rim. The gesture is chosen when the grab starts,
  so switching mid-grab can't jump. Grab reach for tracked hands 7 → 9 cm.

Tests: PhysicsTest + PlayModeTest OK. APK installed; ⏳ frame rate to be measured in the headset (the Quest only renders while worn).

## 2026-10-18: Speed lever rebuilt
Headset: frame rate better, but the speed lever "either won't move, or moves but not in the right direction". The geared, straight-line mapping
(hand forward/back → lever) with a dead 3 cm notch felt wrong: small movements did nothing, the lever then jumped out of the notch, and the knob
didn't stay under the hand because a lever swings in an arc.

New behaviour (`CabControls`):
- **The lever follows the hand around its pivot** (side view): the knob stays in the hand. It's relative to where it was grabbed (no jump).
- **Neutral is in the speed, not the lever**: the lever moves freely; the speed command is 0 in the middle ±25 % of the swing and rises smoothly beyond it.
  It clicks when passing the edge of neutral, and snaps upright if let go there.
- **HUD while held**: "LEVER: N", "LEVER: F 47%", "LEVER: R 20%".
- **Diagnostics**: wheel / lever grabs and releases are logged (`[Cab]` in `adb logcat -s Unity`), with the hand type (controller, hand fist, hand pinch).

Test (`PhysicsTest` N): swings of 5° / 15° / 25° / 0° / -15° give speed commands 0 / 0.47 / 1.00 / 0 / -0.47 exactly, with the knob 0.0 cm from the hand;
re-grab doesn't jump; let go at -3° snaps to neutral. Noisy hand-tracking replay (+20° → -20°): never the wrong direction. PlayModeTest OK. APK installed.

## 2026-10-18: Lever confirmed; frame rate, round 2 (tractor triangles)
Session log (`adb logcat -s Unity VrApi` recorded in the background):
- **Lever and wheel work**: lever grabbed by pinch (into reverse, -0.65), by fist (no jump, back to forward), by controller (full forward 1.00, full reverse -1.00);
  wheel grabbed by pinch.
- **Frame rate**: average 58 fps (48–71). Two clear states: ~13.5 ms GPU (65–70 fps) and ~17 ms (48–54 fps); the budget is 13.9 ms.

Where the GPU goes: the **tractor model is 749k triangles** (~70 % of the scene), and **all of it cast shadows**, so it was drawn a second time
for the shadow map. The steps alone are 41k + 37k triangles, and the engine under the hood 47k.
- **Shadow stand-in**: a 416-triangle shadows-only mesh (cab roof + pillars so light still falls through the windows, cab body, hood, rear frame,
  wheels) casts the tractor's shadow. The 66 model renderers no longer cast shadows, so the second 750k-triangle pass is gone.
- **Mesh LODs** (Unity 6 mesh LOD generation on import, up to 4 levels): LOD0 625k, LOD1 355k, LOD2 233k, LOD3 151k triangles.
  `QualitySettings.meshLodThreshold` 1.5. Forced-LOD renders (`lod1/2/3.png`): LOD2 at ~8 m looks the same as LOD0; LOD3 shows defects (rims, exhaust).
  Close-up views keep full detail.
- PlayModeTest OK. APK installed, log recording again for the next session.

## 2026-10-18: End of day
Last session (log recorded over USB): **72 fps most of the time** (GPU ~11.3 ms), stretches at ~15 ms (59–60 fps) and one short drop to 20–23 ms (30–39 fps).
Average 61 fps, up from 41–53 at the start of the day. Accepted for now; a context logger for slow frames was started and then left for later (not committed).

Summary of the day:
1. ROADMAP tidy-up, phase 3 planned (physics, terrain, cab buttons); step 1 built: ground map, natural terrain, farmyard, roads, fields, per-surface physics.
2. README turned into a gallery with new showcase renders; the phases section restored.
3. Decision: finish phases 1 and 2 first. Batch 1 done: hand-control improvements (highlight, rebuilt speed lever, hand snap, STOP), play-mode test.
4. Frame rate on the Quest: foveated rendering, cheaper terrain and interior shaders, no terrain shadows, a shadow stand-in instead of the
   750k-triangle model, mesh LODs.

Next: batch 2 (cab), then batch 3 (implements), then phase 3 step 2 (traction).

## 2026-10-21: Finishing phases 1 and 2, batch 2 (cab)
All four batch 2 items, built and tested in the editor. APK (62.2 MB) installed, ⏳ headset check.

**Hydraulic remote levers** (`CabControls.Swing`, cut out in `ProjectSetup.SplitSteeringWheel`): a probe of the cab mesh's connected pieces showed
each of the three levers behind the speed lever is three pieces: a knob (2 cm tall), a handle (8 cm) and a stem (10 cm) going into the console.
Setup finds each kind by its height, sorts by x and zips them; the pivot is the bottom of the stem (y 2.155–2.199). Spring-centred, ±20°,
15 % dead band. Pull back = raise, push forward = lower, speed proportional to the deflection: 1 = rear hitch, 2 = front linkage
(through `Hitch.MenuLift`), 3 = trailer tipping (batch 3). The HUD shows e.g. "REMOTE 1 HITCH: RAISE 60%".

**Doors**: the model's `Door_L/R` pivots already sit on the hinge line at the rear edge (JD cabs have rear-hinged doors). Each door gets a
tractor-aligned `Hinge_Door_*` and opens outward up to 65°; more would swing the lower door panel into the rear fender (checked with
`doors_side.png` / `doors_top.png`). Grab anywhere on the door: it follows the hand around the hinge, relative to the grab point, and stays where
it is let go. The panel's DOORS button opens / closes both. They clunk when they shut. Doors and remote levers share one class (`Swing`):
grab box in hinge space, angle of the hand around the hinge axis, limits, optional spring return. A grab goes to the nearest swing in
reach, or to the speed lever if its knob is closer.

**Torque curve** (`Drivetrain`): 520 Nm at 800 rpm, 880 Nm at 1500–1600 (34 % torque rise), 659 Nm at 2100 (145 kW rated), governor down to 0
at 2250; peak power 151 kW at 1800. `TractorController` now limits the drive force with the power at the current engine speed
(it used a flat 145 kW). Three follow-ups the physics test showed:
- RPM stayed at idle in the test: `SmoothDamp` and the shift timer used Unity's clock. Both now use the `dt` passed to `Drivetrain.FrameUpdate`,
  which the test steps (before, the test never ran the drivetrain, so the engine had no influence on it).
- In the low gears the 0.5 s between shifts let the engine run into the governor (76 kW at 2174 rpm). AUTO now skip-shifts straight to the
  first gear below the upshift speed.
- Pulling away with the plough already in the ground stalled at 0.4 km/h: at idle the engine has 49 kW, not enough for 30 kN of draft.
  When the drive force is power-limited, the engine is now brought up to peak torque (1550 rpm, the clutch slips) for at least a second,
  and AUTO shifts down when it lugs. The plough run is back to 7.1 km/h with 30.4 kN of draft, ending where it did before.

**Hand throttle**: THROTTLE − / + on the cab panel and the CommandCenter (hold; 400 rpm/s), `[` / `]` on the keyboard. It sets the engine speed:
AUTO holds the engine there (shift band hand rpm −200 / +150, as with the PTO), and in manual the tractor drives at the selected gear's speed
for that engine speed as soon as the lever or stick asks for motion. Test: C1 at 1500 rpm, lever at 0.15 → 6.2 km/h (expected 6.2).

**Fuel and coolant**: 340 L tank (85 % at the start and after RESET TRACTOR), use = 2.5 L/h × rpm/850 + 0.24 L/kWh × engine output
→ 2.5 L/h idling, 41 L/h at full power (the 6195M's real figure is about 40). Kept in a double: one 90 Hz step of idle use is below float
precision at 289 L, so the first version never used any fuel at idle. Coolant from 35 °C to the thermostat's 85 °C (+8 under load)
in ~6 min at idle, faster under load.

**CommandCenter display** (`CommandCenter.cs`, built at runtime like the cab panel): a 24 × 15 cm screen on a short arm at the right front
corner post (0.50, 2.40, 1.00), facing the eye point. It shows engine speed, hand throttle, fuel (bar, %, litres), coolant (bar, blue when cold),
fuel use, power used / available and torque, hitch / front linkage / PTO and engine hours, with two touch buttons. Fuel and temperature
are on this display, not the cluster: from the seat the hub and the column shroud hide everything but the top of the dials.
The play-mode test caught a bug: its quads were built with `CreatePrimitive`, whose MeshCollider sat on the tractor's Rigidbody until the
deferred `Destroy` ("Concave Mesh Colliders are not supported..."). They're plain MeshFilter quads now.

Tests: PhysicsTest Q (torque table, hand throttle manual + auto, fuel, coolant warm-up) and N (remote levers 1 and 2 move the hitch /
front linkage and spring back, door swung by hand to 40° stays, DOORS button closes / opens both). The older scenarios match the previous
run (climb, parking, bale, plough, mower, trailer, surfaces). PlayModeTest OK (19 panel buttons, CommandCenter hand throttle).

| CommandCenter | Remote levers (1 raised, 2 lowered) | Door open |
|---|---|---|
| ![commandcenter](images/commandcenter.png) | ![remotes](images/remotes.png) | ![door](images/doors_side.png) |

## 2026-10-21: Finishing phases 1 and 2, batch 3 (implements)
All five batch 3 items, built and tested in the editor. APK (62.3 MB) installed, ⏳ headset check. With this, every phase 1 and 2 item
is built; phase 2 closes after the headset check of batches 2 and 3.

**Probe first**: a temporary editor script printed the pivots and extents of the top link and the front cylinders, and rendered tinted
close-ups. The top link `Rear_Holder_01` has its pivot on the tractor bracket at (0, 1.179, −0.511) and stands 0.94 m tall. The front
cylinder barrels pivot on their frame pin at the top (1.162, 3.337), but the model pins the rods *on the lower link's own pivot*, where a
cylinder couldn't move anything. The renders show a lug below each link's pivot, so the rods ride on that lug (centroid of the link's
vertices more than 4 cm below the pivot, (0.773, 3.588)).

**Top link with three-point pitch** (`Hitch`): when a three-point implement with a `topPin` is coupled, the top link is set to the length that
fits at that moment (plough: 92 cm) and stays that long, like a turnbuckle. Each frame `SolvePitch` finds the implement pitch about the
lower-link pins that puts the top pin at that distance from the bracket (bisection, root nearest level), and turns `RearMount` by it. The
implement follows because it takes the mount's rotation. The top link mesh is turned onto the pin and stretched along its axis.
PhysicsTest J: plough level when coupled (0.6°), −1.2° at working depth, +14.6° fully raised (tail up), with the same draft (30.4 kN)
and track as before.

**Front linkage cylinders**: the barrel turns on its frame pin towards the lug pin. The rod is turned by the same angle and slides along
the barrel by the change in length (`front_cyl_up/mid/down.png`).

**Tipping trailer** (`Tipper.cs`): the body (floor, sides, stakes, headboard with ladder, body collider) is a child hinged at the rear of the
chassis. A seven-stage telescopic ram runs from the drawbar to the top of the headboard; its stages spread evenly between base and pin,
from 0.7 m closed to 4.1 m at the 45° limit. The tailgate hangs from its top edge and stays plumb, so it swings open. The trailer
starts with 3 t of soil: above 20° it pours (full rate at 40°) into a mound behind the tailgate, sized by volume (1500 kg/m³, repose ~25°).
A new mound starts when the trailer has moved on 1.5 m. Mass and centre of mass follow the load and the body angle, and the tractor's towed
mass follows too. It's worked by remote lever 3, the panel's TIP UP / DOWN, or I / K, only while coupled.
PhysicsTest S: 3000 kg poured in ~5 s at 45°, CoM 1.23 → 2.14 m high at the peak, one mound of 1.41 m radius 7 m behind the eye, then
lowered in 4 s and driven off at 12 km/h.

**Front weight**: 900 kg of cast slabs on a carrier, coupled to the front linkage like the mower. PhysicsTest R: front axle 29.4 → 42.0 kN,
rear 44.2 → 40.4 kN (lever rule: +12.3 / −3.5). Two bugs showed up here:
- The weight couldn't be uncoupled ("LOWER FRONT LINKAGE FIRST"): the lowered check uses ground points and the weight has none. Now an
  implement without working parts measures its depth by its coupling point against its parked height.
- That left everything after it coupled. The trailer stayed on the drawbar, and the next scenario's teleport dragged it along and made
  the physics explode (6100 km/h). The tipping scenario now uncouples at the end, and reports it.
- The centre of mass was only computed when coupling, so lifting an implement didn't move it. `Hitch` now updates mass and CoM every
  frame while anything is coupled (the plough raised shifts the front axle 0.2 kN more than before).

**Better implement models** (all generated in `ProjectSetup.BuildImplements`):
- Procedural meshes: lugged tractor tyres (two staggered rows, a castellated profile swept across each half, flat-shaded) and a twisted
  mouldboard surface.
- Plough: cross shaft with yellow pins, A-frame headstock and top pin, braces, beam, raked legs, frogs, shares and points, mouldboards,
  landsides, disc coulters, depth wheel on an arm, parking stand.
- Mower: A-frame headstock with hooks, gearbox, PTO shaft guard, knives on the discs, sloping hood, side shields, rubber curtain, skids.
- Trailer: A-frame drawbar with eye ring, jack with crank, rails and cross members, leaf springs, mudguards, light bar with lamps and plate,
  stakes and top rails, headboard ladder, tailgate with bar.

Tests: PhysicsTest R and S are new and J logs the pitch. All other scenarios match the batch 2 run. PlayModeTest OK: 21 panel buttons, and
the parked trailer's body raised in play mode pours its whole load into one heap.

| Top link at work / raised | Tipping | Front weight |
|---|---|---|
| ![top link](images/toplink_raised.png) | ![tipping](images/tipping_rear.png) | ![front weight](images/front_weight.png) |

## 2026-10-21: Headset test of batches 2 and 3, phases 1 and 2 closed
Tested in the headset by the user: everything in batches 2 and 3 works, and **phases 1 and 2 are complete**.
One bug, to fix next session (in the ROADMAP under "Known bug"): RESET TRACTOR with the trailer coupled makes the trailer disappear, and
it stays coupled and can't be detached. The optional original-textures item of phase 1 moved to "Future ideas".
Next: fix that bug, then phase 3 step 2 (traction).

## 2026-10-30: Reset-with-trailer bug fixed; phase 3 step 2 (traction)
Both built and tested in the editor, APK (66 MB) installed on the headset, ✅ headset-checked 2026-10-30 ("looks good").

**RESET TRACTOR with the trailer coupled.** The reset moved only the tractor's Rigidbody. The trailer stayed on its joint ~120 m away;
the joint yanked it across the map, and it ended up out of sight and too far round for "STRAIGHTEN UP TO DETACH". Now the reset also sets
the tractor's transform (so the mounts are in their new place at once) and calls `Hitch.AfterTeleport`, which puts a coupled trailer
straight back on the drawbar, at rest, with its suspension history cleared (`Implement.SnapToMount`, `SimpleWheels.ResetSuspension`).
Mounted implements are children of their mounts and follow by themselves. PhysicsTest T resets while driving with the trailer coupled:
it comes along (gap 0.0 cm, straight), still tows at 6.5 km/h, and detaches.

A second teleport bug turned up while testing traction: `ResetSuspension` didn't clear the **parking-brake anchor**. When the tractor stood
still before and after a teleport, the hold kept pulling it towards the old spot; the sideways part (capped at full grip) rocked it up to 20°.
It's why PhysicsTest R measured wrong "before" axle loads in this run and why the hay bale in H didn't move. Both are right again
(R: 29.4 → 42.0 / 44.2 → 40.4 kN as in batch 3; bale pushed 9 m). This also hit RESET TRACTOR in the game when standing still.

**Traction** (`TractorController`):
- The drive / brake force goes to the wheels that take it: rear always; front with 4WD; all four while parked (park lock + front axle).
  Braking: all wheels with all-wheel brakes or 4WD engaged, otherwise rear only.
- Lateral grip comes first now (a tractor sliding sideways is the worst for comfort); the drive force gets the rest of the friction circle.
- Differentials: an **open** diff gives both wheels the same force, so the wheel with less grip limits the axle to twice its grip and spins.
  The **diff lock** lets each wheel use its own grip; it lets go above 12° steering or 15 km/h, like the real one. With 4WD the axles are
  coupled rigidly (no centre diff), so what one axle can't put down goes to the other. Brakes act per wheel.
- **Slip** per tyre from a traction curve μ(s) = μmax·(1 − e^(−k·s)) with k per surface (`GroundMap.Surface.slipK`: asphalt 35 … mud 6).
  A wheel the ground can't hold spins up (an open diff's wheel twice as fast); a braked wheel locks. Each tyre has its own speed, which
  drives the wheel animation, and the **engine RPM follows the rear wheels' speed** (with slip the engine runs faster than the ground speed).
- Readouts: CommandCenter `SLIP %` (red above 20 %) and drive state (4WD / 2WD, DIFF LOCK, REAR BRAKES); HUD shows slip above 5 %.
- **Clods**: one small particle system per wheel (mesh cubes, lit, ≤ 40 particles, no shadows), tinted per surface; emits when the wheel
  slips more than 20 % on soft ground. Material `Generated/WheelDirt.mat` (URP Particles/Simple Lit) from setup.
- Controls: panel buttons 4WD, DIFF LOCK, BRAKES (24 buttons now), keyboard F / L / B.

PhysicsTest U (plough at work, 30 kN draft on the crop field, 7 km/h): slip 16 % with 4WD, 18 % 2WD, 17 % 2WD + diff lock
(ploughing slip is 10–15 % in practice). Split grip (left wheels in the mud at the north gate, right on grass/crop, plough in):
2WD open diff: the left rear wheel spins at 14 km/h, the right one stands, the tractor stalls; 2WD + diff lock: both rear wheels spin, slow
crawl; 4WD open diffs: both left wheels spin, stuck; 4WD + diff lock: pulls away (4 km/h) but bogs down: half the tractor in mud can't
pull 30+ kN. V (STOP at 18.5 km/h entering mud): all-wheel brakes ~5 % brake slip, rear only ~20 %; no lock-up at the comfort rate
(2.5 m/s²), both stop in 5.2 m. All other scenarios unchanged. PlayModeTest OK (24 buttons pressed twice).

**Plan change (2026-10-30, after the headset check).** The user will do the environment work with Blender, so phase 3 step 3
(standing crops cut by the mower, the ploughed-ground look) is moved to a new ROADMAP section "Environment art (with Blender, later)",
together with phase 2's "field with crop rows". Ploughed ground already has its own grip, draft and slip since steps 1–2. Next: step 4
(cab buttons + lights).

## 2026-10-30 (later): Phase 3 step 4, cab buttons + lights
Built and tested in the editor, APK installed on the headset, ⏳ headset check.

**Switches from the model.** The right-hand side console of the cab interior has real switch pieces: rockers (1 × 3.5 × 2 cm), a
double rocker, a flat round button and a small knob on its top, next to three big HVAC knobs. Setup cuts eight of them out of the
interior mesh like the levers (found by their centres, within 8 mm) and gives each a hinge: rockers tip ±7° around a hinge behind their
face, push buttons sink 3 mm, the knob turns 45° per step. `CabSwitches` pokes them like the cab panel buttons (fingertip or controller
tip; each hand only the switch its tip is deepest in, and it has to move 3 cm clear before the next push). Each switch shows its state
and has a small lamp (green / amber / red). Functions: road-light knob (OFF / SIDE / HEAD), hazards (round button), front work lights, rear
work lights, beacon, 4WD, diff lock, PTO. The corner post has no switches in the model (the CommandCenter is there).

**Indicator stalk.** Left of the column at hub height, five pieces (the ribbed end included) out to x −0.29; the column's switch
housing stays. It is a `CabControls.Swing` (axis −Z, ±12°, + = up = right) with a small grab reach (2.5 cm), and the rim wins when the hand
is closer to it (they are ~5 cm apart). `TractorLights` reads it while held and moves it to match the indicator otherwise (cancel, keys,
panel). The indicator cancels when the wheels come back below 3° after being turned more than 10° its way.

**Lamps.** The lamp glass of the model shares a few materials across all lamps (`headlights_cap01` is the grille lights *and* the
roof work lights), so no lamp can light up on its own. Setup measures each lamp from the lamp materials, then lays a lens disc on its
glass (a ray from in front against temporary colliders of the lamp parts; 22 of 22 found) and adds a halo: a camera-facing quad
(`TractorVR/Halo`, billboarded per eye in the vertex shader, additive, computed falloff) that shows at dusk / night and only from the
front of the lamp (it glared into the cab from the cab-front lamps). Beacons are spheres over the domes, pulsing as a rotating reflector.
Real light only where it matters (Quest per-object light budget): one spot for the headlights, one for each work-light set, a dim cab
light at dusk / night. The work-light spots start beyond the bonnet / hitch: from the roof they lit the bonnet 1–2 m away so brightly
that the whole view from the cab was washed out.

**Day / dusk / night.** `TractorLights` keeps the day settings and scales sun (dusk: 6° high, orange; night: weak bluish moonlight),
ambient colours and the ambient probe, fog colour, reflection intensity and the skybox exposure. The cab glass is unlit, so it stayed
day-bright and laid a grey veil over the night view: it is now dimmed per material slot with the ambient light. The skybox is a shared
asset, so it is put back to day when the component goes away (editor play mode).

Also: brake lights from `TractorController.Braking` (braking harder than coasting); indicator tell-tales `<<` / `>>` on the CommandCenter;
cab panel 31 buttons in three columns (LIGHTS, WORK, BEACON, HAZARDS, < INDICATOR, INDICATOR >, SKY); keys G / N / O / Y / , / . / U.
PlayModeTest presses all 31 panel buttons and the 8 console switches, then drives on at night with lights, beacon and indicator: OK.
PhysicsTest unchanged.

**Fix after the first headset try (same day): levers grabbed while pressing switches.** The user: "the levers and the wheel are
getting in the way sometimes". The headset log (hand tracking) showed remote levers 2 / 3 grabbed and let go within half a second,
over and over, while reaching for the console. Cause: a fist was the average of the index, middle and ring tips' distance to the palm
(< 6.5 cm); a pointing hand (index out, the others curled) averages under that, so pressing a switch was a fist grab, and a hand's swing
reach (9 cm around the lever) covered the switches 5–7 cm away. Fixes: a fist only starts with the index curled too (index tip < 8 cm from
the palm, `VRHands`); no grab at all while the fingertip is on a switch (`CabSwitches.Touching`, `CabControls.TryGrab`); the remote
levers' grab reach is 4 cm (`Swing.reach`). The pointing rule also keeps the wheel from being grabbed while pressing something behind it.
APK reinstalled, ✅ headset-checked ("looks good"). **Phase 3 closed 2026-10-30** (step 3 moved to the Blender section).

## 2026-10-30 (evening): Labelled cab guide in the README
The user asked for cab images with arrows and text boxes for every control. `CabGuide.Run` (editor, menu Tractor VR > Render Cab
Guide) renders four views from the seat with a soft fill light: the driver's view, the right-hand console, a close-up of the switch
panel (from the front: seen from the seat, the 4WD rocker hides behind remote lever 3) and the open cab panel. It writes where each
control lands in the picture (`WorldToScreenPoint`, flagged when outside). `tools/annotate_guide.py` puts the render between two label
columns, stacks the boxes near their control's height without overlaps and draws the arrows. The panel buttons carry their own
labels, so the README explains them in a table instead.

## 2026-10-30 (evening): Plan for phases 4–6; blender-mcp installed
The user wants two modes (free roam + tutorial), a better environment made with Blender, and, because the Quest GPU is weak, a
version where the laptop renders (PC VR over Link) plus a PC desktop version. Agreed order: **phase 4 versions → phase 5 environment →
phase 6 tutorial**. The versions first, because they set each version's graphics budget before any art is made (every Blender asset gets a
full and a light version) and editor play through Link speeds up the testing; the environment before the tutorial, whose lessons
are tied to places. The old "PC VR build" and "Environment art" sections of the ROADMAP became phases 4 and 5.

blender-mcp (github.com/ahujasid/blender-mcp, add-on v1.8): Blender 5.2 and uv were already installed. The MCP server is registered
with Claude Code (`claude mcp add blender -- uvx blender-mcp`, local config for `Irum_proj`; connects). The add-on was read before
installing: socket server on localhost:9876 that runs what the MCP client sends, telemetry off by default, optional online asset
sources (Poly Haven, Sketchfab, generators) off unless switched on. Copied to `%APPDATA%\Blender Foundation\Blender\5.2\scripts\addons\blender_mcp_addon.py`; it still has to be enabled once in Blender (Preferences → Add-ons), the command-line enable was blocked.


## 2026-10-30 (night): Phase 4 started: quality tiers, PC build
Checked first: phases 1–3 have no open items (the crop-row items are moved to phase 5). On the laptop, the Meta Horizon Link app is
already installed and is the active OpenXR runtime (`HKLM\SOFTWARE\Khronos\OpenXR\1\ActiveRuntime` → `oculus_openxr_64.json`), and the
Unity Windows module is there (Mono only, no IL2CPP variation).

**Two quality levels instead of Unity's six defaults** (`ProjectSetup.ConfigureRenderPipeline`, edited through the QualitySettings asset):
- **Quest** (Android default): `QuestURP` as before: MSAA 4×, render scale 1.0, hard shadows, 1024 shadow map, 40 m, 1 cascade, mesh LOD threshold 1.5.
- **PC** (Windows default): `PCURP`: MSAA 4×, render scale 1.2, soft shadows, 4096 shadow map, 80 m, 2 cascades, mesh LOD threshold 0.5
  (full model up close).
- `QualityTier` (on the tractor, filled by `BuildShadowProxy`) applies the scene side at start: on PC the 78 model renderers cast their own
  shadows and the 416-triangle `ShadowProxy` is switched off; on Quest nothing changes. It also holds the **budget** the phase 5
  environment assets will read (`QualityTier.CurrentBudget`: detail distance 120 / 400 m, vegetation density 0.35 / 1, terrain shadows off / on).
- The Quest-only optimisations stay on Android without per-build switches: foveation is an Android OpenXR feature only, the URP asset
  and LOD threshold come from the per-platform default level.
- The editor plays at **PC** quality (for play through Link); menu *Tractor VR → Quality: Quest / PC* switches it.

**Windows player settings** (setup): Mono, Direct3D 11 only (the most reliable API for OpenXR over Link), windowed 1600×900, resizable,
runs in the background. `BuildPC.Build` (menu *Tractor VR → Build PC (Windows)*) → `Builds/PC/TractorVR.exe` (153 MB).

**Tests**: `PlayModeTest` now logs the tier; OK at both levels (PC: stand-in off, 78 renderers cast; Quest: stand-in on, 0 cast).
Windows build: succeeded. Quest APK rebuilt after the change: 66.8 MB, same as before.

**NVIDIA driver warning in the Link app**: the laptop has driver 621.01; Meta Horizon Link asks to roll back to **591.86** (it reports a
known NVIDIA bug that freezes Link during play). The Meta community forums say the rollback fixes the freezes for many, some still
see them on newer drivers, and Air Link with sliced encoding off is reported to work regardless. No official advisory was found.

Waiting on the user: the cable / Air Link and a first run through Link (editor play mode and the exe), 90 Hz in the Link app, and
what "standalone PC version" means for the desktop part.

## 2026-10-30 (night): Black PC window; desktop version
**Black window.** The user started `TractorVR.exe` and saw a black window. Player.log: OpenXR connected to the Link runtime and the
session went IDLE → READY → SYNCHRONIZED but never VISIBLE / FOCUSED: no headset in use through Link, so the app rendered for nobody
and the window (the headset mirror) stayed black. Not a crash, but a black screen without a headset is bad, and the user confirmed what
"standalone PC version" means: **on the monitor, mouse and keyboard, no headset**. So the same exe now does both.

**`DesktopMode`** (on the rig, wired by setup):
- Start-up on Windows / in the editor: VR if the OpenXR session is focused within 6 s (`OpenXRUtility.IsSessionFocused`), with a
  "waiting for the headset, D = monitor" hint meanwhile; otherwise the XR loader is stopped and deinitialised and the camera renders
  to the window. `-desktop` / `-vr` force a mode; F9 / TRY VR re-initialises XR later. Android never uses it.
- Camera: the head's `TrackedPoseDriver` is off. Cab: right mouse = look (±160° / ±75°, starts 12° down), wheel = zoom (FOV 30–90°).
  Third person: right mouse orbits a pivot 1.8 m above the tractor, wheel = distance (5–30 m). R = look ahead again. vSync on.
- **Mouse hand**: instead of new mouse code for every control, the mouse is fed into `VRHands` as the right hand, so `CabControls`,
  `CabSwitches` and `CommandCenter` work unchanged. The ray hits **pick proxies**: static `MeshCollider`s on layer 30 for the 34 readable
  (generated) meshes within 1.3 m of the seat, moved onto their parts every frame. They can't be colliders on the parts themselves (the tractor
  is a dynamic Rigidbody). Layer 30 ignores all collisions, and the wheel rays (`TractorController.GroundRay`, `SimpleWheels`) now leave it out.
  Grab = left button (the drag stays at the depth where it started); the poke tip exists only while the button is down and stays on the part
  first clicked, so sweeping the mouse doesn't press every switch on the way.
- **Screen menu**: the cab panel's 31 buttons as an IMGUI menu (M or the MENU button, top right; `CabPanel.Entry / PressEntry`, the
  in-cab panel hidden with `SetHidden`), plus FULLSCREEN / WINDOW (F11) and TRY VR (F9). Key help at the bottom left; the HUD moves onto
  the camera (bottom centre); the comfort vignette is off.

Tests: `PlayModeTest` (batch mode has no XR, so it now runs in desktop mode: 34 pick proxies) OK; `PhysicsTest` OK; Windows build OK.
The exe started with `-desktop` renders the cab on the monitor (`docs/images/desktop_cab.png`). Mouse interaction still needs the user's test.

## 2026-10-30 (night): First Link try: grey / stuck on the loading screen
After the driver rollback (591.86) the user reports: desktop version works flawlessly; in VR the headset stays black / grey and hangs
on the Link loading screen. Player.log: three sessions, each READY → SYNCHRONIZED and never VISIBLE / FOCUSED, then STOPPING after
6–10 s. The stop was `DesktopMode`'s own fallback: it shut XR down while Link was still loading, which leaves the headset stuck there.
Changes: the wait is now 20 s, and the waiting hint names the likely cause. A Link session that stays SYNCHRONIZED for a self-built app
usually means **Unknown Sources** is off in the Meta Horizon Link app (Settings → General), the open laptop-setup item. ⏳ user check.

## 2026-10-30 (late): Link grey screen: the Link dashboard can't start Vulkan (laptop GPU)
After a restart, Link still showed only grey and the game's session stayed SYNCHRONIZED for 24 s. Meta's own logs
(`%LOCALAPPDATA%\Oculus\`): `OculusDash.exe` (the Link dashboard, app 1438194916201007) restarts about once a second, each time with
`ovrError_InitializeVulkan` (VK_INCOMPLETE) in the Client log and "Missing entitlement for 1438194916201007" in the Service log as it is
closed. The laptop has two GPUs (Ryzen's AMD Radeon iGPU, AMD Radeon Software installed, plus the RTX 3060), and no Windows GPU
preference was set for any Link program. Likely cause: Link starting on the AMD chip. With no dashboard, nothing becomes visible, our app included.
Fix (the ROADMAP "force onto the NVIDIA GPU" item): Windows per-app GPU preference **High performance** (`HKCU\Software\Microsoft\DirectX\
UserGpuPreferences`, `GpuPreference=2;`, the same as Settings → System → Display → Graphics) for `OVRServer_x64.exe`, `OVRRedir.exe`,
`OculusDash.exe`, `OculusClient.exe`, `Builds\PC\TractorVR.exe` and `Unity.exe`. Needs a Link restart. ⏳ user check.
Also from the Link app: the headset is on a USB 2 connection ("USB 3 recommended").
**Cause found (same evening):** the GPU preference alone didn't help (49 more dashboard crashes after a restart). The full error is
`vkEnumeratePhysicalDevices failed` (VK_INCOMPLETE). `vulkaninfo --summary` listed **only the AMD iGPU**: the AMD driver's
`VK_LAYER_AMD_switchable_graphics` (driver 27.20.14032, Vulkan 1.2.150, from 2020) hides the RTX 3060 from Vulkan apps. With the layer's
own off switch (`DISABLE_LAYER_AMD_SWITCHABLE_GRAPHICS_1=1`, from its manifest `amd-vulkan64.json`) vulkaninfo lists GPU0 = RTX 3060,
GPU1 = AMD. Set as a **user environment variable** (OVRServer runs as the user). Needs a sign-out or restart to reach the Link processes. ⏳ user check.
(Also present: a "SudoMaker Virtual Display Adapter" virtual display driver; not touched, a suspect if Link still fails.)
**Result:** after the restart the Link dashboard starts (no Vulkan errors), and `TractorVR.exe` runs in the headset over Link (USB 2 cable).
User: "works beautifully". ✅ PC VR over Link working, 2026-10-30.
Refresh rate: the old menu path (Link app → Devices → Quest 2 → Graphics preferences) was given to the user; afterwards: "all works
amazing". Desktop version: "works flawlessly" (earlier the same evening).

## 2026-10-31: Version comparison (benchmark + quality stills); phase 4 closed
**`PerfBenchmark`** (cab panel / screen menu BENCHMARK, F8, `-benchmark`): resets the tractor (automatic gearbox, hand throttle idle,
PTO off, day), 3 s settle, 30 s cab view driving straight out of the yard at 0.55 throttle (25.5 km/h), 30 s third person with a gentle
left turn. Every frame: frame time, Unity `FrameTimingManager` CPU / GPU (`enableFrameTimingStats` on), and in VR the runtime's
`XR_META_performance_metrics` through `XRStats` (`perfmetrics.appgputime`, `perfmetrics.compositorgputime`,
`appstats.compositordroppedframes`; units checked: the XR app GPU time equals the FrameTimingManager GPU time in ms). Output in
`persistentDataPath/Benchmark/<version>_<time>/` (summary.txt, frames.csv, two stills); runs kept in `docs/benchmark/`.
Fixes found on the way: the gearbox mode is remembered between sessions, and manual mode held the first desktop run at 3 km/h in B2, so
the benchmark now sets a fixed machine state. Reading a still back stalls the next frame (one 120 ms frame on the PC, ~20 dropped frames
on the Quest), so the stills are now taken outside the timed drive. A still read from a multisampled target came out black on the Quest
(Vulkan), so it now renders to a single-sampled one (URP still applies MSAA). The first Quest run (`QuestStandalone_..._first`) is
kept for reference: its third-person phase also had a 3 s GPU peak of 19 ms in the turn, which the second run didn't repeat.

| | Quest standalone | PC VR (Link, USB 2) | PC desktop |
|---|---|---|---|
| Hardware | Quest 2 (Adreno 650, Vulkan) | RTX 3060 Laptop (D3D11), streamed | RTX 3060 Laptop, monitor |
| Quality level | Quest | PC | PC |
| Refresh / frame budget | 72 Hz / 13.9 ms | 90 Hz / 11.1 ms | 144 Hz (vSync) / 6.9 ms |
| Render target | 1440×1584 per eye, scale 1.0, foveation MEDIUM | 1998×2036 per eye (scale 1.2) | 1920×1080 (scale 1.2) |
| fps cab / third person | 71.8 / 71.8 | 89.7 / 89.7 | 143.9 / 144.0 |
| Frame time p99 | 13.9 ms | 11.2 ms | 7.0 ms |
| Frames over budget (>110 %) | 0.0 % | 0.1 % | 0.0 % |
| GPU time avg (p95) | 9.7 ms (10.4) | 2.1 ms (2.6) | 2.0 ms (3.5) |
| GPU share of the budget | 70 % | 19 % | 29 % |
| Compositor GPU | 1.4 ms | 0.8 ms | – |
| Dropped frames (60 s) | 5 | 0 | – |

Latency: not measured. Motion-to-photon isn't reported through this path, and the Link encoding / USB stream happens outside the app.
Reading: every version holds its refresh rate. The Quest uses 70 % of its GPU budget, so phase 5's environment has about 4 ms there
(light versions of every asset). PC VR uses under a fifth of its GPU budget: the picture over Link is limited by the USB 2 stream, not by
the laptop, and the PC level has room for a higher render scale and the full environment assets.

**Quality stills** (`VersionStills`, menu *Tractor VR → Render Quality Comparison*, + `tools/compare_stills.py`): the benchmark stills
from the headset follow the head (the PC VR one looked up at the open cab panel), so the image comparison renders fixed viewpoints in the
editor at both quality levels (PC VR and desktop share the PC level): `docs/images/quality_compare.png`, `quality_detail.png`.
Visible differences: on PC the tractor shadows itself (exhaust and cab on the bonnet) and its ground shadow has the real outline instead
of the stand-in's blocks; full-detail meshes up close; soft shadows reaching 80 m (the machine shed's shadow shows in the far view).

**Phase 4 closed (2026-10-31).**
