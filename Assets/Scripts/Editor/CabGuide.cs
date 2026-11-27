using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Renders the cab guide for the README: views from the driver's seat, plus where each control lands in the picture
/// (`Logs/Guide/*.json`). `tools/annotate_guide.py` then draws the text boxes and arrows into `docs/images/guide_*.png`.
///   Menu: Tractor VR > Render Cab Guide
///   CLI : Unity.exe -batchmode -quit -projectPath . -executeMethod CabGuide.Run   then   python tools/annotate_guide.py
/// </summary>
public static class CabGuide
{
    const int Width = 1600, Height = 1250;
    // Controller shot: models tipped back by CtlPitch (faces towards the camera), CtlGap either side of the centre, camera at CtlCam.
    const float CtlPitch = -30f, CtlGap = 0.075f;
    static readonly Vector3 CtlCam = new Vector3(0f, 0.40f, -0.07f);

    [MenuItem("Tractor VR/Render Cab Guide")]
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/TractorFlat.unity");
        ShaderUtil.allowAsyncCompilation = false;
        var tractor = GameObject.Find("Tractor").transform;
        Object.FindAnyObjectByType<TerrainGenerator>().Build();
        foreach (var n in new[] { "Left Controller", "Right Controller" }) { var g = GameObject.Find(n); if (g) g.SetActive(false); }
        var seat = tractor.Find("DriverEye");
        var cab = tractor.GetComponent<CabControls>();
        var switches = tractor.GetComponent<CabSwitches>();
        var panel = Object.FindAnyObjectByType<CabPanel>();

