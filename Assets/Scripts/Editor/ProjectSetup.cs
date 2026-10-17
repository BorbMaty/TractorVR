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
        // Physics at 90 Hz (vehicle stability); the tractor body is interpolated, so the camera stays smooth at 72/90 Hz.
        // Fixed Timestep is stored as a rational (count / rate); Time.fixedDeltaTime set from the editor doesn't persist.
        var time = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TimeManager.asset")[0]);
        var rate = time.FindProperty("Fixed Timestep.m_Rate.m_Numerator").longValue / (double)time.FindProperty("Fixed Timestep.m_Rate.m_Denominator").longValue;
        time.FindProperty("Fixed Timestep.m_Count").longValue = (long)System.Math.Round(rate / 90.0);
        time.ApplyModifiedProperties();
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
        sun.transform.rotation = Quaternion.Euler(35f, -35f, 0f);   // lowish sun: slopes show light and shade
        // Light haze: far hills fade into the sky, which shows depth and hides the world edge (fog is cheap on Quest).
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 250f;
        RenderSettings.fogEndDistance = 1300f;
        RenderSettings.fogColor = new Color(0.72f, 0.8f, 0.88f);
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

    // Rolling terrain (built at runtime by TerrainGenerator from its height function), marker posts and hay bales.
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
        mat.SetTexture("_BaseMap", tex);   // terrain UVs are world XZ / 10 → one tile per 10 m
        mat.SetFloat("_Smoothness", 0.05f);
        SaveMat(mat, "Ground");

        var terrain = new GameObject("Terrain").AddComponent<TerrainGenerator>();
        terrain.material = mat;
        terrain.gameObject.isStatic = true;

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
            float x = j * 30f, z = i * 25f;
            post.transform.position = new Vector3(x, TerrainGenerator.Height(x, z) + 0.7f, z);
            post.transform.localScale = new Vector3(0.15f, 0.75f, 0.15f);
            post.GetComponent<Renderer>().sharedMaterial = postMat;
            Object.DestroyImmediate(post.GetComponent<Collider>());
            post.isStatic = true;
        }

        // Round hay bales (Ø 1.5 m × 1.2 m, 350 kg) on the flat start area: things to push around.
        var baleMat = SaveMat(new Material(LitShader) { color = new Color(0.82f, 0.7f, 0.38f) }, "HayBale");
        baleMat.SetFloat("_Smoothness", 0.05f);
        var bales = new GameObject("HayBales").transform;
        var spots = new[] { new Vector3(6f, 0f, 22f), new Vector3(7.6f, 0f, 22.3f), new Vector3(6.8f, 0f, 24f),
                            new Vector3(-9f, 0f, 35f), new Vector3(-12f, 0f, 41f), new Vector3(4f, 0f, 48f) };
        for (int i = 0; i < spots.Length; i++)
        {
            var bale = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            bale.name = "HayBale";
            bale.transform.SetParent(bales);
            Object.DestroyImmediate(bale.GetComponent<Collider>());
            var mc = bale.AddComponent<MeshCollider>();
            mc.convex = true;
            // Lying on its side: cylinder axis (local Y) horizontal, diameter 1.5 m (X/Z), length 1.2 m (Y = half height).
            bale.transform.localScale = new Vector3(1.5f, 0.6f, 1.5f);
            bale.transform.rotation = Quaternion.Euler(0f, 30f * i, 90f);
            bale.transform.position = spots[i] + Vector3.up * (TerrainGenerator.Height(spots[i].x, spots[i].z) + 0.77f);
            bale.GetComponent<Renderer>().sharedMaterial = baleMat;
            var rb = bale.AddComponent<Rigidbody>();
            rb.mass = 350f;
            rb.angularDamping = 0.5f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
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

        // Physics body (mass, centre of mass and interpolation are set by TractorController.Awake).
        // The collider starts 0.45 m above the ground so only the raycast wheels touch it; it's on the Ignore Raycast
        // layer so those rays don't hit the tractor itself.
        var rb = root.GetComponent<Rigidbody>();   // added with TractorController (RequireComponent)
        rb.isKinematic = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        var box = root.AddComponent<BoxCollider>();
        const float clearance = 0.45f;
        box.center = root.transform.InverseTransformPoint(all.center) + Vector3.up * clearance * 0.5f;
        box.size = all.size - Vector3.up * clearance;
        // Wheel blocks for bumping into things (the wheel rays only see static ground): tyre-sized boxes that stop 0.1 m above it.
        foreach (var w in wheels)
        {
            var wheelBox = root.AddComponent<BoxCollider>();
            float width = w.steers ? 0.5f : 0.65f;
            wheelBox.center = w.steerPivot.localPosition + Vector3.up * 0.05f;
            wheelBox.size = new Vector3(width, 2f * w.radius - 0.1f, 2f * w.radius);
        }
        // Slippery body: a round bale rolling against a grippy flat front drags the nose up onto it (seen in PhysicsTest).
        var slick = new PhysicsMaterial("TractorBody")
        {
            dynamicFriction = 0.1f, staticFriction = 0.15f,
            frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Minimum
        };
        string slickPath = GenDir + "/TractorBody.physicMaterial";
        AssetDatabase.DeleteAsset(slickPath);
        AssetDatabase.CreateAsset(slick, slickPath);
        foreach (var col in root.GetComponents<Collider>()) col.sharedMaterial = slick;
        root.layer = 2; // Ignore Raycast

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

        // 6) Steering wheel cut out of the cab interior mesh, instrument cluster on the column, drivetrain.
        ctrl.steeringWheel = SplitSteeringWheel(root.transform, Part("Cab_Interier_01"), log, out Pose clusterPose, out Vector2 clusterSize);
        var drivetrain = root.AddComponent<Drivetrain>();
        BuildDashboard(root.transform, clusterPose, clusterSize, ctrl, drivetrain, log);

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

    // ------------------------------------------------------------------ steering wheel + instrument cluster
    // The steering wheel is baked into Cab_Interier_01. The mesh is split into its connected pieces (vertices welded
    // by position). The rim is the biggest piece that is centred left-right and 0.3–0.5 m across. Its plane (PCA)
    // gives the steering column axis. Every piece lying flat inside the rim (spokes, hub cap) goes with it.
    // The result is two new meshes: the interior without the wheel, and the wheel in its own pivot frame.
    static Transform SplitSteeringWheel(Transform root, Transform interior, StringBuilder log, out Pose clusterPose, out Vector2 clusterSize)
    {
        var mf = interior.GetComponent<MeshFilter>();
        var renderer = interior.GetComponent<MeshRenderer>();
        Mesh src = mf.sharedMesh;
        Vector3[] local = src.vertices;
        Vector3[] p = local.Select(v => root.InverseTransformPoint(interior.TransformPoint(v))).ToArray();
        int[] comp = MeshComponents(src);
        var parts = Enumerable.Range(0, p.Length).GroupBy(i => comp[i]).Select(g => g.ToArray()).ToList();
        Bounds PartBounds(int[] g) { var b = new Bounds(p[g[0]], Vector3.zero); foreach (int i in g) b.Encapsulate(p[i]); return b; }

        var rim = parts.Where(g =>
        {
            var b = PartBounds(g);
            return Mathf.Abs(b.center.x) < 0.03f && b.size.x > 0.3f && b.size.x < 0.5f;
        }).OrderByDescending(g => g.Length).First();

        // Column axis = rim plane normal, pointing away from the driver (forward/down).
        Vector3 axis = PlaneNormal(rim.Select(i => p[i]));
        if (axis.z < 0f) axis = -axis;
        Quaternion frame = Quaternion.LookRotation(axis, Vector3.up);
        FrameExtents(rim.Select(i => p[i]), frame, out Vector3 centre, out Vector3 size);
        float radius = Mathf.Max(size.x, size.y) * 0.5f;
        log.AppendLine($"Steering wheel: centre {centre:F3}, axis {axis:F3} (rim {Vector3.Angle(axis, Vector3.down):0}° from horizontal), diameter {radius * 2f:0.###} m");

        var inWheel = new bool[p.Length];
        int pieces = 0;
        foreach (var g in parts)
        {
            bool inside = g.All(i =>
            {
                Vector3 d = p[i] - centre;
                float along = Vector3.Dot(d, axis);
                return Mathf.Abs(along) < 0.06f && (d - along * axis).magnitude < radius + 0.005f;
            });
            if (!inside) continue;
            pieces++;
            foreach (int i in g) inWheel[i] = true;
        }
        log.AppendLine($"Steering wheel: {pieces} pieces, {inWheel.Count(b => b)} of {p.Length} vertices");

        // Pivot frame: SteeringColumn (fixed, Z = axis) > SteeringWheel (spins around local Z) > mesh.
        var column = new GameObject("SteeringColumn").transform;
        column.SetParent(root, false);
        column.SetLocalPositionAndRotation(centre, frame);
        var pivot = new GameObject("SteeringWheel").transform;
        pivot.SetParent(column, false);

        var mats = renderer.sharedMaterials;
        Mesh rest = SubsetMesh(src, inWheel, false, null, mats, out Material[] restMats);
        Mesh wheel = SubsetMesh(src, inWheel, true, v => pivot.InverseTransformPoint(interior.TransformPoint(v)), mats, out Material[] wheelMats,
            n => pivot.InverseTransformDirection(interior.TransformDirection(n)));
        rest.name = "CabInterior_NoWheel";
        wheel.name = "SteeringWheel";
        mf.sharedMesh = SaveMesh(rest, "CabInterior_NoWheel");
        renderer.sharedMaterials = restMats;

        var wheelGo = new GameObject("WheelMesh");
        wheelGo.transform.SetParent(pivot, false);
        wheelGo.AddComponent<MeshFilter>().sharedMesh = SaveMesh(wheel, "SteeringWheel");
        wheelGo.AddComponent<MeshRenderer>().sharedMaterials = wheelMats;

        // Instrument cluster: the glass piece just ahead of the wheel. The dials are drawn on a quad over it.
        var glassIndex = System.Array.FindIndex(mats, m => m != null && m.name.Contains("Glass_windows"));
        var subOf = SubmeshOfVertex(src);
        var glass = parts.Where(g => subOf[g[0]] == glassIndex)
            .OrderBy(g => Vector3.Distance(PartBounds(g).center, centre)).First();
        Vector3 glassNormal = PlaneNormal(glass.Select(i => p[i]));
        if (glassNormal.z < 0f) glassNormal = -glassNormal;   // away from the driver
        Quaternion gFrame = Quaternion.LookRotation(glassNormal, Vector3.up);
        FrameExtents(glass.Select(i => p[i]), gFrame, out Vector3 gCentre, out Vector3 gSize);
        clusterPose = new Pose(gCentre, gFrame);
        clusterSize = new Vector2(gSize.x, gSize.y);
        log.AppendLine($"Instrument cluster glass: centre {gCentre:F3}, size {gSize.x:0.###} x {gSize.y:0.###} m, tilt {Vector3.Angle(glassNormal, Vector3.forward):0}°");
        return pivot;
    }

    // Connected pieces of a mesh: vertices joined by triangles, plus vertices sharing a position (UV/normal seams).
    static int[] MeshComponents(Mesh mesh)
    {
        var v = mesh.vertices;
        var parent = Enumerable.Range(0, v.Length).ToArray();
        int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
        void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[a] = b; }
        var weld = new System.Collections.Generic.Dictionary<Vector3Int, int>();
        for (int i = 0; i < v.Length; i++)
        {
            var key = Vector3Int.RoundToInt(v[i] * 10000f);
            if (weld.TryGetValue(key, out int j)) Union(i, j); else weld[key] = i;
        }
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            var t = mesh.GetTriangles(s);
            for (int i = 0; i < t.Length; i += 3) { Union(t[i], t[i + 1]); Union(t[i], t[i + 2]); }
        }
        return Enumerable.Range(0, v.Length).Select(Find).ToArray();
    }

    static int[] SubmeshOfVertex(Mesh mesh)
    {
        var sub = new int[mesh.vertexCount];
        for (int s = 0; s < mesh.subMeshCount; s++)
            foreach (int i in mesh.GetTriangles(s)) sub[i] = s;
        return sub;
    }

    // Copies the triangles whose vertices are (keep == true) or aren't (keep == false) selected into a compact new mesh.
    // Empty submeshes are dropped together with their materials.
    static Mesh SubsetMesh(Mesh src, bool[] selected, bool keep, System.Func<Vector3, Vector3> mapPoint, Material[] mats, out Material[] outMats,
        System.Func<Vector3, Vector3> mapDir = null)
    {
        var verts = src.vertices; var normals = src.normals; var tangents = src.tangents;
        var uv0 = new System.Collections.Generic.List<Vector2>(); src.GetUVs(0, uv0);
        var uv1 = new System.Collections.Generic.List<Vector2>(); src.GetUVs(1, uv1);
        var colors = src.colors;
        var remap = new int[verts.Length];
        for (int i = 0; i < remap.Length; i++) remap[i] = -1;
        var order = new System.Collections.Generic.List<int>();
        var subs = new System.Collections.Generic.List<int[]>();
        var subMats = new System.Collections.Generic.List<Material>();
        for (int s = 0; s < src.subMeshCount; s++)
        {
            var tris = src.GetTriangles(s);
            var outTris = new System.Collections.Generic.List<int>();
            for (int i = 0; i < tris.Length; i += 3)
            {
                if (selected[tris[i]] != keep) continue;   // pieces are all-or-nothing, so one vertex decides
                for (int k = 0; k < 3; k++)
                {
                    int vi = tris[i + k];
                    if (remap[vi] < 0) { remap[vi] = order.Count; order.Add(vi); }
                    outTris.Add(remap[vi]);
                }
            }
            if (outTris.Count == 0) continue;
            subs.Add(outTris.ToArray());
            subMats.Add(s < mats.Length ? mats[s] : null);
        }

        var m = new Mesh { indexFormat = order.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        m.SetVertices(order.Select(i => mapPoint != null ? mapPoint(verts[i]) : verts[i]).ToArray());
        if (normals.Length > 0) m.SetNormals(order.Select(i => mapDir != null ? mapDir(normals[i]).normalized : normals[i]).ToArray());
        if (tangents.Length > 0) m.SetTangents(order.Select(i =>
        {
            Vector4 t = tangents[i];
            Vector3 d = mapDir != null ? mapDir(t).normalized : (Vector3)t;
            return new Vector4(d.x, d.y, d.z, t.w);
        }).ToArray());
        if (uv0.Count > 0) m.SetUVs(0, order.Select(i => uv0[i]).ToList());
        if (uv1.Count > 0) m.SetUVs(1, order.Select(i => uv1[i]).ToList());
        if (colors.Length > 0) m.SetColors(order.Select(i => colors[i]).ToArray());
        m.subMeshCount = subs.Count;
        for (int s = 0; s < subs.Count; s++) m.SetTriangles(subs[s], s);
        m.RecalculateBounds();
        outMats = subMats.ToArray();
        return m;
    }

    static Mesh SaveMesh(Mesh m, string name)
    {
        string path = $"{GenDir}/{name}.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    // Normal of the best-fit plane: the cross product of the two main axes of the point cloud (power iteration).
    static Vector3 PlaneNormal(System.Collections.Generic.IEnumerable<Vector3> points)
    {
        var pts = points.ToArray();
        Vector3 mean = Vector3.zero;
        foreach (var q in pts) mean += q;
        mean /= pts.Length;
        var c = new float[3, 3];
        foreach (var q in pts)
        {
            Vector3 d = q - mean;
            for (int a = 0; a < 3; a++) for (int b = 0; b < 3; b++) c[a, b] += d[a] * d[b];
        }
        Vector3 Mul(Vector3 v) => new Vector3(
            c[0, 0] * v.x + c[0, 1] * v.y + c[0, 2] * v.z,
            c[1, 0] * v.x + c[1, 1] * v.y + c[1, 2] * v.z,
            c[2, 0] * v.x + c[2, 1] * v.y + c[2, 2] * v.z);
        Vector3 Top(Vector3 v, Vector3 exclude)
        {
            for (int k = 0; k < 100; k++) { v = Mul(v); v -= Vector3.Dot(v, exclude) * exclude; v.Normalize(); }
            return v;
        }
        Vector3 e1 = Top(new Vector3(1f, 0.31f, 0.17f).normalized, Vector3.zero);
        Vector3 e2 = Top(new Vector3(0.23f, 1f, 0.41f).normalized, e1);
        return Vector3.Cross(e1, e2).normalized;
    }

    // Box around the points in a rotated frame: its centre (back in the original space) and size (in the frame).
    static void FrameExtents(System.Collections.Generic.IEnumerable<Vector3> points, Quaternion frame, out Vector3 centre, out Vector3 size)
    {
        Quaternion inv = Quaternion.Inverse(frame);
        var pts = points.Select(q => inv * q).ToArray();
        var b = new Bounds(pts[0], Vector3.zero);
        foreach (var q in pts) b.Encapsulate(q);
        centre = frame * b.center;
        size = b.size;
    }

    // Two dials (engine speed 0–3000 rpm, road speed 0–60 km/h) on a generated texture + needles + a digital line.
    static void BuildDashboard(Transform root, Pose glass, Vector2 glassSize, TractorController ctrl, Drivetrain drivetrain, StringBuilder log)
    {
        float h = glassSize.y, w = Mathf.Max(glassSize.x, h * 2.2f);
        var dash = new GameObject("Dashboard").transform;
        dash.SetParent(root, false);
        // A few millimetres towards the driver, in front of the glass.
        dash.SetLocalPositionAndRotation(glass.position - glass.rotation * Vector3.forward * 0.004f, glass.rotation);

        var unlit = Shader.Find("Universal Render Pipeline/Unlit");
        var face = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.DestroyImmediate(face.GetComponent<Collider>());
        face.name = "Face";
        face.transform.SetParent(dash, false);
        face.transform.localScale = new Vector3(w, h, 1f);
        var faceMat = new Material(unlit);
        faceMat.SetTexture("_BaseMap", DashTexture(w / h));
        face.GetComponent<MeshRenderer>().sharedMaterial = SaveMat(faceMat, "DashFace");
        face.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

        var needleMat = SaveMat(new Material(unlit), "DashNeedle");
        needleMat.SetColor("_BaseColor", new Color(1f, 0.45f, 0.1f));
        float dialR = h * 0.45f;
        Transform Needle(string name, float x)
        {
            var pivot = new GameObject(name).transform;
            pivot.SetParent(dash, false);
            pivot.localPosition = new Vector3(x, 0f, -0.001f);
            var n = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(n.GetComponent<Collider>());
            n.transform.SetParent(pivot, false);
            n.transform.localPosition = new Vector3(0f, dialR * 0.4f, 0f);
            n.transform.localScale = new Vector3(dialR * 0.06f, dialR * 0.9f, 1f);
            n.GetComponent<MeshRenderer>().sharedMaterial = needleMat;
            n.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            return pivot;
        }
        float dialX = w * 0.5f - h * 0.5f;
        var board = dash.gameObject.AddComponent<Dashboard>();
        board.tractor = ctrl;
        board.drivetrain = drivetrain;
        board.rpmNeedle = Needle("RpmNeedle", -dialX);
        board.speedNeedle = Needle("SpeedNeedle", dialX);

        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var textMat = new Material(Shader.Find("TractorVR/WorldText"));
        textMat.mainTexture = font.material.mainTexture;   // for the preview renders; Dashboard re-assigns it at runtime
        board.textMaterial = SaveMat(textMat, "DashText");
        TextMesh Label(string text, Vector3 pos, float height, Color color)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(dash, false);
            go.transform.localPosition = pos + new Vector3(0f, 0f, -0.0015f);
            var tm = go.AddComponent<TextMesh>();
            tm.font = font;
            tm.fontSize = 48;
            tm.characterSize = height / (tm.fontSize * 0.1f);   // TextMesh glyph height ≈ characterSize × fontSize / 10
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            tm.text = text;
            go.GetComponent<MeshRenderer>().sharedMaterial = board.textMaterial;
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            return tm;
        }
        // Scale numbers just inside the tick ring.
        void Numbers(float x, int count, System.Func<int, string> text)
        {
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)(count - 1);
                float a = (-135f + 270f * t) * Mathf.Deg2Rad;
                Label(text(i), new Vector3(x + Mathf.Sin(a) * dialR * 0.62f, Mathf.Cos(a) * dialR * 0.62f, 0f), h * 0.075f, Color.white);
            }
        }
        Numbers(-dialX, 4, i => i.ToString());              // ×1000 rpm
        Numbers(dialX, 7, i => (i * 10).ToString());         // km/h
        var caption = new Color(0.7f, 0.7f, 0.7f);
        var digital = new Color(1f, 0.85f, 0.3f);
        Label("x1000", new Vector3(-dialX, -dialR * 0.3f, 0f), h * 0.055f, caption);
        Label("km/h", new Vector3(dialX, -dialR * 0.3f, 0f), h * 0.055f, caption);
        // From the seat the column shroud hides the lower half of the dials, so the readouts go in the upper half:
        // direction + gear in the rev counter, gearbox mode in the speedometer.
        board.gearText = Label("N B2", new Vector3(-dialX, dialR * 0.36f, 0f), h * 0.12f, digital);
        board.modeText = Label("AUTO", new Vector3(dialX, dialR * 0.36f, 0f), h * 0.09f, digital);
        log.AppendLine($"Dashboard {w:0.###} x {h:0.###} m at {dash.localPosition:F3}");
    }

    // Black face with two tick rings. Angle convention matches Dashboard.Needle: 0 at -135° (lower left), full scale at +135°.
    static Texture2D DashTexture(float aspect)
    {
        int th = 256, tw = Mathf.RoundToInt(th * aspect);
        var tex = new Texture2D(tw, th, TextureFormat.RGBA32, true);
        float r = th * 0.45f, cxL = th * 0.5f, cxR = tw - th * 0.5f, cy = th * 0.5f;
        var px = new Color[tw * th];
        Color bg = new Color(0.03f, 0.035f, 0.04f), tick = new Color(0.92f, 0.92f, 0.92f), red = new Color(0.9f, 0.12f, 0.08f);
        float TickCover(float ang, float step, float halfWidthDeg)
        {
            float t = (ang + 135f) / step;
            float d = Mathf.Abs(t - Mathf.Round(t)) * step;
            return d < halfWidthDeg ? 1f : 0f;
        }
        for (int y = 0; y < th; y++)
        for (int x = 0; x < tw; x++)
        {
            Color c = bg;
            foreach (var (cx, majorStep, minorStep, redFrom) in new[] { (cxL, 90f, 22.5f, 2250f / 3000f), (cxR, 45f, 22.5f, 2f) })
            {
                float dx = x - cx, dy = y - cy, rr = Mathf.Sqrt(dx * dx + dy * dy) / r;
                if (rr > 1.02f) continue;
                float ang = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg;   // clockwise from up
                bool onArc = ang >= -136f && ang <= 136f;
                if (rr > 0.97f && rr < 1.0f && onArc) c = tick * 0.6f;
                if (onArc && rr > 0.78f && rr < 0.97f && TickCover(ang, majorStep, 1.6f) > 0f) c = tick;
                else if (onArc && rr > 0.87f && rr < 0.97f && TickCover(ang, minorStep, 0.9f) > 0f) c = tick * 0.8f;
                if (onArc && rr > 0.9f && rr < 0.97f && (ang + 135f) / 270f >= redFrom) c = red;
                if (rr < 0.07f) c = new Color(0.25f, 0.25f, 0.25f);
            }
            px[y * tw + x] = c;
        }
        tex.SetPixels(px);
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.anisoLevel = 4;
        string path = GenDir + "/DashFace.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(tex, path);
        return tex;
    }

    // ------------------------------------------------------------------ audio
    // All clips are synthesised here and written as 16-bit WAVs to Assets/Generated/Audio (no third-party samples).
    const int Rate = 44100;

    static void BuildAudio(GameObject tractor, VRCameraRig rig)
    {
        Directory.CreateDirectory(GenDir + "/Audio");
        var (light, heavy) = EngineClips();
        var audio = tractor.AddComponent<TractorAudio>();
        audio.drivetrain = tractor.GetComponent<Drivetrain>();
        audio.input = tractor.GetComponent<TractorInput>();
        audio.rig = rig;

        AudioSource Source(string name, Vector3 pos, AudioClip clip, bool loop, float spatial)
        {
            var go = new GameObject(name);
            go.transform.SetParent(tractor.transform, false);
            go.transform.localPosition = pos;
            var s = go.AddComponent<AudioSource>();
            s.clip = clip;
            s.loop = loop;
            s.playOnAwake = false;
            s.spatialBlend = spatial;
            s.dopplerLevel = 0f;
            s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.minDistance = 4f;
            s.maxDistance = 80f;
            return s;
        }
        Vector3 engine = new Vector3(0f, 1.64f, 2.56f);
        audio.engineLight = Source("EngineLight", engine, light, true, 1f);
        audio.engineHeavy = Source("EngineHeavy", engine, heavy, true, 1f);
        audio.lightFilter = audio.engineLight.gameObject.AddComponent<AudioLowPassFilter>();
        audio.heavyFilter = audio.engineHeavy.gameObject.AddComponent<AudioLowPassFilter>();
        audio.shift = Source("ShiftSound", new Vector3(0.35f, 1.9f, 0.6f), ShiftClip(), false, 0.6f);
        audio.beeper = Source("ReverseBeeper", new Vector3(0f, 1.6f, -0.8f), BeeperClip(), true, 1f);
        audio.beeper.volume = 0.35f;
        audio.horn = Source("Horn", new Vector3(0f, 1.6f, 3.4f), HornClip(), true, 1f);
    }

    // Diesel engine recorded at 1000 rpm: a six-cylinder four-stroke fires 3× per revolution (50 Hz).
    // Each firing excites exhaust/block resonances and a short clatter burst. The cylinders differ slightly in
    // strength, which gives the uneven diesel beat. The loop is exactly 10 engine cycles (1.2 s), so it repeats seamlessly.
    static (AudioClip light, AudioClip heavy) EngineClips()
    {
        const float rpm = 1000f;
        float cycle = 120f / rpm;                       // s per four-stroke cycle
        int n = Mathf.RoundToInt(Rate * cycle * 10f);   // 52920 samples
        int firing = n / 60;                            // 882 samples between firings
        float[] strength = { 1.0f, 0.86f, 1.08f, 0.93f, 1.12f, 0.9f };
        var rnd = new System.Random(11);
        var noise = new float[n];
        for (int i = 0; i < n; i++) noise[i] = (float)rnd.NextDouble() * 2f - 1f;

        float[] Synth(float heavy)
        {
            var exhaust = new Biquad(85f, 3f);
            var body = new Biquad(240f, 5f);
            var block = new Biquad(700f, 6f);
            var clatter = new Biquad(3200f, 2.5f);
            var breath = new Biquad(1400f, 1.2f);
            var outp = new float[n];
            for (int pass = 0; pass < 2; pass++)        // first pass settles the filters, the second is the loop
            for (int i = 0; i < n; i++)
            {
                int k = i / firing;
                float tau = (i - k * firing) / (float)Rate;
                float a = strength[k % 6];
                float pulse = a * Mathf.Exp(-tau / (heavy > 0.5f ? 0.0025f : 0.0015f));
                float knock = a * noise[i] * Mathf.Exp(-tau / 0.0006f);
                float hiss = noise[(i * 7) % n] * a * Mathf.Exp(-tau / 0.006f);
                float turbo = Mathf.Sin(2f * Mathf.PI * 2500f * i / Rate);   // 3000 whole cycles per loop
                float y = exhaust.Run(pulse) * 1.0f
                        + body.Run(pulse) * (0.55f + 0.3f * heavy)
                        + block.Run(pulse) * (0.25f + 0.35f * heavy)
                        + clatter.Run(knock) * (1.4f + 0.4f * heavy)   // the diesel "clatter": carries on small speakers
                        + breath.Run(hiss) * (0.12f + 0.4f * heavy)
                        + turbo * 0.012f * heavy;
                if (pass == 1) outp[i] = y;
            }
            return outp;
        }
        var lightS = Synth(0f);
        var heavyS = Synth(1f);
        // Normalised separately: the heavy loop is only a little louder. TractorAudio sets the overall level.
        return (WriteClip("EngineLight", Normalize(lightS, 0.75f)), WriteClip("EngineHeavy", Normalize(heavyS, 0.9f)));
    }

    // Powershift clutch change: a dull thump, a click and a short hydraulic hiss.
    static AudioClip ShiftClip()
    {
        int n = Rate * 3 / 10;
        var rnd = new System.Random(3);
        var click = new Biquad(2000f, 2f);
        var hiss = new Biquad(4500f, 1.5f);
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate, wn = (float)rnd.NextDouble() * 2f - 1f;
            float h = (t - 0.08f) / 0.05f;
            s[i] = Mathf.Sin(2f * Mathf.PI * 55f * t) * Mathf.Exp(-t / 0.05f) * 0.8f
                 + click.Run(wn * Mathf.Exp(-t / 0.004f)) * 1.5f
                 + hiss.Run(wn) * 0.25f * Mathf.Exp(-h * h);
        }
        return WriteClip("Shift", Normalize(s, 0.8f));
    }

    // Reverse alarm: 1 kHz, half a second on, half off (the loop is one beep).
    static AudioClip BeeperClip()
    {
        var s = new float[Rate];
        for (int i = 0; i < Rate / 2; i++)
        {
            float t = i / (float)Rate;
            float env = Mathf.Min(1f, Mathf.Min(t, 0.5f - t) / 0.005f);
            float w = 2f * Mathf.PI * 1000f * t;
            s[i] = env * (Mathf.Sin(w) + Mathf.Sin(3f * w) / 3f + Mathf.Sin(5f * w) / 5f);
        }
        return WriteClip("ReverseBeeper", Normalize(s, 0.7f));
    }

    // Two-tone electric horn (400 + 500 Hz, band-limited sawtooth). Whole cycles per second, so the 1 s clip loops.
    static AudioClip HornClip()
    {
        var s = new float[Rate];
        for (int i = 0; i < Rate; i++)
        {
            float t = i / (float)Rate, y = 0f;
            foreach (float f in new[] { 400f, 500f })
                for (int k = 1; k <= 7; k++) y += Mathf.Sin(2f * Mathf.PI * f * k * t) / Mathf.Pow(k, 1.3f);
            s[i] = y;
        }
        return WriteClip("Horn", Normalize(s, 0.7f));
    }

    static float[] Normalize(float[] s, float peak)
    {
        float m = s.Max(Mathf.Abs);
        for (int i = 0; i < s.Length; i++) s[i] *= peak / m;
        return s;
    }

    static AudioClip WriteClip(string name, float[] samples)
    {
        string path = $"{GenDir}/Audio/{name}.wav";
        using (var w = new BinaryWriter(File.Create(path)))
        {
            int bytes = samples.Length * 2;
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes); w.Write(Encoding.ASCII.GetBytes("WAVE"));
            w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(bytes);
            foreach (float f in samples) w.Write((short)Mathf.RoundToInt(Mathf.Clamp(f, -1f, 1f) * 32767f));
        }
        AssetDatabase.ImportAsset(path);
        var imp = (AudioImporter)AssetImporter.GetAtPath(path);
        imp.forceToMono = true;
        var settings = imp.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat = AudioCompressionFormat.PCM;   // short clips; PCM loops without encoder gaps
        imp.defaultSampleSettings = settings;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }

    // RBJ band-pass (0 dB peak gain).
    class Biquad
    {
        readonly float b0, b2, a1, a2;
        float x1, x2, y1, y2;
        public Biquad(float f, float q)
        {
            float w0 = 2f * Mathf.PI * f / Rate, alpha = Mathf.Sin(w0) / (2f * q), a0 = 1f + alpha;
            b0 = alpha / a0; b2 = -alpha / a0; a1 = -2f * Mathf.Cos(w0) / a0; a2 = (1f - alpha) / a0;
        }
        public float Run(float x)
        {
            float y = b0 * x + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1; x1 = x; y2 = y1; y1 = y;
            return y;
        }
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
        hud.drivetrain = tractor.GetComponent<Drivetrain>();

        hud.comfort = BuildVignette(camGo.transform, ctrl, vr);
        var controllerMat = ControllerMaterial();
        BuildController(offset, true, controllerMat);
        BuildController(offset, false, controllerMat);
        BuildAudio(tractor, vr);

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
    // Official Quest 2 Touch models from the Meta XR Core SDK (see Assets/Models/MetaControllers/README.md).
    // They are authored in Meta's pointer frame (origin on the face plate, +Z along the laser), so they are
    // driven by the OpenXR aim pose (pointerPosition/pointerRotation) and offset so that the model's
    // *_laser_begin node lands exactly on that pose.
    const string ControllerDir = "Assets/Models/MetaControllers";

    static Material ControllerMaterial()
    {
        foreach (var side in new[] { "Left", "Right" })
        {
            var imp = (ModelImporter)AssetImporter.GetAtPath($"{ControllerDir}/OculusTouchForQuest2_{side}.fbx");
            imp.importAnimation = false;
            imp.importCameras = false;
            imp.importLights = false;
            imp.animationType = ModelImporterAnimationType.None; // bones stay plain transforms, no Animator
            imp.materialImportMode = ModelImporterMaterialImportMode.None;
            imp.SaveAndReimport();
        }
        var mat = new Material(LitShader);
        mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{ControllerDir}/OculusTouchForQuest2_AlbedoRoughness.tga"));
        mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_Smoothness", 0.35f);
        return SaveMat(mat, "Controller_Quest2");
    }

    static void BuildController(Transform cameraOffset, bool left, Material mat)
    {
        string hand = left ? "LeftHand" : "RightHand";
        var root = new GameObject(left ? "Left Controller" : "Right Controller");
        root.transform.SetParent(cameraOffset, false);
        var tpd = root.AddComponent<TrackedPoseDriver>();
        tpd.positionInput = new InputActionProperty(new InputAction(hand + "Pos", InputActionType.Value, $"<XRController>{{{hand}}}/pointerPosition", expectedControlType: "Vector3"));
        tpd.rotationInput = new InputActionProperty(new InputAction(hand + "Rot", InputActionType.Value, $"<XRController>{{{hand}}}/pointerRotation", expectedControlType: "Quaternion"));
        tpd.trackingStateInput = new InputActionProperty(new InputAction(hand + "State", InputActionType.Value, $"<XRController>{{{hand}}}/trackingState", expectedControlType: "Integer"));

        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{ControllerDir}/OculusTouchForQuest2_{(left ? "Left" : "Right")}.fbx");
        var model = Object.Instantiate(fbx, root.transform);
        model.name = "Model";
        foreach (var r in model.GetComponentsInChildren<Renderer>())
        {
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        Transform Bone(string name) => model.GetComponentsInChildren<Transform>().First(t => t.name == name);
        Transform laser = model.GetComponentsInChildren<Transform>().First(t => t.name.EndsWith("_laser_begin"));
        Quaternion laserRot = Quaternion.Inverse(model.transform.rotation) * laser.rotation;
        Vector3 laserPos = Vector3.Scale(model.transform.InverseTransformPoint(laser.position), model.transform.localScale);
        model.transform.localRotation = Quaternion.Inverse(laserRot);
        model.transform.localPosition = -(model.transform.localRotation * laserPos);

        var vis = root.AddComponent<ControllerVisual>();
        vis.leftHand = left;
        vis.stick = Bone("b_thumbstick");
        vis.primaryButton = Bone(left ? "b_button_x" : "b_button_a");
        vis.secondaryButton = Bone(left ? "b_button_y" : "b_button_b");
        vis.trigger = Bone("b_trigger_front");
        vis.grip = Bone("b_trigger_grip");
        // Meta's press animation moves A/B down 1.78 mm; the buttons are 15.53 mm apart. Scale to bone-local units.
        vis.buttonTravel = 1.78f / 15.53f * Vector3.Distance(vis.primaryButton.localPosition, vis.secondaryButton.localPosition);
    }

    // ------------------------------------------------------------------ preview renders (batch mode QA)
    [MenuItem("Tractor VR/Render Preview Images")]
    public static void RenderPreviews()
    {
        EditorSceneManager.OpenScene(ScenePath);
        ShaderUtil.allowAsyncCompilation = false; // otherwise first renders show placeholder shaders
        var tractor = GameObject.Find("Tractor").transform;
        var seat = tractor.Find("DriverEye");
        Object.FindAnyObjectByType<TerrainGenerator>().Build();   // normally built at runtime (not saved in the scene)
        var cam = Camera.main;
        cam.fieldOfView = 90f;
        cam.farClipPlane = 1500f;
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
        // Overview of the hills and the hay bales from behind and above the start.
        Shot("hills.png", new Vector3(-25f, 22f, -45f), Quaternion.Euler(12f, 25f, 0f));

        // Instrument cluster from the seat: needles at 1500 rpm / 20 km/h, steering wheel turned 90° for a right turn.
        var board = Object.FindAnyObjectByType<Dashboard>();
        var wheel = tractor.GetComponent<TractorController>().steeringWheel;
        board.rpmNeedle.localRotation = Quaternion.Euler(0f, 0f, 135f - 1500f / board.rpmMax * 270f);
        board.speedNeedle.localRotation = Quaternion.Euler(0f, 0f, 135f - 20f / board.speedMax * 270f);
        board.gearText.text = "F B3";
        wheel.localRotation = Quaternion.Euler(0f, 0f, -90f);
        cam.fieldOfView = 55f;
        Shot("dash.png", seat.position, Quaternion.LookRotation(board.transform.position - seat.position));
        cam.fieldOfView = 90f;
        wheel.localRotation = Quaternion.identity;

        // Both controllers held in front of the driver, pointing forward and slightly down (aim pose).
        leftCtl.SetActive(true);
        ctl.gameObject.SetActive(true);
        Vector3 hands = seat.position + new Vector3(0f, -0.3f, 0.35f);
        leftCtl.transform.SetPositionAndRotation(hands + new Vector3(-0.1f, 0f, 0f), Quaternion.Euler(15f, 5f, 0f));
        ctl.SetPositionAndRotation(hands + new Vector3(0.1f, 0f, 0f), Quaternion.Euler(15f, -5f, 0f));
        Vector3 eye = hands + new Vector3(0f, 0.2f, -0.25f);
        Shot("controller.png", eye, Quaternion.LookRotation(hands - eye));
        Vector3 side = hands + new Vector3(0.3f, 0.02f, 0.02f);
        Shot("controller_side.png", side, Quaternion.LookRotation(hands + new Vector3(0.1f, 0f, 0f) - side));
        leftCtl.SetActive(false);

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
