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
