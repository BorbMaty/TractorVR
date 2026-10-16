using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using TrackedPoseDriver = UnityEngine.InputSystem.XR.TrackedPoseDriver;

/// <summary>
/// One-click (or batch-mode) setup: Quest 2 player settings, OpenXR, URP, model import,
/// and generation of the driving scene. Safe to run repeatedly.
///   Menu: Tractor VR > Run Full Setup
///   CLI : Unity.exe -batchmode -projectPath . -buildTarget Android -executeMethod ProjectSetup.Run -quit
/// </summary>
public static class ProjectSetup
{
    const string FbxPath = "Assets/Models/John_Deere_6195M.fbx";
    const string ScenePath = "Assets/Scenes/TractorFlat.unity";
    const string GenDir = "Assets/Generated";
    const string Prefix = "John_Deere_6195M_";

    // John Deere 6195M spec sheet: wheelbase 2800 mm. The model is scaled to match.
    const float RealWheelbase = 2.80f;

    [MenuItem("Tractor VR/Run Full Setup")]
    public static void Run()
    {
        ConfigurePlayer();
        ConfigureRenderPipeline();
        ConfigureXR();
        ConfigureModelImport();
        BuildScene();
        AssetDatabase.SaveAssets();
        RenderPreviews();
        Debug.Log("[Setup] Done.");
    }

    [MenuItem("Tractor VR/Rebuild Scene Only")]
    public static void BuildSceneMenu() => BuildScene();

    // ------------------------------------------------------------------ player / platform
    static void ConfigurePlayer()
    {
        PlayerSettings.companyName = "Sapientia";
        PlayerSettings.productName = "Tractor VR";
        var android = NamedBuildTarget.Android;
        PlayerSettings.SetApplicationIdentifier(android, "com.sapientia.tractorvr");
        PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
        EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;

        // Active Input Handling = Input System package (new). 0 = old, 1 = new, 2 = both.
        var ps = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
        if (ps != null)
        {
            var so = new SerializedObject(ps);
            var prop = so.FindProperty("activeInputHandler");
            if (prop != null && prop.intValue != 1) { prop.intValue = 1; so.ApplyModifiedProperties(); Debug.Log("[Setup] Input handling set to Input System (restart editor if open)."); }
        }
    }

    // ------------------------------------------------------------------ URP
    static void ConfigureRenderPipeline()
    {
        Directory.CreateDirectory(GenDir);
        string assetPath = GenDir + "/QuestURP.asset";
        var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath);
        if (urp == null)
        {
            var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(renderer, GenDir + "/QuestURP_Renderer.asset");
            urp = UniversalRenderPipelineAsset.Create(renderer);
            AssetDatabase.CreateAsset(urp, assetPath);
        }
        urp.msaaSampleCount = 4;
        urp.supportsHDR = false;
        urp.renderScale = 1f;
        urp.shadowDistance = 40f;
        urp.supportsCameraDepthTexture = false;
        urp.supportsCameraOpaqueTexture = false;
        EditorUtility.SetDirty(urp);

