# Roadmap

## ▶ Where we left off (2026-10-17, evening)
Phase 2 order agreed on 2026-10-17: **1. physics + terrain → 2. hydraulics + implements → 3. VR interaction**.
- Block 0 (steering wheel, cluster, gearbox, sounds): ✅ headset-tested.
- Block 1 (**physics + terrain**): ✅ headset-tested 2026-10-17 (hills, cab pitch/roll, bales all fine; no tuning needed).
- Block 2 (**hydraulics + implements**): in progress.

**Next session:**
1. Headset test of the physics build: comfort on the hills (the cab view now pitches and rolls with the tractor), driving feel, pushing bales, frame rate
2. Tune from the test. If the cab pitch/roll is uncomfortable, options: horizon-levelled cab view, flatter hills, stronger vignette on pitch/roll
3. Then block 2: hydraulics + implements

## Phase 1: flat world, general controls ✅ done (closed 2026-10-17)
Goal: drive the John Deere 6195M on a flat world with the Quest 2 thumbsticks, from two viewpoints.

Done:
- [x] Unity 6 project with URP, OpenXR (Meta Quest), Input System, Android/Quest player settings
- [x] Model imported at real scale (wheelbase 2.80 m), oriented, wheels rigged (spin + Ackermann steer, front hubs follow)
- [x] Materials rebuilt (textures missing from the FBX): JD green body, yellow rims, clear glass
- [x] Flat 2 × 2 km field with 10 m grid and marker posts every 25 m
- [x] Cab view (driver's seat) and third-person view (behind/above, smoothed yaw), switch with A
- [x] Driving: left stick steer, right stick throttle/brake/reverse, coasting, speed-dependent steering lock
- [x] Recenter (B + automatic), seat calibration (hold left grip), speed HUD
- [x] Scene generator + batch-mode preview renders + APK build script

Status at closing (the open items below were accepted as good enough after the headset tests):
- [ ] Play test in the editor (keyboard): not done, not needed so far
- [x] First successful APK build (`Tractor VR → Build Quest APK`), 2026-10-16
- [x] Headset test on the Quest 2 (2026-10-16): app runs, wheels spin and steer correctly
  - [x] Controller bindings (sticks, A/B, left grip)
  - [x] Seat height/position feels right (could still bake a calibrated value into `ProjectSetup`)
  - [ ] Third-person comfort (distance 9.5 m, height 4.2 m, yaw smoothing 0.35 s): **caused motion sickness**, see comfort below
  - [ ] Speed feel (max 40 km/h forward, 15 km/h reverse, acceleration 1.8 m/s²): looks right, but acceleration/turning too abrupt for comfort
  - [ ] Frame rate holds 72/90 Hz (no visible stutter yet; not measured with OVR Metrics Tool)
- [ ] Comfort (motion sickness reported in the first headset test):
  - [x] Comfort vignette while turning/accelerating ✅ headset-tested 2026-10-16
  - [x] Gentler defaults: acceleration 1.0 m/s², braking 2.5 m/s², coast 0.8 m/s², steer rate 35°/s, squared stick response, yaw ≤ 25°/s, lateral ≤ 2.5 m/s² ✅ headset-tested 2026-10-16
  - [x] Comfort level setting (off/low/high) on X, saved in PlayerPrefs ✅ headset-tested 2026-10-16
- [x] Visible controllers: official Quest 2 Touch models (from the Meta XR Core SDK) with animated stick/buttons/trigger/grip ✅ headset-tested 2026-10-16
- [ ] Optional: find the original TurboSquid `tex\` folder, put it in `Assets/Models/tex/`, and use it instead of the generated materials

## Phase 2: "the specifications" (started 2026-10-17)
First block, chosen 2026-10-17: **steering wheel + drivetrain** (biggest effect on how it feels in the headset).

### Cab realism
- [x] Split the steering wheel out of `Cab_Interier_01` (done in code, no Blender needed) and rotate it with the steering input (ratio 13 → ~2.9 turns lock to lock). Built 2026-10-17, ✅ headset-tested 2026-10-17
- [x] Instrument cluster: RPM + speed dials with needles, direction + gear, AUTO/MAN. Built 2026-10-17, ✅ headset-tested 2026-10-17
- [ ] Dashboard extras: fuel/temperature, CommandCenter display on the right corner post
- [ ] Opening doors (`Door_L/R` are already separate)

### Drivetrain
- [x] Engine RPM: idle 850, rated 2100, high idle 2250; RPM follows the gear, clutch slip when pulling away; load estimate for sound. ✅ headset-tested 2026-10-17
- [x] Gearbox: 20F/20R AutoQuad Plus-style (ranges A–E × 4 steps), auto + manual mode, F/N/R shuttle, 50 km/h top speed. ✅ headset-tested 2026-10-17
- [x] Sounds (synthesised, no samples): engine (two RPM-pitched loops cross-faded by load, muffled in the cab), shift clunk, reverse beeper, horn. ✅ headset-tested 2026-10-17
- [ ] Torque curve / real tractive force (needs the physics block), hand throttle

### Physics (block 1)
- [x] Replace the kinematic model with real wheel physics: Rigidbody + custom raycast suspension (spring-damper per wheel), tyre forces with a friction circle, 90 Hz physics + interpolation. Built 2026-10-17, ✅ headset-tested 2026-10-17
- [x] Mass 7.5 t, centre of gravity (40 % front), grip μ 0.8, slopes (195 hp power limit on climbs), parking brake that holds on a slope, collisions (pushable hay bales). Built 2026-10-17, ✅ headset-tested 2026-10-17
- [x] Offline physics test in the editor (`PhysicsTest`): settle, acceleration, turns, steepest hill, parking, bale push
- [ ] Wheel slip / spin visuals, soil types, ballast settings

### Hydraulics and implements
- [ ] Animate the 3-point hitch (`Rear_Holder_*`, `Rear_Forcer_*`) and front linkage (`Front_Forcer_*`)
- [ ] PTO
- [ ] Attach implements: plough, trailer, mower

### Environment
- [x] Terrain with gentle hills (2 × 2 km, generated at runtime, slopes up to ~11°, flat start area), haze, lower sun. Built 2026-10-17, ✅ headset-tested 2026-10-17
- [ ] Field with crop rows, roads, a farmyard

### VR interaction
- [x] Real Quest 2 controller models
- [ ] Hands (XR Hands) instead of / in addition to controllers
- [ ] Grab the real steering wheel and the armrest joystick instead of using the thumbsticks
- [ ] Menu (wrist or world-space) for settings: view distance, seat reset, speed units

Done first in phase 2: **drivetrain + turning steering wheel** (see above).