        // CommandCenter with the values it shows after start-up, hand throttle at 1500 rpm (as in the preview renders).
        var drive = tractor.GetComponent<Drivetrain>();
        typeof(Drivetrain).GetMethod("Awake", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(drive, null);
        drive.MoveHandThrottle(1f, (1500f - drive.idleRpm) / drive.handThrottleRate);
        var cc = Object.FindAnyObjectByType<CommandCenter>();
        cc.Build();
        // Switch lamps as at start-up (4WD on, the rest off): the components' start-up code doesn't run in edit mode.
        var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        tractor.GetComponent<TractorInput>().scripted = true;
        typeof(TractorLights).GetMethod("Awake", bf).Invoke(tractor.GetComponent<TractorLights>(), null);
        typeof(CabSwitches).GetMethod("Start", bf).Invoke(switches, null);
        switches.FrameUpdate(1f);

        // A soft fill light in the cab: the interior is dark grey and the renders are for reading.
        var fill = new GameObject("GuideFill").AddComponent<Light>();
        fill.type = LightType.Point;
        fill.range = 2.5f;
        fill.intensity = 0.9f;
        fill.transform.position = tractor.TransformPoint(new Vector3(0.1f, 2.85f, 0.7f));

        var cam = Camera.main;
        cam.nearClipPlane = 0.01f;
        Directory.CreateDirectory("Logs/Guide");
        var labels = new List<(string title, string text, Vector3 world)>();
        void Label(string title, string text, Vector3 world) => labels.Add((title, text, world));
        Vector3 Local(Vector3 p) => tractor.TransformPoint(p);
        Vector3 Centre(Transform t) => t.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; }).center;
        Vector3 SwitchAt(string id) => Centre(switches.switches.First(s => s.id == id).hinge);

        void Shot(string name, Vector3 pos, Vector3 lookAt, float fov)
        {
            cam.fieldOfView = fov;
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(lookAt - pos));
            var rt = new RenderTexture(Width, Height, 24) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            cam.Render();   // the first batch-mode render can come out without its lighting
            RenderTexture.active = rt;
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            File.WriteAllBytes($"Logs/Guide/{name}.png", tex.EncodeToPNG());
            var json = new StringBuilder("{\n  \"labels\": [\n");
            for (int i = 0; i < labels.Count; i++)
            {
                Vector3 s = cam.WorldToScreenPoint(labels[i].world);
                bool visible = s.z > 0f && s.x >= 0f && s.x < Width && s.y >= 0f && s.y < Height;
                if (!visible) Debug.LogWarning($"[Guide] {name}: '{labels[i].title}' is outside the picture");
                json.Append($"    {{\"title\": \"{labels[i].title}\", \"text\": \"{labels[i].text}\", \"x\": {s.x:0}, \"y\": {Height - s.y:0}, \"visible\": {(visible ? "true" : "false")}}}");
                json.Append(i < labels.Count - 1 ? ",\n" : "\n");
            }
            json.Append("  ]\n}\n");
            File.WriteAllText($"Logs/Guide/{name}.json", json.ToString());
            cam.targetTexture = null;
            RenderTexture.active = null;
            labels.Clear();
        }

        // Warm-up (the first render in batch mode is grey).
        Shot("warmup", seat.position, seat.position + tractor.forward, 60f);
        File.Delete("Logs/Guide/warmup.png"); File.Delete("Logs/Guide/warmup.json");

        // 1) Driver's view: wheel, stalk, cluster, CommandCenter, doors.
        var wheel = tractor.GetComponent<TractorController>().steeringWheel;
        Label("Steering wheel", "Grab the rim (grip / fist / pinch) and turn; stays where you let go", wheel.parent.TransformPoint(new Vector3(0.14f, 0.14f, 0f)));   // upper right of the rim
        Label("Indicator stalk", "Grab, push up = right, down = left; cancels after the turn", Centre(cab.stalk.hinge) - tractor.right * 0.05f);
        Label("Instrument cluster", "Engine rpm, speed, gear, AUTO / MAN", tractor.Find("Dashboard/SpeedNeedle").position);   // the hub hides the middle
        Label("CommandCenter", "Engine, fuel, coolant, power, slip, linkage, PTO; 4WD / diff lock / indicator tell-tales", cc.transform.position + cc.transform.up * 0.03f);
        Label("THROTTLE - / +", "Touch to set the hand throttle (engine speed)", cc.transform.TransformPoint(new Vector3(-0.053f, -CommandCenter.H * 0.5f - 0.02f, 0f)));
        Label("Doors", "Grab anywhere on the inside and swing open / shut", Local(new Vector3(-0.62f, 2.25f, 1.30f)));   // front of the left door
        Shot("guide_driver", seat.position, seat.position + tractor.forward * 2f - tractor.up * 0.55f + tractor.right * 0.12f, 95f);

        // 2) The right-hand console: speed lever and remote levers (the switches get their own close-up).
        Label("Speed lever", "Grab the knob: forward = drive, back = reverse, middle = stop; stays set", cab.speedLever.TransformPoint(cab.knobLocal));
        Label("Remote lever 1", "Rear hitch: pull back = raise, push = lower (springs back)", cab.remotes[0].hinge.TransformPoint(cab.remotes[0].grabBox.center));
        Label("Remote lever 2", "Front linkage: pull back = raise, push = lower", cab.remotes[1].hinge.TransformPoint(cab.remotes[1].grabBox.center));
        Label("Remote lever 3", "Tipping trailer body: pull back = raise, push = lower", cab.remotes[2].hinge.TransformPoint(cab.remotes[2].grabBox.center));
        Label("Switch panel", "Lights, beacon, hazards, 4WD, diff lock, PTO (close-up below)", Local(new Vector3(0.585f, 2.33f, 0.40f)));
        Shot("guide_console", Local(new Vector3(0.06f, 2.72f, 0.66f)), Local(new Vector3(0.50f, 2.27f, 0.40f)), 80f);

        // 3) Close-up of the switch panel on the right-hand console (touch with a fingertip / controller tip).
        Label("Road lights (knob)", "Touch to turn: OFF / SIDE / HEAD", SwitchAt("lights"));
        Label("Hazards", "All indicators blink (red lamp)", SwitchAt("hazards"));
        Label("4WD", "Front axle drive on / off (green lamp = on)", SwitchAt("fourWd"));
        Label("Diff lock", "Rear diff lock (lets go above 12° steering / 15 km/h)", SwitchAt("diffLock"));
        Label("PTO", "Power take-off on / off (front mower)", SwitchAt("pto"));
        Label("Front work lights", "Roof work lights, front", SwitchAt("workFront"));
        Label("Rear work lights", "Roof work lights, rear", SwitchAt("workRear"));
        Label("Beacon", "Rotating amber roof beacons", SwitchAt("beacon"));
        Shot("guide_switches", Local(new Vector3(0.28f, 2.60f, 0.66f)), Local(new Vector3(0.59f, 2.32f, 0.42f)), 62f);   // from the front: the 4WD rocker is behind remote lever 3 seen from the seat

        // 4) The cab panel, open (its buttons are labelled; the README table explains them).
        panel.PreviewOpen();
        Shot("guide_panel", seat.position, panel.transform.position + new Vector3(0f, 0.01f, 0f), 70f);
        panel.gameObject.SetActive(false);

        // 5) The Quest 2 controllers (phase 6, the start menu's CONTROLS page): both models side by side high above the farm,
        // seen from in front and above, on a plain background, with a label on every button.
        var ctlL = Resources.FindObjectsOfTypeAll<ControllerVisual>().Select(c => c.transform).FirstOrDefault(t => t.name == "Left Controller");
        var ctlR = Resources.FindObjectsOfTypeAll<ControllerVisual>().Select(c => c.transform).FirstOrDefault(t => t.name == "Right Controller");
        // Skipped if either controller is the ring fallback (Meta files missing, no bones to label): the existing guide pictures stay.
        if (ctlL && ctlR && ctlL.GetComponent<ControllerVisual>().stick != null && ctlR.GetComponent<ControllerVisual>().stick != null)
        {
            var basePos = new Vector3(0f, 300f, 0f);
            var pose = Quaternion.Euler(CtlPitch, 0f, 0f);
            foreach (var (t, x) in new[] { (ctlL, -CtlGap), (ctlR, CtlGap) })
            {
                t.gameObject.SetActive(true);
                t.SetParent(null, true);
                t.SetPositionAndRotation(basePos + new Vector3(x, 0f, 0f), pose);
                foreach (var r in t.GetComponentsInChildren<Renderer>(true)) { r.enabled = true; r.gameObject.layer = 31; }
            }
            Transform Bone(Transform root, string n) => root.GetComponentsInChildren<Transform>(true).First(b => b.name == n);
            // The grip bone sits inside the ring, and from above the handle is hidden: point at the ring's inner side (towards the
            // other hand), where the grip button is, under the ring.
            Vector3 Handle(Transform root, float side)
            {
                var b = root.GetComponentsInChildren<Renderer>(true).Select(r => r.bounds).Aggregate((a, c) => { a.Encapsulate(c); return a; });
                return new Vector3(b.center.x + side * b.extents.x * 0.9f, b.center.y, b.center.z + b.extents.z * 0.1f);
            }
            Debug.Log("[Guide] controller bones: " + string.Join(", ", ctlR.GetComponentsInChildren<Transform>(true).Select(b => b.name)));
            fill.transform.position = basePos + new Vector3(0f, 0.4f, -0.4f);
            fill.range = 3f; fill.intensity = 1.5f;
            var clear = cam.clearFlags; var bg = cam.backgroundColor; int mask = cam.cullingMask;
            cam.cullingMask = 1 << 31;   // only the controllers: no terrain or horizon behind them
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.82f, 0.84f, 0.8f);
            Label("Left stick", "Steer. Push up / down: rear hitch. Click: attach / detach", Bone(ctlL, "b_thumbstick").position);
            Label("X", "Comfort vignette: off / low / high", Bone(ctlL, "b_button_x").position);
            Label("Y", "Gearbox: automatic / manual", Bone(ctlL, "b_button_y").position);
            Label("Menu button", "The cab panel (all functions as buttons) open / closed", Bone(ctlL, "b_button_oculus").position);
            Label("Left trigger", "(index finger, underneath) Hold + left stick up / down: front linkage", Bone(ctlL, "b_trigger_front").position);
            Label("Left grip", "(middle finger, side) Grab the wheel, a lever, the stalk, a door", Bone(ctlL, "b_button_x").position + new Vector3(0.036f, -0.01f, -0.012f));
            Label("Right stick", "Forward: drive. Back: brake, then reverse. Flick right / left: shift up / down. Click: PTO", Bone(ctlR, "b_thumbstick").position);
            Label("A", "Cab view / outside view", Bone(ctlR, "b_button_a").position);
            Label("B", "Recenter the view", Bone(ctlR, "b_button_b").position);
            Label("Right trigger", "(index finger, underneath) Horn", Bone(ctlR, "b_trigger_front").position);
            Label("Right grip", "(middle finger, side) Grab the wheel, the speed lever, the remote levers", Bone(ctlR, "b_button_a").position + new Vector3(-0.036f, -0.01f, -0.012f));
            Shot("guide_controllers", basePos + CtlCam, basePos + new Vector3(0f, -0.03f, -0.045f), 31f);
            cam.clearFlags = clear; cam.backgroundColor = bg; cam.cullingMask = mask;
        }

        Object.DestroyImmediate(fill.gameObject);
        Debug.Log("[Guide] done: Logs/Guide/");
    }
}
