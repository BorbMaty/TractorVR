# Roadmap

## Phase 1: flat world, general controls ✅ built / ✅ runs on Quest 2 / ⏳ comfort tuning
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

Still to do:
- [ ] Play test in the editor (keyboard)
- [x] First successful APK build (`Tractor VR → Build Quest APK`), 2026-10-16
- [x] Headset test on the Quest 2 (2026-10-16): app runs, wheels spin and steer correctly
  - [x] Controller bindings (sticks, A/B, left grip)
  - [x] Seat height/position feels right (could still bake a calibrated value into `ProjectSetup`)
  - [ ] Third-person comfort (distance 9.5 m, height 4.2 m, yaw smoothing 0.35 s): **caused motion sickness**, see comfort below
  - [ ] Speed feel (max 40 km/h forward, 15 km/h reverse, acceleration 1.8 m/s²): looks right, but acceleration/turning too abrupt for comfort
  - [ ] Frame rate holds 72/90 Hz (no visible stutter yet; not measured with OVR Metrics Tool)
- [ ] Comfort (motion sickness reported in the first headset test):
  - [x] Comfort vignette while turning/accelerating (built, needs headset test)
  - [x] Gentler defaults: acceleration 1.0 m/s², braking 2.5 m/s², coast 0.8 m/s², steer rate 35°/s, squared stick response, yaw ≤ 25°/s, lateral ≤ 2.5 m/s² (built, needs headset test)
  - [x] Comfort level setting (off/low/high) on X, saved in PlayerPrefs (built, needs headset test)
- [x] Visible controllers: stand-in Touch models with animated stick/buttons/trigger/grip (built, needs headset test)
- [ ] Optional: find the original TurboSquid `tex\` folder, put it in `Assets/Models/tex/`, and use it instead of the generated materials

## Phase 2: "the specifications" (priority to be decided from the project brief)

### Cab realism
- [ ] Split the steering wheel out of `Cab_Interier_01` (Blender) and rotate it with the steering input (~2.5–3 turns lock to lock)
- [ ] Working dashboard: speed, RPM, gear, fuel/temperature
- [ ] Opening doors (`Door_L/R` are already separate)

### Drivetrain
- [ ] Engine model: idle/max RPM, torque curve (6195M: 195 hp rated, ~2100 rpm), hand throttle
- [ ] Gearbox: forward/reverse shuttle, ranges (AutoQuad/AutoPowr-style), real top speed of 50 km/h
- [ ] Sounds: engine (RPM-pitched loops), gear shifts, reverse beeper, horn

### Physics
- [ ] Replace the kinematic model with real wheel physics (WheelColliders or a custom raycast suspension)
- [ ] Mass (~7.5 t), centre of gravity, grip, slopes, collisions with obstacles

### Hydraulics and implements
- [ ] Animate the 3-point hitch (`Rear_Holder_*`, `Rear_Forcer_*`) and front linkage (`Front_Forcer_*`)
- [ ] PTO
- [ ] Attach implements: plough, trailer, mower

### Environment
- [ ] Terrain with gentle hills
- [ ] Field with crop rows, roads, a farmyard

### VR interaction
- [ ] Real controller models / hands (stand-in primitives exist; Meta XR SDK models or XR Hands)
- [ ] Grab the real steering wheel and the armrest joystick instead of using the thumbsticks
- [ ] Menu (wrist or world-space) for settings: view distance, seat reset, speed units

Suggested first step for phase 2: **drivetrain + turning steering wheel**. They make the biggest difference to how it feels in the headset.
