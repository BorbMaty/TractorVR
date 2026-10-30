using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using TrackedPoseDriver = UnityEngine.InputSystem.XR.TrackedPoseDriver;

/// <summary>
/// PC desktop version (phase 4): the same Windows build plays on the monitor when no headset is in use.
///   Start-up ... Windows / editor: VR if the OpenXR session gets focus within <see cref="waitSeconds"/> (headset on, Link running,
///                Unknown Sources allowed in the Link app: without it the session stays SYNCHRONIZED on the loading screen);
///                otherwise XR is shut down and the game renders to the window. <c>-desktop</c> / <c>-vr</c> on the command line force
///                a mode. F9 tries VR again later (put the headset on first). Android (Quest) never uses this.
///   Camera ..... cab: hold the right mouse button to look around, wheel = zoom; third person: right mouse orbits, wheel = distance.
///                R (recenter) looks straight ahead again.
///   Mouse hand . the left mouse button is a hand for the existing cab controls (fed into <see cref="VRHands"/> as the right hand):
///                click a switch / CommandCenter button to press it, drag the steering wheel rim, speed lever or remote levers.
///                The ray hits proxy colliders of the cab parts (readable generated meshes around the seat) on <see cref="PickLayer"/>.
///   Screen UI .. the cab panel's buttons as an on-screen menu (M), plus window / fullscreen; the in-cab panel is hidden.
///                The HUD moves onto the camera (bottom of the screen); the comfort vignette is off.
/// </summary>
[DefaultExecutionOrder(-95)]   // after TractorInput, before VRHands reads the mouse hand
public class DesktopMode : MonoBehaviour
{
    public const int PickLayer = 30;

    public VRCameraRig rig;
    public Camera cam;
    public TrackedPoseDriver headPose;
    public Transform hud;
    public CabPanel panel;
    public ComfortVignette vignette;
    public float waitSeconds = 20f;   // Link can sit on its loading screen for a while; stopping XR during it leaves the headset stuck there
    public float lookSpeed = 0.15f;   // degrees per pixel

    public static bool Active { get; private set; }
    /// <summary>Waiting for the headset at start (the window shows a hint meanwhile).</summary>
    public static bool Waiting { get; private set; }

    float yaw, pitch, fov = 65f;
    float orbitYaw, orbitPitch = 14f, orbitDistance = 9.8f;
    readonly List<(Transform source, Transform proxy)> proxies = new List<(Transform, Transform)>();
    Vector3 hudPos; Quaternion hudRot; Transform hudParent;
    float grabDepth;
    Transform pokeTarget; Vector3 pokeLocal;
    Rect menuRect;
    readonly HashSet<int> heldRepeat = new HashSet<int>();
    GUIStyle buttonStyle, labelStyle, boxStyle;

    static bool XRRunning
    {
        get
        {
            var m = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            return m != null && m.activeLoader != null;
        }
    }

    IEnumerator Start()
    {
        if (Application.platform == RuntimePlatform.Android) yield break;
        yield return null;   // the cab panel and CommandCenter build their buttons in Start: the pick proxies need them
        var args = System.Environment.GetCommandLineArgs();
        bool forceDesktop = args.Contains("-desktop") || Application.isBatchMode, forceVR = args.Contains("-vr");   // batch-mode tests: never wait for a headset
        if (!forceDesktop) yield return WaitForHeadset(forceVR ? float.PositiveInfinity : waitSeconds);
        if (!(XRRunning && OpenXRUtility.IsSessionFocused)) Enter();
    }

    IEnumerator WaitForHeadset(float seconds)
    {
        Waiting = true;
        for (float t = 0f; t < seconds && XRRunning && !OpenXRUtility.IsSessionFocused; t += Time.unscaledDeltaTime)
        {
            if (Keyboard.current != null && Keyboard.current.dKey.wasPressedThisFrame) break;
            yield return null;
        }
        Waiting = false;
    }