        GraphicsSettings.defaultRenderPipeline = urp;
        int current = QualitySettings.GetQualityLevel();
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = urp;
        }
        QualitySettings.SetQualityLevel(current, false);
    }

    // ------------------------------------------------------------------ XR / OpenXR
    static void ConfigureXR()
    {
        Directory.CreateDirectory("Assets/XR");
        EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget);
        if (perTarget == null)
        {
            perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
        }

        foreach (var group in new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone })
        {
            if (!perTarget.HasSettingsForBuildTarget(group)) perTarget.CreateDefaultSettingsForBuildTarget(group);
            if (!perTarget.HasManagerSettingsForBuildTarget(group)) perTarget.CreateDefaultManagerSettingsForBuildTarget(group);
            var settings = perTarget.SettingsForBuildTarget(group);
            settings.InitManagerOnStart = true;
            bool ok = XRPackageMetadataStore.AssignLoader(settings.Manager, "UnityEngine.XR.OpenXR.OpenXRLoader", group);
            Debug.Log($"[Setup] OpenXR loader on {group}: {ok}");
            EditorUtility.SetDirty(settings);
            EditorUtility.SetDirty(settings.Manager);

            UnityEditor.XR.OpenXR.Features.FeatureHelpers.RefreshFeatures(group);
            var oxr = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (oxr == null) { Debug.LogWarning($"[Setup] No OpenXR settings for {group} yet — run setup again."); continue; }
            foreach (var f in oxr.GetFeatures())
            {
                string n = f.GetType().Name;
                bool want = n == "OculusTouchControllerProfile" || n == "MetaQuestTouchPlusControllerProfile"
                            || (group == BuildTargetGroup.Android && n == "MetaQuestFeature");
                if (want && !f.enabled) f.enabled = true;
                if (want) Debug.Log($"[Setup]   {group} feature {n} enabled");
            }
            EditorUtility.SetDirty(oxr);
        }
        AssetDatabase.SaveAssets();
    }

    // ------------------------------------------------------------------ model import
    static void ConfigureModelImport()
    {
        var imp = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
        if (imp == null) { Debug.LogError("[Setup] FBX not found at " + FbxPath); return; }
        imp.importAnimation = false;
        imp.importCameras = false;
        imp.importLights = false;
        imp.animationType = ModelImporterAnimationType.None;
        imp.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        imp.materialLocation = ModelImporterMaterialLocation.InPrefab;
        imp.isReadable = false;
        imp.addCollider = false;
        imp.SaveAndReimport(); // reimport so materials are generated for URP
    }

    // ------------------------------------------------------------------ scene
    static void BuildScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var log = new StringBuilder();

        // Light + sky
        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 1.3f;
        sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
        RenderSettings.sun = sun;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.62f, 0.72f, 0.85f);
        RenderSettings.ambientEquatorColor = new Color(0.55f, 0.58f, 0.55f);
        RenderSettings.ambientGroundColor = new Color(0.3f, 0.28f, 0.22f);
        var skyShader = Shader.Find("Skybox/Procedural");
        if (skyShader != null)
        {
            var sky = SaveMat(new Material(skyShader), "Sky");
            sky.SetFloat("_AtmosphereThickness", 0.9f);
            RenderSettings.skybox = sky;
        }

        BuildGround();
        var tractor = BuildTractor(log);
        BuildRig(tractor, log);

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        File.WriteAllText("Logs/TractorSetupReport.txt", log.ToString());
        Debug.Log("[Setup] Scene report:\n" + log);
    }

    static Shader LitShader => Shader.Find("Universal Render Pipeline/Lit");

    static Material SaveMat(Material m, string name)
    {
        string p = $"{GenDir}/{name}.mat";
        AssetDatabase.DeleteAsset(p);
        AssetDatabase.CreateAsset(m, p);
        return m;
    }

    static void BuildGround()
    {
        // Procedural field texture: grass tone with a faint 1 m grid and stronger 10 m lines,
        // so speed and distance are readable in VR.
        const int size = 512;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
        var rnd = new System.Random(7);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float n = (float)rnd.NextDouble() * 0.06f;
            Color c = new Color(0.30f + n, 0.45f + n, 0.18f + n * 0.5f);
            bool major = x < 3 || y < 3;
            bool minor = (x % 51) < 1 || (y % 51) < 1;
            if (major) c = Color.Lerp(c, new Color(0.85f, 0.85f, 0.75f), 0.6f);
            else if (minor) c = Color.Lerp(c, new Color(0.2f, 0.3f, 0.1f), 0.4f);
            tex.SetPixel(x, y, c);
        }
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.anisoLevel = 8;
        string texPath = GenDir + "/GroundGrid.asset";
        AssetDatabase.DeleteAsset(texPath);
        AssetDatabase.CreateAsset(tex, texPath);

        var mat = new Material(LitShader);
        mat.SetTexture("_BaseMap", tex);
        mat.SetTextureScale("_BaseMap", new Vector2(200f, 200f)); // 2000 m / 200 = one tile per 10 m
        mat.SetFloat("_Smoothness", 0.05f);
        SaveMat(mat, "Ground");

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(200f, 1f, 200f); // 2 km x 2 km
        ground.GetComponent<Renderer>().sharedMaterial = mat;
        ground.isStatic = true;

        // Marker posts every 25 m along a few lines — reference points for speed and steering.
        var postMat = SaveMat(new Material(LitShader) { color = new Color(0.95f, 0.55f, 0.1f) }, "Post");
        var parent = new GameObject("Markers").transform;
        for (int i = -20; i <= 20; i++)
        for (int j = -2; j <= 2; j++)
        {
            if (j == 0 && Mathf.Abs(i) < 2) continue;
            var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            post.name = "Post";
            post.transform.SetParent(parent);
            post.transform.position = new Vector3(j * 30f, 0.75f, i * 25f);
            post.transform.localScale = new Vector3(0.15f, 0.75f, 0.15f);
            post.GetComponent<Renderer>().sharedMaterial = postMat;
            Object.DestroyImmediate(post.GetComponent<Collider>());
            post.isStatic = true;
        }
    }

    // ------------------------------------------------------------------ tractor
    static GameObject BuildTractor(StringBuilder log)
    {
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        var root = new GameObject("Tractor");
        var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        model.name = "Model";
        model.transform.SetParent(root.transform, false);

        Transform Part(string n) => model.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(t => t.name.EndsWith(Prefix + n));

        string[] fl = { "Front_Wheel_L", "Front_Tire_L" }, fr = { "Front_Wheel_R", "Front_Tire_R" };
        string[] rl = { "Rear_Wheel_L", "Rear_Tire_L" }, rr = { "Rear_Wheel_R", "Rear_Tire_R" };
        Bounds B(params string[] names) => WorldBounds(names.Select(Part).Where(t => t != null).ToArray());

        log.AppendLine($"Raw bounds: {WorldBounds(model.transform)}");

        ApplyMaterials(model, fl.Concat(fr).Concat(rl).Concat(rr).Select(Part).Where(t => t != null).ToArray());

        // 1) Orientation: front axle must point to +Z.
        Vector3 dir = B(fl.Concat(fr).ToArray()).center - B(rl.Concat(rr).ToArray()).center;
        dir.y = 0f;
        float yaw = Vector3.SignedAngle(dir, Vector3.forward, Vector3.up);
        model.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * model.transform.rotation;
        log.AppendLine($"Yaw correction: {yaw:0.0}°");

        // 2) Scale to the real wheelbase.
        float wb = Vector3.Distance(Flat(B(fl.Concat(fr).ToArray()).center), Flat(B(rl.Concat(rr).ToArray()).center));
        float scale = RealWheelbase / wb;
        model.transform.localScale *= scale;
        log.AppendLine($"Model wheelbase {wb:0.###} -> scale x{scale:0.#####}");

        // 3) Origin = rear axle centre on the ground.
        Bounds all = WorldBounds(model.transform);
        Vector3 rear = B(rl.Concat(rr).ToArray()).center;
        model.transform.position -= new Vector3(all.center.x, all.min.y, rear.z);
        all = WorldBounds(model.transform);
        log.AppendLine($"Final bounds: size {all.size} (W x H x L), min {all.min}, max {all.max}");

        // 4) Wheel pivots: SteerPivot (Y) > SpinPivot (X) > meshes.
        root.AddComponent<TractorInput>();
        var ctrl = root.AddComponent<TractorController>();
        var wheels = new[]
        {
            MakeWheel(root.transform, "Wheel_FL", fl, new[] { "rotater_01_L", "rotater_02_L", "rotater_03_L" }, true, true, Part, log),
            MakeWheel(root.transform, "Wheel_FR", fr, new[] { "rotater_01_R", "rotater_02_R", "rotater_03_R" }, true, false, Part, log),
            MakeWheel(root.transform, "Wheel_RL", rl, new string[0], false, true, Part, log),
            MakeWheel(root.transform, "Wheel_RR", rr, new string[0], false, false, Part, log),
        };
        ctrl.wheels = wheels;
        ctrl.wheelbase = wheels[0].steerPivot.localPosition.z - wheels[2].steerPivot.localPosition.z;
        ctrl.trackWidth = Mathf.Abs(wheels[1].steerPivot.localPosition.x - wheels[0].steerPivot.localPosition.x);
        log.AppendLine($"Controller wheelbase {ctrl.wheelbase:0.###} m, front track {ctrl.trackWidth:0.###} m");

        var rb = root.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        var box = root.AddComponent<BoxCollider>();
        box.center = root.transform.InverseTransformPoint(all.center);
        box.size = all.size;

        // 5) Driver seat (eye point) estimated from the cab geometry; fine-tune in VR (hold left grip).
        Bounds cab = WorldBounds(Part("Cab"));
        Bounds interior = WorldBounds(Part("Cab_Interier_01"));
        log.AppendLine($"Cab bounds {cab.min} .. {cab.max}");
        log.AppendLine($"Interior bounds {interior.min} .. {interior.max}");
        var seat = new GameObject("DriverEye").transform;
        seat.SetParent(root.transform, false);
        Bounds rearWindow = WorldBounds(Part("Rear_Window"));
        float eyeY = interior.min.y + 1.20f;      // cab floor + seated eye height
        float eyeZ = rearWindow.center.z + 0.45f; // seat back sits just ahead of the rear window
        seat.localPosition = new Vector3(0f, eyeY, eyeZ);
        log.AppendLine($"DriverEye at {seat.localPosition}");

        log.AppendLine("Nodes:");
        foreach (var t in model.GetComponentsInChildren<Transform>(true))
        {
            var r = t.GetComponent<Renderer>();
            log.AppendLine($"  {Path(t, model.transform)}  {(r ? "c=" + r.bounds.center.ToString("F2") + " s=" + r.bounds.size.ToString("F2") : "")}");
            if (r) foreach (var m in r.sharedMaterials)
                log.AppendLine($"      mat {m?.name} shader={m?.shader.name} color={(m != null && m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor").ToString("F2") : "-")} tex={(m != null && m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap")?.name : "-")}");
        }
        return root;
    }

    // The FBX references external textures that aren't shipped with it, so the look is rebuilt
    // from its material names: John Deere green paint, yellow rims, clear glass.
    static void ApplyMaterials(GameObject model, Transform[] wheelParts)
    {
        var cache = new System.Collections.Generic.Dictionary<string, Material>();
        Material Get(Material src, bool onWheel)
        {
            string key = src.name + (onWheel ? "_wheel" : "");
            if (cache.TryGetValue(key, out var m)) return m;
            m = new Material(LitShader) { name = key };
            Color c = src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : Color.gray;
            float smooth = 0.4f, metal = 0f;
            switch (src.name)
            {
                case "Body_paint":
                    c = onWheel ? new Color(1f, 0.82f, 0.05f) : new Color(0.21f, 0.49f, 0.17f);
                    smooth = 0.65f; break;
                case "Glass_windows":
                    c = new Color(0.55f, 0.65f, 0.7f, 0.12f); smooth = 0.95f; break;
                case "Chrome": case "mirror": smooth = 0.9f; metal = 1f; break;
                case "Steel": case "Aluminium": case "headlights_reflector": smooth = 0.6f; metal = 0.8f; break;
                case "Rubber black": case "Tyres_front": case "Tyres_rear": c = new Color(0.08f, 0.08f, 0.08f); smooth = 0.15f; break;
                case "Seat_cloth01": case "Seat_cloth02": c = new Color(0.12f, 0.12f, 0.12f); smooth = 0.1f; break;
                case "Dashboard": c = new Color(0.06f, 0.06f, 0.06f); smooth = 0.3f; break;
                case "fill_holes": c = new Color(0.02f, 0.02f, 0.02f); break;
                case "Emitter":
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", new Color(0.9f, 0.9f, 0.8f));
                    break;
            }
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            if (c.a < 0.99f) MakeTransparent(m);
            if (src.name == "Glass_windows")
            {
                // Unlit, near-clear glass: no grey environment reflections, cheap on Quest.
                m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = key };
                m.SetColor("_BaseColor", new Color(0.7f, 0.8f, 0.85f, 0.06f));
                MakeTransparent(m);
                m.SetFloat("_Cull", 0f);
            }
            cache[key] = SaveMat(m, "Tractor_" + key.Replace(' ', '_'));
            return m;
        }

        foreach (var r in model.GetComponentsInChildren<Renderer>(true))
        {
            bool onWheel = wheelParts.Any(w => r.transform.IsChildOf(w));
            r.sharedMaterials = r.sharedMaterials.Select(m => m == null ? null : Get(m, onWheel)).ToArray();
        }
    }

    static void MakeTransparent(Material m)
    {
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_ReceiveShadows", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetShaderPassEnabled("ShadowCaster", false);
        m.renderQueue = (int)RenderQueue.Transparent;
    }

    static TractorController.Wheel MakeWheel(Transform root, string name, string[] parts, string[] hubParts, bool front, bool left,
        System.Func<string, Transform> part, StringBuilder log)
    {
        var ts = parts.Select(part).Where(t => t != null).ToList();
        // Only move top-most transforms (a tire may be parented under its rim).
        ts = ts.Where(t => !ts.Any(o => o != t && t.IsChildOf(o))).ToList();
        Bounds b = WorldBounds(ts.ToArray());

        var steer = new GameObject(name).transform;
        steer.SetParent(root, false);
        steer.position = b.center;
        var spin = new GameObject("Spin").transform;
        spin.SetParent(steer, false);
        foreach (var t in ts) t.SetParent(spin, true);
        // Front hub / knuckle parts steer with the wheel but don't spin.
        foreach (var t in hubParts.Select(part).Where(t => t != null)) t.SetParent(steer, true);

        float radius = b.extents.y;
        log.AppendLine($"{name}: centre {steer.localPosition}, radius {radius:0.###} m, parts {string.Join(",", ts.Select(t => t.name))}");
        return new TractorController.Wheel { steerPivot = steer, spinPivot = spin, radius = radius, steers = front, leftSide = left };
    }

    // ------------------------------------------------------------------ XR rig + HUD
    static void BuildRig(GameObject tractor, StringBuilder log)
    {
        var rig = new GameObject("XR Rig");
        var offset = new GameObject("Camera Offset").transform;
        offset.SetParent(rig.transform, false);
        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        camGo.transform.SetParent(offset, false);
        var cam = camGo.AddComponent<Camera>();
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 1500f;
        camGo.AddComponent<AudioListener>();
        var data = camGo.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = false;

        var tpd = camGo.AddComponent<TrackedPoseDriver>();
        tpd.positionInput = new InputActionProperty(new InputAction("HeadPos", InputActionType.Value, "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
        tpd.rotationInput = new InputActionProperty(new InputAction("HeadRot", InputActionType.Value, "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
        tpd.trackingStateInput = new InputActionProperty(new InputAction("HeadState", InputActionType.Value, "<XRHMD>/trackingState", expectedControlType: "Integer"));

        var ctrl = tractor.GetComponent<TractorController>();
        var vr = rig.AddComponent<VRCameraRig>();
        vr.tractor = ctrl;
        vr.input = tractor.GetComponent<TractorInput>();
        vr.seatAnchor = tractor.transform.Find("DriverEye");
        vr.cameraOffset = offset;
        vr.head = camGo.transform;
        rig.transform.SetPositionAndRotation(vr.seatAnchor.position, vr.seatAnchor.rotation);

        // HUD: rig-locked, low and slightly forward, tilted toward the eyes.
        var hudGo = new GameObject("HUD");
        hudGo.transform.SetParent(rig.transform, false);
        hudGo.transform.localPosition = new Vector3(0f, -0.38f, 0.75f);
        hudGo.transform.localRotation = Quaternion.Euler(30f, 0f, 0f);
        var tm = hudGo.AddComponent<TextMesh>();
        tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        tm.fontSize = 64;
        tm.characterSize = 0.0045f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = new Color(1f, 0.95f, 0.6f);
        hudGo.GetComponent<MeshRenderer>().sharedMaterial = tm.font.material;
        var hud = hudGo.AddComponent<TractorHud>();
        hud.tractor = ctrl;
        hud.rig = vr;
        hud.input = vr.input;
        hud.text = tm;

        hud.comfort = BuildVignette(camGo.transform, ctrl, vr);
        var kit = new ControllerKit
        {
            body = SaveMat(Lit(new Color(0.11f, 0.11f, 0.12f), 0.55f), "Controller_Body"),
            ring = SaveMat(Lit(new Color(0.2f, 0.2f, 0.22f), 0.6f), "Controller_Ring"),
            idle = SaveMat(Lit(new Color(0.55f, 0.56f, 0.58f), 0.5f), "Controller_Button"),
            pressed = SaveMat(Lit(new Color(1f, 0.85f, 0.1f), 0.5f), "Controller_Pressed"),
            ringMesh = RingMesh(),
        };
        BuildController(offset, true, kit);
        BuildController(offset, false, kit);

        log.AppendLine("Rig built.");
    }

    // Head-locked quad 1 m ahead, 5 m wide → covers ±68° (tan = 2.5), beyond the Quest 2 field of view.
    static ComfortVignette BuildVignette(Transform cam, TractorController tractor, VRCameraRig rig)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "Comfort Vignette";
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(cam, false);
        go.transform.localPosition = new Vector3(0f, 0f, 1f);
        go.transform.localScale = new Vector3(5f, 5f, 1f);
        var r = go.GetComponent<MeshRenderer>();
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.sharedMaterial = SaveMat(new Material(Shader.Find("TractorVR/ComfortVignette")), "ComfortVignette");
        r.enabled = false; // ComfortVignette turns it on while moving

        var v = go.AddComponent<ComfortVignette>();
        v.tractor = tractor;
        v.input = rig.input;
        v.rig = rig.transform;
        v.quad = r;
        v.quadTanHalf = 2.5f;
        return v;
    }

    // ------------------------------------------------------------------ controllers
    // Stand-in Quest Touch model from primitives, in OpenXR grip-pose space
    // (+Z along the handle toward the face plate, +Y back of the hand, -Y trigger side).
    class ControllerKit { public Material body, ring, idle, pressed; public Mesh ringMesh; }

    static void BuildController(Transform cameraOffset, bool left, ControllerKit kit)
    {
        string hand = left ? "LeftHand" : "RightHand";
        float m = left ? -1f : 1f; // mirror X for the left hand
        Material body = kit.body, idle = kit.idle;

        var root = new GameObject(left ? "Left Controller" : "Right Controller");
        root.transform.SetParent(cameraOffset, false);
        var tpd = root.AddComponent<TrackedPoseDriver>();
        tpd.positionInput = new InputActionProperty(new InputAction(hand + "Pos", InputActionType.Value, $"<XRController>{{{hand}}}/devicePosition", expectedControlType: "Vector3"));
        tpd.rotationInput = new InputActionProperty(new InputAction(hand + "Rot", InputActionType.Value, $"<XRController>{{{hand}}}/deviceRotation", expectedControlType: "Quaternion"));
        tpd.trackingStateInput = new InputActionProperty(new InputAction(hand + "State", InputActionType.Value, $"<XRController>{{{hand}}}/trackingState", expectedControlType: "Integer"));

        Transform Part(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 euler, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return go.transform;
        }

        // Handle along Z: 10 cm long, 3.4 cm thick.
        Part(PrimitiveType.Capsule, "Handle", root.transform, new Vector3(0f, -0.004f, -0.025f), new Vector3(90f, 0f, 0f), new Vector3(0.034f, 0.05f, 0.034f), body);

        // Face plate on top of the handle, tilted 45° so it faces up when the controller is held naturally.
        var face = new GameObject("Face").transform;
        face.SetParent(root.transform, false);
        face.localPosition = new Vector3(0f, 0.006f, 0.035f);
        face.localRotation = Quaternion.Euler(45f, 0f, 0f);
        Part(PrimitiveType.Cylinder, "Plate", face, Vector3.zero, Vector3.zero, new Vector3(0.06f, 0.006f, 0.06f), body);

        var stick = new GameObject("Stick").transform;
        stick.SetParent(face, false);
        stick.localPosition = new Vector3(-0.009f * m, 0.006f, -0.004f);
        Part(PrimitiveType.Cylinder, "Shaft", stick, new Vector3(0f, 0.004f, 0f), Vector3.zero, new Vector3(0.006f, 0.004f, 0.006f), body);
        Part(PrimitiveType.Cylinder, "Cap", stick, new Vector3(0f, 0.009f, 0f), Vector3.zero, new Vector3(0.019f, 0.002f, 0.019f), idle);

        var primary = Part(PrimitiveType.Cylinder, left ? "X" : "A", face, new Vector3(0.012f * m, 0.0065f, -0.012f), Vector3.zero, new Vector3(0.011f, 0.002f, 0.011f), idle);
        var secondary = Part(PrimitiveType.Cylinder, left ? "Y" : "B", face, new Vector3(0.016f * m, 0.0065f, 0.004f), Vector3.zero, new Vector3(0.011f, 0.002f, 0.011f), idle);

        // Tracking ring: hoops over the fingers in front of the face plate.
        var ringGo = new GameObject("Ring");
        ringGo.transform.SetParent(root.transform, false);
        ringGo.transform.localPosition = new Vector3(0f, -0.028f, 0.045f);
        ringGo.transform.localRotation = Quaternion.Euler(0f, 0f, 90f); // torus axis (Y) → X
        ringGo.AddComponent<MeshFilter>().sharedMesh = kit.ringMesh;
        var rr = ringGo.AddComponent<MeshRenderer>();
        rr.sharedMaterial = kit.ring;
        rr.shadowCastingMode = ShadowCastingMode.Off;

        var trigger = Part(PrimitiveType.Cube, "Trigger", root.transform, new Vector3(0f, -0.022f, 0.012f), new Vector3(-20f, 0f, 0f), new Vector3(0.012f, 0.01f, 0.024f), idle);
        var grip = Part(PrimitiveType.Cube, "Grip", root.transform, new Vector3(-0.016f * m, -0.008f, -0.02f), Vector3.zero, new Vector3(0.006f, 0.016f, 0.032f), idle);

        var vis = root.AddComponent<ControllerVisual>();
        vis.leftHand = left;
        vis.stick = stick;
        vis.primaryButton = primary.GetComponent<Renderer>();
        vis.secondaryButton = secondary.GetComponent<Renderer>();
        vis.trigger = trigger;
        vis.grip = grip;
        vis.idle = idle;
        vis.pressed = kit.pressed;
    }

    static Material Lit(Color c, float smoothness)
    {
        var mat = new Material(LitShader);
        mat.SetColor("_BaseColor", c);
        mat.SetFloat("_Smoothness", smoothness);
        return mat;
    }

    // Torus around local Y, radius 4.5 cm, tube 0.6 cm. Saved as an asset so the scene can reference it.
    static Mesh RingMesh()
    {
        string p = $"{GenDir}/ControllerRing.asset";
        AssetDatabase.DeleteAsset(p);

        const int seg = 48, tube = 10;
        const float R = 0.045f, r = 0.006f;
        var verts = new Vector3[(seg + 1) * (tube + 1)];
        var tris = new int[seg * tube * 6];
        for (int i = 0; i <= seg; i++)
        for (int j = 0; j <= tube; j++)
        {
            float u = i * Mathf.PI * 2f / seg, v = j * Mathf.PI * 2f / tube;
            float d = R + r * Mathf.Cos(v);
            verts[i * (tube + 1) + j] = new Vector3(d * Mathf.Cos(u), r * Mathf.Sin(v), d * Mathf.Sin(u));
        }
        int t = 0;
        for (int i = 0; i < seg; i++)
        for (int j = 0; j < tube; j++)
        {
            int a = i * (tube + 1) + j, b = a + tube + 1;
            tris[t++] = a; tris[t++] = a + 1; tris[t++] = b;
            tris[t++] = b; tris[t++] = a + 1; tris[t++] = b + 1;
        }
        var mesh = new Mesh { name = "ControllerRing", vertices = verts, triangles = tris };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, p);
        return mesh;
    }

    // ------------------------------------------------------------------ preview renders (batch mode QA)
    [MenuItem("Tractor VR/Render Preview Images")]
    public static void RenderPreviews()
    {
        EditorSceneManager.OpenScene(ScenePath);
        ShaderUtil.allowAsyncCompilation = false; // otherwise first renders show placeholder shaders
        var tractor = GameObject.Find("Tractor").transform;
        var seat = tractor.Find("DriverEye");
        var cam = Camera.main;
        cam.fieldOfView = 90f;
        Directory.CreateDirectory("Logs/Previews");
        void Shot(string file, Vector3 pos, Quaternion rot)
        {
            cam.transform.SetPositionAndRotation(pos, rot);
            var rt = new RenderTexture(1280, 1000, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            File.WriteAllBytes("Logs/Previews/" + file, tex.EncodeToPNG());
            cam.targetTexture = null;
            RenderTexture.active = null;
        }
        // Untracked controllers sit at the rig origin (the eye point); hide them except in their own shot.
        var leftCtl = GameObject.Find("Left Controller");
        var ctl = GameObject.Find("Right Controller").transform;
        leftCtl.SetActive(false);
        ctl.gameObject.SetActive(false);
        // The first batch-mode render comes out grey (lighting/environment not ready yet): throw one away.
        Shot("warmup.png", seat.position, Quaternion.Euler(10f, 0f, 0f));
        File.Delete("Logs/Previews/warmup.png");
        Shot("cab.png", seat.position, Quaternion.Euler(10f, 0f, 0f));
        Shot("cab_down.png", seat.position, Quaternion.Euler(35f, 0f, 0f));
        Shot("third.png", tractor.position + new Vector3(0f, 4.2f, -9.5f) + Vector3.up * 0f, Quaternion.Euler(15f, 0f, 0f));
        Shot("side.png", tractor.position + new Vector3(8f, 1.8f, 1.4f), Quaternion.Euler(5f, -90f, 0f));
        Shot("front.png", tractor.position + new Vector3(3f, 2f, 9f), Quaternion.Euler(8f, 200f, 0f));

        // Right controller close-up, posed as if held in front of the driver (grip Z tilted 45° forward-up).
        ctl.gameObject.SetActive(true);
        ctl.SetPositionAndRotation(seat.position + new Vector3(0.15f, -0.35f, 0.4f), Quaternion.Euler(-45f, 0f, 0f));
        Shot("controller.png", ctl.position + new Vector3(-0.12f, 0.12f, -0.22f), Quaternion.LookRotation(ctl.position - (ctl.position + new Vector3(-0.12f, 0.12f, -0.22f))));

        // Cab view with the vignette at full "High" strength.
        var vig = Object.FindAnyObjectByType<ComfortVignette>();
        var vr = vig.GetComponent<Renderer>();
        var vm = vr.sharedMaterial;
        vm.SetFloat("_InnerDeg", vig.high.innerDeg);
        vm.SetFloat("_OuterDeg", vig.high.outerDeg);
        vm.SetFloat("_TanHalf", vig.quadTanHalf);
        vr.enabled = true;
        ctl.gameObject.SetActive(false);
        Shot("vignette_high.png", seat.position, Quaternion.Euler(10f, 0f, 0f));
        vr.enabled = false;
        vm.SetFloat("_InnerDeg", vig.idleInnerDeg);
        vm.SetFloat("_OuterDeg", vig.idleOuterDeg);
        Debug.Log("[Setup] Preview images written to Logs/Previews");
    }

    // ------------------------------------------------------------------ helpers
    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    static Bounds WorldBounds(params Transform[] roots)
    {
        bool has = false;
        var b = new Bounds();
        foreach (var root in roots)
        {
            if (root == null) continue;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
            }
        }
        return b;
    }

    static string Path(Transform t, Transform stop)
    {
        string p = t.name;
        while (t.parent != null && t != stop) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }
}
