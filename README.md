# Tractor VR — John Deere 6195M on Meta Quest 2

Unity **6000.6.3f1** · URP · OpenXR (Meta Quest feature) · Input System · XR Hands

A VR tractor simulator for the Quest 2: drive a John Deere 6195M from the cab, plough, mow and haul on a hilly farm,
by day or at night, with real-feeling physics (wheel slip, 4WD, diff lock), a working cab with pressable switches and hand tracking. University project (Sapientia Hungarian University of Transylvania, IRUM).

![on the farm track](docs/images/hero.png)

Docs: [Roadmap](docs/ROADMAP.md) · [Development log](docs/DEVLOG.md) · [CLAUDE.md](CLAUDE.md) (project guide)

## Phases

### Phase 1: flat world, general controls ✅ done
- John Deere 6195M model at real scale (wheelbase 2.80 m), wheels rigged (spin + Ackermann steering, front hubs follow)
- Flat 2 × 2 km test field with a 10 m grid and marker posts (replaced by the farm in phase 3)
- Two viewpoints: **cab** (driver's seat) and **third person** (behind and above), recenter, seat adjustment
- Driving with the Touch controller thumbsticks, small speed readout
- **Comfort**: edge vignette while turning / changing speed (off / low / high), gentle acceleration, yaw-rate and sideways-force limits
- Official Quest 2 Touch controller models with moving stick, buttons, trigger and grip

### Phase 2: the specifications ✅ done
- **Steering wheel + drivetrain**: turning steering wheel, instrument cluster, 20F/20R gearbox (auto / manual), engine RPM and load, synthesised sounds
- **Physics + terrain**: 7.5 t Rigidbody on raycast wheels, power limit on climbs, parking brake on slopes, rolling hills, pushable hay bales
- **Hydraulics + implements**: rear three-point hitch with a working top link, front linkage with moving cylinders, PTO; plough, front mower,
  900 kg front weight, tipping trailer with a 3 t load; attach / detach
- **VR interaction**: grab the steering wheel, the speed lever, the hydraulic remote levers and the doors; hand tracking, cab panel with touch buttons
- **Cab visuals**: generated interior textures and baked ambient occlusion
- **Cab + engine extras**: opening doors, CommandCenter display (fuel, coolant temperature, power), torque curve, hand throttle

### Phase 3: physics, terrain, cab buttons ✅ done
1. ✅ **Ground + layout**: farmyard, roads, fields, mud; grip and rolling resistance per surface
2. ✅ **Traction**: wheel slip per tyre and ground, wheels that spin and throw clods, 4WD on/off, diff lock, all-wheel / rear brakes
3. ↪ **Fields + crops**: moved to phase 5 (environment with Blender)
4. ✅ **Cab buttons + lights**: pressable console switches (road-light knob, work lights, beacon, hazards,
   4WD, diff lock, PTO), indicator stalk, headlights / side + tail / brake lights / indicators / work lights / beacons, DAY / DUSK / NIGHT

### Phase 4: three versions ⏳ next
Quest standalone (as now), **PC VR** (the laptop renders, streamed to the headset over Link: full-detail model, real shadows, 90 Hz)
and a **PC desktop** version on the monitor.

### Phase 5: environment with Blender ⏳ planned
Crops the mower cuts, the look of ploughed ground, a better farmyard, trees and field edges; each asset in a full and a light version.

### Phase 6: free roam + tutorial ⏳ planned
A start menu with FREE ROAM and TUTORIAL; step-by-step lessons in the headset that show which control to use.

Future ideas: working mirrors, original model textures. See the [Roadmap](docs/ROADMAP.md).

## What's in it

### The world
2 × 2 km of rolling farmland built at start-up: a farmyard with a barn and an open machine shed, gravel tracks out to three fields on the
hills, an asphalt road, mud at the field gates and in the hollows. Every surface has its own tyre grip and rolling resistance
(asphalt 0.015 … mud 0.15), so mud and ploughed ground make the engine work.

| Farmyard | The main road |
|---|---|
| ![farmyard](docs/images/farmyard.png) | ![road](docs/images/road.png) |

| Field gate: gravel track, mud, field soil | Rolling hills |
|---|---|
| ![field gate](docs/images/field_gate.png) | ![hills](docs/images/hills.png) |

### Driving
- 7.5 t physics body on four raycast wheels (suspension, tyre friction circle), 90 Hz physics, power limit on climbs (195 hp), parking brake that holds on slopes
- Gearbox like the 6195M's AutoQuad Plus 20/20 (A1–E4, automatic or manual, skip-shifts), engine RPM and load, top speed 50 km/h
- Engine torque curve (880 Nm peak at 1500–1600 rpm, 34 % torque rise): the power available depends on the engine speed
- Hand throttle: sets the engine speed (AUTO holds it there; in manual the tractor drives at that engine speed in the selected gear)
- Fuel use from the engine power (about 41 L/h at full power, 2.5 L/h at idle), coolant that warms up to 85 °C
- Synthesised sounds: diesel engine following RPM and load (muffled in the cab), shift clunk, reverse beeper, horn
- **Comfort first**: gentle acceleration, yaw-rate and sideways-force limits, edge vignette (off / low / high)

### Traction
- Each tyre slips according to the ground under it (asphalt grips, mud lets go); the CommandCenter shows **SLIP %** (ploughing: about 16 %)
- Wheels turn at their own speed: they spin up when the ground can't take the force and throw soil clods on soft ground;
  the engine revs follow the wheels
- **4WD** on / off, **diff lock** (open diffs: in a split-grip spot one wheel spins and the other stands; the lock lets go above 12°
  steering or 15 km/h), brakes on **all wheels** or the **rear only**

| Third person | Side |
|---|---|
| ![third person](docs/images/third.png) | ![side](docs/images/side.png) |

### Implements and hydraulics
Rear three-point hitch (real four-bar linkage motion from the model's pivots), front linkage, drawbar and PTO.
Back up or drive up to an implement and press attach.
- **Top link**: stowed upright until a three-point implement is coupled, then set to the length that fits and connected to the
  implement's top pin. The implement pitches as the hitch moves, as with real three-point geometry (level at work, tail up when raised).
- **Front linkage cylinders** swing and slide with the front links.
- **Front weight** (900 kg) for the front linkage: about 12 kN more on the steering axle.
- **Tipping trailer**: rear-hinged body on a seven-stage telescopic ram, tailgate that swings open, 3 t of soil that pours out into a heap
  (remote lever 3, panel TIP buttons). The trailer's mass and centre of mass follow the load.
- Implement models built in code: lugged tyres, twisted mouldboards, disc coulters, mower hood and knives, trailer stakes, mudguards, lamps.

| Plough: 30 kN draft, depth control, furrows | Front disc mower on the PTO |
|---|---|
| ![ploughing](docs/images/ploughing.png) | ![mower](docs/images/mower.png) |

| Top link at working depth | Raised: the plough pitches |
|---|---|
| ![top link](docs/images/toplink_work.png) | ![raised](docs/images/toplink_raised.png) |

| Tipping trailer on the drawbar | Tipping: the load pours out |
|---|---|
| ![trailer](docs/images/trailer.png) | ![tipping](docs/images/tipping_rear.png) |

| Front weight + plough raised | Rear hitch lowered |
|---|---|
| ![front weight](docs/images/front_weight.png) | ![hitch](docs/images/hitch_low.png) |

### The cab
- The steering wheel turns (cut out of the cab mesh), the instrument cluster shows RPM, speed, gear and mode
- Grab the steering wheel and the speed lever with the controller grips or, with hand tracking, a fist or a pinch
- Three hydraulic remote levers behind the speed lever (spring-centred): 1 = rear hitch, 2 = front linkage, 3 = trailer tipping
- **Console switches** you press with a fingertip (cut out of the cab model): road-light knob, hazards, work lights front / rear, beacon,
  4WD, diff lock, PTO; each moves when pressed and has a small lamp that lights while it's on
- **Indicator stalk** on the steering column: grab it and push it up / down
- Doors (rear-hinged, as on the real cab): swing them open by hand, or with the panel's DOORS button
- CommandCenter display on the right corner post: engine speed, hand throttle, fuel, coolant temperature, fuel use, power, linkage positions, PTO
- Cab panel with 31 touch buttons, everything in one place (view, comfort, gearbox, hitch, PTO, attach, seat, units, reset, doors,
  hand throttle, tipping, 4WD, diff lock, brakes, lights, work lights, beacon, hazards, indicators, sky)
- Generated plastic grain, seat fabric and floor mat textures, baked ambient occlusion

| Driver's view | Interior |
|---|---|
| ![cab](docs/images/cab.png) | ![interior](docs/images/interior.png) |

| Instrument cluster | Cab panel |
|---|---|
| ![dashboard](docs/images/dash.png) | ![panel](docs/images/panel.png) |

| Speed lever and armrest | Quest 2 controllers |
|---|---|
| ![lever](docs/images/lever.png) | ![controllers](docs/images/controller.png) |

| CommandCenter display | Seen from the seat |
|---|---|
| ![commandcenter](docs/images/commandcenter.png) | ![commandcenter from the seat](docs/images/commandcenter_seat.png) |

| Hydraulic remote levers (1 raised, 2 lowered) | Door open |
|---|---|
| ![remote levers](docs/images/remotes.png) | ![door](docs/images/doors_side.png) |

| Console switches (work lights, beacon, hazards, diff lock on) | The console at night |
|---|---|
| ![console](docs/images/console_on.png) | ![console at night](docs/images/night_console.png) |

Driver's view at night, headlights on:

![night cab](docs/images/night_cab.png)

### Lights and time of day
- Headlights, side and tail lights, brake lights, indicators and hazards (ticking, self-cancelling), front and rear work lights,
  two rotating amber beacons
- **DAY / DUSK / NIGHT**: sun, sky, ambient light and fog change; at night the headlights and work lights light up the ground

| Night, from the front | Night, from behind (indicator, work lights) |
|---|---|
| ![night front](docs/images/night_front.png) | ![night rear](docs/images/night_rear.png) |

| Head and work lights from above | Dusk |
|---|---|
| ![night above](docs/images/night_above.png) | ![dusk](docs/images/dusk_third.png) |

## Cab guide: what every control does

### From the driver's seat
![driver's view, labelled](docs/images/guide_driver.png)

### The right-hand console: speed lever and hydraulic remote levers
Grab the knobs (controller grip, or a fist / pinch with hand tracking).

![console, labelled](docs/images/guide_console.png)

### The switch panel
Touch a switch with a fingertip or the controller tip. The small lamp in a switch lights while it's on.

![switch panel, labelled](docs/images/guide_switches.png)

### The cab panel
Open it with the **Menu** button on the left controller (keyboard M) or the MENU tab at the right front of the cab, then touch the buttons.
It has everything in one place, including the functions of the switches and levers above.

![cab panel](docs/images/guide_panel.png)

| Button | What it does |
|---|---|
| VIEW | Cab view ↔ third-person view |
| RECENTER | Puts the view back in the driver's seat |
| COMFORT | Comfort vignette: off / low / high |
| GEARBOX | Automatic ↔ manual gear changes |
| HITCH UP / HITCH DOWN | Raise / lower the rear three-point hitch (hold) |
| FRONT UP / FRONT DOWN | Raise / lower the front linkage (hold) |
| PTO | Power take-off on / off (drives the mower) |
| ATTACH/DETACH | Couple the implement in reach / uncouple (lower it first) |
| SEAT ADJ | Seat adjust mode: the sticks move the seat (height, forward / back, sideways) |
| SEAT RESET | Seat back to the default position |
| UNITS | km/h ↔ mph |
| RESET TRACTOR | Back to the start, upright (a coupled trailer comes along) |
| STOP / NEUTRAL | Speed lever to neutral and brake to a stop |
| FOVEATION | Foveated rendering level (lower = sharper edges, slower) |
| FRAME TIME | Shows the frame time on the HUD |
| DOORS | Opens / closes both doors |
| THROTTLE − / + | Hand throttle: engine speed down / up (hold) |
| TIP UP / TIP DOWN | Raise / lower the tipping trailer's body (hold) |
| 4WD | Front axle drive on / off |
| DIFF LOCK | Rear diff lock on / off (lets go above 12° steering or 15 km/h) |
| BRAKES | Brakes on all wheels ↔ rear only |
| LIGHTS | Road lights: off / side / head |
| WORK | Work lights: off / front / front + rear |
| BEACON | Rotating roof beacons on / off |
| HAZARDS | Hazard lights on / off |
| < INDICATOR / INDICATOR > | Left / right indicator (press again to cancel) |
| SKY | Time of day: day / dusk / night |

## Controls
With **hand tracking** (controllers put down): make a fist (or pinch) on the wheel rim, a lever knob, the stalk or a door to grab;
point with your index finger to press buttons and switches (pointing never grabs).
The panel's MENU tab is always visible at the right front of the cab.


| Quest 2 controller | Keyboard (editor) | Action |
|---|---|---|
| Left stick ← → | A / D | Steering |
| Right stick ↑ | W | Drive forward (stick amount = target speed, max 50 km/h; in manual mode, a share of the gear's top speed) |
| Right stick ↓ | S | Brake, then reverse when stopped (max 15 km/h) |
| Release stick | — | Coast to a stop |
| **A** | C | Switch view: cab ↔ third person |
| **B** | R | Recenter the view |
| **X** (left) | V | Comfort vignette: off / low / high (saved, default high) |
| Panel: SEAT ADJ ON + sticks | Hold Left Shift | Seat adjust: left stick ↑↓ = height, right stick = forward/back/sideways (saved) |
| **Grip** (either hand) near the wheel rim | — | Grab the steering wheel and turn it; it stays where you let go until you use the left stick |
| **Grip** on the lever knob (right armrest) | — | Speed lever: push forward = drive, pull back = reverse; stays set. The right stick puts it back to neutral |
| **Grip** on a remote lever knob (behind the speed lever) | — | Pull back = raise, push forward = lower (1 rear hitch, 2 front linkage, 3 tipping trailer); springs back to the middle |
| Panel TIP UP / TIP DOWN | I / K | Raise / lower the coupled tipping trailer's body |
| **Grip** on a door | — | Swing the door open / closed (it stays where you let go); panel DOORS opens / closes both |
| **Menu** button (left) | M | Cab panel at the right front: touch the buttons with a controller tip or a fingertip |
| CommandCenter / panel THROTTLE − / + | [ / ] | Hand throttle (engine speed, idle = off) |
| Right stick flick → / ← | X / Z | Shift up / down (switches the gearbox to manual) |
| **Y** (left) | T | Gearbox: automatic ↔ manual (saved) |
| Right trigger | H | Horn |
| Left stick ↑ / ↓ | Q / E | Raise / lower the rear hitch |
| Hold left trigger + left stick ↑ / ↓ | Left Ctrl + Q / E | Raise / lower the front linkage |
| Right stick click | P | PTO on / off |
| Left stick click | J | Attach the implement in reach / detach (lower it first) |
| Panel 4WD | F | Front axle drive on / off |
| Panel DIFF LOCK | L | Diff lock (lets go above 12° steering or 15 km/h) |
| Panel BRAKES | B | Brakes on all wheels ↔ rear only |
| Touch a console switch (right side console) | — | Fingertip / controller tip: road-light knob (OFF / SIDE / HEAD), front and rear work lights, beacon, hazards (top), 4WD, diff lock, PTO; a lamp in the switch shows it's on |
| **Grip** on the indicator stalk (left of the column) | , / . | Push up = right, down = left; cancels when the steering comes back |
| Panel LIGHTS / WORK / BEACON / HAZARDS | G / N / O / Y | Road lights (off / side / head), work lights (off / front / front + rear), beacon, hazards |
| Panel SKY | U | Time of day: day / dusk / night |

## Project layout
```
Assets/Scripts/TractorInput.cs        input bindings (Quest + keyboard/gamepad fallback)
Assets/Scripts/TractorController.cs   driving physics (Rigidbody + raycast suspension, tyre forces, Ackermann, wheel animation)
Assets/Scripts/TerrainGenerator.cs    rolling hills, generated at startup
Assets/Scripts/Hitch.cs               rear hitch + front linkage + drawbar, PTO, attach / detach
Assets/Scripts/Implement.cs           coupling, depth, draft, PTO parts, work trails
Assets/Scripts/Tipper.cs              tipping trailer body, ram, tailgate, load and heaps
Assets/Scripts/SimpleWheels.cs        trailer wheels
Assets/Scripts/VRHands.cs             hands from controllers or hand tracking (grab point, grabbing, poke tip)
Assets/Scripts/CabControls.cs         grab the steering wheel, the speed lever, the remote levers and the doors
Assets/Scripts/CabPanel.cs            cab panel with touch buttons
Assets/Scripts/CommandCenter.cs       display on the right corner post (fuel, temperature, power, hand throttle)
Assets/Scripts/CabSwitches.cs         pressable switches on the right-hand console
Assets/Scripts/TractorLights.cs       lamps, work lights, beacons, indicators, brake lights; day / dusk / night (+ Assets/Shaders/Halo.shader)
Assets/Scripts/Ground.cs              farm layout + ground map (grip, rolling resistance, draft, slip per surface)
Assets/Models/Hands/                  hand models (XR Hands sample, Unity Package Distribution License)
Assets/Scripts/Editor/PhysicsTest.cs  offline driving test         (menu: Tractor VR > Run Physics Test)
Assets/Scripts/Editor/PlayModeTest.cs real scene in play mode, every button and switch (menu: Tractor VR > Run Play Mode Test)
Assets/Scripts/VRCameraRig.cs         cab / third-person views, recenter, seat calibration
Assets/Scripts/Drivetrain.cs          engine (torque curve, hand throttle, fuel, coolant) + 20F/20R gearbox (auto / manual)
Assets/Scripts/TractorAudio.cs        engine, shift, reverse beeper and horn sounds
Assets/Scripts/Dashboard.cs           instrument cluster (needles, gear, mode)
Assets/Scripts/TractorHud.cs          speed / gear / rpm readout
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

## PC VR over Quest Link / Air Link
The laptop renders at the **PC** quality level (full model, real shadows, soft 4096 shadow map, render scale 1.2) and streams to the headset.
1. Meta Horizon Link app on the PC (set as the active OpenXR runtime: Settings → General). In its Graphics preferences, choose 90 Hz.
2. Connect the Quest with a USB 3 cable (Link) or over 5 GHz Wi-Fi (Air Link), and start Link from the headset's Quick Settings.
3. Either press Play in the Unity editor (it plays at PC quality; *Tractor VR → Quality: Quest* previews the Quest look),
   or run `Builds/PC/TractorVR.exe` (built with *Tractor VR → Build PC (Windows)*). The PC window shows the mirror view.

## Notes
- The FBX references external textures (`tex\*.tif`) that weren't included with the model.
  The setup script rebuilds the look from the material names instead (green paint, yellow rims, clear glass).
  If the original texture folder turns up, put it in `Assets/Models/tex/` and re-run the setup.
- Since phase 2 the tractor is a physics body (7.5 t) on rolling hills. The round hay bales near the start can be pushed around.
  In the cab view the view pitches and rolls with the tractor on slopes. If it ever ends up on its side, it resets to the start after 3 s.
- Turning is deliberately limited for comfort (max 25°/s yaw, 2.5 m/s² lateral). At speed the tractor turns wide, so slow down for tight turns.
- If you feel motion sick: set the vignette to high (X), prefer the cab view, keep sessions short.