    // ------------------------------------------------------------------ switching
    void Enter()
    {
        var m = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
        if (m != null && m.activeLoader != null) { m.StopSubsystems(); m.DeinitializeLoader(); }
        Active = true;
        headPose.enabled = false;
        cam.ResetProjectionMatrix();
        cam.fieldOfView = fov;
        rig.cameraOffset.localPosition = Vector3.zero;
        rig.cameraOffset.localRotation = Quaternion.identity;
        ResetLook();
        hudParent = hud.parent; hudPos = hud.localPosition; hudRot = hud.localRotation;
        hud.SetParent(cam.transform, false);
        hud.localPosition = new Vector3(0f, -0.44f, 0.8f);   // bottom centre, below the dash
        hud.localRotation = Quaternion.identity;
        panel.SetHidden(true);
        vignette.enabled = false;
        vignette.quad.enabled = false;
        QualitySettings.vSyncCount = 1;   // no XR runtime pacing the frames now
        BuildProxies();
        Debug.Log("[Desktop] No headset in use: playing on the monitor (F9 = try VR again).");
    }

    void Exit()
    {
        Active = false;
        headPose.enabled = true;
        cam.transform.localPosition = Vector3.zero;
        cam.transform.localRotation = Quaternion.identity;
        hud.SetParent(hudParent, false);
        hud.localPosition = hudPos; hud.localRotation = hudRot;
        panel.SetHidden(false);
        vignette.enabled = true;
        QualitySettings.vSyncCount = 0;
        foreach (var (_, proxy) in proxies) Destroy(proxy.gameObject);
        proxies.Clear();
        rig.Recenter();
        Debug.Log("[Desktop] Headset in use: VR.");
    }

    IEnumerator TryVR()
    {
        var m = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
        if (m == null) yield break;
        yield return m.InitializeLoader();
        if (m.activeLoader == null) { Debug.Log("[Desktop] No OpenXR runtime / headset."); yield break; }
        m.StartSubsystems();
        yield return WaitForHeadset(waitSeconds);
        if (OpenXRUtility.IsSessionFocused) Exit();
        else { m.StopSubsystems(); m.DeinitializeLoader(); cam.ResetProjectionMatrix(); cam.fieldOfView = fov; Debug.Log("[Desktop] Headset not in use: staying on the monitor."); }
    }

    public void ResetLook()
    {
        yaw = 0f; pitch = 12f;   // a little down: wheel, dash and bonnet in view
        orbitYaw = 0f; orbitPitch = 14f; orbitDistance = 9.8f;
    }

    // ------------------------------------------------------------------ pick proxies
    // Static mesh colliders on their own layer, following the cab parts every frame. The cab parts can't own colliders
    // themselves: the tractor is a dynamic Rigidbody (non-convex mesh colliders aren't allowed on it, and they would collide).
    void BuildProxies()
    {
        for (int i = 0; i < 32; i++) Physics.IgnoreLayerCollision(PickLayer, i, true);
        Vector3 eye = rig.seatAnchor.position;
        foreach (var mf in rig.tractor.GetComponentsInChildren<MeshFilter>())
        {
            var r = mf.GetComponent<MeshRenderer>();
            if (mf.sharedMesh == null || !mf.sharedMesh.isReadable || r == null || !r.enabled) continue;
            if (Vector3.Distance(r.bounds.center, eye) > 1.3f) continue;
            var go = new GameObject("Pick_" + mf.name) { layer = PickLayer };
            go.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            proxies.Add((mf.transform, go.transform));
        }
        SyncProxies();
    }

    void SyncProxies()
    {
        foreach (var (source, proxy) in proxies)
        {
            proxy.SetPositionAndRotation(source.position, source.rotation);
            proxy.localScale = source.lossyScale;
        }
        Physics.SyncTransforms();
    }

    Transform SourceOf(Collider c)
    {
        foreach (var (source, proxy) in proxies) if (proxy == c.transform) return source;
        return null;
    }

