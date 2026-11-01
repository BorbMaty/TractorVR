# Roadmap

## ▶ Where we left off (2026-11-01, end of the day): a complete game, documented
- **Phase 5** (environment) and **phase 6** (start screen + tutorial) are built: trees, stream, village, Poly Haven skies, power
  lines and signs, grass tufts; the practice area; the start screen (free roam / tutorial / controls with annotated pictures / quit),
  nine lessons with set-ups (teleports), glows and 1-5 stars. Hand tracking works in PC VR too (Link: Developer Runtime Features).
- **Documentation**: README brought up to date (new pictures, phases 5-6, credits); new **docs/PHYSICS.md**: the vehicle model with
  its formulas, the numbers, 10 graphs (model curves + PhysicsTest runs) and a validation table.
- Tests: `PlayModeTest` (both quality levels; start menu, controls pages, all lessons) and `PhysicsTest` pass. Quest APK and PC exe current.

### ▶ Next session: start here
1. **Quest benchmark** (cab panel BENCHMARK) with the trees, skies and tufts: the first session with the trees averaged 69 fps
   (72 fps 70 % of the time). If the trees cost too much: shorten their near distance on the Quest (120 m → ~80 m).
2. **Tutorial tuning** after real runs: par times for the stars, the step checks (coupling distance, reversing bay, earth ramp).
3. **Readability** of the CONTROLS pictures in the headset (bigger lettering or zoomed crops if needed).
4. Still open from before: the desktop check of coupling from the shed bays with the mouse.


## ▶ Where we left off (2026-11-01, late evening): the game is playable
Goal of the evening (user): a working game. Built: **phase 5 step 4** (trees, a stream, a village on the horizon), **step 5**
(practice area), **phase 6** (start menu FREE ROAM / TUTORIAL, 9 lessons with self-checking steps and glows). `PlayModeTest` (both
levels, walks every lesson step) and `PhysicsTest` pass; Quest APK and PC exe rebuilt. Details in the DEVLOG.

### ▶ Next session: start here
1. **Headset play-through** (Quest APK, and PC VR over Link): the start menu, then the lessons one by one. Look for: board readable
   and in reach (left front), buttons easy to poke, glows visible on the right controls, instructions match the controls, each check
   passes when it should (coupling distances, the slalom, the reversing bay, the earth ramp check).
2. **Quest benchmark** with the trees (target: trees under ~1.5 ms; detailDistance 120 m), and the open items of the list below
   (the landscape look, mowing / ploughing look, steep slopes comfort, desktop coupling from the shed bays).
3. Desktop check of the tutorial box (top left) and the start menu with the mouse.

