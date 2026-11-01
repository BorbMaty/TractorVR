using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Trees of the land around Reghin (phase 5 step 4), modelled in Blender (tools/blender/trees.py) and drawn instanced:
/// every frame each tree in view is put on its species' near list (within the tier's <see cref="QualityTier.Budget.detailDistance"/>:
/// Quest 120 m, PC 400 m; Full mesh on PC, Light on the Quest) or far list (a ~50-triangle mesh), then one
/// <see cref="Graphics.RenderMeshInstanced"/> per species, list and sub-mesh. Placed by <see cref="TreeLayout"/> (deterministic, so
/// nothing is stored); each trunk gets a static capsule collider (the tractor stops at a tree like at a wall).
/// </summary>
public class Trees : MonoBehaviour
{
    [System.Serializable]
    public class Species
    {
        public TreeLayout.Kind kind;
        public Mesh near, nearLight, far;       // far: the Light far mesh (both tiers)
        public Material[] nearMats, farMats;    // one per sub-mesh
        public float trunkRadius = 0.3f, trunkHeight = 3f;
        [System.NonSerialized] public List<Matrix4x4> nearList = new List<Matrix4x4>(), farList = new List<Matrix4x4>();
    }

    public Species[] species = new Species[0];
    public float farDistance = 1400f;
    readonly Plane[] planes = new Plane[6];
    List<TreeLayout.Tree> trees;
    Matrix4x4[] matrices;

    public int Count => trees?.Count ?? 0;
    public int DrawnNear { get; private set; }
    public int DrawnFar { get; private set; }

    void Start()
    {
        Build();
        if (Application.isPlaying) MakeColliders();
    }

    public void Build()
    {
        trees = TreeLayout.Generate();
        matrices = new Matrix4x4[trees.Count];
        for (int i = 0; i < trees.Count; i++)
        {
            var t = trees[i];
            matrices[i] = Matrix4x4.TRS(t.position, Quaternion.Euler(0f, t.yaw, 0f), new Vector3(t.scale, t.scale * t.stretch, t.scale));
        }
    }

    void MakeColliders()
    {
        var root = new GameObject("TreeTrunks").transform;
        root.SetParent(transform, false);
        for (int i = 0; i < trees.Count; i++)
        {
            var t = trees[i];
            var sp = Find(t.kind);
            if (sp == null) continue;
            var go = new GameObject("Trunk");
            go.isStatic = true;
            go.transform.SetParent(root, false);
            go.transform.position = t.position;
            var c = go.AddComponent<CapsuleCollider>();
            float r = sp.trunkRadius * t.scale, h = sp.trunkHeight * t.scale * t.stretch;
            c.radius = r;
            c.height = h;
            c.center = new Vector3(0f, h * 0.5f, 0f);
        }
    }

    Species Find(TreeLayout.Kind k) => System.Array.Find(species, s => s.kind == k);

    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam) Submit(cam, null);
    }

    /// <summary>Draws the trees for <paramref name="cam"/> (<paramref name="only"/>: just that camera, for the editor renders).</summary>
    public void Submit(Camera cam, Camera only)
    {
        if (trees == null) Build();
        GeometryUtility.CalculateFrustumPlanes(cam, planes);
        Vector3 c = cam.transform.position;
        var budget = QualityTier.CurrentBudget;
        bool pc = QualityTier.Current == QualityTier.Tier.PC;
        float near2 = budget.detailDistance * budget.detailDistance, far2 = farDistance * farDistance;
        foreach (var s in species) { s.nearList.Clear(); s.farList.Clear(); }
        for (int i = 0; i < trees.Count; i++)
        {
            var t = trees[i];
            Vector3 d = t.position - c;
            float d2 = d.x * d.x + d.z * d.z;
            if (d2 > far2) continue;
            // Sphere test against the frustum (crowns up to ~12 m above the foot; VR: the culling frustum is narrower than both eyes).
            Vector3 mid = t.position + new Vector3(0f, 8f * t.scale, 0f);
            bool visible = true;
            for (int p = 0; p < 6 && visible; p++)
                if (planes[p].GetDistanceToPoint(mid) < -16f * t.scale) visible = false;
            if (!visible) continue;
            var sp = Find(t.kind);
            if (sp == null) continue;
            (d2 < near2 ? sp.nearList : sp.farList).Add(matrices[i]);
        }
        DrawnNear = DrawnFar = 0;
        var shadows = budget.terrainShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        foreach (var s in species)
        {
            var nearMesh = pc || s.nearLight == null ? s.near : s.nearLight;
            Draw(nearMesh, s.nearMats, s.nearList, shadows, only);
            Draw(s.far, s.farMats, s.farList, ShadowCastingMode.Off, only);
            DrawnNear += s.nearList.Count;
            DrawnFar += s.farList.Count;
        }
    }

    static void Draw(Mesh mesh, Material[] mats, List<Matrix4x4> list, ShadowCastingMode shadows, Camera only)
    {
        if (mesh == null || list.Count == 0) return;
        for (int sub = 0; sub < mesh.subMeshCount && sub < mats.Length; sub++)
        {
            var rp = new RenderParams(mats[sub]) { shadowCastingMode = shadows, receiveShadows = true, camera = only };
            for (int start = 0; start < list.Count; start += 1023)
                Graphics.RenderMeshInstanced(rp, mesh, sub, list, Mathf.Min(1023, list.Count - start), start);
        }
    }
}