    // ------------------------------------------------------------------ per frame
    void Update()
    {
        var kb = Keyboard.current;
        if (!Active) return;
        if (kb != null && kb.f9Key.wasPressedThisFrame) StartCoroutine(TryVR());
        if (kb != null && kb.f11Key.wasPressedThisFrame) ToggleFullscreen();
        if (rig.input.RecenterPressed) ResetLook();

        // The rig is placed for this frame now (VRCameraRig does it in LateUpdate, too late for the mouse hand in VRHands).
        if (rig.view == VRCameraRig.View.Cab) rig.transform.SetPositionAndRotation(rig.seatAnchor.position, rig.seatAnchor.rotation);

        var mouse = Mouse.current;
        if (mouse != null)
        {
            Vector2 d = mouse.delta.ReadValue() * lookSpeed;
            float wheel = mouse.scroll.ReadValue().y;
            if (rig.view == VRCameraRig.View.Cab)
            {
                if (mouse.rightButton.isPressed) { yaw = Mathf.Clamp(yaw + d.x, -160f, 160f); pitch = Mathf.Clamp(pitch - d.y, -75f, 75f); }
                if (wheel != 0f) fov = Mathf.Clamp(fov - Mathf.Sign(wheel) * 5f, 30f, 90f);
            }
            else
            {
                if (mouse.rightButton.isPressed) { orbitYaw += d.x; orbitPitch = Mathf.Clamp(orbitPitch - d.y, -5f, 80f); }
                if (wheel != 0f) orbitDistance = Mathf.Clamp(orbitDistance * (wheel > 0f ? 0.9f : 1.1f), 5f, 30f);
            }
        }
        cam.fieldOfView = rig.view == VRCameraRig.View.Cab ? fov : 60f;
        PlaceCamera();
        SyncProxies();

        foreach (int i in heldRepeat) panel.PressEntry(i);
        heldRepeat.Clear();
    }

    void LateUpdate()
    {
        if (Active) PlaceCamera();
    }

    // Camera pose in rig space. Third person: the rig sits at (0, 4.2, -9.5) behind the tractor looking ahead, so the orbit
    // pivot (1.8 m above the tractor origin) is at (0, -2.4, 9.5) from it.
    void PlaceCamera()
    {
        Transform c = cam.transform;
        if (rig.view == VRCameraRig.View.Cab)
        {
            c.localPosition = Vector3.zero;
            c.localRotation = Quaternion.Euler(pitch, yaw, 0f);
        }
        else
        {
            Vector3 pivot = new Vector3(0f, 1.8f, 0f) - rig.thirdPersonOffset;
            Quaternion rot = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
            c.localRotation = rot;
            c.localPosition = pivot + rot * new Vector3(0f, 0f, -orbitDistance);
        }
    }

    /// <summary>
    /// The mouse as a hand (called by VRHands instead of the controllers): tracked while the ray is on a cab part or something is
    /// held, grabbing while the left button is down. The poke tip only exists while the button is down and stays on the part first
    /// clicked, so sweeping the mouse with the button held doesn't press everything on the way.
    /// </summary>
    public void FeedHand(VRHands.Hand h)
    {
        var mouse = Mouse.current;
        Vector3 far = cam.transform.position - cam.transform.forward * 100f;
        if (mouse == null || rig.view != VRCameraRig.View.Cab) { h.PokeTip = far; VRHands.Apply(h, false, far, false); return; }
        Vector2 mp = mouse.position.ReadValue();
        bool down = mouse.leftButton.isPressed;
        bool overMenu = panel.Open && menuRect.Contains(new Vector2(mp.x, Screen.height - mp.y));
        Ray ray = cam.ScreenPointToRay(mp);

        if (h.Grabbing && down)
        {
            Vector3 p = ray.GetPoint(grabDepth);
            h.PokeTip = pokeTarget ? pokeTarget.TransformPoint(pokeLocal) : far;
            VRHands.Apply(h, true, p, true);
            return;
        }
        if (!overMenu && Physics.Raycast(ray, out RaycastHit hit, 3f, 1 << PickLayer, QueryTriggerInteraction.Ignore))
        {
            bool start = down && !h.Grabbing;
            if (start)
            {
                grabDepth = hit.distance;
                pokeTarget = SourceOf(hit.collider);
                pokeLocal = pokeTarget ? pokeTarget.InverseTransformPoint(hit.point + ray.direction * 0.005f) : Vector3.zero;
            }
            h.PokeTip = down && pokeTarget ? pokeTarget.TransformPoint(pokeLocal) : far;
            VRHands.Apply(h, true, hit.point, down);
            return;
        }
        pokeTarget = null;
        h.PokeTip = far;
        VRHands.Apply(h, false, far, false);
    }

