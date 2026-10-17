# Roadmap

## ▶ Where we left off (2026-10-18, end of day)
**Finishing phases 1 and 2 before continuing phase 3** (decided 2026-10-18).
- Batch 1 (hand controls + testing) ✅ done and headset-checked: grab highlight, rebuilt speed lever (follows the hand around its pivot,
  neutral zone in the speed command, HUD status), hand snapping, STOP / NEUTRAL, FRAME TIME, FOVEATION, play-mode test.
- Frame rate: two optimisation rounds (foveated rendering, cheaper shaders, shadow stand-in, mesh LODs). The last session held **72 fps most of the time**
  (GPU ~11.3 ms of the 13.9 ms budget), with some stretches at ~59 fps and one short drop to 30–39 fps. Accepted for now (2026-10-18).
  If it needs another pass: log the context of slow frames (view, position, implements, trails) to find the expensive situations.

**Next session:**
1. Batch 2 (cab): opening doors, fuel / temperature + CommandCenter display, torque curve + hand throttle, grabbable hydraulic levers
2. Batch 3 (implements): top link, front-linkage cylinders, trailer tipping, front ballast weight, better implement models
3. Then phase 3 step 2 (traction)

Still open with the user: whether the original TurboSquid `tex\` folder is available.

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
- [x] Play test in the editor: automated (`Tractor VR → Run Play Mode Test`, `PlayModeTest`): the real scene in play mode with scripted input, 25 s, fails on any runtime error. ✅ 2026-10-18
- [x] First successful APK build (`Tractor VR → Build Quest APK`), 2026-10-16
- [x] Headset test on the Quest 2 (2026-10-16): app runs, wheels spin and steer correctly
  - [x] Controller bindings (sticks, A/B, left grip)
  - [x] Seat height/position feels right (could still bake a calibrated value into `ProjectSetup`)
  - [x] Third-person comfort: caused motion sickness at first; solved by the comfort pass below ✅ headset-tested 2026-10-16
  - [x] Speed feel: acceleration/turning were too abrupt; solved by the comfort pass (and the phase 2 drivetrain: 50 km/h top speed) ✅
  - [x] Frame rate: measured on the Quest (2026-10-18). Started at 41–53 fps (GPU-bound, 18 ms); after foveated rendering, cheaper shaders, a shadow stand-in
    and mesh LODs it holds 72 fps most of the time (11.3 ms GPU), occasional dips to ~59 and one to 30–39 fps. Accepted for now; further tuning possible
- [x] Comfort (motion sickness reported in the first headset test):
  - [x] Comfort vignette while turning/accelerating ✅ headset-tested 2026-10-16
  - [x] Gentler defaults: acceleration 1.0 m/s², braking 2.5 m/s², coast 0.8 m/s², steer rate 35°/s, squared stick response, yaw ≤ 25°/s, lateral ≤ 2.5 m/s² ✅ headset-tested 2026-10-16
  - [x] Comfort level setting (off/low/high) on X, saved in PlayerPrefs ✅ headset-tested 2026-10-16
- [x] Visible controllers: official Quest 2 Touch models (from the Meta XR Core SDK) with animated stick/buttons/trigger/grip ✅ headset-tested 2026-10-16
- [ ] Optional: find the original TurboSquid `tex\` folder, put it in `Assets/Models/tex/`, and use it instead of the generated materials (⏳ asked the user)

## Phase 2: "the specifications" (started 2026-10-17)
First block, chosen 2026-10-17: **steering wheel + drivetrain** (biggest effect on how it feels in the headset).

### Cab realism
- [x] Split the steering wheel out of `Cab_Interier_01` (done in code, no Blender needed) and rotate it with the steering input (ratio 13 → ~2.9 turns lock to lock). Built 2026-10-17, ✅ headset-tested 2026-10-17
- [x] Instrument cluster: RPM + speed dials with needles, direction + gear, AUTO/MAN. Built 2026-10-17, ✅ headset-tested 2026-10-17
- [x] Interior textures: generated tiling detail (fine plastic grain, seat fabric weave, ribbed floor mat, pedal tread) through a triplanar shader
  (the cab plastics have no usable UVs), plus baked ambient occlusion on the cab interior, wheel and lever. Built 2026-10-18, ✅ headset-checked 2026-10-18
- [ ] Dashboard extras: fuel/temperature, CommandCenter display on the right corner post (batch 2)
- [ ] Opening doors (`Door_L/R` are already separate) (batch 2)

### Drivetrain
- [x] Engine RPM: idle 850, rated 2100, high idle 2250; RPM follows the gear, clutch slip when pulling away; load estimate for sound. ✅ headset-tested 2026-10-17
- [x] Gearbox: 20F/20R AutoQuad Plus-style (ranges A–E × 4 steps), auto + manual mode, F/N/R shuttle, 50 km/h top speed. ✅ headset-tested 2026-10-17
- [x] Sounds (synthesised, no samples): engine (two RPM-pitched loops cross-faded by load, muffled in the cab), shift clunk, reverse beeper, horn. ✅ headset-tested 2026-10-17
- [x] Real tractive force: done with the physics block (drive force limited by engine power and tyre grip)
- [ ] Torque curve (power depends on engine speed), hand throttle (batch 2)

### Physics (block 1)
- [x] Replace the kinematic model with real wheel physics: Rigidbody + custom raycast suspension (spring-damper per wheel), tyre forces with a friction circle, 90 Hz physics + interpolation. Built 2026-10-17, ✅ headset-tested 2026-10-17
- [x] Mass 7.5 t, centre of gravity (40 % front), grip μ 0.8, slopes (195 hp power limit on climbs), parking brake that holds on a slope, collisions (pushable hay bales). Built 2026-10-17, ✅ headset-tested 2026-10-17
- [x] Offline physics test in the editor (`PhysicsTest`): settle, acceleration, turns, steepest hill, parking, bale push
- [x] Soil types: done in phase 3 step 1 (GroundMap: grip, rolling resistance, draft per surface)
- [ ] Wheel slip / spin visuals → moved to phase 3 step 2 (traction)
- [ ] Ballast: front weight on the front linkage (batch 3)

### Hydraulics and implements (block 2)
- [x] Rear 3-point hitch animated as a real four-bar linkage from the model's pivots (lift arms, lift rods, lower links, stabilisers, lift cylinders); link ends 0.43–1.27 m. Built 2026-10-17, ✅ headset-tested 2026-10-17
- [x] Front linkage: lower links fold from stowed down to working position. Built 2026-10-17, ✅ headset-tested 2026-10-17
- [x] PTO on/off (1000 rpm at 1900 engine rpm; AUTO holds the engine at PTO speed; PTO power reduces drive power). ✅ headset-tested 2026-10-17
- [x] Implements (built from primitives): 3-furrow plough (rear, 30 kN draft, depth control, furrow trail), 3 m front disc mower (PTO, spinning discs, mown strip), single-axle trailer (drawbar ball joint, own raycast wheels, jack). ✅ headset-tested 2026-10-17
- [x] Attach/detach with reach/alignment/height checks and HUD guidance; mounted mass moves the centre of mass
- [x] Trailer collides with the tractor while coupled and after detaching (bug from the headset test, fixed 2026-10-17). ✅ re-checked in the headset
- [ ] Top link for mounted implements, front-linkage cylinders, trailer tipping, better implement models (batch 3)

### Environment
- [x] Terrain with gentle hills (2 × 2 km, generated at runtime, slopes up to ~11°, flat start area), haze, lower sun. Built 2026-10-17, ✅ headset-tested 2026-10-17
- [x] Roads and a farmyard: done in phase 3 step 1
- [ ] Field with crop rows → moved to phase 3 step 3

### VR interaction (block 3)
- [x] Real Quest 2 controller models
- [x] Hand tracking (XR Hands 1.9, bundled): sample hand meshes driven by `XRHandSkeletonDriver`, shown while tracked; HAND_TRACKING in the manifest. ✅ hands show and press panel buttons
- [x] Grab the steering wheel (grip button, fist or pinch), one or two hands, hand over hand; stays put when let go. ✅ Controllers; hand tracking works (shake/drops fixed) but still a bit fussy, see the improvements below
- [x] Grab the speed lever on the armrest (the shift lever + knob pieces + stem, cut out of the cab mesh, pivot inside the gate): forward = drive, back = reverse, stays set (cruise). ✅ Controllers; hand tracking as for the wheel
- [x] Cab panel at the right front corner (touch buttons): view, recenter, comfort, gearbox, hitch/front up/down, PTO, attach, seat adjust/reset, units, reset tractor. ✅ headset-tested 2026-10-17
- [ ] Grab the hydraulic remote levers / hitch control on the armrest (batch 2; also listed in phase 3 step 4)

Hand-control improvements (from the hand-tracking tests, built 2026-10-18 in batch 1, ✅ headset-checked 2026-10-18):
- [x] Highlight the steering wheel rim / lever knob when a hand is in reach, brighter while held
- [x] Speed lever reworked: it follows the hand around its pivot (the knob stays in the hand), relative to the grab point (never jumps); HUD shows N / F % / R %
  (first tried as a geared straight-line mapping: reverse was out of reach and small moves did nothing)
- [x] The visible tracked hand snaps onto the rim / knob while holding (hides tracking jitter)
- [x] Neutral zone on the speed lever: speed 0 in the middle ±25 % of the swing, smooth beyond; click at the edge; snaps upright if let go there
- [x] STOP / NEUTRAL button on the cab panel (lever to neutral, brakes at 2.5 m/s² and holds); also FRAME TIME and FOVEATION buttons
- [x] Pinch grabs at the pinch point, grab reach 9 cm for tracked hands (the steering wheel couldn't be grabbed by hand before)

Done first in phase 2: **drivetrain + turning steering wheel** (see above).

## Phase 3: physics, terrain, cab buttons (started 2026-10-18)
Chosen 2026-10-18 from a list: soil + wheel slip, brakes + diff lock + 4WD; fields + crop rows, roads + farmyard, varied ground;
hydraulic levers, pressable switches, lights, and keep the cab panel. Order (each step needs the one before):

### Step 1: ground + layout
- [x] Ground map: surface type per square metre (grass, field soil, crop, ploughed, mud, gravel, asphalt, concrete) with grip, rolling resistance and plough draft. ✅ headset-checked 2026-10-18
- [x] Terrain shader (`GroundLit`) blending grass / soil / mud from a control map, generated textures, large-scale variation. ✅ headset-checked 2026-10-18
- [x] Farmyard at the start (concrete yard, barn, open machine shed), gravel tracks to three fields, an asphalt road (x = 80), mud at the field gates and in hollows. ✅ headset-checked 2026-10-18
- [x] Physics per surface: tyre grip and rolling resistance under each wheel (tractor and trailer), plough draft by soil, no ploughing on hard ground,
  ploughing turns the ground to "ploughed". Measured in `PhysicsTest` O (rolling 1.1 kN on asphalt … 11 kN in mud). The grid floor and marker posts are gone.

### Step 2: traction
- [ ] Wheel slip: driven wheels spin when the ground can't take the force (slip % on the dash), wheel spin visuals
- [ ] Brakes (rear / all-wheel), 4WD (front axle drive) on/off, diff lock

### Step 3: fields + crops
- [ ] Standing crop rows on the crop field, cut by the mower
- [ ] Ploughing turns the ground to "ploughed" (grip, draft, look)

### Step 4: cab buttons
- [ ] The three hydraulic remote levers on the armrest: grabbable, move the hitch / front linkage / trailer tipping
- [ ] Pressable switches and knobs on the console and corner post: lights, PTO, 4WD, diff lock, hazards, beacon
- [ ] Working lights (headlights, work lights, beacon, indicators), day / dusk option
- [ ] Keep the cab panel as the easy way to reach everything

## Future ideas / reference (not scheduled)
- **Working mirrors**: the model has three parts with the `mirror` material. One small render-texture camera per mirror (mono, from the reflected
  eye position), ~256², short draw distance, no shadows, updated every other frame, shown on the mirror glass; a MIRRORS on/off panel button.
  Expected cost ~1–2 ms per frame on the Quest 2, so measure the frame rate (OVR Metrics Tool) before keeping it.
- Original TurboSquid textures: if the `tex\` folder turns up, put it in `Assets/Models/tex/` and use it instead of the generated materials.
- Hand-control improvements listed under VR interaction.
