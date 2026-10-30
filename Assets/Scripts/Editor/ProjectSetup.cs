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

    // How eagerly mesh LODs switch to less detail (higher = sooner). Tuned with the forced-LOD preview renders.
    const float MeshLodThreshold = 1.5f;

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
        urp.mainLightShadowmapResolution = 1024;   // Quest GPU budget (hard shadows; soft shadows are off)
        urp.supportsCameraDepthTexture = false;
        urp.supportsCameraOpaqueTexture = false;
        EditorUtility.SetDirty(urp);

        GraphicsSettings.defaultRenderPipeline = urp;
        int current = QualitySettings.GetQualityLevel();
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = urp;
            QualitySettings.meshLodThreshold = MeshLodThreshold;
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
                bool want = n == "OculusTouchControllerProfile" || n == "MetaQuestTouchPlusControllerProfile" || n == "HandTracking"
                            || (group == BuildTargetGroup.Android && n == "FoveatedRenderingFeature")
                            || (group == BuildTargetGroup.Android && n == "MetaQuestFeature");
                if (want && !f.enabled) f.enabled = true;
                if (want && n == "FoveatedRenderingFeature")
                {
                    // Fixed foveation (the Quest 2 has no eye tracking: no eye-tracking permission); subsampled layout is a Vulkan speed-up.
                    var so = new SerializedObject(f);
                    so.FindProperty("useEyeTracking").boolValue = false;
                    so.FindProperty("enableSubsampledLayout").boolValue = true;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
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
        // Quest GPU budget: the tractor is ~750k triangles. Mesh LODs let distant / small parts draw fewer.
        imp.generateMeshLods = true;
        imp.maximumMeshLod = 4;
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
        BuildImplements(tractor.GetComponent<Hitch>(), log);
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

    // Rolling terrain with fields, roads and the farmyard (built at runtime by TerrainGenerator), farm buildings and hay bales.
    static void BuildGround()
    {
        // Natural ground: grass / soil / mud textures for the terrain (GroundLit), gravel / asphalt / concrete for the
        // tracks, road and yard (TriplanarLit). All generated here; the layout is WorldLayout, the surface types GroundMap.
        foreach (var old in new[] { GenDir + "/GroundGrid.asset", GenDir + "/Post.mat" }) AssetDatabase.DeleteAsset(old);
        var tex = GroundTextures();
        var ground = new Material(Shader.Find("TractorVR/GroundLit"));
        ground.SetTexture("_GrassTex", tex["Grass"].albedo); ground.SetTexture("_GrassN", tex["Grass"].normal);
        ground.SetTexture("_SoilTex", tex["Soil"].albedo); ground.SetTexture("_SoilN", tex["Soil"].normal);
        ground.SetTexture("_MudTex", tex["Mud"].albedo); ground.SetTexture("_MudN", tex["Mud"].normal);
        ground.SetTexture("_Macro", tex["Macro"].albedo);
        ground.SetVector("_Tiling", new Vector4(1f / 2f, 1f / 2f, 1f / 3f, 1f / 64f));
        ground.SetVector("_Smooth", new Vector4(0.08f, 0.05f, 0.28f, 0f));   // mud: damp, but not a mirror
        SaveMat(ground, "Ground");

        Material Surface(string name, float tile, float smooth)
        {
            var m = new Material(Shader.Find("TractorVR/TriplanarLit"));
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_DetailMap", tex[name].albedo);
            m.SetTexture("_NormalMap", tex[name].normal);
            m.SetFloat("_NormalStrength", 1f);
            m.SetFloat("_Tiling", 1f / tile);
            m.SetFloat("_Smoothness", smooth);
            return SaveMat(m, "Ground_" + name);
        }

        var terrain = new GameObject("Terrain").AddComponent<TerrainGenerator>();
        terrain.material = ground;
        terrain.gravel = Surface("Gravel", 1.5f, 0.1f);
        terrain.asphalt = Surface("Asphalt", 2f, 0.2f);
        terrain.concrete = Surface("Concrete", 4f, 0.15f);
        terrain.gameObject.isStatic = true;

        BuildFarmyard();

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

    // ------------------------------------------------------------------ ground textures + farmyard
    // Tileable 512² textures (albedo + normal map from a height field), written as PNGs to Generated/Textures.
    static System.Collections.Generic.Dictionary<string, (Texture2D albedo, Texture2D normal)> GroundTextures()
    {
        var result = new System.Collections.Generic.Dictionary<string, (Texture2D, Texture2D)>();
        // Cellular (Worley) noise that tiles: distance to the nearest of one random point per cell (0 at a point).
        float Worley(float u, float v, int cells, int seed)
        {
            float x = u * cells, y = v * cells, best = 9f;
            int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int gx = cx + dx, gy = cy + dy;
                int wx = ((gx % cells) + cells) % cells, wy = ((gy % cells) + cells) % cells;
                float px = gx + Hash(wx, wy, seed), py = gy + Hash(wx, wy, seed + 17);
                best = Mathf.Min(best, (px - x) * (px - x) + (py - y) * (py - y));
            }
            return Mathf.Sqrt(best);
        }
        int CellId(float u, float v, int cells, int seed)
        {
            float x = u * cells, y = v * cells, best = 9f; int id = 0;
            int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int gx = cx + dx, gy = cy + dy;
                int wx = ((gx % cells) + cells) % cells, wy = ((gy % cells) + cells) % cells;
                float px = gx + Hash(wx, wy, seed), py = gy + Hash(wx, wy, seed + 17);
                float d = (px - x) * (px - x) + (py - y) * (py - y);
                if (d < best) { best = d; id = wx * 977 + wy; }
            }
            return id;
        }

        void Make(string name, float bump, System.Func<float, float, (Color c, float h)> f, int n = 512)
        {
            Directory.CreateDirectory(TexDir);
            var cols = new Color[n * n];
            var h = new float[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var (c, hh) = f(x / (float)n, y / (float)n);
                cols[y * n + x] = c; h[y * n + x] = hh;
            }
            var albedo = new Texture2D(n, n, TextureFormat.RGB24, false);
            albedo.SetPixels(cols);
            var normal = new Texture2D(n, n, TextureFormat.RGB24, false);
            var np = new Color[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = h[y * n + (x + 1) % n] - h[y * n + (x + n - 1) % n];
                float dy = h[((y + 1) % n) * n + x] - h[((y + n - 1) % n) * n + x];
                Vector3 nn = new Vector3(-dx * bump, -dy * bump, 1f).normalized;
                np[y * n + x] = new Color(nn.x * 0.5f + 0.5f, nn.y * 0.5f + 0.5f, nn.z * 0.5f + 0.5f);
            }
            normal.SetPixels(np);
            Texture2D Write(Texture2D t, string file, bool isNormal)
            {
                string path = $"{TexDir}/{file}.png";
                File.WriteAllBytes(path, t.EncodeToPNG());
                AssetDatabase.ImportAsset(path);
                var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                imp.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                imp.sRGBTexture = !isNormal;
                imp.wrapMode = TextureWrapMode.Repeat;
                imp.mipmapEnabled = true;
                imp.anisoLevel = 8;
                imp.maxTextureSize = 512;
                imp.SaveAndReimport();
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            result[name] = (Write(albedo, "Ground_" + name, false), Write(normal, "Ground_" + name + "_N", true));
        }

        Make("Grass", 3f, (u, v) =>
        {
            float blades = TileNoise(u, v * 0.35f, 220, 21) * 0.6f + TileNoise(u, v, 140, 22) * 0.4f;
            float clumps = TileNoise(u, v, 18, 23), dry = Mathf.SmoothStep(0.55f, 0.85f, TileNoise(u, v, 7, 24));
            Color green = new Color(0.20f, 0.34f, 0.10f) * (0.7f + 0.6f * blades) * (0.85f + 0.3f * clumps);
            Color c = Color.Lerp(green, new Color(0.42f, 0.40f, 0.20f) * (0.8f + 0.4f * blades), 0.35f * dry);
            return (c, blades * 0.7f + clumps * 0.3f);
        });
        Make("Soil", 6f, (u, v) =>
        {
            float clod = 1f - Mathf.Clamp01(Worley(u, v, 28, 31) * 1.4f);
            float fine = TileNoise(u, v, 160, 32);
            bool stone = Hash(Mathf.FloorToInt(u * 300f), Mathf.FloorToInt(v * 300f), 33) > 0.995f;
            Color c = new Color(0.33f, 0.24f, 0.16f) * (0.65f + 0.45f * clod + 0.15f * fine);
            if (stone) c = new Color(0.55f, 0.52f, 0.47f);
            return (c, clod * 0.7f + fine * 0.3f);
        });
        Make("Mud", 2f, (u, v) =>
        {
            float n1 = TileNoise(u, v, 6, 41), n2 = TileNoise(u, v, 40, 42);
            float wet = Mathf.SmoothStep(0.45f, 0.7f, n1);
            Color c = Color.Lerp(new Color(0.27f, 0.20f, 0.13f), new Color(0.17f, 0.12f, 0.08f), wet) * (0.85f + 0.3f * n2);
            return (c, n1 * 0.5f + n2 * 0.5f);
        });
        Make("Gravel", 8f, (u, v) =>
        {
            float d = Worley(u, v, 48, 51);
            float stone = Mathf.Clamp01(1f - d * 1.6f);
            float tint = Hash(CellId(u, v, 48, 51), 0, 52);
            Color sc = Color.Lerp(new Color(0.46f, 0.44f, 0.40f), new Color(0.62f, 0.58f, 0.52f), tint);
            Color c = Color.Lerp(new Color(0.22f, 0.20f, 0.18f), sc, Mathf.SmoothStep(0.05f, 0.35f, stone));
            return (c, stone);
        });
        Make("Asphalt", 3f, (u, v) =>
        {
            float fine = TileNoise(u, v, 256, 61), agg = Hash(Mathf.FloorToInt(u * 512f), Mathf.FloorToInt(v * 512f), 62);
            float patch = TileNoise(u, v, 5, 63);
            Color c = new Color(0.17f, 0.17f, 0.18f) * (0.8f + 0.3f * fine + 0.15f * patch);
            if (agg > 0.97f) c = new Color(0.36f, 0.35f, 0.33f);
            return (c, fine * 0.6f + (agg > 0.97f ? 0.4f : 0f));
        });
        Make("Concrete", 4f, (u, v) =>
        {
            float stain = TileNoise(u, v, 6, 71), fine = TileNoise(u, v, 200, 72);
            // One 4 m slab per tile with a 1.5 cm joint along two edges.
            bool joint = u < 0.004f || v < 0.004f;
            Color c = new Color(0.47f, 0.46f, 0.44f) * (0.92f + 0.08f * stain + 0.06f * fine);
            if (joint) c *= 0.6f;
            return (c, joint ? 0f : 0.6f + 0.4f * fine * 0.3f);
        });
        Make("Macro", 0f, (u, v) =>
        {
            float m = TileNoise(u, v, 4, 81) * 0.6f + TileNoise(u, v, 9, 82) * 0.4f;
            return (new Color(m, m, m), 0f);
        }, 256);
        return result;
    }

    // Barn on the west side of the yard (doors facing the yard) and an open machine shed along the south side.
    static void BuildFarmyard()
    {
        var parent = new GameObject("Farmyard").transform;
        parent.gameObject.isStatic = true;
        var boards = SaveMat(new Material(LitShader) { color = new Color(0.42f, 0.12f, 0.08f) }, "Barn_Boards");
        boards.SetFloat("_Smoothness", 0.15f);
        var roof = SaveMat(new Material(LitShader) { color = new Color(0.26f, 0.27f, 0.28f) }, "Barn_Roof");
        roof.SetFloat("_Smoothness", 0.35f); roof.SetFloat("_Metallic", 0.4f);
        var door = SaveMat(new Material(LitShader) { color = new Color(0.30f, 0.09f, 0.06f) }, "Barn_Door");
        var trim = SaveMat(new Material(LitShader) { color = new Color(0.85f, 0.84f, 0.80f) }, "Barn_Trim");
        var shedWall = SaveMat(new Material(LitShader) { color = new Color(0.33f, 0.40f, 0.35f) }, "Shed_Wall");
        shedWall.SetFloat("_Smoothness", 0.3f); shedWall.SetFloat("_Metallic", 0.3f);
        var steelMat = SaveMat(new Material(LitShader) { color = new Color(0.45f, 0.46f, 0.47f) }, "Shed_Steel");

        Transform Block(string name, Vector3 centre, Vector3 size, Material m, Vector3 euler = default, bool solid = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = centre;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = m;
            if (!solid) Object.DestroyImmediate(go.GetComponent<Collider>());
            go.isStatic = true;
            return go.transform;
        }

        // Barn: 14 × 24 m, 6 m walls, gable roof (ridge 9 m) along its length.
        var b = WorldLayout.Barn;
        Vector2 bc = b.Centre; float bw = b.x1 - b.x0, bl = b.z1 - b.z0;
        Block("Barn walls", new Vector3(bc.x, 3f, bc.y), new Vector3(bw, 6f, bl), boards);
        float rise = 3f, half = bw * 0.5f + 0.4f, slope = Mathf.Atan2(rise, bw * 0.5f) * Mathf.Rad2Deg;
        float plane = Mathf.Sqrt(half * half + rise * rise);
        Block("Barn roof W", new Vector3(bc.x - half * 0.5f, 6f + rise * 0.5f, bc.y), new Vector3(plane, 0.2f, bl + 0.8f), roof, new Vector3(0f, 0f, slope), false);
        Block("Barn roof E", new Vector3(bc.x + half * 0.5f, 6f + rise * 0.5f, bc.y), new Vector3(plane, 0.2f, bl + 0.8f), roof, new Vector3(0f, 0f, -slope), false);
        Block("Barn gable N", new Vector3(bc.x, 7.4f, b.z1 - 0.05f), new Vector3(bw * 0.7f, 2.8f, 0.1f), boards, default, false);
        Block("Barn gable S", new Vector3(bc.x, 7.4f, b.z0 + 0.05f), new Vector3(bw * 0.7f, 2.8f, 0.1f), boards, default, false);
        Block("Barn door", new Vector3(b.x1 + 0.06f, 2.4f, bc.y), new Vector3(0.1f, 4.8f, 5f), door, default, false);
        Block("Barn door trim", new Vector3(b.x1 + 0.08f, 2.4f, bc.y), new Vector3(0.06f, 0.15f, 5.2f), trim, new Vector3(45f, 0f, 0f), false);

        // Machine shed: back and side walls, a roof on posts, open to the yard (north).
        var s = WorldLayout.Shed;
        Vector2 sc = s.Centre; float sw = s.x1 - s.x0, sd = s.z1 - s.z0;
        Block("Shed back wall", new Vector3(sc.x, 2.5f, s.z0 + 0.15f), new Vector3(sw, 5f, 0.3f), shedWall);
        Block("Shed wall W", new Vector3(s.x0 + 0.15f, 2.5f, sc.y), new Vector3(0.3f, 5f, sd), shedWall);
        Block("Shed wall E", new Vector3(s.x1 - 0.15f, 2.5f, sc.y), new Vector3(0.3f, 5f, sd), shedWall);
        Block("Shed roof", new Vector3(sc.x, 5.3f, sc.y), new Vector3(sw + 1f, 0.2f, sd + 1.2f), roof, new Vector3(-6f, 0f, 0f));
        for (int i = 1; i < 4; i++)
            Block("Shed post", new Vector3(s.x0 + i * sw / 4f, 2.6f, s.z1 - 0.2f), new Vector3(0.25f, 5.2f, 0.25f), steelMat);
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
        // Clods thrown by spinning wheels: lit particle cubes, tinted per surface (vertex colour = particle colour).
        var dirt = new Material(Shader.Find("Universal Render Pipeline/Particles/Simple Lit"));
        dirt.SetColor("_BaseColor", Color.white);
        dirt.enableInstancing = true;
        ctrl.dirtMaterial = SaveMat(dirt, "WheelDirt");

        // Physics body (mass, centre of mass and interpolation are set by TractorController.Awake).
        // The collider starts 0.45 m above the ground so only the raycast wheels touch it; it's on the Ignore Raycast
        // layer so those rays don't hit the tractor itself.
        var rb = root.GetComponent<Rigidbody>();   // added with TractorController (RequireComponent)
        rb.isKinematic = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        // Main block from just behind the rear axle forward (full width: fenders, steps, mirrors), plus a narrow block for
        // the rear frame / hitch. A single full-width box out to the hitch stopped a coupled trailer from swinging past ~12°.
        const float clearance = 0.45f;
        Vector3 lo = root.transform.InverseTransformPoint(all.min), hi = root.transform.InverseTransformPoint(all.max);
        const float mainBack = -0.55f;
        var main = root.AddComponent<BoxCollider>();
        main.center = new Vector3(0f, (clearance + hi.y) * 0.5f, (mainBack + hi.z) * 0.5f);
        main.size = new Vector3(hi.x - lo.x, hi.y - clearance, hi.z - mainBack);
        var rearBlock = root.AddComponent<BoxCollider>();
        rearBlock.center = new Vector3(0f, (clearance + 2.2f) * 0.5f, (lo.z + mainBack) * 0.5f);
        rearBlock.size = new Vector3(1.0f, 2.2f - clearance, mainBack - lo.z);
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
        ctrl.steeringWheel = SplitSteeringWheel(root.transform, Part("Cab_Interier_01"), log, out Pose clusterPose, out Vector2 clusterSize,
            out Transform speedLever, out Vector3 leverKnob, out CabControls.Swing[] remotes);
        var cabControls = root.AddComponent<CabControls>();
        cabControls.speedLever = speedLever;
        cabControls.knobLocal = speedLever.InverseTransformPoint(root.transform.TransformPoint(leverKnob));
        cabControls.remotes = remotes;
        cabControls.doors = BuildDoors(root.transform, Part, log);

        // 6b) Shadows: a low-poly stand-in casts the tractor's shadow; the 750k-triangle model itself doesn't (saves a whole pass).
        BuildShadowProxy(root.transform, wheels, cab, log);
        LogMeshLods(model, log);

        // 7) Interior detail: baked ambient occlusion on the cab interior, wheel and lever, then textured materials.
        var aoTargets = new[] { Part("Cab_Interier_01").GetComponent<MeshFilter>(), ctrl.steeringWheel.GetComponentInChildren<MeshFilter>(), speedLever.GetComponentInChildren<MeshFilter>() }
            .Concat(remotes.Select(r => r.hinge.GetComponentInChildren<MeshFilter>())).ToArray();
        BakeAmbientOcclusion(root.transform, aoTargets, log);
        ApplyDetailMaterials(root.transform, new System.Collections.Generic.HashSet<Mesh>(aoTargets.Select(t => t.sharedMesh)), log);
        var drivetrain = root.AddComponent<Drivetrain>();
        BuildLinkage(root.transform, Part, log);
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
    static Transform SplitSteeringWheel(Transform root, Transform interior, StringBuilder log, out Pose clusterPose, out Vector2 clusterSize,
        out Transform lever, out Vector3 leverKnob, out CabControls.Swing[] remotes)
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

        // Speed lever (the shift lever in the H-gate at the front of the right armrest): a ~19 cm tall piece right of the seat.
        // Its pivot is the bottom of the stalk, the knob is the top.
        var leverPart = parts.Where(g => { var b = PartBounds(g); return b.center.x > 0.2f && b.size.y > 0.15f && b.size.y < 0.25f && b.size.magnitude < 0.3f; })
            .OrderBy(g => Vector3.Distance(PartBounds(g).center, new Vector3(0.31f, 2.34f, 0.64f))).First();
        var inLever = new bool[p.Length];
        foreach (int i in leverPart) inLever[i] = true;
        float minY = leverPart.Min(i => p[i].y), maxY = leverPart.Max(i => p[i].y);
        Vector3 Centroid(System.Func<int, bool> f) { var sel = leverPart.Where(f).ToArray(); var c = Vector3.zero; foreach (int i in sel) c += p[i]; return c / sel.Length; }
        leverKnob = Centroid(i => p[i].y > maxY - 0.04f);
        // The thin rod that goes down into the gate is a separate piece just below the stalk. Take it too and pivot at
        // its bottom (inside the gate), otherwise the stalk bends away from a rod that stays put.
        Vector3 stalkBottom = Centroid(i => p[i].y < minY + 0.012f);
        var stem = parts.Where(g => g != leverPart && PartBounds(g).size.magnitude < 0.1f && Vector3.Distance(PartBounds(g).center, stalkBottom) < 0.05f).ToList();
        foreach (var g in stem) foreach (int i in g) inLever[i] = true;
        var leverAll = leverPart.Concat(stem.SelectMany(g => g)).ToArray();
        float stemBottom = leverAll.Min(i => p[i].y);
        Vector3 leverPivot = Vector3.zero; int pivotN = 0;
        foreach (int i in leverAll) if (p[i].y < stemBottom + 0.012f) { leverPivot += p[i]; pivotN++; }
        leverPivot /= pivotN;
        log.AppendLine($"Speed lever stem: {stem.Count} piece(s), {stem.Sum(g => g.Length)} vertices, bottom {stemBottom:0.000} (stalk bottom {minY:0.000})");
        // The knob's button block and its buttons are separate pieces: take every small piece centred around the knob.
        Vector3 knobCentre = leverKnob;
        int knobPieces = 0, leverVerts = leverAll.Length;
        foreach (var g in parts)
        {
            if (g == leverPart || PartBounds(g).size.magnitude > 0.15f) continue;
            if (Vector3.Distance(PartBounds(g).center, knobCentre) > 0.08f) continue;
            foreach (int i in g) inLever[i] = true;
            knobPieces++; leverVerts += g.Length;
        }
        log.AppendLine($"Speed lever: {leverVerts} vertices ({knobPieces} knob pieces), pivot {leverPivot:F3}, knob {leverKnob:F3}");
        foreach (var g in parts)
            if (!inLever[g[0]] && g.Any(i => Vector3.Distance(p[i], knobCentre) < 0.1f))
                log.AppendLine($"   left in the cab near the knob: {g.Length} vertices, bounds {PartBounds(g).center:F3} size {PartBounds(g).size:F3}");
        foreach (var g in parts)
            if (!inLever[g[0]] && g.Any(i => Vector3.Distance(p[i], leverPivot) < 0.08f))
                log.AppendLine($"   left in the cab near the stalk bottom: {g.Length} vertices, bounds {PartBounds(g).center:F3} size {PartBounds(g).size:F3}");
        log.AppendLine($"   lever piece bounds {PartBounds(leverPart).center:F3} size {PartBounds(leverPart).size:F3}");
        lever = new GameObject("SpeedLever").transform;
        lever.SetParent(root, false);
        lever.localPosition = leverPivot;
        var leverHinge = lever;
        Mesh leverMesh = SubsetMesh(src, inLever, true, v => leverHinge.InverseTransformPoint(interior.TransformPoint(v)), renderer.sharedMaterials, out Material[] leverMats,
            n => leverHinge.InverseTransformDirection(interior.TransformDirection(n)));
        leverMesh.name = "SpeedLever";
        var leverGo = new GameObject("LeverMesh");
        leverGo.transform.SetParent(lever, false);
        leverGo.AddComponent<MeshFilter>().sharedMesh = SaveMesh(leverMesh, "SpeedLever");
        leverGo.AddComponent<MeshRenderer>().sharedMaterials = leverMats;
        // Hydraulic remote levers: three in a row behind the speed lever, each a knob, a handle and a stem (separate pieces).
        // Each kind is found by its height, sorted by x and zipped. Pivot = bottom of the stem, inside the console housing;
        // they swing fore / aft (around X) like the speed lever.
        var inRemote = new bool[p.Length];
        var remoteParts = parts.Where(g =>
        {
            var b = PartBounds(g);
            return b.center.x > 0.38f && b.center.x < 0.58f && b.center.y > 2.15f && b.center.y < 2.4f && b.center.z > 0.47f && b.center.z < 0.53f && b.size.magnitude < 0.13f;
        }).ToList();
        int[][] Kind(float minH, float maxH) => remoteParts.Where(g => PartBounds(g).size.y >= minH && PartBounds(g).size.y < maxH).OrderBy(g => PartBounds(g).center.x).ToArray();
        var knobs = Kind(0f, 0.03f); var handles = Kind(0.06f, 0.09f); var stems = Kind(0.09f, 0.12f);
        log.AppendLine($"Remote levers: {knobs.Length} knobs, {handles.Length} handles, {stems.Length} stems");
        var remoteList = new System.Collections.Generic.List<CabControls.Swing>();
        if (knobs.Length == 3 && handles.Length == 3 && stems.Length == 3)
        {
            for (int r = 0; r < 3; r++)
            {
                var sel = new bool[p.Length];
                foreach (var g in new[] { knobs[r], handles[r], stems[r] }) foreach (int i in g) sel[i] = inRemote[i] = true;
                float bottom = stems[r].Min(i => p[i].y);
                var low = stems[r].Where(i => p[i].y < bottom + 0.008f).ToArray();
                Vector3 pivotPos = Vector3.zero; foreach (int i in low) pivotPos += p[i]; pivotPos /= low.Length;
                var hinge = new GameObject($"RemoteLever_{r + 1}").transform;
                hinge.SetParent(root, false);
                hinge.localPosition = pivotPos;
                var h = hinge;
                Mesh m = SubsetMesh(src, sel, true, v => h.InverseTransformPoint(interior.TransformPoint(v)), renderer.sharedMaterials, out Material[] rMats,
                    n => h.InverseTransformDirection(interior.TransformDirection(n)));
                m.name = $"RemoteLever{r + 1}";
                var go = new GameObject("LeverMesh");
                go.transform.SetParent(hinge, false);
                go.AddComponent<MeshFilter>().sharedMesh = SaveMesh(m, m.name);
                go.AddComponent<MeshRenderer>().sharedMaterials = rMats;
                // Grab box: knob + handle, in hinge space.
                var grab = new Bounds(h.InverseTransformPoint(root.TransformPoint(p[knobs[r][0]])), Vector3.zero);
                foreach (int i in knobs[r].Concat(handles[r])) grab.Encapsulate(h.InverseTransformPoint(root.TransformPoint(p[i])));
                remoteList.Add(new CabControls.Swing { name = $"Remote {r + 1}", hinge = hinge, axis = Vector3.right, grabBox = grab, min = -20f, max = 20f, springBack = true });
                log.AppendLine($"Remote lever {r + 1}: pivot {pivotPos:F3}, knob {PartBounds(knobs[r]).center:F3}, {sel.Count(b => b)} vertices");
            }
        }
        else Debug.LogWarning("[Setup] Hydraulic remote levers not found, they stay part of the cab");
        remotes = remoteList.ToArray();

        var removed = new bool[p.Length];
        for (int i = 0; i < p.Length; i++) removed[i] = inWheel[i] || inLever[i] || inRemote[i];

        // Pivot frame: SteeringColumn (fixed, Z = axis) > SteeringWheel (spins around local Z) > mesh.
        var column = new GameObject("SteeringColumn").transform;
        column.SetParent(root, false);
        column.SetLocalPositionAndRotation(centre, frame);
        var pivot = new GameObject("SteeringWheel").transform;
        pivot.SetParent(column, false);

        var mats = renderer.sharedMaterials;
        Mesh rest = SubsetMesh(src, removed, false, null, mats, out Material[] restMats);
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

    // Doors: the model's door pivots sit on the hinge line at the rear edge of each door (JD cabs have rear-hinged doors).
    // Each door goes under a tractor-aligned Hinge_Door_*; it opens outward up to 65° (more would swing the lower door
    // panel into the rear tyre). The grab box is the whole door, so it can be taken anywhere within reach of the seat.
    static CabControls.Swing[] BuildDoors(Transform root, System.Func<string, Transform> part, StringBuilder log)
    {
        var doors = new System.Collections.Generic.List<CabControls.Swing>();
        foreach (var (side, sign) in new[] { ("Door_L", -1f), ("Door_R", 1f) })
        {
            var door = part(side);
            if (door == null) { Debug.LogWarning($"[Setup] {side} not found"); continue; }
            var hinge = new GameObject("Hinge_" + side).transform;
            hinge.SetParent(root, false);
            hinge.localPosition = root.InverseTransformPoint(door.position);
            door.SetParent(hinge, true);
            var mesh = door.GetComponent<MeshFilter>().sharedMesh;
            var verts = mesh.vertices;
            var box = new Bounds(hinge.InverseTransformPoint(door.TransformPoint(verts[0])), Vector3.zero);
            foreach (var v in verts) box.Encapsulate(hinge.InverseTransformPoint(door.TransformPoint(v)));
            // + angle = open (outward): left door turns around -Y, right door around +Y.
            doors.Add(new CabControls.Swing { name = side == "Door_L" ? "LEFT DOOR" : "RIGHT DOOR", hinge = hinge, axis = Vector3.up * sign, grabBox = box, min = 0f, max = 65f });
            log.AppendLine($"{side}: hinge {hinge.localPosition:F3}, door box {box.center:F3} size {box.size:F3}");
        }
        return doors.ToArray();
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
    // Short tick for the cab panel buttons.
    static AudioClip ClickClip()
    {
        int n = Rate / 25;
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            s[i] = Mathf.Sin(2f * Mathf.PI * 2400f * t) * Mathf.Exp(-t / 0.006f) + 0.5f * Mathf.Sin(2f * Mathf.PI * 900f * t) * Mathf.Exp(-t / 0.01f);
        }
        return WriteClip("UiClick", Normalize(s, 0.6f));
    }

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

    // ------------------------------------------------------------------ interior detail: textures + baked ambient occlusion
    // The FBX's textures are missing and most cab plastics have degenerate UVs, so surfaces get generated tiling detail
    // (grain, fabric weave, rubber mat ribs, pedal tread) through the TractorVR/TriplanarLit shader, which needs no UVs.
    // Cab interior, steering wheel and speed lever also get baked ambient occlusion in their vertex colours.
    const string TexDir = GenDir + "/Textures";

    class DetailKind
    {
        public string name; public float tile; public float normal; public float smooth; public float brighten = 1.08f;
        public System.Func<int, int, int, float> height;    // (x, y, size) → 0..1, tileable
        public float bump = 6f;                              // normal-map slope scale
        public float albedoContrast = 0.15f;
    }

    static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFFFF) / (float)0xFFFFFF;
        }
    }

    // Value noise that tiles over the texture: `cells` lattice cells across, wrapped.
    static float TileNoise(float u, float v, int cells, int seed)
    {
        float x = u * cells, y = v * cells;
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
        int W(int a) => ((a % cells) + cells) % cells;
        float a00 = Hash(W(x0), W(y0), seed), a10 = Hash(W(x0 + 1), W(y0), seed);
        float a01 = Hash(W(x0), W(y0 + 1), seed), a11 = Hash(W(x0 + 1), W(y0 + 1), seed);
        return Mathf.Lerp(Mathf.Lerp(a00, a10, fx), Mathf.Lerp(a01, a11, fx), fy);
    }

    static readonly DetailKind[] DetailKinds =
    {
        new DetailKind { name = "Grain", tile = 0.06f, normal = 0.3f, smooth = 0.32f, bump = 4f, albedoContrast = 0.07f, height = (x, y, n) =>   // fine leather-like grain
        {
            float u = x / (float)n, v = y / (float)n;
            float h = 0.55f * TileNoise(u, v, 40, 1) + 0.3f * TileNoise(u, v, 80, 2) + 0.15f * TileNoise(u, v, 160, 3);
            return Mathf.Pow(h, 1.6f);
        }},
        new DetailKind { name = "Fabric", tile = 0.06f, normal = 1f, smooth = 0.08f, bump = 9f, albedoContrast = 0.3f, height = (x, y, n) =>
        {
            // Plain weave, 40 threads each way per tile: in alternate cells the warp or the weft lies on top.
            float u = x * 40f / n, v = y * 40f / n;
            int iu = Mathf.FloorToInt(u), iv = Mathf.FloorToInt(v);
            float fu = u - iu, fv = v - iv;
            float warp = Mathf.Sqrt(Mathf.Sin(Mathf.PI * fv)), weft = Mathf.Sqrt(Mathf.Sin(Mathf.PI * fu));
            float h = ((iu + iv) & 1) == 0 ? 0.35f + 0.65f * warp * (0.6f + 0.4f * Mathf.Sin(Mathf.PI * fu)) : 0.35f + 0.65f * weft * (0.6f + 0.4f * Mathf.Sin(Mathf.PI * fv));
            return h * (0.85f + 0.15f * TileNoise(x / (float)n, y / (float)n, 64, 4));
        }},
        new DetailKind { name = "Mat", tile = 0.24f, normal = 1f, smooth = 0.2f, bump = 7f, albedoContrast = 0.25f, height = (x, y, n) =>
        {
            // Rubber floor mat: 16 rounded ribs per tile with a fine grain on top.
            float v = y * 16f / n, f = v - Mathf.Floor(v);
            float rib = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((0.42f - Mathf.Abs(f - 0.5f)) * 8f));
            return 0.8f * rib + 0.2f * TileNoise(x / (float)n, y / (float)n, 96, 5);
        }},
        new DetailKind { name = "Tread", tile = 0.05f, normal = 1f, smooth = 0.25f, bump = 8f, albedoContrast = 0.25f, height = (x, y, n) =>
        {
            // Pedal pads: 6 × 6 raised blocks per tile.
            float u = x * 6f / n, v = y * 6f / n;
            float fu = u - Mathf.Floor(u), fv = v - Mathf.Floor(v);
            float e = Mathf.Min(Mathf.Min(fu, 1f - fu), Mathf.Min(fv, 1f - fv));
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((e - 0.08f) * 12f));
        }},
    };

    // Which rebuilt material gets which detail (base colours stay as ApplyMaterials made them; the floor mat goes darker).
    static readonly System.Collections.Generic.Dictionary<string, string> DetailFor = new System.Collections.Generic.Dictionary<string, string>
    {
        { "Tractor_color_plastics", "Grain" }, { "Tractor_Plastic_black", "Grain" }, { "Tractor_Dashboard", "Grain" },
        { "Tractor_Rubber_black", "Grain" }, { "Tractor_Seat_cloth01", "Fabric" }, { "Tractor_Seat_cloth02", "Fabric" },
        { "Tractor_cloth", "Fabric" }, { "Tractor_Cabin_floor", "Mat" }, { "Tractor_pedal", "Tread" },
    };

    static (Texture2D detail, Texture2D normal) DetailTextures(DetailKind k)
    {
        const int n = 512;
        Directory.CreateDirectory(TexDir);
        var h = new float[n, n];
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) h[x, y] = k.height(x, y, n);
        var albedo = new Texture2D(n, n, TextureFormat.RGB24, false);
        var normal = new Texture2D(n, n, TextureFormat.RGB24, false);
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float a = Mathf.Lerp(1f - k.albedoContrast, 1f, h[x, y]);
            albedo.SetPixel(x, y, new Color(a, a, a));
            float dx = h[(x + 1) % n, y] - h[(x + n - 1) % n, y];
            float dy = h[x, (y + 1) % n] - h[x, (y + n - 1) % n];
            Vector3 nn = new Vector3(-dx * k.bump, -dy * k.bump, 1f).normalized;
            normal.SetPixel(x, y, new Color(nn.x * 0.5f + 0.5f, nn.y * 0.5f + 0.5f, nn.z * 0.5f + 0.5f));
        }
        Texture2D Write(Texture2D t, string file, bool isNormal)
        {
            string path = $"{TexDir}/{file}.png";
            File.WriteAllBytes(path, t.EncodeToPNG());
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.sRGBTexture = !isNormal;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.mipmapEnabled = true;
            imp.anisoLevel = 4;
            imp.maxTextureSize = 512;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        return (Write(albedo, "Detail_" + k.name, false), Write(normal, "Normal_" + k.name, true));
    }

    // Swaps the plain rebuilt materials for TriplanarLit versions with detail; "_ao" variants on meshes with baked AO.
    static void ApplyDetailMaterials(Transform root, System.Collections.Generic.HashSet<Mesh> withAO, StringBuilder log)
    {
        var shader = Shader.Find("TractorVR/TriplanarLit");
        var textures = DetailKinds.ToDictionary(k => k.name, k => (kind: k, tex: DetailTextures(k)));
        var cache = new System.Collections.Generic.Dictionary<string, Material>();
        int swapped = 0;
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mf = r.GetComponent<MeshFilter>();
            bool ao = mf != null && withAO.Contains(mf.sharedMesh);
            r.sharedMaterials = r.sharedMaterials.Select(m =>
            {
                if (m == null || !DetailFor.TryGetValue(m.name, out string kindName)) return m;
                string key = m.name + (ao ? "_ao" : "");
                if (cache.TryGetValue(key, out var done)) return done;
                var (kind, tex) = textures[kindName];
                var t = new Material(shader) { name = key + "_detail" };
                Color c = m.GetColor("_BaseColor");
                if (m.name == "Tractor_Cabin_floor") c = new Color(0.16f, 0.16f, 0.16f);
                t.SetColor("_BaseColor", new Color(c.r * kind.brighten, c.g * kind.brighten, c.b * kind.brighten, 1f));
                t.SetTexture("_DetailMap", tex.detail);
                t.SetTexture("_NormalMap", tex.normal);
                t.SetFloat("_NormalStrength", kind.normal);
                t.SetFloat("_Tiling", 1f / kind.tile);
                t.SetFloat("_Smoothness", kind.smooth);
                t.SetFloat("_Metallic", 0f);
                t.SetFloat("_AOStrength", ao ? 1f : 0f);
                swapped++;
                return cache[key] = SaveMat(t, key + "_detail");
            }).ToArray();
        }
        log.AppendLine($"Detail materials: {cache.Count} ({string.Join(", ", cache.Keys)})");
    }

    // Ambient occlusion per vertex: 32 cosine-weighted rays up to 35 cm against the tractor's opaque geometry (temporary
    // mesh colliders without the glass), stored in the vertex colour (red). Darkens corners, gaps, the footwell and under the dash.
    static void BakeAmbientOcclusion(Transform root, MeshFilter[] targets, StringBuilder log)
    {
        var temp = new System.Collections.Generic.List<GameObject>();
        Bounds cab = WorldBounds(targets.Select(t => t.transform).ToArray());
        cab.Expand(0.8f);
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null || !r.bounds.Intersects(cab)) continue;
            var src = mf.sharedMesh;
            var tris = new System.Collections.Generic.List<int>();
            for (int s = 0; s < src.subMeshCount && s < r.sharedMaterials.Length; s++)
            {
                var m = r.sharedMaterials[s];
                if (m == null || m.renderQueue >= (int)RenderQueue.Transparent || m.name.Contains("Glass")) continue;
                tris.AddRange(src.GetTriangles(s));
            }
            if (tris.Count == 0) continue;
            // World-space copy at the scene root: the tractor is a non-kinematic Rigidbody, which can't own non-convex mesh colliders.
            var cm = new Mesh { indexFormat = IndexFormat.UInt32, vertices = src.vertices.Select(x => r.transform.TransformPoint(x)).ToArray() };
            cm.SetTriangles(tris, 0);
            var go = new GameObject("AO collider");
            go.AddComponent<MeshCollider>().sharedMesh = cm;
            temp.Add(go);
        }
        Physics.SyncTransforms();

        // Fixed cosine-weighted hemisphere directions (around +Z).
        const int rays = 32;
        var dirs = new Vector3[rays];
        for (int i = 0; i < rays; i++)
        {
            float u = (i + 0.5f) / rays, phi = i * 2.39996323f;   // golden angle
            float rr = Mathf.Sqrt(u);
            dirs[i] = new Vector3(rr * Mathf.Cos(phi), rr * Mathf.Sin(phi), Mathf.Sqrt(1f - u));
        }
        const float maxDist = 0.35f;
        int total = 0; float sum = 0f;
        foreach (var mf in targets)
        {
            var mesh = mf.sharedMesh;
            var v = mesh.vertices; var nrm = mesh.normals;
            var colors = new Color[v.Length];
            var cacheAo = new System.Collections.Generic.Dictionary<(Vector3Int, Vector3Int), float>();
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 p = mf.transform.TransformPoint(v[i]);
                Vector3 n = mf.transform.TransformDirection(nrm[i]).normalized;
                var key = (Vector3Int.RoundToInt(p * 2000f), Vector3Int.RoundToInt(n * 50f));
                if (!cacheAo.TryGetValue(key, out float ao))
                {
                    Quaternion frame = Quaternion.FromToRotation(Vector3.forward, n);
                    float occ = 0f;
                    for (int r = 0; r < rays; r++)
                        if (Physics.Raycast(p + n * 0.003f, frame * dirs[r], out RaycastHit hit, maxDist, 1 << 0, QueryTriggerInteraction.Ignore))
                        {
                            float f = 1f - hit.distance / maxDist;
                            occ += f * f;
                        }
                    ao = Mathf.Clamp(1f - 1.4f * occ / rays, 0.2f, 1f);
                    cacheAo[key] = ao;
                }
                colors[i] = new Color(ao, ao, ao, 1f);
                sum += ao; total++;
            }
            mesh.colors = colors;
            EditorUtility.SetDirty(mesh);
        }
        foreach (var go in temp) Object.DestroyImmediate(go);
        AssetDatabase.SaveAssets();
        log.AppendLine($"Ambient occlusion baked: {total} vertices on {targets.Length} meshes, {temp.Count} occluder meshes, mean AO {sum / Mathf.Max(1, total):0.00}");
    }

    // ------------------------------------------------------------------ shadow stand-in + mesh LOD stats
    // Boxes and cylinders in tractor space: cab roof and pillars (so sunlight still falls through the windows into the cab),
    // cab body below the windows, hood, rear frame, and the four wheels. Shadows-only renderer, a few hundred triangles.
    static void BuildShadowProxy(Transform root, TractorController.Wheel[] wheels, Bounds cab, StringBuilder log)
    {
        var parts = new System.Collections.Generic.List<CombineInstance>();
        Mesh cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx"), cyl = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
        void Add(Mesh m, Vector3 centre, Vector3 size, Quaternion rot) =>
            parts.Add(new CombineInstance { mesh = m, transform = Matrix4x4.TRS(centre, rot, size) });
        Vector3 lo = root.InverseTransformPoint(cab.min), hi = root.InverseTransformPoint(cab.max);
        float roofY = hi.y - 0.12f, windowY = lo.y + 1.05f;
        Add(cube, new Vector3(0f, roofY, (lo.z + hi.z) * 0.5f), new Vector3(hi.x - lo.x, 0.24f, hi.z - lo.z), Quaternion.identity);
        foreach (float x in new[] { lo.x + 0.08f, hi.x - 0.08f })
        foreach (float z in new[] { lo.z + 0.1f, hi.z - 0.1f })
            Add(cube, new Vector3(x, (windowY + roofY) * 0.5f, z), new Vector3(0.12f, roofY - windowY, 0.12f), Quaternion.identity);
        Add(cube, new Vector3(0f, (lo.y + windowY) * 0.5f, (lo.z + hi.z) * 0.5f), new Vector3((hi.x - lo.x) * 0.8f, windowY - lo.y, hi.z - lo.z), Quaternion.identity);
        Add(cube, new Vector3(0f, 1.65f, (hi.z + 3.6f) * 0.5f), new Vector3(0.95f, 1.3f, 3.6f - hi.z), Quaternion.identity);   // hood
        Add(cube, new Vector3(0f, 1.15f, -0.55f), new Vector3(1.0f, 1.0f, 1.0f), Quaternion.identity);                      // rear frame
        foreach (var w in wheels)
        {
            float width = w.steers ? 0.5f : 0.65f, d = 2f * w.radius;
            Add(cyl, w.steerPivot.localPosition, new Vector3(d, width * 0.5f, d), Quaternion.Euler(0f, 0f, 90f));
        }
        var mesh = new Mesh { name = "TractorShadowProxy" };
        mesh.CombineMeshes(parts.ToArray(), true, true);
        var go = new GameObject("ShadowProxy");
        go.transform.SetParent(root, false);
        go.AddComponent<MeshFilter>().sharedMesh = SaveMesh(mesh, "TractorShadowProxy");
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = SaveMat(new Material(LitShader), "ShadowProxy");
        r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        int off = 0;
        foreach (var other in root.GetComponentsInChildren<Renderer>(true))
            if (other != r && other.shadowCastingMode != ShadowCastingMode.Off) { other.shadowCastingMode = ShadowCastingMode.Off; off++; }
        log.AppendLine($"Shadow stand-in: {mesh.triangles.Length / 3} triangles; shadow casting off on {off} tractor renderers");
    }

    // Triangles per mesh LOD level, summed over the tractor model.
    static void LogMeshLods(GameObject model, StringBuilder log)
    {
        var perLod = new long[8];
        foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
        {
            var m = mf.sharedMesh;
            if (m == null) continue;
            for (int lod = 0; lod < perLod.Length; lod++)
            {
                int l = Mathf.Min(lod, m.lodCount - 1);
                long tris = 0;
                for (int sm = 0; sm < m.subMeshCount; sm++) tris += m.lodCount > 1 ? m.GetLod(sm, l).indexCount / 3 : m.GetIndexCount(sm) / 3;
                perLod[lod] += tris;
            }
        }
        log.AppendLine("Tractor triangles per mesh LOD level: " + string.Join(", ", perLod.Take(5).Select((t, i) => $"LOD{i} {t:N0}")));
    }

    // ------------------------------------------------------------------ hydraulics: linkage hinges
    // The model's linkage parts have their pivots on the real hinge points (rockshaft, lower link pins, arm tips).
    // Each moving part gets a tractor-aligned hinge at its pivot, so Hitch only has to turn hinges around X.
    static Hitch BuildLinkage(Transform root, System.Func<string, Transform> part, StringBuilder log)
    {
        var hitch = root.gameObject.AddComponent<Hitch>();
        Vector3 Piv(Transform t) => root.InverseTransformPoint(t.position);
        Vector3 NoX(Vector3 v) => new Vector3(0f, v.y, v.z);
        Transform Hinge(string name, Transform t)
        {
            var h = new GameObject("Hinge_" + name).transform;
            h.SetParent(root, false);
            h.localPosition = Piv(t);
            t.SetParent(h, true);
            return h;
        }
        // Mesh vertex (tractor space, side view) farthest from a point: the far end of a link or rod.
        Vector3 FarEnd(Transform t, Vector3 from)
        {
            var mf = t.GetComponent<MeshFilter>();
            Vector3 best = from; float bd = 0f;
            foreach (var v in mf.sharedMesh.vertices)
            {
                Vector3 p = root.InverseTransformPoint(t.TransformPoint(v));
                float d = Vector2.Distance(new Vector2(p.y, p.z), new Vector2(from.y, from.z));
                if (d > bd) { bd = d; best = p; }
            }
            return best;
        }

        var arms = part("Rear_Holder_Ruler_01");
        var links = part("Rear_Holder_02");
        var rodL = part("Rear_Holder_Ruler_02_L");
        var rodR = part("Rear_Holder_Ruler_02_R");
        var barrel = part("Rear_Forcer_01");
        var cylRod = part("Rear_Forcer_02");

        hitch.rockshaftPivot = Piv(arms);
        hitch.armTip = NoX(Piv(rodL));
        hitch.rodBottom = NoX(FarEnd(rodL, Piv(rodL)));
        hitch.lowerPivot = Piv(links);
        hitch.linkEnd = NoX(FarEnd(links, Piv(links)));
        hitch.cylinderBase = Piv(barrel);
        hitch.cylinderTop = Piv(cylRod);

        hitch.liftArms = Hinge("LiftArms", arms);
        hitch.lowerLinks = Hinge("LowerLinks", links);
        hitch.liftRods = new[] { Hinge("LiftRod_L", rodL), Hinge("LiftRod_R", rodR) };
        hitch.stabilisers = new[] { Hinge("Stabiliser_L", part("Rear_Holder_Ruler_03_L")), Hinge("Stabiliser_R", part("Rear_Holder_Ruler_03_R")) };
        hitch.cylinderBarrel = Hinge("LiftCylinders", barrel);
        hitch.cylinderRod = Hinge("LiftCylinderRods", cylRod);
        hitch.rearMount = new GameObject("RearMount").transform;
        hitch.rearMount.SetParent(root, false);
        hitch.rearMount.localPosition = hitch.linkEnd;
        // Top link: pivot on its bracket at the bottom end; stowed upright. Hitch swings it to a coupled implement's top pin.
        var topLink = part("Rear_Holder_01");
        hitch.topLinkPivot = NoX(Piv(topLink));
        Vector3 topEnd = NoX(FarEnd(topLink, Piv(topLink)));
        hitch.topLinkRestDir = (topEnd - hitch.topLinkPivot).normalized;
        hitch.topLinkModelLength = Vector3.Distance(topEnd, hitch.topLinkPivot);
        hitch.topLink = Hinge("TopLink", topLink);
        log.AppendLine($"Top link: pivot {hitch.topLinkPivot:F3}, length {hitch.topLinkModelLength:0.000} m");
        log.AppendLine($"Rear linkage: rockshaft {hitch.rockshaftPivot:F3}, arm tip {hitch.armTip:F3}, rod bottom {hitch.rodBottom:F3}, " +
                       $"lower pivot {hitch.lowerPivot:F3}, link end {hitch.linkEnd:F3}, cylinder {hitch.cylinderBase:F3} → {hitch.cylinderTop:F3}");

        var fl = part("Front_Holder_02_L");
        var fr = part("Front_Holder_02_R");
        hitch.frontPivot = NoX(Piv(fl));
        hitch.frontTip = NoX(FarEnd(fl, Piv(fl)));
        // Front linkage cylinders: the barrel turns on its frame pin (its model pivot, at the top). The model pins the rod on the
        // link's own pivot, which wouldn't move anything; the link has a lug below its pivot, so the rod rides on that.
        Vector3 lug = Vector3.zero; int lugN = 0;
        foreach (var v in fl.GetComponent<MeshFilter>().sharedMesh.vertices)
        {
            Vector3 q = root.InverseTransformPoint(fl.TransformPoint(v));
            if (q.y < hitch.frontPivot.y - 0.04f) { lug += q; lugN++; }
        }
        hitch.frontCylPin = lugN > 0 ? NoX(lug / lugN) : hitch.frontPivot;
        var barrelL = part("Front_Forcer_01_L"); var rodFL = part("Front_Forcer_02_L");
        hitch.frontCylBase = NoX(Piv(barrelL));
        hitch.frontRodPivot = NoX(Piv(rodFL));
        hitch.frontBarrels = new[] { Hinge("FrontCylinder_L", barrelL), Hinge("FrontCylinder_R", part("Front_Forcer_01_R")) };
        hitch.frontRods = new[] { Hinge("FrontCylinderRod_L", rodFL), Hinge("FrontCylinderRod_R", part("Front_Forcer_02_R")) };
        log.AppendLine($"Front cylinders: frame pin {hitch.frontCylBase:F3}, link lug {hitch.frontCylPin:F3} ({lugN} vertices), rod pivot {hitch.frontRodPivot:F3}");
        hitch.frontLinks = new[] { Hinge("FrontLink_L", fl), Hinge("FrontLink_R", fr) };
        hitch.frontMount = new GameObject("FrontMount").transform;
        hitch.frontMount.SetParent(root, false);
        hitch.frontMount.localPosition = hitch.frontTip;
        log.AppendLine($"Front linkage: pivot {hitch.frontPivot:F3}, tip {hitch.frontTip:F3}");

        // Drawbar: the rearmost point of the rear frame near the centre line, low down (the pick-up hitch).
        var frame = part("Rear_Frame");
        Vector3 bar = new Vector3(0f, 0f, float.MaxValue);
        foreach (var v in frame.GetComponent<MeshFilter>().sharedMesh.vertices)
        {
            Vector3 p = root.InverseTransformPoint(frame.TransformPoint(v));
            if (Mathf.Abs(p.x) < 0.12f && p.y < 0.75f && p.z < bar.z) bar = p;
        }
        hitch.drawbarMount = new GameObject("DrawbarMount").transform;
        hitch.drawbarMount.SetParent(root, false);
        hitch.drawbarMount.localPosition = new Vector3(0f, bar.y, bar.z - 0.05f);
        log.AppendLine($"Drawbar at {hitch.drawbarMount.localPosition:F3}");
        return hitch;
    }

    // ------------------------------------------------------------------ implements (built from primitives; no models came with the tractor)
    // Implement space: +Z points to the tractor (it is the tractor's forward when coupled), Y up, ground at y = 0 when parked.
    static void BuildImplements(Hitch hitch, StringBuilder log)
    {
        var frameBlue = SaveMat(new Material(LitShader) { color = new Color(0.12f, 0.22f, 0.5f) }, "Impl_FrameBlue");
        var steel = SaveMat(new Material(LitShader) { color = new Color(0.62f, 0.64f, 0.66f) }, "Impl_Steel");
        steel.SetFloat("_Metallic", 0.8f); steel.SetFloat("_Smoothness", 0.7f);
        var red = SaveMat(new Material(LitShader) { color = new Color(0.72f, 0.1f, 0.08f) }, "Impl_Red");
        var dark = SaveMat(new Material(LitShader) { color = new Color(0.08f, 0.08f, 0.08f) }, "Impl_Rubber");
        var green = SaveMat(new Material(LitShader) { color = new Color(0.21f, 0.49f, 0.17f) }, "Impl_Green");
        var unlit = Shader.Find("Universal Render Pipeline/Unlit");
        var soil = SaveMat(new Material(unlit), "Trail_Soil"); soil.SetColor("_BaseColor", new Color(0.33f, 0.22f, 0.13f));
        var mown = SaveMat(new Material(unlit), "Trail_Mown"); mown.SetColor("_BaseColor", new Color(0.5f, 0.62f, 0.27f));
        var cast = SaveMat(new Material(LitShader) { color = new Color(0.16f, 0.16f, 0.17f) }, "Impl_CastIron");
        cast.SetFloat("_Smoothness", 0.25f);
        var chrome = SaveMat(new Material(LitShader) { color = new Color(0.8f, 0.82f, 0.85f) }, "Impl_Chrome");
        chrome.SetFloat("_Metallic", 1f); chrome.SetFloat("_Smoothness", 0.85f);
        var board = SaveMat(new Material(LitShader) { color = new Color(0.32f, 0.34f, 0.36f) }, "Impl_Mouldboard");
        board.SetFloat("_Metallic", 0.7f); board.SetFloat("_Smoothness", 0.8f);
        var lamp = SaveMat(new Material(LitShader) { color = new Color(0.85f, 0.08f, 0.05f) }, "Impl_Lamp");
        lamp.SetFloat("_Smoothness", 0.9f);
        var plate = SaveMat(new Material(LitShader) { color = new Color(0.92f, 0.92f, 0.88f) }, "Impl_Plate");
        var yellow = SaveMat(new Material(LitShader) { color = new Color(0.95f, 0.75f, 0.1f) }, "Impl_Yellow");
        var heapMat = SaveMat(new Material(LitShader) { color = new Color(0.36f, 0.25f, 0.16f) }, "Impl_Soil");
        heapMat.SetFloat("_Smoothness", 0.05f);
        var tyre = SaveMesh(TyreMesh(0.5f, 0.38f, 20, 0.035f), "Impl_TrailerTyre");
        var smallTyre = SaveMesh(TyreMesh(0.3f, 0.16f, 14, 0.02f), "Impl_SmallTyre");
        var mouldboard = SaveMesh(MouldboardMesh(), "Impl_Mouldboard");
        var parent = new GameObject("Implements").transform;

        // A wheel: lugged tyre + rim dish + hub, spinning around its local X (SimpleWheels turns the "Spin" child).
        Transform Wheel(Transform at, string name, Vector3 pos, Mesh tyreMesh, float radius, float width, Material rimMat)
        {
            var pivot = Point(at, name, pos);
            var spin = Point(pivot, "Spin", Vector3.zero);
            MeshPart(spin, "Tyre", tyreMesh, Vector3.zero, Vector3.zero, dark);
            Part(spin, "Rim", PrimitiveType.Cylinder, Vector3.zero, new Vector3(radius * 1.25f, width * 0.46f, radius * 1.25f), rimMat, new Vector3(0f, 0f, 90f));
            Part(spin, "Hub", PrimitiveType.Cylinder, Vector3.zero, new Vector3(radius * 0.45f, width * 0.56f, radius * 0.45f), steel, new Vector3(0f, 0f, 90f));
            return pivot;
        }

        // ---- 3-furrow mounted plough on the rear linkage (right-turning: bodies stepped to the right and back)
        {
            var go = NewImplement(parent, "Plough", new Vector3(-6f, 0f, -6.2f), 0f);
            var imp = go.AddComponent<Implement>();
            imp.displayName = "PLOUGH"; imp.mount = Implement.Mount.Rear;
            imp.mass = 1200f; imp.centreOfMassOffset = new Vector3(0.3f, -0.1f, -1.4f);
            imp.workingDepth = 0.25f; imp.draftKN = 30f;
            var t = go.transform;
            imp.hitchPoint = Point(t, "HitchPoint", new Vector3(0f, 0.75f, 0f));   // lower link pins (this linkage reaches down to 0.43 m)
            imp.topPin = Point(t, "TopPin", new Vector3(0f, 1.36f, -0.12f));
            // Headstock: cross shaft with the two lower pins, an A-frame up to the top pin, braces back to the beam.
            Part(t, "CrossShaft", PrimitiveType.Cylinder, new Vector3(0f, 0.75f, 0f), new Vector3(0.07f, 0.48f, 0.07f), steel, new Vector3(0f, 0f, 90f));
            foreach (float x in new[] { -0.42f, 0.42f })
                Part(t, "LowerPin", PrimitiveType.Cylinder, new Vector3(x, 0.75f, 0f), new Vector3(0.035f, 0.06f, 0.035f), yellow, new Vector3(0f, 0f, 90f));
            Part(t, "Headstock_L", PrimitiveType.Cube, new Vector3(-0.17f, 1.05f, -0.06f), new Vector3(0.08f, 0.66f, 0.1f), frameBlue, new Vector3(-8f, 0f, -27f));
            Part(t, "Headstock_R", PrimitiveType.Cube, new Vector3(0.17f, 1.05f, -0.06f), new Vector3(0.08f, 0.66f, 0.1f), frameBlue, new Vector3(-8f, 0f, 27f));
            Part(t, "TopPlates", PrimitiveType.Cube, new Vector3(0f, 1.36f, -0.12f), new Vector3(0.1f, 0.14f, 0.14f), frameBlue);
            Part(t, "TopPinBolt", PrimitiveType.Cylinder, new Vector3(0f, 1.36f, -0.12f), new Vector3(0.03f, 0.07f, 0.03f), yellow, new Vector3(0f, 0f, 90f));
            Part(t, "Brace", PrimitiveType.Cube, new Vector3(0.12f, 1.0f, -0.45f), new Vector3(0.07f, 0.07f, 0.85f), frameBlue, new Vector3(-36f, 8f, 0f));
            Part(t, "HeadBox", PrimitiveType.Cube, new Vector3(0.12f, 0.72f, -0.38f), new Vector3(0.36f, 0.22f, 0.36f), frameBlue);
            // Main beam (rectangular tube) running diagonally back to the right, over the bodies.
            Part(t, "Beam", PrimitiveType.Cube, new Vector3(0.45f, 0.72f, -1.7f), new Vector3(0.16f, 0.16f, 3.1f), frameBlue, new Vector3(0f, 9f, 0f));
            var grounds = new System.Collections.Generic.List<Transform>();
            for (int i = 0; i < 3; i++)
            {
                float z = -0.95f - i * 0.9f, x = 0.25f + i * 0.36f;
                // Leg (standard) from the beam down to the frog, slightly raked.
                Part(t, "Leg" + i, PrimitiveType.Cube, new Vector3(x + 0.02f, 0.42f, z - 0.02f), new Vector3(0.06f, 0.6f, 0.14f), frameBlue, new Vector3(10f, 0f, 0f));
                Part(t, "Clamp" + i, PrimitiveType.Cube, new Vector3(x + 0.02f, 0.72f, z), new Vector3(0.2f, 0.2f, 0.2f), frameBlue);
                // Body: share (pointed plate), mouldboard (twisted surface), landside on the left, disc coulter ahead.
                Part(t, "Share" + i, PrimitiveType.Cube, new Vector3(x + 0.04f, 0.035f, z + 0.18f), new Vector3(0.38f, 0.02f, 0.16f), steel, new Vector3(-8f, -38f, 0f));
                Part(t, "Point" + i, PrimitiveType.Cube, new Vector3(x - 0.12f, 0.03f, z + 0.27f), new Vector3(0.05f, 0.03f, 0.14f), steel, new Vector3(-12f, -20f, 0f));
                MeshPart(t, "Mouldboard" + i, mouldboard, new Vector3(x - 0.14f, 0.04f, z + 0.3f), Vector3.zero, board);
                Part(t, "Landside" + i, PrimitiveType.Cube, new Vector3(x - 0.14f, 0.08f, z - 0.05f), new Vector3(0.02f, 0.12f, 0.6f), steel);
                Part(t, "Frog" + i, PrimitiveType.Cube, new Vector3(x - 0.05f, 0.14f, z + 0.02f), new Vector3(0.14f, 0.2f, 0.3f), frameBlue);
                var coulter = Point(t, "Coulter" + i, new Vector3(x - 0.13f, 0.2f, z + 0.62f));
                Part(coulter, "Disc", PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.42f, 0.006f, 0.42f), steel, new Vector3(0f, 0f, 90f));
                Part(coulter, "Arm", PrimitiveType.Cube, new Vector3(0f, 0.3f, -0.05f), new Vector3(0.04f, 0.55f, 0.05f), frameBlue, new Vector3(-12f, 0f, 0f));
                grounds.Add(Point(t, "ShareTip" + i, new Vector3(x - 0.12f, 0.02f, z + 0.25f)));
            }
            // Depth wheel on a swinging arm at the rear, and a parking stand under the headstock.
            Part(t, "DepthArm", PrimitiveType.Cube, new Vector3(1.05f, 0.62f, -2.65f), new Vector3(0.06f, 0.06f, 0.5f), frameBlue, new Vector3(-20f, 0f, 0f));
            Wheel(t, "DepthWheel", new Vector3(1.15f, 0.55f, -2.85f) /* rides on the surface at working depth */, smallTyre, 0.3f, 0.16f, yellow);
            Part(t, "Stand", PrimitiveType.Cube, new Vector3(0.12f, 0.33f, -0.38f), new Vector3(0.06f, 0.62f, 0.06f), frameBlue);
            Part(t, "StandFoot", PrimitiveType.Cube, new Vector3(0.12f, 0.02f, -0.38f), new Vector3(0.2f, 0.03f, 0.2f), frameBlue);
            imp.groundPoints = grounds.ToArray();
            // Physics: frame + bodies box (rests on the shares), a stand under the headstock.
            Box(go, new Vector3(0.5f, 0.45f, -1.8f), new Vector3(1.4f, 0.85f, 2.4f));
            Box(go, new Vector3(0f, 0.37f, 0f), new Vector3(0.9f, 0.74f, 0.25f));
            imp.trail = Trail(t, new Vector3(0.55f, 0f, -2.2f), 1.05f, soil);
            Body(go, imp.mass, new Vector3(0.3f, 0.45f, -1.4f));
        }

        // ---- 3 m front disc mower on the front linkage (PTO)
        {
            var go = NewImplement(parent, "Mower", new Vector3(-14f, 0f, 6f), 0f);
            var imp = go.AddComponent<Implement>();
            imp.displayName = "MOWER"; imp.mount = Implement.Mount.Front;
            imp.mass = 900f; imp.centreOfMassOffset = new Vector3(0f, -0.2f, 0.8f);
            imp.workingDepth = 0f; imp.ptoKW = 30f; imp.spinRatio = 3f;
            var t = go.transform;
            imp.hitchPoint = Point(t, "HitchPoint", new Vector3(0f, 0.45f, 0f));
            // A-frame headstock with the two lower hooks and a top bracket, gearbox, PTO shaft guard.
            Part(t, "AFrame_L", PrimitiveType.Cube, new Vector3(-0.2f, 0.72f, 0.12f), new Vector3(0.08f, 0.62f, 0.08f), red, new Vector3(0f, 0f, 22f));
            Part(t, "AFrame_R", PrimitiveType.Cube, new Vector3(0.2f, 0.72f, 0.12f), new Vector3(0.08f, 0.62f, 0.08f), red, new Vector3(0f, 0f, -22f));
            Part(t, "LowerBar", PrimitiveType.Cube, new Vector3(0f, 0.45f, 0.12f), new Vector3(0.8f, 0.09f, 0.09f), red);
            foreach (float x in new[] { -0.38f, 0.38f })
                Part(t, "Hook", PrimitiveType.Cube, new Vector3(x, 0.45f, 0.05f), new Vector3(0.05f, 0.12f, 0.1f), yellow);
            Part(t, "TopBracket", PrimitiveType.Cube, new Vector3(0f, 1.0f, 0.12f), new Vector3(0.14f, 0.12f, 0.12f), red);
            Part(t, "Gearbox", PrimitiveType.Cube, new Vector3(0f, 0.6f, 0.38f), new Vector3(0.3f, 0.26f, 0.26f), dark);
            Part(t, "ShaftGuard", PrimitiveType.Cylinder, new Vector3(0f, 0.62f, 0.05f), new Vector3(0.1f, 0.18f, 0.1f), yellow, new Vector3(90f, 0f, 0f));
            Part(t, "Arm", PrimitiveType.Cube, new Vector3(0f, 0.48f, 0.62f), new Vector3(0.18f, 0.14f, 0.5f), red, new Vector3(18f, 0f, 0f));
            // Cutter bar with six discs, each with two knives; the drum carriers.
            Part(t, "CutterBar", PrimitiveType.Cube, new Vector3(0f, 0.06f, 1.15f), new Vector3(3.0f, 0.06f, 0.5f), steel);
            Part(t, "BarNose", PrimitiveType.Cube, new Vector3(0f, 0.05f, 1.42f), new Vector3(3.0f, 0.04f, 0.06f), dark);
            var discs = new System.Collections.Generic.List<Transform>();
            for (int i = 0; i < 6; i++)
            {
                var hub = Point(t, "Disc" + i, new Vector3(-1.25f + i * 0.5f, 0.11f, 1.15f));
                Part(hub, "Plate", PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.36f, 0.015f, 0.24f), red);
                Part(hub, "Knife_A", PrimitiveType.Cube, new Vector3(0.2f, 0f, 0f), new Vector3(0.1f, 0.006f, 0.045f), steel);
                Part(hub, "Knife_B", PrimitiveType.Cube, new Vector3(-0.2f, 0f, 0f), new Vector3(0.1f, 0.006f, 0.045f), steel);
                Part(hub, "Cap", PrimitiveType.Cylinder, new Vector3(0f, 0.025f, 0f), new Vector3(0.1f, 0.02f, 0.1f), dark);
                discs.Add(hub);
            }
            imp.spinners = discs.ToArray();
            // Guard: frame tubes, a sloping hood, side shields and a rubber curtain at the front.
            Part(t, "GuardFrame", PrimitiveType.Cube, new Vector3(0f, 0.55f, 1.0f), new Vector3(3.0f, 0.06f, 0.06f), red);
            Part(t, "Hood", PrimitiveType.Cube, new Vector3(0f, 0.48f, 1.08f), new Vector3(3.0f, 0.03f, 0.76f), red, new Vector3(12.5f, 0f, 0f));   // slopes down to the front
            Part(t, "HoodTop", PrimitiveType.Cube, new Vector3(0f, 0.57f, 0.64f), new Vector3(3.0f, 0.03f, 0.2f), red);
            foreach (float x in new[] { -1.52f, 1.52f })
                Part(t, "SideShield", PrimitiveType.Cube, new Vector3(x, 0.3f, 1.05f), new Vector3(0.03f, 0.5f, 0.85f), red);
            Part(t, "Curtain", PrimitiveType.Cube, new Vector3(0f, 0.22f, 1.48f), new Vector3(3.0f, 0.36f, 0.015f), dark);
            foreach (float x in new[] { -1.45f, 1.45f })
                Part(t, "Skid", PrimitiveType.Cube, new Vector3(x, 0.02f, 1.15f), new Vector3(0.08f, 0.03f, 0.55f), steel);
            imp.groundPoints = new[] { Point(t, "Skid_L", new Vector3(-1.45f, 0.03f, 1.15f)), Point(t, "Skid_R", new Vector3(1.45f, 0.03f, 1.15f)) };
            Box(go, new Vector3(0f, 0.25f, 1.05f), new Vector3(3.0f, 0.5f, 1.0f));
            Box(go, new Vector3(0f, 0.22f, 0.1f), new Vector3(0.6f, 0.44f, 0.15f));
            imp.trail = Trail(t, new Vector3(0f, 0f, 1.0f), 2.8f, mown);
            Body(go, imp.mass, new Vector3(0f, 0.3f, 0.8f));
        }

        // ---- 900 kg front weight on the front linkage (ballast for heavy rear implements)
        {
            var go = NewImplement(parent, "FrontWeight", new Vector3(-19.5f, 0f, 6f), 0f);
            var imp = go.AddComponent<Implement>();
            imp.displayName = "FRONT WEIGHT"; imp.mount = Implement.Mount.Front;
            imp.mass = 900f; imp.centreOfMassOffset = new Vector3(0f, 0.05f, 0.38f);
            var t = go.transform;
            imp.hitchPoint = Point(t, "HitchPoint", new Vector3(0f, 0.45f, 0f));
            // Carrier: lower pin bar, two side plates up to a top bracket; the cast weight in front, a tow eye at the bottom.
            Part(t, "PinBar", PrimitiveType.Cube, new Vector3(0f, 0.45f, 0.04f), new Vector3(0.82f, 0.08f, 0.08f), green);
            foreach (float x in new[] { -0.38f, 0.38f })
                Part(t, "Pin", PrimitiveType.Cylinder, new Vector3(x, 0.45f, 0.04f), new Vector3(0.035f, 0.05f, 0.035f), yellow, new Vector3(0f, 0f, 90f));
            foreach (float x in new[] { -0.25f, 0.25f })
                Part(t, "SidePlate", PrimitiveType.Cube, new Vector3(x, 0.66f, 0.1f), new Vector3(0.03f, 0.5f, 0.2f), green);
            Part(t, "TopBracket", PrimitiveType.Cube, new Vector3(0f, 0.92f, 0.1f), new Vector3(0.55f, 0.06f, 0.18f), green);
            // Cast iron block: a stack of slabs with grooves between them and a sloping top.
            for (int i = 0; i < 5; i++)
                Part(t, "Slab" + i, PrimitiveType.Cube, new Vector3(0f, 0.22f + i * 0.14f, 0.42f), new Vector3(0.98f - i * 0.03f, 0.125f, 0.5f), cast);
            Part(t, "TopSlope", PrimitiveType.Cube, new Vector3(0f, 0.9f, 0.5f), new Vector3(0.82f, 0.09f, 0.36f), cast, new Vector3(14f, 0f, 0f));
            Part(t, "Badge", PrimitiveType.Cube, new Vector3(0f, 0.6f, 0.675f), new Vector3(0.4f, 0.16f, 0.01f), green);
            Part(t, "BadgeStripe", PrimitiveType.Cube, new Vector3(0f, 0.6f, 0.68f), new Vector3(0.36f, 0.04f, 0.01f), yellow);
            Part(t, "TowEye", PrimitiveType.Cylinder, new Vector3(0f, 0.16f, 0.71f), new Vector3(0.1f, 0.03f, 0.1f), steel, new Vector3(90f, 0f, 0f));
            Part(t, "Feet", PrimitiveType.Cube, new Vector3(0f, 0.04f, 0.42f), new Vector3(0.9f, 0.08f, 0.45f), cast);
            Box(go, new Vector3(0f, 0.5f, 0.42f), new Vector3(0.98f, 0.98f, 0.52f));
            Body(go, imp.mass, new Vector3(0f, 0.5f, 0.4f));
        }

        // ---- Single-axle tipping trailer on the drawbar, loaded with 3 t of soil
        // The body starts 1.6 m behind the drawbar eye (like a real tipping trailer), so it can swing ~50° before touching the rear tyres.
        // It tips backwards on a hinge at the rear of the chassis, pushed by a telescopic ram in front of the headboard.
        {
            float eyeY = hitch.drawbarMount.localPosition.y;
            var go = NewImplement(parent, "Trailer", new Vector3(9f, 0f, -6f), 0f);
            var imp = go.AddComponent<Implement>();
            imp.displayName = "TRAILER"; imp.mount = Implement.Mount.Drawbar;
            imp.mass = 2500f;
            var t = go.transform;
            imp.hitchPoint = Point(t, "Eye", new Vector3(0f, eyeY, 0f));
            // Drawbar: an A-frame from the eye back to the chassis, eye ring, jack with crank and foot.
            float rise = -Mathf.Atan2(0.85f - eyeY, 1.7f) * Mathf.Rad2Deg;
            Part(t, "Drawbar_L", PrimitiveType.Cube, new Vector3(-0.28f, (eyeY + 0.85f) * 0.5f, -0.9f), new Vector3(0.1f, 0.12f, 1.85f), green, new Vector3(rise, 17f, 0f));
            Part(t, "Drawbar_R", PrimitiveType.Cube, new Vector3(0.28f, (eyeY + 0.85f) * 0.5f, -0.9f), new Vector3(0.1f, 0.12f, 1.85f), green, new Vector3(rise, -17f, 0f));
            Part(t, "EyeRing", PrimitiveType.Cylinder, new Vector3(0f, eyeY, 0f), new Vector3(0.14f, 0.025f, 0.14f), steel);
            Part(t, "EyeBlock", PrimitiveType.Cube, new Vector3(0f, eyeY + 0.01f, -0.14f), new Vector3(0.12f, 0.1f, 0.18f), green);
            Part(t, "RamMount", PrimitiveType.Cube, new Vector3(0f, 0.8f, -1.48f), new Vector3(0.62f, 0.1f, 0.16f), green);
            Part(t, "Jack", PrimitiveType.Cylinder, new Vector3(0.3f, 0.35f, -0.62f), new Vector3(0.07f, 0.3f, 0.07f), dark);
            Part(t, "JackCrank", PrimitiveType.Cube, new Vector3(0.3f, 0.7f, -0.62f), new Vector3(0.18f, 0.02f, 0.02f), dark);
            Part(t, "JackFoot", PrimitiveType.Cube, new Vector3(0.3f, 0.03f, -0.62f), new Vector3(0.18f, 0.03f, 0.18f), dark);
            // Chassis: two rails, cross members, axle, mudguards over the wheels, rear light bar with lamps and a number plate.
            foreach (float x in new[] { -0.55f, 0.55f })
                Part(t, "Rail", PrimitiveType.Cube, new Vector3(x, 0.88f, -3.9f), new Vector3(0.12f, 0.18f, 4.4f), green);
            for (int i = 0; i < 4; i++)
                Part(t, "CrossMember", PrimitiveType.Cube, new Vector3(0f, 0.88f, -2.0f - i * 1.35f), new Vector3(1.1f, 0.12f, 0.08f), green);
            Part(t, "Axle", PrimitiveType.Cube, new Vector3(0f, 0.5f, -4.2f), new Vector3(1.9f, 0.1f, 0.1f), dark);
            foreach (float x in new[] { -0.55f, 0.55f })
                Part(t, "Spring", PrimitiveType.Cube, new Vector3(x, 0.66f, -4.2f), new Vector3(0.08f, 0.1f, 1.0f), dark);
            foreach (float x in new[] { -0.95f, 0.95f })
            {
                Part(t, "Mudguard", PrimitiveType.Cube, new Vector3(x, 1.0f, -4.2f), new Vector3(0.44f, 0.025f, 0.7f), green);
                Part(t, "MudguardFront", PrimitiveType.Cube, new Vector3(x, 0.88f, -3.72f), new Vector3(0.44f, 0.025f, 0.32f), green, new Vector3(-50f, 0f, 0f));
                Part(t, "MudguardBack", PrimitiveType.Cube, new Vector3(x, 0.88f, -4.68f), new Vector3(0.44f, 0.025f, 0.32f), green, new Vector3(50f, 0f, 0f));
            }
            Part(t, "LightBar", PrimitiveType.Cube, new Vector3(0f, 0.72f, -6.12f), new Vector3(2.1f, 0.06f, 0.06f), dark);
            foreach (float x in new[] { -0.95f, 0.95f })
                Part(t, "Lamp", PrimitiveType.Cube, new Vector3(x, 0.72f, -6.16f), new Vector3(0.18f, 0.1f, 0.04f), lamp);
            Part(t, "NumberPlate", PrimitiveType.Cube, new Vector3(0.45f, 0.72f, -6.15f), new Vector3(0.34f, 0.1f, 0.01f), plate);
            var wheels = new System.Collections.Generic.List<SimpleWheels.Wheel>();
            foreach (float x in new[] { -0.95f, 0.95f })
                wheels.Add(new SimpleWheels.Wheel { pivot = Wheel(t, x < 0f ? "Wheel_L" : "Wheel_R", new Vector3(x, 0.5f, -4.2f), tyre, 0.5f, 0.38f, green), radius = 0.5f });

            // Tipping body: hinge at the rear of the chassis; everything below is in hinge space (z forward from the hinge).
            var body = Point(t, "Body", new Vector3(0f, 1.0f, -6.05f));
            Vector3 B(float x, float y, float z) => new Vector3(x, y - 1.0f, z + 6.05f);   // trailer space → hinge space
            Part(body, "Floor", PrimitiveType.Cube, B(0f, 1.04f, -3.9f), new Vector3(2.3f, 0.08f, 4.6f), green);
            Part(body, "SubFrame", PrimitiveType.Cube, B(0f, 0.97f, -3.9f), new Vector3(1.2f, 0.06f, 4.5f), dark);
            foreach (float x in new[] { -1.12f, 1.12f })
            {
                Part(body, "Side", PrimitiveType.Cube, B(x, 1.43f, -3.9f), new Vector3(0.05f, 0.7f, 4.6f), green);
                Part(body, "TopRail", PrimitiveType.Cylinder, B(x, 1.79f, -3.9f), new Vector3(0.07f, 2.3f, 0.07f), green, new Vector3(90f, 0f, 0f));
                for (int i = 0; i < 6; i++)
                    Part(body, "Stake", PrimitiveType.Cube, B(x * 1.03f, 1.43f, -1.75f - i * 0.86f), new Vector3(0.04f, 0.72f, 0.07f), green);
            }
            Part(body, "Headboard", PrimitiveType.Cube, B(0f, 1.53f, -1.63f), new Vector3(2.3f, 0.9f, 0.05f), green);
            for (int i = 0; i < 4; i++)
                Part(body, "Ladder", PrimitiveType.Cube, B(0.7f, 1.2f + i * 0.2f, -1.58f), new Vector3(0.35f, 0.025f, 0.04f), steel);
            var gate = Point(body, "Tailgate", B(0f, 1.78f, -6.17f));
            Part(gate, "Gate", PrimitiveType.Cube, new Vector3(0f, -0.35f, 0f), new Vector3(2.3f, 0.7f, 0.05f), green);
            Part(gate, "GateBar", PrimitiveType.Cube, new Vector3(0f, -0.6f, -0.03f), new Vector3(2.2f, 0.06f, 0.03f), dark);
            var cargo = Point(body, "Load", B(0f, 1.08f, -3.9f));
            MeshPart(cargo, "Heap", SaveMesh(Tipper.Mound(1.1f, 2.25f, 0.85f, 20, 6), "Impl_LoadHeap"), Vector3.zero, Vector3.zero, heapMat);
            var bodyBox = body.gameObject.AddComponent<BoxCollider>();
            bodyBox.center = B(0f, 1.42f, -3.9f);
            bodyBox.size = new Vector3(2.3f, 0.8f, 4.6f);
            Box(go, new Vector3(0f, 0.85f, -3.9f), new Vector3(1.3f, 0.2f, 4.4f));   // chassis

            // Telescopic ram: base on the drawbar, top pin at the headboard; seven nested stages.
            var ram = Point(t, "Ram", new Vector3(0f, 0.86f, -1.48f));
            var stages = new System.Collections.Generic.List<Transform>();
            const float stageLength = 0.7f;
            for (int i = 0; i < 7; i++)
            {
                var st = Point(ram, "Stage" + i, Vector3.zero);
                float r = 0.13f - i * 0.012f;
                Part(st, "Tube", PrimitiveType.Cylinder, new Vector3(0f, 0f, stageLength * 0.5f), new Vector3(r, stageLength * 0.5f, r), i == 0 ? dark : chrome, new Vector3(90f, 0f, 0f));
                stages.Add(st);
            }

            imp.stands = new[] { Box(go, new Vector3(0.3f, 0.3f, -0.62f), new Vector3(0.12f, 0.6f, 0.12f)) };   // jack: only while parked
            var rb = Body(go, imp.mass, new Vector3(0f, 1.0f, -3.85f));
            var sw = go.AddComponent<SimpleWheels>();
            sw.wheels = wheels.ToArray();
            sw.implement = imp;
            sw.staticShare = 0.46f;
            var tip = go.AddComponent<Tipper>();
            tip.implement = imp;
            tip.body = body;
            tip.tailgate = gate;
            tip.cargo = cargo;
            tip.heapMaterial = heapMat;
            tip.ram = ram;
            tip.ramStages = stages.ToArray();
            tip.stageLength = stageLength;
            tip.ramPinOnBody = B(0f, 1.56f, -1.52f);
            tip.emptyMass = imp.mass;
        }
        log.AppendLine("Implements: plough (rear), mower (front), front weight (front), tipping trailer (drawbar)");
    }

    static GameObject NewImplement(Transform parent, string name, Vector3 pos, float yaw)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(new Vector3(pos.x, TerrainGenerator.Height(pos.x, pos.z) + 0.02f, pos.z), Quaternion.Euler(0f, yaw, 0f));
        return go;
    }

    static Transform Point(Transform parent, string name, Vector3 local)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = local;
        return t;
    }

    static Transform Part(Transform parent, string name, PrimitiveType type, Vector3 pos, Vector3 size, Material mat, Vector3 euler = default)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(euler);
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go.transform;
    }

    static Transform MeshPart(Transform parent, string name, Mesh mesh, Vector3 pos, Vector3 euler, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(euler);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        return go.transform;
    }

    // Flat-shaded quads: each gets its own four vertices, wound to face `outward`.
    static void FlatQuad(System.Collections.Generic.List<Vector3> v, System.Collections.Generic.List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
    {
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f) { var x = b; b = d; d = x; }
        int i = v.Count;
        v.Add(a); v.Add(b); v.Add(c); v.Add(d);
        t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
    }

    static Mesh FlatMesh(string name, System.Collections.Generic.List<Vector3> v, System.Collections.Generic.List<int> t)
    {
        var m = new Mesh { name = name, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        m.SetVertices(v);
        m.SetTriangles(t, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    // Tractor-style tyre around the X axis: two staggered rows of lugs (a left and a right half, offset by half a pitch),
    // flat sidewalls down to the rim. Each half's tread is a castellated profile swept across its width.
    static Mesh TyreMesh(float radius, float width, int lugs, float lugDepth)
    {
        var v = new System.Collections.Generic.List<Vector3>();
        var t = new System.Collections.Generic.List<int>();
        int steps = lugs * 4;
        float rim = radius * 0.62f, half = width * 0.5f;
        Vector3 P(float a, float r, float x) => new Vector3(x, Mathf.Sin(a) * r, Mathf.Cos(a) * r);
        float R(int k, bool right) => ((k + (right ? 2 : 0)) % 4) < 2 ? radius : radius - lugDepth;   // lug for 2 of every 4 steps
        for (int k = 0; k < steps; k++)
        {
            float a0 = k * Mathf.PI * 2f / steps, a1 = (k + 1) * Mathf.PI * 2f / steps, am = 0.5f * (a0 + a1);
            Vector3 radial = P(am, 1f, 0f);
            foreach (bool right in new[] { false, true })
            {
                float x0 = right ? 0f : -half, x1 = right ? half : 0f, r = R(k, right), rNext = R(k + 1, right);
                FlatQuad(v, t, P(a0, r, x0), P(a0, r, x1), P(a1, r, x1), P(a1, r, x0), radial);   // tread / groove floor
                if (rNext != r)   // lug side face, looking into the groove next to it
                    FlatQuad(v, t, P(a1, r, x0), P(a1, r, x1), P(a1, rNext, x1), P(a1, rNext, x0), (P(a1 + 0.01f, 1f, 0f) - P(a1, 1f, 0f)) * Mathf.Sign(r - rNext));
                // Sidewall from the rim to the tread at this half's outer edge.
                float xs = right ? half : -half;
                FlatQuad(v, t, P(a0, rim, xs), P(a0, r, xs), P(a1, r, xs), P(a1, rim, xs), new Vector3(right ? 1f : -1f, 0f, 0f));
            }
            // Step between the two halves at x = 0 where one has a lug and the other a groove.
            float rl = R(k, false), rr = R(k, true);
            if (rl != rr)
                FlatQuad(v, t, P(a0, rl, 0f), P(a0, rr, 0f), P(a1, rr, 0f), P(a1, rl, 0f), new Vector3(rl > rr ? 1f : -1f, 0f, 0f));
        }
        return FlatMesh("Tyre", v, t);
    }

    // Mouldboard of a right-turning plough body, from the share's leading corner (origin): it sweeps back (−Z) and turns
    // the soil to the right (+X), the top curling further out than the bottom. Both sides are drawn.
    static Mesh MouldboardMesh()
    {
        const int nu = 6, nv = 8;
        const float length = 0.75f, height = 0.38f;
        Vector3 S(float u, float w) => new Vector3(w * (0.30f + 0.22f * u) + 0.14f * u * u * w * w,
                                                   height * u * (1f - 0.12f * w),
                                                   -length * w - 0.1f * u * (1f - w));
        var v = new System.Collections.Generic.List<Vector3>();
        var t = new System.Collections.Generic.List<int>();
        for (int i = 0; i < nu; i++)
        for (int j = 0; j < nv; j++)
        {
            float u0 = i / (float)nu, u1 = (i + 1) / (float)nu, w0 = j / (float)nv, w1 = (j + 1) / (float)nv;
            Vector3 a = S(u0, w0), b = S(u1, w0), c = S(u1, w1), d = S(u0, w1);
            Vector3 front = Vector3.Cross(c - a, b - d);   // either side; both are emitted
            FlatQuad(v, t, a, b, c, d, front);
            FlatQuad(v, t, a, b, c, d, -front);
        }
        return FlatMesh("Mouldboard", v, t);
    }

    static Collider Box(GameObject go, Vector3 centre, Vector3 size)
    {
        var b = go.AddComponent<BoxCollider>();
        b.center = centre;
        b.size = size;
        return b;
    }

    static Rigidbody Body(GameObject go, float mass, Vector3 com)
    {
        var rb = go.AddComponent<Rigidbody>();
        rb.mass = mass;
        rb.centerOfMass = com;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.angularDamping = 0.5f;
        return rb;
    }

    // Flat strip left on the ground while working (TrailRenderer facing up; Implement keeps it at ground height).
    static TrailRenderer Trail(Transform parent, Vector3 local, float width, Material mat)
    {
        var t = Point(parent, "Trail", local);
        var tr = t.gameObject.AddComponent<TrailRenderer>();
        tr.time = 1200f;
        tr.minVertexDistance = 0.5f;
        tr.widthMultiplier = width;
        tr.alignment = LineAlignment.TransformZ;
        tr.sharedMaterial = mat;
        tr.shadowCastingMode = ShadowCastingMode.Off;
        tr.receiveShadows = false;
        tr.emitting = false;
        tr.autodestruct = false;
        return tr;
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
        hud.hitch = tractor.GetComponent<Hitch>();
        hud.cab = tractor.GetComponent<CabControls>();

        hud.comfort = BuildVignette(camGo.transform, ctrl, vr);
        var controllerMat = ControllerMaterial();
        var leftCtl = BuildController(offset, true, controllerMat);
        var rightCtl = BuildController(offset, false, controllerMat);
        BuildAudio(tractor, vr);

        // Hands: grip poses for grabbing (controllers), tracked hand meshes, and the cab panel.
        rig.AddComponent<Foveation>();
        var hands = rig.AddComponent<VRHands>();
        hands.cameraOffset = offset;
        hands.leftGrip = GripPose(offset, "LeftHand");
        hands.rightGrip = GripPose(offset, "RightHand");
        hands.leftAim = leftCtl.transform;
        hands.rightAim = rightCtl.transform;
        hands.leftControllerVisual = leftCtl;
        hands.rightControllerVisual = rightCtl;
        var handMat = SaveMat(new Material(LitShader) { color = new Color(0.78f, 0.72f, 0.68f) }, "Hand");
        handMat.SetFloat("_Smoothness", 0.25f);
        hands.leftDriver = BuildHand(offset, true, handMat, log);
        hands.rightDriver = BuildHand(offset, false, handMat, log);
        BuildCabPanel(rig.transform, vr, tractor, hud.comfort, hands);

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

    // OpenXR grip pose (the centre of the hand around the handle): what the hands grab with.
    static Transform GripPose(Transform cameraOffset, string hand)
    {
        var t = new GameObject(hand + " Grip").transform;
        t.SetParent(cameraOffset, false);
        var tpd = t.gameObject.AddComponent<TrackedPoseDriver>();
        tpd.positionInput = new InputActionProperty(new InputAction(hand + "GripPos", InputActionType.Value, $"<XRController>{{{hand}}}/devicePosition", expectedControlType: "Vector3"));
        tpd.rotationInput = new InputActionProperty(new InputAction(hand + "GripRot", InputActionType.Value, $"<XRController>{{{hand}}}/deviceRotation", expectedControlType: "Quaternion"));
        tpd.trackingStateInput = new InputActionProperty(new InputAction(hand + "GripState", InputActionType.Value, $"<XRController>{{{hand}}}/trackingState", expectedControlType: "Integer"));
        return t;
    }

    // Tracked hand (XR Hands sample model) under the tracking space: XRHandTrackingEvents + XRHandSkeletonDriver move the bones,
    // XRHandMeshController shows the mesh only while the hand is tracked.
    static UnityEngine.XR.Hands.XRHandSkeletonDriver BuildHand(Transform cameraOffset, bool left, Material mat, StringBuilder log)
    {
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Models/Hands/{(left ? "Left" : "Right")}Hand.fbx");
        var root = new GameObject(left ? "Left Hand" : "Right Hand");
        root.transform.SetParent(cameraOffset, false);
        var events = root.AddComponent<UnityEngine.XR.Hands.XRHandTrackingEvents>();
        events.handedness = left ? UnityEngine.XR.Hands.Handedness.Left : UnityEngine.XR.Hands.Handedness.Right;
        var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx, root.transform);
        PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        var smr = model.GetComponentInChildren<SkinnedMeshRenderer>();
        smr.sharedMaterial = mat;
        smr.shadowCastingMode = ShadowCastingMode.Off;
        smr.updateWhenOffscreen = true;
        var driver = root.AddComponent<UnityEngine.XR.Hands.XRHandSkeletonDriver>();
        driver.handTrackingEvents = events;
        driver.rootTransform = model.GetComponentsInChildren<Transform>().First(t => t.name.ToLowerInvariant().Contains("wrist"));
        var missing = new System.Collections.Generic.List<string>();
        driver.jointTransformReferences = new System.Collections.Generic.List<UnityEngine.XR.Hands.JointToTransformReference>();   // null on a new component
        driver.FindJointsFromRoot(missing);
        var mesh = root.AddComponent<UnityEngine.XR.Hands.XRHandMeshController>();
        mesh.handTrackingEvents = events;
        mesh.handMeshRenderer = smr;
        mesh.showMeshWhenTrackingIsAcquired = true;
        mesh.hideMeshWhenTrackingIsLost = true;
        smr.enabled = false;
        log.AppendLine($"{root.name}: root bone {driver.rootTransform.name}, {driver.jointTransformReferences.Count} joints, missing: {string.Join(", ", missing)}");
        return driver;
    }

    // Cab panel at the right front, within reach, facing the driver (built by CabPanel at runtime from these materials).
    static void BuildCabPanel(Transform rig, VRCameraRig vr, GameObject tractor, ComfortVignette comfort, VRHands hands)
    {
        var unlit = Shader.Find("Universal Render Pipeline/Unlit");
        Material Flat(string name, Color c) { var m = new Material(unlit); m.SetColor("_BaseColor", c); return SaveMat(m, name); }
        var go = new GameObject("Cab Panel");
        go.transform.SetParent(rig, false);
        go.transform.localPosition = new Vector3(0.30f, -0.17f, 0.40f);
        go.transform.localRotation = Quaternion.LookRotation(go.transform.localPosition);   // face the eye point
        var panel = go.AddComponent<CabPanel>();
        panel.rig = vr;
        panel.tractor = tractor.GetComponent<TractorController>();
        panel.drivetrain = tractor.GetComponent<Drivetrain>();
        panel.hitch = tractor.GetComponent<Hitch>();
        panel.comfort = comfort;
        panel.input = tractor.GetComponent<TractorInput>();
        panel.hands = hands;
        panel.buttonMat = Flat("Panel_Button", new Color(0.16f, 0.2f, 0.16f));
        panel.buttonHotMat = Flat("Panel_ButtonHot", new Color(0.35f, 0.62f, 0.25f));
        panel.panelMat = Flat("Panel_Back", new Color(0.04f, 0.04f, 0.045f));
        panel.textMat = AssetDatabase.LoadAssetAtPath<Material>(GenDir + "/DashText.mat");
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.spatialBlend = 0.5f;
        src.volume = 0.5f;
        src.clip = ClickClip();
        panel.click = src;
        // The speed lever clicks into its neutral notch.
        var cab = tractor.GetComponent<CabControls>();
        var leverClick = cab.speedLever.gameObject.AddComponent<AudioSource>();
        leverClick.playOnAwake = false; leverClick.spatialBlend = 0.8f; leverClick.volume = 0.6f; leverClick.pitch = 0.7f;
        leverClick.clip = src.clip;
        cab.click = leverClick;
        // Doors: a low clunk when they shut or come off the latch.
        foreach (var d in cab.doors)
        {
            var a = d.hinge.gameObject.AddComponent<AudioSource>();
            a.playOnAwake = false; a.spatialBlend = 1f; a.volume = 0.9f; a.pitch = 0.35f;
            a.clip = src.clip;
            d.sound = a;
        }
        BuildCommandCenter(tractor, hands, panel, src.clip);
    }

    // CommandCenter display at the right front corner post, on a short arm, facing the driver's eye point.
    static void BuildCommandCenter(GameObject tractor, VRHands hands, CabPanel panel, AudioClip clickClip)
    {
        var unlit = Shader.Find("Universal Render Pipeline/Unlit");
        Material Flat(string name, Color c) { var m = new Material(unlit); m.SetColor("_BaseColor", c); return SaveMat(m, name); }
        var root = tractor.transform;
        Vector3 eye = root.Find("DriverEye").localPosition;
        var go = new GameObject("CommandCenter");
        go.transform.SetParent(root, false);
        go.transform.localPosition = new Vector3(0.50f, 2.40f, 1.00f);
        go.transform.localRotation = Quaternion.LookRotation(go.transform.localPosition - eye);   // +Z away from the driver
        var cc = go.AddComponent<CommandCenter>();
        cc.drivetrain = tractor.GetComponent<Drivetrain>();
        cc.tractor = tractor.GetComponent<TractorController>();
        cc.hitch = tractor.GetComponent<Hitch>();
        cc.hands = hands;
        cc.screenMat = Flat("CC_Screen", new Color(0.02f, 0.035f, 0.05f));
        var bezel = SaveMat(new Material(LitShader) { color = new Color(0.05f, 0.05f, 0.05f) }, "CC_Bezel");
        bezel.SetFloat("_Smoothness", 0.35f);
        cc.bezelMat = bezel;
        cc.barMat = Flat("CC_Bar", Color.white);   // tinted per bar (MaterialPropertyBlock)
        cc.buttonMat = panel.buttonMat;
        cc.buttonHotMat = panel.buttonHotMat;
        cc.textMat = panel.textMat;
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false; src.spatialBlend = 0.8f; src.volume = 0.5f;
        src.clip = clickClip;
        cc.click = src;
        // Arm from the back of the display to the corner post.
        var arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(arm.GetComponent<Collider>());
        arm.name = "Arm";
        arm.transform.SetParent(root, false);
        Vector3 from = go.transform.localPosition + go.transform.localRotation * new Vector3(0f, 0f, 0.02f), to = new Vector3(0.60f, 2.42f, 1.36f);
        arm.transform.localPosition = (from + to) * 0.5f;
        arm.transform.localRotation = Quaternion.LookRotation(to - from);
        arm.transform.localScale = new Vector3(0.03f, 0.03f, (to - from).magnitude);
        arm.GetComponent<MeshRenderer>().sharedMaterial = bezel;
        arm.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
    }

    static ControllerVisual BuildController(Transform cameraOffset, bool left, Material mat)
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
        return vis;
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
            try { File.WriteAllBytes("Logs/Previews/" + file, tex.EncodeToPNG()); }
            catch (IOException e) { Debug.LogWarning($"[Setup] Preview {file} not written (open in a viewer?): {e.Message}"); }
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
        // The farmyard from above, a stretch of the main road, and the gate into the north field (mud, soil, grass).
        Vector3 At(float x, float z, float up) => new Vector3(x, TerrainGenerator.Height(x, z) + up, z);
        Shot("farmyard.png", new Vector3(42f, 28f, -60f), Quaternion.LookRotation(new Vector3(-8f, 0f, -6f) - new Vector3(42f, 28f, -60f)));
        Shot("road.png", At(77f, -30f, 2.5f), Quaternion.LookRotation(At(80f, 60f, 0f) - At(77f, -30f, 2.5f)));
        Shot("field_gate.png", At(-6f, 96f, 3f), Quaternion.LookRotation(At(4f, 135f, 0f) - At(-6f, 96f, 3f)));
        Shot("ground_close.png", At(3f, 110f, 1.6f), Quaternion.LookRotation(At(3.5f, 116f, 0f) - At(3f, 110f, 1.6f)));

        // Linkages and implements.
        var hitch = tractor.GetComponent<Hitch>();
        Vector3 rearCam = tractor.TransformPoint(new Vector3(2.6f, 1.6f, -4.2f));
        Quaternion rearLook = Quaternion.LookRotation(tractor.TransformPoint(new Vector3(0f, 0.9f, -0.8f)) - rearCam);
        hitch.PreviewPose(0f, 1f);
        Shot("hitch_low.png", rearCam, rearLook);
        hitch.PreviewPose(1f, 0f);
        Shot("hitch_high.png", rearCam, rearLook);
        Vector3 frontCam = tractor.TransformPoint(new Vector3(2.4f, 1.5f, 6.5f));
        Shot("front_linkage_down.png", frontCam, Quaternion.LookRotation(tractor.TransformPoint(new Vector3(0f, 0.8f, 3.4f)) - frontCam));
        // Front linkage cylinders from inside the left wheel track: links folded up / down.
        cam.fieldOfView = 70f;
        Vector3 cylCam = tractor.TransformPoint(new Vector3(-0.62f, 1.0f, 3.45f));
        Quaternion cylLook = Quaternion.LookRotation(tractor.right);
        hitch.PreviewPose(1f, 1f);
        Shot("front_cyl_up.png", cylCam, cylLook);
        hitch.PreviewPose(1f, 0.5f);
        Shot("front_cyl_mid.png", cylCam, cylLook);
        hitch.PreviewPose(1f, 0f);
        Shot("front_cyl_down.png", cylCam, cylLook);
        cam.fieldOfView = 90f;
        Shot("implements.png", new Vector3(0f, 9f, -22f), Quaternion.LookRotation(new Vector3(-3f, 0f, -3f) - new Vector3(0f, 9f, -22f)));
        // Plough on the hitch at working depth (posed by hand, like Implement does when coupled).
        var plough = GameObject.Find("Plough").transform;
        var ploughImp = plough.GetComponent<Implement>();
        Vector3 ploughHome = plough.position; Quaternion ploughRot = plough.rotation;
        // Coupled at its pin height (top link set there), then at working depth and fully raised: the top link pitches it.
        hitch.PreviewAttach(ploughImp);
        void PoseOnMount() { plough.rotation = hitch.rearMount.rotation; plough.position = hitch.rearMount.position - plough.rotation * ploughImp.hitchPoint.localPosition; }
        hitch.PreviewPose(0.17f, 1f);
        PoseOnMount();
        Vector3 pc = tractor.TransformPoint(new Vector3(4.5f, 2.2f, -5.5f));
        Shot("plough_attached.png", pc, Quaternion.LookRotation(tractor.TransformPoint(new Vector3(0.3f, 0.4f, -2.2f)) - pc));
        Vector3 ps = tractor.TransformPoint(new Vector3(3.6f, 1.3f, -1.6f));
        Quaternion psLook = Quaternion.LookRotation(tractor.TransformPoint(new Vector3(0f, 1.0f, -1.6f)) - ps);
        Shot("toplink_work.png", ps, psLook);
        hitch.PreviewPose(1f, 1f);
        PoseOnMount();
        Shot("toplink_raised.png", ps, psLook);
        hitch.PreviewDetach();
        plough.SetPositionAndRotation(ploughHome, ploughRot);
        hitch.PreviewPose(1f, 1f);

        // Cab panel (open) and the speed lever pushed forward, from the seat looking right.
        var panelComp = Object.FindAnyObjectByType<CabPanel>();
        var cabCtl = tractor.GetComponent<CabControls>();
        panelComp.PreviewOpen();
        cabCtl.speedLever.localRotation = Quaternion.AngleAxis(15f, Vector3.right);
        cam.fieldOfView = 70f;
        Shot("panel.png", seat.position, Quaternion.LookRotation(panelComp.transform.position + new Vector3(0f, -0.08f, 0f) - seat.position));
        // Close-up of the lever, pushed fully forward: stalk, shell, button block and buttons must move together.
        cabCtl.speedLever.localRotation = Quaternion.AngleAxis(cabCtl.leverTilt, Vector3.right);
        cam.fieldOfView = 40f;
        Vector3 knobW = cabCtl.speedLever.TransformPoint(cabCtl.knobLocal);
        Vector3 leverCam = tractor.TransformPoint(new Vector3(0.05f, 2.62f, 0.85f));   // inside the cab, front left of the lever
        Shot("lever.png", leverCam, Quaternion.LookRotation(knobW + tractor.up * -0.06f - leverCam));
        // Side views of the stalk at rest and fully forward / back (does anything stay behind at the gate?).
        Vector3 pivotW = cabCtl.speedLever.position;
        Vector3 sideCam = tractor.TransformPoint(cabCtl.speedLever.localPosition + new Vector3(-0.32f, 0.12f, 0f));
        Quaternion sideLook = Quaternion.LookRotation(pivotW + tractor.up * 0.08f - sideCam);
        foreach (var (file, tilt) in new[] { ("lever_side_rest.png", 0f), ("lever_side_fwd.png", 1f), ("lever_side_back.png", -1f) })
        {
            cabCtl.speedLever.localRotation = Quaternion.AngleAxis(tilt * cabCtl.leverTilt, Vector3.right);
            Shot(file, sideCam, sideLook);
        }
        cam.fieldOfView = 90f;
        cabCtl.speedLever.localRotation = Quaternion.identity;

        // Hydraulic remote levers: 1 pulled back (raise), 2 pushed forward (lower), 3 in the middle.
        var r1 = cabCtl.remotes[0].hinge; var r2 = cabCtl.remotes[1].hinge;
        r1.localRotation = Quaternion.AngleAxis(-20f, Vector3.right);
        r2.localRotation = Quaternion.AngleAxis(20f, Vector3.right);
        cam.fieldOfView = 40f;
        Vector3 remotesAt = cabCtl.remotes[1].hinge.TransformPoint(cabCtl.remotes[1].grabBox.center);
        Vector3 remoteCam = tractor.TransformPoint(new Vector3(0.12f, 2.60f, 0.78f));
        Shot("remotes.png", remoteCam, Quaternion.LookRotation(remotesAt + tractor.up * -0.05f - remoteCam));
        Vector3 remoteSide = tractor.TransformPoint(cabCtl.remotes[1].hinge.localPosition + new Vector3(-0.38f, 0.14f, 0f));
        Shot("remotes_side.png", remoteSide, Quaternion.LookRotation(cabCtl.remotes[1].hinge.position + tractor.up * 0.08f - remoteSide));
        r1.localRotation = r2.localRotation = Quaternion.identity;

        panelComp.gameObject.SetActive(false);   // keep it out of the other shots

        // CommandCenter on the right corner post, from the seat (drivetrain values as after start-up, hand throttle at 1500).
        var cc = Object.FindAnyObjectByType<CommandCenter>();
        var drive = tractor.GetComponent<Drivetrain>();
        typeof(Drivetrain).GetMethod("Awake", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(drive, null);
        drive.MoveHandThrottle(1f, (1500f - drive.idleRpm) / drive.handThrottleRate);
        cc.Build();
        cam.fieldOfView = 70f;
        Shot("commandcenter_seat.png", seat.position, Quaternion.LookRotation(cc.transform.position + tractor.right * -0.12f - seat.position));
        cam.fieldOfView = 35f;
        Shot("commandcenter.png", seat.position, Quaternion.LookRotation(cc.transform.position - seat.position));
        cam.fieldOfView = 90f;

        // Both doors open, from the front left.
        foreach (var d in cabCtl.doors) d.hinge.localRotation = Quaternion.AngleAxis(d.max, d.axis);
        Vector3 doorCam = tractor.TransformPoint(new Vector3(-4.2f, 2.8f, 3.6f));
        Shot("doors_open.png", doorCam, Quaternion.LookRotation(tractor.TransformPoint(new Vector3(0f, 1.9f, 0.6f)) - doorCam));
        Vector3 doorTop = tractor.TransformPoint(new Vector3(-0.3f, 5.5f, 0.9f));
        Shot("doors_top.png", doorTop, Quaternion.LookRotation(tractor.TransformPoint(new Vector3(-0.6f, 1.6f, 0.5f)) - doorTop, tractor.forward));
        Vector3 doorSide = tractor.TransformPoint(new Vector3(-2.6f, 1.75f, 2.2f));
        Shot("doors_side.png", doorSide, Quaternion.LookRotation(tractor.TransformPoint(new Vector3(-1.1f, 1.75f, 0.6f)) - doorSide));
        foreach (var d in cabCtl.doors) d.hinge.localRotation = Quaternion.identity;

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
        ShowcaseShots(tractor, cam, Shot);
        Debug.Log("[Setup] Preview images written to Logs/Previews");
    }

    // README pictures: the tractor posed out in the world (scene not saved afterwards). Implements are placed the way
    // Implement does when coupled: their coupling point on the tractor's mount, facing the tractor's way.
    static void ShowcaseShots(Transform tractor, Camera cam, System.Action<string, Vector3, Quaternion> shot)
    {
        var hitch = tractor.GetComponent<Hitch>();
        Implement Imp(string name) => GameObject.Find(name).GetComponent<Implement>();
        void Pose(float x, float z, float yaw)
        {
            var rot = Quaternion.FromToRotation(Vector3.up, TerrainGenerator.Normal(x, z, 2f)) * Quaternion.Euler(0f, yaw, 0f);
            tractor.SetPositionAndRotation(new Vector3(x, TerrainGenerator.Height(x, z), z), rot);
        }
        void Couple(Implement imp, Transform mount)
        {
            imp.transform.rotation = mount.rotation;
            imp.transform.position = mount.position - imp.transform.rotation * imp.hitchPoint.localPosition;
        }
        void Look(string file, Vector3 camLocal, Vector3 targetLocal, float fov)
        {
            cam.fieldOfView = fov;
            Vector3 c = tractor.TransformPoint(camLocal), t = tractor.TransformPoint(targetLocal);
            c.y = Mathf.Max(c.y, TerrainGenerator.Height(c.x, c.z) + 0.6f);
            shot(file, c, Quaternion.LookRotation(t - c));
        }

        // On the gravel track out of the yard, heading east.
        hitch.PreviewPose(1f, 1f);
        Pose(48f, 0f, 90f);
        Look("hero.png", new Vector3(-5.5f, 1.4f, 8f), new Vector3(0f, 1.5f, 1.3f), 55f);
        // Mesh LOD check: the same view with every tractor part forced to LOD 1 / 2 / 3.
        var tractorRenderers = tractor.GetComponentsInChildren<MeshRenderer>(true);
        foreach (short lod in new short[] { 1, 2, 3 })
        {
            foreach (var r in tractorRenderers) r.forceMeshLod = lod;
            Look($"lod{lod}.png", new Vector3(-5.5f, 1.4f, 8f), new Vector3(0f, 1.5f, 1.3f), 55f);
        }
        foreach (var r in tractorRenderers) r.forceMeshLod = -1;

        // Ploughing in the north field: plough at working depth.
        Pose(40f, 220f, 0f);
        hitch.PreviewAttach(Imp("Plough"));
        hitch.PreviewPose(0.17f, 1f);
        Couple(Imp("Plough"), hitch.rearMount);
        Look("ploughing.png", new Vector3(7f, 2.6f, -6f), new Vector3(0.3f, 0.8f, -1.8f), 60f);

        // Plough raised for transport, front weight on the front linkage to keep weight on the steering axle.
        Pose(48f, 0f, 90f);
        hitch.PreviewPose(1f, 0.85f);
        Couple(Imp("Plough"), hitch.rearMount);
        Couple(Imp("FrontWeight"), hitch.frontMount);
        Look("front_weight.png", new Vector3(-6f, 1.8f, 7.5f), new Vector3(0f, 1.1f, 0.8f), 60f);
        hitch.PreviewDetach();

        // Towing the trailer up the north track.
        hitch.PreviewPose(1f, 1f);
        Pose(0f, 70f, 0f);
        Couple(Imp("Trailer"), hitch.drawbarMount);
        Look("trailer.png", new Vector3(-7.5f, 3f, 5f), new Vector3(0f, 1.2f, -2.5f), 60f);

        // Tipping: body raised, part of the load already on the ground behind the trailer.
        var tipper = Imp("Trailer").GetComponent<Tipper>();
        typeof(Tipper).GetMethod("Awake", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(tipper, null);
        tipper.Move(1f, 4f);
        for (int i = 0; i < 25; i++) tipper.FrameUpdate(0.1f);
        tipper.Move(1f, 4f);
        for (int i = 0; i < 10; i++) tipper.FrameUpdate(0.1f);
        Look("tipping.png", new Vector3(-8f, 2.5f, -3f), new Vector3(0f, 1.6f, -5.5f), 60f);
        Look("tipping_rear.png", new Vector3(-4.5f, 2.2f, -12f), new Vector3(0f, 1.2f, -7f), 60f);
        tipper.Move(-1f, 10f);

        // Front mower on the pasture, linkage down.
        Pose(-70f, 40f, 0f);
        hitch.PreviewPose(1f, 0.04f);
        Couple(Imp("Mower"), hitch.frontMount);
        Look("mower.png", new Vector3(6f, 2.2f, 10f), new Vector3(0f, 0.9f, 3.5f), 55f);
        cam.fieldOfView = 90f;
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