## ▶ Where we left off (2026-11-01, end of day)
**Phase 5 steps 1–3 are built** in one day (details in the DEVLOG, pictures in the README):
- **Step 1, farmyard** ✅: farmhouse (user: "love it"), Székely gate + fences (top fixed twice after the user's screenshot), silos,
  diesel tank, tipping pad, bale stack, worn concrete yard, speed bumps + a moving front axle, silage clamp (18°), earth ramp (15° / 22°);
  the tractor's colliders made round / higher (they stuck on steep ramps).
- **Step 2, implements remade** ✅ in Blender (user: "looks good to me").
- **Step 3, fields** ✅: FieldMap (furrows, stubble, swaths painted on the GPU), a meadow of standing grass the mower cuts, a wheat field;
  then, at the user's request, **the land around Reghin**: Mezőség hills with strip parcels and balks outside the farm and fields,
  the Görgényi / Kelemen mountains on the horizon, haystacks (boglya), pole gates, a wayside cross.
- Quest (run 02-47, before the Reghin landscape): every frame at 72 fps, worst case 10.8 ms of 13.9 (meadow with grass), 3.1 ms left.
- `PhysicsTest` (new: W bumps, X clamp / ramp, FieldMap checks; plough runs on the west field) and `PlayModeTest` (both levels) pass.
  PC exe and Quest APK are current.

### ▶ Next session (2026-11-02): start here
1. **User's look in the headset** at what was built after the last check (nothing of it is headset-checked yet):
   - the land around Reghin: hills, strip parcels, mountains on the horizon (also at DUSK / NIGHT: their haze follows the fog colour),
     haystacks, the pole gate at the meadow, the wayside cross at the main road;
   - mow a few passes in the meadow and look back (stubble, swaths); plough a strip on the west field (furrows);
   - drive the steeper countryside (up to 22.8° south of the south-east field) and the earth ramp's 22° side from the cab: comfort.
2. **One Quest benchmark** (cab panel BENCHMARK): the last run was before the hills, strip parcels and horizon; confirm they cost
   nothing (expected: the horizon is one mesh, the parcels no texture reads) and that the drive still holds 72 fps.
3. Still open from step 1: the **desktop check** of coupling from the shed bays with the mouse.
4. Then **phase 5 step 4, landscape**, first task: **trees for this landscape**. Blender scripts for a walnut, a wild pear, a black
   locust (acacia) and a willow / poplar, Full + Light, plus flat billboard impostors for the far ones; placed as solitary trees on the
   balks of the strip parcels, acacia strips along the tracks and ravines, willows and poplars along a new stream in the valley. Keep
   the Quest under ~1.5 ms for all trees (instanced like the grass, impostors beyond ~120 m), and benchmark before the village.

## ▶ Where we left off (2026-10-31)
**Phase 5 started**: plan agreed with the user (see phase 5 below): Transylvanian look, Poly Haven + Blender scripts, implements
remade, farmyard first, plus speed bumps and steep practice slopes.
**Step 0 (pipeline) built the same day**: Blender scripts → FBX with a Full / Light variant → `ProjectSetup` → `TractorVR/EnvLit`;
first asset = the barn (replaces the grey block barn), `PlayModeTest` passes at both quality levels. First Quest run: no change on the
drive (it never shows the barn), so the benchmark got a barn phase (`env` / `noenv`). Run 2: facing the barn the Quest GPU is 9.6 ms
with it and 10.0 ms without (it hides hills that cost more than it does) → **step 0 done**.
**Step 1 started**: the machine shed is built (Blender, 4 marked bays, the implements now wait in them). Next: farmhouse, yard details.
**Next session (user, 2026-10-31):** the new shed in the PC version. `Builds/PC/TractorVR.exe` rebuilt the same night with the shed
(desktop benchmark run: 144 fps, GPU 1.2 ms, no new errors in Player.log). Still to do: the user's check in PC VR over Link and on the
desktop (Full variant, normal maps, coupling from the bays with the mouse), then go on with the farmhouse and yard details.
Phase 4 has no open items left (desktop screen UI ✅ user, 2026-10-31).

## ▶ Where we left off (2026-10-30, end of day)
**Phase 3 is complete** (user's headset check, 2026-10-30): reset-with-trailer bug fixed, step 1 ground + layout, step 2 traction,
step 4 cab switches + lights (after a fix: pointing at a switch with hand tracking grabbed the remote levers). Step 3 (fields + crops)
moved to phase 5 (environment with Blender).

**Plan agreed 2026-10-30** (user): two modes, free roam and a tutorial; a better environment made with Blender (blender-mcp);
three versions because the Quest GPU is weak. Order: **phase 4 versions → phase 5 environment → phase 6 tutorial**:
- the versions first: they set the graphics budget per version before any art is made (each Blender asset gets a full and a light
  version from the start), and playing from the editor through Link speeds up all the later testing;
- the environment second: the tutorial's lessons are tied to places (the shed, the field gate, the fields);
- the tutorial last, on the final map and controls (the cab guide's control positions can highlight what to touch).

Next: phase 5 (environment with Blender). blender-mcp installed 2026-10-30.

**Phase 4 closed 2026-10-31**: three versions from one project (Quest APK; one Windows exe that plays in VR over Link or on the monitor),
Quest / PC quality levels, benchmark + quality comparison. Budget for phase 5: Quest GPU at 70 % (about 4 ms left), PC VR at 19 %.

**Phase 4 started (2026-10-30, night):** two quality levels (Quest / PC) with their own URP assets, `QualityTier` (model shadows on PC,
budget for phase 5 assets), Windows build (`Builds/PC/TractorVR.exe`). Next: the user's first run through Link (cable or Air Link,
editor play mode + the exe), then measure. Desktop version built the same night (the exe plays on the monitor without a headset): user test.

## Earlier: end of day 2026-10-21
**Phases 1 and 2 are complete** (user's headset test, 2026-10-21). Batches 1–3 finished every open item:
hand controls + testing, cab (doors, CommandCenter, torque curve, hand throttle, remote levers), implements (top link, front
cylinders, tipping trailer, front weight, better models).
- Frame rate: 72 fps most of the time after two optimisation rounds (2026-10-18). If it needs another pass: log the context of slow frames.

**Known bug (found in the headset test, 2026-10-21; fix first next session):**
- [x] RESET TRACTOR with the trailer coupled: the trailer disappears but stays coupled, and it can't be detached afterwards.
  Fixed 2026-10-30: the reset brings a coupled trailer along, straight on the drawbar and at rest, and also clears the parking-brake
  anchor (it pulled towards the old spot after a teleport). PhysicsTest T. ✅ headset-checked 2026-10-30

**Next session:**
1. Fix the reset-with-trailer bug above ✅
2. Phase 3 step 2 (traction) ✅

Planned (2026-10-19): a second, PC VR build over Quest Link next to the standalone one, see "PC VR build" below.

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
- Optional: original TurboSquid `tex\` folder → moved to "Future ideas" (2026-10-21, phase 1 closed without it)

## Phase 2: "the specifications" ✅ done (closed 2026-10-21)
First block, chosen 2026-10-17: **steering wheel + drivetrain** (biggest effect on how it feels in the headset).

### Cab realism
- [x] Split the steering wheel out of `Cab_Interier_01` (done in code, no Blender needed) and rotate it with the steering input (ratio 13 → ~2.9 turns lock to lock). Built 2026-10-17, ✅ headset-tested 2026-10-17
- [x] Instrument cluster: RPM + speed dials with needles, direction + gear, AUTO/MAN. Built 2026-10-17, ✅ headset-tested 2026-10-17
- [x] Interior textures: generated tiling detail (fine plastic grain, seat fabric weave, ribbed floor mat, pedal tread) through a triplanar shader
  (the cab plastics have no usable UVs), plus baked ambient occlusion on the cab interior, wheel and lever. Built 2026-10-18, ✅ headset-checked 2026-10-18
- [x] Dashboard extras: fuel / coolant temperature and a CommandCenter display on the right corner post (engine speed, hand throttle,
  fuel level and use, coolant, power, linkage positions, PTO, engine hours, THROTTLE − / + touch buttons). Fuel and temperature live on
  the CommandCenter: the cluster has no visible space left (hub and column shroud). Built 2026-10-21 in batch 2, ✅ headset-checked 2026-10-21
- [x] Opening doors: rear-hinged on the model's pivots, open outward up to 65° (clears the rear fender), grab anywhere on the door or
  panel DOORS button, clunk when they shut. Built 2026-10-21 in batch 2, ✅ headset-checked 2026-10-21

### Drivetrain
- [x] Engine RPM: idle 850, rated 2100, high idle 2250; RPM follows the gear, clutch slip when pulling away; load estimate for sound. ✅ headset-tested 2026-10-17
- [x] Gearbox: 20F/20R AutoQuad Plus-style (ranges A–E × 4 steps), auto + manual mode, F/N/R shuttle, 50 km/h top speed. ✅ headset-tested 2026-10-17
- [x] Sounds (synthesised, no samples): engine (two RPM-pitched loops cross-faded by load, muffled in the cab), shift clunk, reverse beeper, horn. ✅ headset-tested 2026-10-17
- [x] Real tractive force: done with the physics block (drive force limited by engine power and tyre grip)
- [x] Torque curve (880 Nm at 1500–1600 rpm, 145 kW rated, 151 kW peak at 1800): the drive force is limited by the power at the current
  engine speed; AUTO skip-shifts, shifts down when lugging, and revs up to peak torque when pulling away under load.
  Hand throttle (panel / CommandCenter / [ ]): AUTO holds the engine there, manual drives at the gear's speed for that engine speed.
  Built 2026-10-21 in batch 2, ✅ headset-checked 2026-10-21

### Physics (block 1)
- [x] Replace the kinematic model with real wheel physics: Rigidbody + custom raycast suspension (spring-damper per wheel), tyre forces with a friction circle, 90 Hz physics + interpolation. Built 2026-10-17, ✅ headset-tested 2026-10-17
- [x] Mass 7.5 t, centre of gravity (40 % front), grip μ 0.8, slopes (195 hp power limit on climbs), parking brake that holds on a slope, collisions (pushable hay bales). Built 2026-10-17, ✅ headset-tested 2026-10-17
- [x] Offline physics test in the editor (`PhysicsTest`): settle, acceleration, turns, steepest hill, parking, bale push
- [x] Soil types: done in phase 3 step 1 (GroundMap: grip, rolling resistance, draft per surface)
- [x] Wheel slip / spin visuals → moved to phase 3 step 2 (traction), done there 2026-10-30
- [x] Ballast: 900 kg front weight for the front linkage (cast slabs on a carrier). Front axle +12.6 kN, rear −3.8 kN (expected +12.3 / −3.5).
  The centre of mass now follows the linkage positions every frame. Built 2026-10-21 in batch 3, ✅ headset-checked 2026-10-21

### Hydraulics and implements (block 2)
- [x] Rear 3-point hitch animated as a real four-bar linkage from the model's pivots (lift arms, lift rods, lower links, stabilisers, lift cylinders); link ends 0.43–1.27 m. Built 2026-10-17, ✅ headset-tested 2026-10-17
- [x] Front linkage: lower links fold from stowed down to working position. Built 2026-10-17, ✅ headset-tested 2026-10-17
- [x] PTO on/off (1000 rpm at 1900 engine rpm; AUTO holds the engine at PTO speed; PTO power reduces drive power). ✅ headset-tested 2026-10-17
- [x] Implements (built from primitives): 3-furrow plough (rear, 30 kN draft, depth control, furrow trail), 3 m front disc mower (PTO, spinning discs, mown strip), single-axle trailer (drawbar ball joint, own raycast wheels, jack). ✅ headset-tested 2026-10-17
- [x] Attach/detach with reach/alignment/height checks and HUD guidance; mounted mass moves the centre of mass
- [x] Trailer collides with the tractor while coupled and after detaching (bug from the headset test, fixed 2026-10-17). ✅ re-checked in the headset
- [x] Top link: stowed upright until a three-point implement is coupled, then set to the length that fits (plough: 92 cm) and pinned to
  the implement's top pin; the implement pitches with the hitch (plough −1.2° at work, +14.6° fully raised). Built 2026-10-21 in batch 3, ✅ headset-checked 2026-10-21
- [x] Front-linkage cylinders: barrel on its frame pin, rod on the lug below each lower link's pivot. Built 2026-10-21 in batch 3, ✅ headset-checked 2026-10-21
- [x] Trailer tipping: rear-hinged body, 7-stage telescopic ram in front of the headboard, tailgate hanging plumb, 3 t of soil that pours
  out above 20° into a heap (3 t in ~5 s at 45°); remote lever 3, panel TIP UP / DOWN, I / K. Built 2026-10-21 in batch 3, ✅ headset-checked 2026-10-21
- [x] Better implement models (built in code): lugged tyres, twisted mouldboards, coulters, A-frame headstocks, mower hood / knives / curtain,
  trailer stakes, ladder, mudguards, lamps. Built 2026-10-21 in batch 3, ✅ headset-checked 2026-10-21

### Environment
- [x] Terrain with gentle hills (2 × 2 km, generated at runtime, slopes up to ~11°, flat start area), haze, lower sun. Built 2026-10-17, ✅ headset-tested 2026-10-17
- [x] Roads and a farmyard: done in phase 3 step 1
- [ ] Field with crop rows → moved to phase 3 step 3, then to phase 5, environment with Blender (2026-10-30)

### VR interaction (block 3)
- [x] Real Quest 2 controller models
- [x] Hand tracking (XR Hands 1.9, bundled): sample hand meshes driven by `XRHandSkeletonDriver`, shown while tracked; HAND_TRACKING in the manifest. ✅ hands show and press panel buttons
- [x] Grab the steering wheel (grip button, fist or pinch), one or two hands, hand over hand; stays put when let go. ✅ Controllers; hand tracking works (shake/drops fixed) but still a bit fussy, see the improvements below
- [x] Grab the speed lever on the armrest (the shift lever + knob pieces + stem, cut out of the cab mesh, pivot inside the gate): forward = drive, back = reverse, stays set (cruise). ✅ Controllers; hand tracking as for the wheel
- [x] Cab panel at the right front corner (touch buttons): view, recenter, comfort, gearbox, hitch/front up/down, PTO, attach, seat adjust/reset, units, reset tractor. ✅ headset-tested 2026-10-17
- [x] Grab the hydraulic remote levers on the armrest: three levers cut out of the cab mesh (knob + handle + stem), spring-centred,
  1 = rear hitch, 2 = front linkage, 3 = tipping trailer (pull back = raise, speed ∝ deflection). Built 2026-10-21 in batches 2 and 3, ✅ headset-checked 2026-10-21

Hand-control improvements (from the hand-tracking tests, built 2026-10-18 in batch 1, ✅ headset-checked 2026-10-18):
- [x] Highlight the steering wheel rim / lever knob when a hand is in reach, brighter while held
- [x] Speed lever reworked: it follows the hand around its pivot (the knob stays in the hand), relative to the grab point (never jumps); HUD shows N / F % / R %
  (first tried as a geared straight-line mapping: reverse was out of reach and small moves did nothing)
- [x] The visible tracked hand snaps onto the rim / knob while holding (hides tracking jitter)
- [x] Neutral zone on the speed lever: speed 0 in the middle ±25 % of the swing, smooth beyond; click at the edge; snaps upright if let go there
- [x] STOP / NEUTRAL button on the cab panel (lever to neutral, brakes at 2.5 m/s² and holds); also FRAME TIME and FOVEATION buttons
- [x] Pinch grabs at the pinch point, grab reach 9 cm for tracked hands (the steering wheel couldn't be grabbed by hand before)

Done first in phase 2: **drivetrain + turning steering wheel** (see above).

## Phase 3: physics, terrain, cab buttons ✅ done (closed 2026-10-30)
Chosen 2026-10-18 from a list: soil + wheel slip, brakes + diff lock + 4WD; fields + crop rows, roads + farmyard, varied ground;
hydraulic levers, pressable switches, lights, and keep the cab panel. Order (each step needs the one before):

### Step 1: ground + layout
- [x] Ground map: surface type per square metre (grass, field soil, crop, ploughed, mud, gravel, asphalt, concrete) with grip, rolling resistance and plough draft. ✅ headset-checked 2026-10-18
- [x] Terrain shader (`GroundLit`) blending grass / soil / mud from a control map, generated textures, large-scale variation. ✅ headset-checked 2026-10-18
- [x] Farmyard at the start (concrete yard, barn, open machine shed), gravel tracks to three fields, an asphalt road (x = 80), mud at the field gates and in hollows. ✅ headset-checked 2026-10-18
- [x] Physics per surface: tyre grip and rolling resistance under each wheel (tractor and trailer), plough draft by soil, no ploughing on hard ground,
  ploughing turns the ground to "ploughed". Measured in `PhysicsTest` O (rolling 1.1 kN on asphalt … 11 kN in mud). The grid floor and marker posts are gone.

### Step 2: traction
- [x] Wheel slip: driven wheels spin when the ground can't take the force (slip % on the CommandCenter + HUD), wheels turn at their own
  speed (faster when spinning, still when locked), soil clods from wheels slipping hard on soft ground, engine RPM follows the wheels.
  Built 2026-10-30, ✅ headset-checked 2026-10-30
- [x] Brakes (all wheels / rear only), 4WD (front axle drive) on/off, diff lock (open diffs: the wheel with less grip limits the axle;
  locks off above 12° steering or 15 km/h): panel buttons + F / L / B. Built 2026-10-30, ✅ headset-checked 2026-10-30

### Step 3: fields + crops → moved (2026-10-30)
Skipped by the user's decision: environment work will be done with Blender. Both items are in phase 5 (environment with Blender).
(Ploughed ground already gets its own grip and draft since step 1; what is left is the look and the crops.)

### Step 4: cab buttons
- [x] The three hydraulic remote levers on the armrest: grabbable, move the hitch / front linkage / trailer tipping (done in phase 2, batches 2 and 3)
- [x] Pressable switches on the right-hand console, cut out of the cab model: road-light knob, hazards button, front / rear work lights,
  beacon, 4WD, diff lock, PTO (a lamp in each), plus the indicator stalk on the column (grab, up / down, self-cancelling).
  Built 2026-10-30, ✅ headset-checked 2026-10-30. (The corner post has no switches in the model: the CommandCenter sits there.)
- [x] Lights: headlights, side + tail lights, brake lights, indicators + hazards (ticking), front / rear work lights, two rotating
  beacons; lens discs + halos, real spot lights for head / work lights; DAY / DUSK / NIGHT. Built 2026-10-30, ✅ headset-checked 2026-10-30
- [x] Keep the cab panel as the easy way to reach everything (31 buttons in three columns now)

## Phase 4: three versions — Quest standalone, PC VR over Link, PC desktop ✅ done (closed 2026-10-31)
From the same project: the optimised **Quest standalone** (Android) build stays as it is; a **PC VR** build renders on the laptop
(RTX 3060 Laptop 6 GB, Ryzen 7 5800H, 16 GB) and streams to the Quest 2 over Link / Air Link (the 750k-triangle model is cheap for that
GPU); a **PC desktop** build runs on the monitor without a headset (confirmed by the user 2026-10-30: monitor, mouse and keyboard, no headset).

PC VR (planned 2026-10-19):
- [x] Laptop setup: install the Meta Quest Link app, set it as the active OpenXR runtime, allow unknown sources; force OVRServer and Unity / the build onto the NVIDIA GPU
  (2026-10-30: the Meta Horizon Link app is installed and already the active OpenXR runtime; NVIDIA driver rolled back to 591.86 as the Link app asks;
  High-performance GPU preference set for the Link programs, TractorVR.exe and Unity; the AMD switchable-graphics Vulkan layer hid the RTX 3060 and
  crashed the Link dashboard: disabled with the user variable `DISABLE_LAYER_AMD_SWITCHABLE_GRAPHICS_1=1`. ✅ Link works, the PC build runs in the headset)
- [x] Cable: USB 2 (the Link app says "USB 3 recommended"); works, and stays for now (user's decision 2026-10-30). Better: USB-C with USB 3 data (Link cable or a good 3–5 m cable); Air Link is an option: a 5 GHz Wi-Fi network is available (best with the laptop on Ethernet to the router, headset close to it)
- [x] Play mode through Link in the Editor (faster test loop, no APK install). ✅ user, 2026-10-30
- [x] Windows Standalone build profile: OpenXR + Oculus Touch profiles, quality level "PC", Mono + D3D11, windowed; `BuildPC.Build` → `Builds/PC/TractorVR.exe`. Built 2026-10-30
- [x] PC quality: full model (mesh LOD threshold 0.5), real model shadows instead of the `ShadowProxy` (`QualityTier`), MSAA 4×, render scale 1.2,
  soft shadows, 4096 shadow map, 80 m. Built 2026-10-30, ✅ seen in the headset over Link 2026-10-30
- [x] 90 Hz: set in the Link app (Devices → Quest 2 → Graphics preferences), not by the app. User: "all works amazing" (2026-10-30)
- [x] Platform-specific settings so the Android build keeps its optimisations: per-platform default quality level (Android → Quest), foveation is
  an Android-only OpenXR feature, the stand-in stays on in the Quest tier. `PlayModeTest` checked at both levels, 2026-10-30
- [x] Measure and compare the builds (frame time, image quality) for the documentation: `PerfBenchmark` runs of all three versions and
  `VersionStills` quality sheets, table in the DEVLOG and README (2026-10-31). Latency not measurable from the app (noted)

PC desktop:
- [x] Non-VR camera rig: mouse look in the cab (right mouse, zoom), third-person orbit; keyboard / gamepad driving. Same exe: VR when the headset
  is in use, the monitor otherwise (`DesktopMode`, 2026-10-30). ✅ user: "works flawlessly"
- [x] Screen UI instead of the in-cab panel (same 31 buttons, M), mouse clicks on the switches and drags on the wheel / levers (the mouse is a
  `VRHands` hand; pick proxies on layer 30). Built 2026-10-30, ✅ user 2026-10-31 (after the wider-buttons fix)
- [x] Desktop quality level (the PC level), windowed 1600×900 resizable / fullscreen (F11 or the menu), vSync. Built 2026-10-30

Shared:
- [x] Quality tiers in setup (Quest / PC) that the environment assets of phase 5 plug into: `QualityTier.CurrentBudget` (detail distance,
  vegetation density, terrain shadows), LOD threshold and shadows per level. Built 2026-10-30

## Phase 5: environment with Blender (planned 2026-10-30, started 2026-10-31)
Made with Blender through blender-mcp, exported to Unity; every asset in a full (PC) and a light (Quest) version, checked on all three builds.

Decisions (user, 2026-10-31): **Transylvanian countryside** look (rolling hills, wooden fences, a village with a church spire on the
horizon); **Poly Haven** (CC0) for textures, sky and plants, everything else modelled by scripts in Blender; the implements are
**remade** (same four, no new ones); **farmyard first**. New: speed bumps (to see the suspension and hydraulics work) and steep parts
to practise driving up and down.

How: Blender Python scripts in `tools/blender/` (reproducible, like `ProjectSetup`), FBX out to `Assets/Models/Env/`, full + light
version per asset. Quest budget ~4 ms GPU (phase 4): baked AO in the vertex colour, few shared textures, cheap shaders, instancing for
repeated things, no shadows on Quest except buildings, impostors for far trees, `QualityTier.CurrentBudget` distances (120 m / 400 m).

### Step 0: pipeline
- [x] Blender → Unity pipeline: script layout, export settings (metric, +Z forward), full / light versions, import in `ProjectSetup`.
  Built 2026-10-31: `tools/blender/` (`tvr.py` library, one script per asset, `build.py` headless or through blender-mcp),
  `tools/fetch_polyhaven.py` (textures + licence list), `TractorVR/EnvLit` shader, `TierVariant` + `QualityTier.ApplyEnvironment`
- [x] One asset end to end, measured on the Quest (GPU ms) before making the rest. Barn built 2026-10-31 (Full 11.1k / Light 1.6k
  triangles, `docs/images/phase5_barn.png`), `PlayModeTest` passes at both levels. Quest run 1 (2026-10-31): drive GPU 9.48 ms vs 9.55
  before = no regression, but the drive never shows the barn; benchmark now has `env` / `noenv` (10 s each facing the barn, with / without
  the env assets; desktop: 1.4 vs 1.3 ms). Quest run 2 (2026-10-31): env 9.59 ms, noenv 9.96 ms GPU: the barn costs less than
  the hills it hides; ~4.3 ms of the 13.9 ms budget left with it on screen. ✅

### Step 1: farmyard (built 2026-11-01; the user's headset look at the new parts still to do)
- [x] Barn, open-front machine shed with marked parking bays per implement (tutorial spots), farmhouse.
  Barn ✅ (step 0). Machine shed ✅ 2026-10-31: `tools/blender/shed.py`, 36 × 10 m, lean-to corrugated roof, bays FRONT WEIGHT / MOWER /
  PLOUGH / TRAILER (`WorldLayout.ShedBays`, yellow lines, name boards); the implements start in their bays (`docs/images/phase5_shed.png`).
  Farmhouse ✅ 2026-11-01: `tools/blender/farmhouse.py`, 9 × 15 m east of the yard (`WorldLayout.Farmhouse`), gable to the yard road,
  porch (tornác) facing the yard, ochre plaster, green shutters, clay tiles; Full 6.5k / Light 1.7k triangles (`docs/images/phase5_house.png`)
  ✅ user, PC VR over Link 2026-11-01: "got the house, love it" (the shed was seen in the same session)
- [x] Yard details: grain silos, fuel tank, muck heap / tipping pad for the trailer, bale stack, wooden fences, yard gate.
  Built 2026-11-01 (`silos.py`, `fueltank.py`, `muckpad.py`, `balestack.py`, `fences.py`, `docs/images/phase5_yard.png`): two silos with a
  filling auger east of the shed, a red diesel tank, a three-walled TIPPING PAD with a muck heap (north-west of the yard, open to the south),
  a round-bale stack north of the barn, a Székely-style carved gate (székelykapu, "ISTEN HOZOTT") on the yard road at x 28, a picket fence
  round the house garden, a rail fence south of the gate. User check in PC VR / Quest still to do
- [x] Better yard surface (concrete slabs with wear, dirt at the edges) from Poly Haven textures. Built 2026-11-01: Poly Haven
  `dirty_concrete` with the 4 m slab joints baked in (`tools/make_yard_concrete.py`). Dirt at the yard edges: not done (the yard is one
  textured patch; the grass / mud blend around it stays)
- [x] Speed bumps (static colliders the wheel rays hit) where the suspension, the front axle cylinders (`Front_Small_Forcer_*`, still
  static) and a raised implement can be seen working. Built 2026-11-01: two 10 cm bumps on the west track by the barn (`speedbump.py`,
  sloped colliders); the **front axle now moves**: axle, steering cylinder and the suspension cylinder rods on `Hinge_FrontAxle`, rising
  with the front wheels' spring travel and swinging with their difference, the rods sliding in the barrels. `PhysicsTest` W
- [x] Steep parts for practice: ramps / embankments up and down (steeper than the hills' ~11°), e.g. a silage clamp in the yard and an
  earth ramp on the practice area. Check the comfort rules (pitch is seen from the cab) and the physics limits (`PhysicsTest` steepest hill).
  Built 2026-11-01: silage clamp behind the barn (1:3 = 18° ramps, `clamp.py`), earth ramp on the pasture north-west of the yard (15° and
  22° sides, `earthramp.py`). The tractor first stuck on them: its box colliders (wheel blocks, a low front) hit the ramps; now round
  wheel colliders and a higher front block. `PhysicsTest` X / X2 / X3: all three climbed at 8 km/h, slip <= 11 %, power <= 51 %.
  Comfort: pitch up to 23° from the cab is real vehicle motion (no camera motion added); to be judged in the headset

### Step 2: implements remade (same sizes and coupling points, so `Hitch` / `Implement` stay) (built 2026-11-01)
- [x] Plough, disc mower, tipping trailer, front weight in Blender: frames, hoses, bolts, decals, worn edges, dirt.
  Built 2026-11-01 (`plough.py`, `mower.py`, `trailer.py`, `frontweight.py`, `docs/images/phase5_implements.png`): one mesh per moving
  part in its pivot's space (plough frame + depth wheel, mower frame + disc, trailer frame / wheel / body / tailgate / 7 ram stages),
  worn paint from Poly Haven `green_metal_rust` (`tools/make_impl_paint.py`), hoses, bolts, lamps, warning stripes, number plate.
  Points, pivots, colliders, masses unchanged: `PhysicsTest` identical. ✅ user, 2026-11-01: "looks good to me"

### Step 3: fields + crops (moved here from phase 3 step 3 and phase 2)
- [x] Standing crop on the crop field, cut by the mower (a grass / alfalfa meadow: the mower is a grass mower), swath behind it.
  2026-11-01: the north field is now a meadow (west of the main road) and a wheat field (east of it); the mower paints the cut strip
  and a swath into the `FieldMap`; standing grass (`Vegetation`, instanced clumps) turns to stubble / lying hay there. Quest: lighter
  clumps, 18 m: **+0.6 ms GPU** (meadow 10.8 vs 10.2 ms, 72 fps)
- [x] Ploughed ground look (furrows in the ploughing direction, darker soil) where the plough has been. Built 2026-11-01: `FieldMap`
  (GPU texture over the fields, 0.5 m per texel: cut / swath / ploughed / furrow direction) painted by the implements, drawn by
  `GroundLit` (furrows 0.35 m apart across the plough's heading, faded in the distance). Replaces the trail ribbons
- [x] Field with crop rows (phase 2 environment item): maize or wheat next to the meadow, for the look. Built 2026-11-01: wheat
  (instanced stalks with ears near the camera, golden ground beyond)
- [x] Field edges: headlands, gates, ditches, hedges. Changed 2026-11-01 (user: make it look like the land around Reghin / the
  Mezőség): Mezőség hills outside the farm and fields, hillside strip parcels with grass balks, the Görgényi / Kelemen mountains on
  the horizon (part 3a); haystacks (boglya), pole gates at the three field entries, a wayside cross (part 3b). Hedges and ditches
  dropped (not part of this landscape); the balks are the field edges. Trees (walnut, wild pear, acacia, willows) go to step 4

### Step 4: landscape (built 2026-11-01 evening; headset look + Quest benchmark still to do)
- [x] Trees around Reghin: solitary walnut and wild pear trees on the balks, acacia (black locust) on the steep slopes and along the
  west track, Lombardy poplars along the main road, willows (and a few poplars) along a new stream in the valley east of the road,
  walnut + pear in the farmhouse garden. `trees.py` + `Trees.cs` (instanced, near / far meshes, trunk colliders), 1036 trees.
  Far trees are low-poly meshes instead of billboards (no alpha test on the Quest). Stream: channel in `Height` + water ribbon
- [x] Sky (Poly Haven HDRIs), roadside (poles, signs), grass tufts near the camera: first moved to "Future ideas", then built the
  same evening at the user's request (2026-11-01): three pure-sky HDRIs (`tools/fetch_sky.py`: day kloofendal_38d_partly_cloudy, dusk
  kloppenheim_06, night kloppenheim_02; sun position, horizon colour and exposure measured in the pictures; the sun light and the fog
  follow them), a concrete power line with wires along the main road and a feeder to the farmhouse, delineator posts, three road signs
  (`roadside.py`), grass tufts on the pastures near the camera (`GrassTufts.cs`). Headset look still to do
- [x] Village silhouette with a church spire on the horizon: `village.py`, ~700 m north-west on a plateau, a Reformed church with a
  spire and four corner turrets, ~30 houses and barns

### Step 5: tutorial places (phase 6) (built 2026-11-01 evening)
- [x] Practice area: cones, a reversing bay for the trailer, coupling spots. Concrete pad east of the yard (`WorldLayout.PracticeArea`),
  `practice.py` (slalom lines, bay with a kerb, coupling spot, sign), 12 pushable cones (`cone.py`). The shed bays stay the coupling
  spots the tutorial uses

## Phase 6: free roam + tutorial modes (planned 2026-10-30, built 2026-11-01 evening; headset play-through still to do)
- [x] Start menu: FREE ROAM (as now) / TUTORIAL. Since 2026-11-01 (late night, user): a separate start screen (`StartMenu`): the
  player stands beside the tractor with a big board: TUTORIAL (lessons with stars) / FREE ROAM / CONTROLS (4 information pages) /
  QUIT; the cab panel's MAIN MENU brings it back
- [x] Lessons take the tractor to where they happen (teleport + scene reset; ploughing starts with the plough coupled) and are rated
  1-5 stars (time against par, skipped steps, mistakes); the tutorial board moved low on the left (it hid the view)
- [x] Tutorial engine: steps with a goal and a check, in-headset prompts, highlight of the control to use, skip / repeat
  (`Tutorial.cs`: checks read the real state, so done steps pass at once; glows on controls and over places to drive to)
- [x] Lessons: driving and the cab, gearbox and hand throttle, slalom, coupling the plough, ploughing, mowing, trailer + reversing +
  tipping, traction (4WD, diff lock, earth ramp), lights and road driving (9 lessons, 45 steps)
- [x] Works in all three versions (VR hands / controllers: poke; desktop: the same board drawn on screen, mouse). `PlayModeTest` walks
  every step at both quality levels. In the headset: still to do

## Future ideas / reference (not scheduled)
- Tutorial: voice-over / sounds per step, a score for the slalom and the reversing bay.
- **Working mirrors**: the model has three parts with the `mirror` material. One small render-texture camera per mirror (mono, from the reflected
  eye position), ~256², short draw distance, no shadows, updated every other frame, shown on the mirror glass; a MIRRORS on/off panel button.
  Expected cost ~1–2 ms per frame on the Quest 2, so measure the frame rate (OVR Metrics Tool) before keeping it.
- Original TurboSquid textures: if the `tex\` folder turns up, put it in `Assets/Models/tex/` and use it instead of the generated materials.
- Hand-control improvements listed under VR interaction.