/// <summary>
/// Where the trees stand (phase 5 step 4), from the world's own rules, so it never needs saving:
///   balks ........ solitary walnut and wild pear trees on the grass balks between the countryside's strip parcels
///                  (the same strip pattern GroundLit paints: 230 m blocks, 12–30 m strips);
///   ravines ...... black locust (acacia) groves on the steepest countryside slopes, and an acacia row along the west track;
///   roads ........ Lombardy poplars along both sides of the main road;
///   wet ground ... white willows along the stream in the valley east of the farm (<see cref="WorldLayout.Stream"/>) and in the pasture's
///                  wet hollows;
///   farm ......... a walnut and a pear tree in the farmhouse garden, a walnut by the barn.
/// Never on a field, a road or track (4 m clear), the farmyard, the practice area or within 6 m of a placed asset.
/// </summary>
public static class TreeLayout
{
    public enum Kind { Walnut, Pear, Acacia, Poplar, Willow }
    public struct Tree { public Kind kind; public Vector3 position; public float yaw, scale, stretch; }

    public static List<Tree> Generate()
    {
        if (!GroundMap.Built) GroundMap.Build();
        var list = new List<Tree>();
        var rnd = new System.Random(4242);
        float R() => (float)rnd.NextDouble();

        void Add(Kind k, float x, float z, float scale = 1f)
        {
            if (!Free(x, z, k == Kind.Poplar ? 4f : 5f)) return;
            foreach (var t in list)   // no two crowns in one spot
                if ((t.position.x - x) * (t.position.x - x) + (t.position.z - z) * (t.position.z - z) < 16f) return;
            list.Add(new Tree
            {
                kind = k, position = new Vector3(x, TerrainGenerator.SurfaceHeight(x, z), z), yaw = R() * 360f,
                scale = scale * (0.8f + 0.4f * R()), stretch = 0.9f + 0.2f * R(),
            });
        }

        // Farm: the yard's own trees.
        Add(Kind.Walnut, 45f, 24f, 1.1f);
        Add(Kind.Pear, 47f, 13f, 0.9f);
        Add(Kind.Walnut, -50f, -22f);

        // Balks: candidates on a jittered 45 m grid over the countryside, moved onto the nearest strip boundary.
        for (float gx = -980f; gx < 980f; gx += 45f)
        for (float gz = -980f; gz < 980f; gz += 45f)
        {
            float x = gx + (R() - 0.5f) * 40f, z = gz + (R() - 0.5f) * 40f;
            if (TerrainGenerator.Countryside(x, z) < 0.85f) continue;
            if (R() > 0.32f) continue;
            float bx = Mathf.Floor(x / 230f), bz = Mathf.Floor(z / 230f);
            float h1 = Frac(Mathf.Sin(bx * 12.9898f + bz * 78.233f) * 43758.5453f);
            float h2 = Frac(Mathf.Sin(bx * 39.346f + bz * 11.135f) * 24634.6345f);
            float ang = h1 * Mathf.PI, width = 12f + 18f * h2;
            var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            float s = (x * dir.x + z * dir.y) / width;
            float shift = (Mathf.Round(s) - s) * width + 0.7f;   // onto the 1.4 m balk at the strip's start
            x += dir.x * shift; z += dir.y * shift;
            if (Mathf.Floor(x / 230f) != bx || Mathf.Floor(z / 230f) != bz) continue;   // stayed in the same block of strips
            Add(R() < 0.55f ? Kind.Pear : Kind.Walnut, x, z);
        }

        // Ravines: acacia groves where the countryside is steepest.
        for (float gx = -980f; gx < 980f; gx += 14f)
        for (float gz = -980f; gz < 980f; gz += 14f)
        {
            float x = gx + (R() - 0.5f) * 12f, z = gz + (R() - 0.5f) * 12f;
            if (TerrainGenerator.Countryside(x, z) < 0.9f) continue;
            float slope = Vector3.Angle(TerrainGenerator.Normal(x, z, 2f), Vector3.up);
            if (slope < 13.5f || R() > 0.6f) continue;
            Add(Kind.Acacia, x, z);
        }
        // Acacia row along the long leg of the west track (south side), with gaps.
        for (float x = -75f; x > -215f; x -= 9f)
            if (R() < 0.7f) Add(Kind.Acacia, x + (R() - 0.5f) * 3f, -68f - R() * 2f, 0.85f);

        // Poplars along the main road, both sides, every ~14 m (gaps at the junctions are made by Free()).
        for (float z = -980f; z < 980f; z += 14f)
        {
            Add(Kind.Poplar, 80f - 9f, z + (R() - 0.5f) * 2f);
            Add(Kind.Poplar, 80f + 9f, z + (R() - 0.5f) * 2f);
        }

        // Willows along the stream (both banks, irregular) and in the pasture's wet hollows.
        for (int i = 0; i + 1 < WorldLayout.Stream.Length; i++)
        {
            Vector2 a = WorldLayout.Stream[i], b = WorldLayout.Stream[i + 1];
            Vector2 n = new Vector2(-(b - a).y, (b - a).x).normalized;
            float len = Vector2.Distance(a, b);
            for (float u = 0f; u < len; u += 11f)
            {
                if (R() > 0.75f) continue;
                Vector2 p = Vector2.Lerp(a, b, u / len) + n * ((R() < 0.5f ? -1f : 1f) * (8f + R() * 4f));
                Add(R() < 0.8f ? Kind.Willow : Kind.Poplar, p.x, p.y);
            }
        }
        for (float gx = -900f; gx < 900f; gx += 20f)
        for (float gz = -900f; gz < 900f; gz += 20f)
        {
            float x = gx + (R() - 0.5f) * 16f, z = gz + (R() - 0.5f) * 16f;
            if (GroundMap.At(x, z) != Ground.Mud || R() > 0.25f || WorldLayout.StreamDistance(x, z, out _) < 30f) continue;
            if (WorldLayout.MudSpots.Length > 0 && NearMudSpot(x, z)) continue;   // the gates' mud is the tracks' end
            Add(Kind.Willow, x + 6f, z);
        }
        return list;
    }

