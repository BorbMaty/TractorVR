# Roadmap

## ▶ Where we left off (2026-10-17, night)
Phase 2 order: **1. physics + terrain ✅ → 2. hydraulics + implements ✅ → 3. VR interaction**.
- Block 3 headset test: controllers ✅, cab panel ✅ (also with hand tracking). Hand tracking on the wheel / lever **shook and kept dropping the grab**.
  Fixed (filter + grab debounce + tracking grace, replay test in `PhysicsTest` N). Hand-tracking grabs now visible: **the lever's button block stayed behind** when the lever moved.
  Fixed (knob pieces cut out with the lever). Then: **the lever looked like it broke off its stem** (the rod into the gate stayed put).
  Fixed (rod included, pivot moved down into the gate). Installed, ⏳ re-check. Hand-tracking grabs still feel fussy, see the proposals in the DEVLOG (2026-10-18).

**Next session:**
1. Headset re-check: hand tracking on the steering wheel and the speed lever (fist or pinch); the whole lever top moves
2. Tune if needed (`VRHands.HandSignal`: minCutoff, beta, releaseDelay, lostGrace), then decide what comes after phase 2

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

### Hydraulics and implements (block 2)
- [x] Rear 3-point hitch animated as a real four-bar linkage from the model's pivots (lift arms, lift rods, lower links, stabilisers, lift cylinders); link ends 0.43–1.27 m. Built 2026-10-17, ✅ headset-tested 2026-10-17
- [x] Front linkage: lower links fold from stowed down to working position. Built 2026-10-17, ✅ headset-tested 2026-10-17
- [x] PTO on/off (1000 rpm at 1900 engine rpm; AUTO holds the engine at PTO speed; PTO power reduces drive power). ✅ headset-tested 2026-10-17
- [x] Implements (built from primitives): 3-furrow plough (rear, 30 kN draft, depth control, furrow trail), 3 m front disc mower (PTO, spinning discs, mown strip), single-axle trailer (drawbar ball joint, own raycast wheels, jack). ✅ headset-tested 2026-10-17
- [x] Attach/detach with reach/alignment/height checks and HUD guidance; mounted mass moves the centre of mass
- [x] Trailer collides with the tractor while coupled and after detaching (bug from the headset test, fixed 2026-10-17). ✅ re-checked in the headset
- [ ] Top link visual for mounted implements, front cylinders, trailer tipping, better implement models

### Environment
- [x] Terrain with gentle hills (2 × 2 km, generated at runtime, slopes up to ~11°, flat start area), haze, lower sun. Built 2026-10-17, ✅ headset-tested 2026-10-17
- [ ] Field with crop rows, roads, a farmyard

### VR interaction (block 3)
- [x] Real Quest 2 controller models
- [x] Hand tracking (XR Hands 1.9, bundled): sample hand meshes driven by `XRHandSkeletonDriver`, shown while tracked; HAND_TRACKING in the manifest. ✅ hands show and press panel buttons
- [x] Grab the steering wheel (grip button, fist or pinch), one or two hands, hand over hand; stays put when let go. Controllers ✅; hand tracking fixed (shake/drops), ⏳ re-check
- [x] Grab the speed lever on the armrest (the shift lever, cut out of the cab mesh): forward = drive, back = reverse, stays set (cruise). Controllers ✅; hand tracking fixed, ⏳ re-check
- [x] Cab panel at the right front corner (touch buttons): view, recenter, comfort, gearbox, hitch/front up/down, PTO, attach, seat adjust/reset, units, reset tractor. ✅ headset-tested 2026-10-17
- [ ] Grab the hydraulic remote levers / hitch control on the armrest

Possible improvements (from the hand-tracking tests, 2026-10-18; not scheduled yet):
- [ ] Highlight the steering wheel rim / lever knob when a hand is in reach, brighter while held
- [ ] Gear the speed lever down: ~20 cm of hand travel for the full range (now ~10 cm), less sensitive to tracking wobble
- [ ] Snap the visible hand onto the rim / knob while holding (hides tracking jitter)
- [ ] Wider, notchy neutral on the speed lever (±20 % instead of ±8 %), visible snap + click
- [ ] NEUTRAL / STOP button on the cab panel (lever to neutral, tractor stops and holds)

Done first in phase 2: **drivetrain + turning steering wheel** (see above).