    // ------------------------------------------------------------------ screen UI
    static void ToggleFullscreen()
    {
        if (Screen.fullScreenMode == FullScreenMode.Windowed)
        {
            var r = Screen.currentResolution;
            Screen.SetResolution(r.width, r.height, FullScreenMode.FullScreenWindow);
        }
        else Screen.SetResolution(1600, 900, FullScreenMode.Windowed);
    }

    int styledHeight;
    float buttonWidth;   // widest label seen so far (labels change with state, e.g. WORK: FRONT+REAR), so the menu doesn't jump

    void Styles()
    {
        if (buttonStyle != null && styledHeight == Screen.height) return;   // rebuilt when the window is resized
        styledHeight = Screen.height;
        buttonWidth = 0f;
        int size = Mathf.RoundToInt(Screen.height / 55f);
        buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = size, fontStyle = FontStyle.Bold };
        buttonStyle.padding = new RectOffset(size / 2, size / 2, 2, 2);
        labelStyle = new GUIStyle(GUI.skin.label) { fontSize = size, wordWrap = true };
        labelStyle.normal.textColor = new Color(1f, 0.95f, 0.75f);
        boxStyle = new GUIStyle(GUI.skin.box);
    }

    void OnGUI()
    {
        if (Waiting)
        {
            Styles();
            GUI.Label(new Rect(20, 20, Screen.width - 40, 200),
                "Waiting for the headset (Quest Link)…  Put it on to play in VR, or press D to play on the monitor.\n" +
                "Stuck on the Link loading screen? Meta Horizon Link app → Settings → General → Unknown Sources must be on.", labelStyle);
            return;
        }
        if (!Active) return;
        Styles();
        float u = Screen.height / 32f;   // row height

        // Menu: the cab panel's buttons, three columns at the right edge of the screen, as wide as the longest label.
        int n = panel.EntryCount, cols = 3, rows = (n + cols - 1) / cols + 1;
        float Fit(string s) => buttonWidth = Mathf.Max(buttonWidth, buttonStyle.CalcSize(new GUIContent(s)).x + u * 0.6f);
        for (int i = 0; i < n; i++) Fit(panel.Entry(i).label);
        Fit("FULLSCREEN (F11)");
        Fit("TRY VR (F9)");
        float gap = u * 0.2f, bw = Mathf.Min(buttonWidth, (Screen.width - gap * 6f) / cols);
        menuRect = new Rect(Screen.width - cols * (bw + gap) - gap * 3f, u, cols * (bw + gap) + gap, rows * (u + gap) + gap);
        Rect tab = new Rect(Screen.width - u * 5.5f - gap * 2f, panel.Open ? menuRect.yMax + gap : u, u * 5.5f, u);
        if (GUI.Button(tab, panel.Open ? "CLOSE (M)" : "MENU (M)", buttonStyle)) panel.ToggleOpen();
        if (panel.Open)
        {
            GUI.Box(menuRect, GUIContent.none, boxStyle);
            for (int i = 0; i < n; i++)
            {
                var (label, repeat) = panel.Entry(i);
                Rect r = new Rect(menuRect.x + gap + (i % cols) * (bw + gap), menuRect.y + gap + (i / cols) * (u + gap), bw, u);
                if (repeat) { if (GUI.RepeatButton(r, label, buttonStyle)) heldRepeat.Add(i); }
                else if (GUI.Button(r, label, buttonStyle)) panel.PressEntry(i);
            }
            Rect win = new Rect(menuRect.x + gap, menuRect.y + gap + (rows - 1) * (u + gap), bw, u);
            if (GUI.Button(win, Screen.fullScreenMode == FullScreenMode.Windowed ? "FULLSCREEN (F11)" : "WINDOW (F11)", buttonStyle)) ToggleFullscreen();
            win.x += bw + gap;
            if (GUI.Button(win, "TRY VR (F9)", buttonStyle)) StartCoroutine(TryVR());
        }

        // Key help, bottom left.
        GUI.Label(new Rect(u * 0.5f, Screen.height - u * 3.2f, Screen.width * 0.6f, u * 3f),
            "W/S speed  A/D steer  C view  right mouse look  left mouse: press switches, drag wheel / levers\n" +
            "Q/E hitch  P PTO  J attach  T gearbox  H horn  G lights  U sky  M menu  R look ahead", labelStyle);
    }
}