    static bool NearMudSpot(float x, float z)
    {
        foreach (var m in WorldLayout.MudSpots)
            if ((m.x - x) * (m.x - x) + (m.y - z) * (m.y - z) < (m.z + 12f) * (m.z + 12f)) return true;
        return false;
    }

    static float Frac(float v) => v - Mathf.Floor(v);

    /// <summary>Room for a tree at x, z: inside the world, on pasture (not a field, road, yard, mud of a gate), clear of the tracks
    /// and of the farm's buildings and practice slopes.</summary>
    public static bool Free(float x, float z, float roadClear)
    {
        if (Mathf.Abs(x) > 990f || Mathf.Abs(z) > 990f) return false;
        var g = GroundMap.At(x, z);
        if (g != Ground.Grass && g != Ground.Mud) return false;
        foreach (var f in WorldLayout.Fields)
            if (x > f.x0 - 3f && x < f.x1 + 3f && z > f.z0 - 3f && z < f.z1 + 3f) return false;
        foreach (var r in WorldLayout.Roads)
            if (WorldLayout.DistanceToRoad(r, x, z) < r.width * 0.5f + roadClear) return false;
        if (x > -80f && x < 60f && z > -70f && z < 60f && !(x > 40f && z > 0f && z < 30f) && !(x < -46f && z < -15f && z > -30f)) return false;
        if (Vector2.Distance(new Vector2(x, z), new Vector2(WorldLayout.SilageClamp.x, WorldLayout.SilageClamp.y)) < 30f) return false;
        if (Vector2.Distance(new Vector2(x, z), new Vector2(WorldLayout.EarthRamp.x, WorldLayout.EarthRamp.y)) < 30f) return false;
        foreach (var h in WorldLayout.Haystacks)
            if (Vector2.Distance(new Vector2(x, z), h) < 8f) return false;
        foreach (var p in WorldLayout.PracticeArea)
            if (x > p.x0 - 6f && x < p.x1 + 6f && z > p.z0 - 6f && z < p.z1 + 6f) return false;
        if (Vector2.Distance(new Vector2(x, z), new Vector2(WorldLayout.Village.x, WorldLayout.Village.y)) < WorldLayout.VillageRadius) return false;
        var wc = WorldLayout.WaysideCross;
        if (Vector2.Distance(new Vector2(x, z), new Vector2(wc.x, wc.y)) < 8f) return false;
        return true;
    }
}
