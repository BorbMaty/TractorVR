using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The world's ground, built at startup (nothing stored in the repo):
///   Terrain ... 2 × 2 km from a height function, 8 × 8 chunks of 250 m (frustum culling), 5 m grid, MeshColliders.
///               Two octaves of Perlin noise give gentle hills (slopes up to ~11°); flat within 60 m of the start (the farmyard).
///               Drawn with TractorVR/GroundLit: grass / soil / mud blended from the GroundMap control texture.
///   Surfaces .. the yard (concrete) and the roads (gravel tracks, asphalt road) from WorldLayout, as thin meshes a few
///               centimetres above the terrain triangles (visual only; the wheels feel them through GroundMap).
/// </summary>
public class TerrainGenerator : MonoBehaviour
{
    public Material material;                          // GroundLit; an instance gets the control texture
    public Material gravel, asphalt, concrete;         // TriplanarLit
    public Material water;                             // the stream (EnvLit, dark and glossy)
    public Shader fieldBrush;                          // FieldMap painting (kept referenced here so builds include it)
    public float size = 2000f;
    public int chunks = 8;
    public float cell = 5f;

    public const float FlatRadius = 60f, BlendRadius = 160f;
    const float WorldSize = 2000f, GridCell = 5f;      // must match size / cell (SurfaceHeight follows the triangles)

    /// <summary>Ground height (m) at world x, z (the smooth function the terrain is sampled from): the hills, flattened under the
    /// practice area, with the stream's channel cut in.</summary>
    public static float Height(float x, float z)
    {
        float h = BaseHeight(x, z);
        foreach (var a in WorldLayout.PracticeArea)   // flat pad (+ 12 m blend), at the yard's level
        {
            float dx = Mathf.Max(a.x0 - x, 0f, x - a.x1), dz = Mathf.Max(a.z0 - z, 0f, z - a.z1);
            float w = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dz * dz) / 12f);
            h = Mathf.Lerp(h, 0f, w * w * (3f - 2f * w));
        }
        {   // the village's plateau: its houses are one mesh, so the ground under it is flat
            var v = WorldLayout.Village;
            float d = Mathf.Sqrt((x - v.x) * (x - v.x) + (z - v.y) * (z - v.y));
            if (d < WorldLayout.VillageRadius + 250f)
            {
                villageLevel ??= BaseHeight(v.x, v.y) + 2f;
                float bank = 40f + 5f * Mathf.Abs(h - villageLevel.Value);   // wider where the plateau is far from the hills' height
                float w = Mathf.Clamp01(1f - (d - WorldLayout.VillageRadius) / bank);
                h = Mathf.Lerp(h, villageLevel.Value, w * w * (3f - 2f * w));
            }
        }
        if (WorldLayout.NearStream(x, z, 120f))
        {
            float d = WorldLayout.StreamDistance(x, z, out float along);
            if (d < 120f)
            {
                // Flat bottom 6 m either side; the banks get wider where the cut (or fill) is deeper, so they stay below ~12°.
                float floor = StreamBed(along);
                float bank = 16f + 4.5f * Mathf.Abs(h - floor);
                float w = Mathf.Clamp01((d - 6f) / bank);
                h = Mathf.Lerp(floor, h, w * w * (3f - 2f * w));
            }
        }
        return h;
    }

    static float? villageLevel;
    public static float VillageLevel => Height(WorldLayout.Village.x, WorldLayout.Village.y);
    static float[] bed;
    /// <summary>Height of the stream's channel bottom at a position along it: the ground at its points (smoothed with the neighbours)
    /// minus 1.6 m, linear in between.</summary>
    public static float StreamBed(float along)
    {
        var pts = WorldLayout.Stream;
        if (bed == null)
        {
            var raw = new float[pts.Length];
            for (int i = 0; i < pts.Length; i++) raw[i] = BaseHeight(pts[i].x, pts[i].y);
            var b = new float[pts.Length];
            for (int i = 0; i < pts.Length; i++)
                b[i] = (raw[Mathf.Max(0, i - 1)] + 2f * raw[i] + raw[Mathf.Min(pts.Length - 1, i + 1)]) * 0.25f - 1.6f;
            bed = b;
        }
        int k = Mathf.Clamp(Mathf.FloorToInt(along), 0, pts.Length - 2);
        return Mathf.Lerp(bed[k], bed[k + 1], along - k);
    }

    static float BaseHeight(float x, float z)
    {
        float h = 34f * (Mathf.PerlinNoise(x / 380f + 13.7f, z / 380f + 4.1f) - 0.5f)
                + 9f * (Mathf.PerlinNoise(x / 140f + 71.3f, z / 140f + 22.9f) - 0.5f);
        float r = Mathf.Sqrt(x * x + z * z);
        float t = Mathf.Clamp01((r - FlatRadius) / (BlendRadius - FlatRadius));
        h *= t * t * (3f - 2f * t);
        // Phase 5: Mezőség relief outside the farm and the fields (they keep the gentle hills above): higher, rounder hills that
        // mostly rise, and the small hummocks of old landslides on their slopes.
        float c = Countryside(x, z);
        if (c > 0f)
        {
            float hills = 40f * (Mathf.PerlinNoise(x / 450f + 3.3f, z / 450f + 8.8f) - 0.3f);
            float hummocks = 1.6f * (Mathf.PerlinNoise(x / 30f + 41.7f, z / 30f + 17.2f) - 0.5f)
                           + 0.8f * (Mathf.PerlinNoise(x / 14f + 9.1f, z / 14f + 63.5f) - 0.5f);
            h += c * (hills + hummocks);
        }
        return h;
    }

    /// <summary>0 on the farm (within 170 m of the yard) and on the fields, rising to 1 in the open countryside (Mezőség relief,
    /// hillside strip parcels in GroundLit through the vertex colour).</summary>
    public static float Countryside(float x, float z)
    {
        float r = Mathf.Sqrt(x * x + z * z);
        float farm = Mathf.Clamp01((r - 170f) / 130f);
        if (farm <= 0f) return 0f;
        float d = float.MaxValue;
        foreach (var f in WorldLayout.Fields)
        {
            float dx = Mathf.Max(f.x0 - x, 0f, x - f.x1), dz = Mathf.Max(f.z0 - z, 0f, z - f.z1);
            d = Mathf.Min(d, Mathf.Sqrt(dx * dx + dz * dz));
        }
        float fields = Mathf.Clamp01((d - 30f) / 120f);
        float s = Mathf.Min(farm, fields);
        return s * s * (3f - 2f * s);
    }

    public static Vector3 Normal(float x, float z, float e = 0.5f)
    {
        float dx = Height(x + e, z) - Height(x - e, z);
        float dz = Height(x, z + e) - Height(x, z - e);
        return new Vector3(-dx, 2f * e, -dz).normalized;
    }

    /// <summary>Height of the actual terrain triangles (up to ~4 cm off <see cref="Height"/> on curved ground).</summary>
    public static float SurfaceHeight(float x, float z)
    {
        float gx = (x + WorldSize * 0.5f) / GridCell, gz = (z + WorldSize * 0.5f) / GridCell;
        int i = Mathf.FloorToInt(gx), j = Mathf.FloorToInt(gz);
        float u = gx - i, v = gz - j;
        float X(int a) => a * GridCell - WorldSize * 0.5f;
        float a00 = Height(X(i), X(j)), a10 = Height(X(i + 1), X(j)), a01 = Height(X(i), X(j + 1)), a11 = Height(X(i + 1), X(j + 1));
        // Same split as ChunkMesh: triangles (a, c, b) and (b, c, d), diagonal from b (i+1, j) to c (i, j+1).
        return u + v <= 1f ? a00 + u * (a10 - a00) + v * (a01 - a00)
                           : a11 + (1f - u) * (a01 - a11) + (1f - v) * (a10 - a11);
    }

    void Awake() => Build();

    /// <summary>Creates the ground map, chunk meshes, colliders, yard and roads (also called by the editor for previews and tests).</summary>
    public void Build()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var c = transform.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
        }
        GroundMap.Build();
        FieldMap.Init(fieldBrush);
        var mat = new Material(material) { name = "Ground (runtime)" };
        mat.SetTexture("_Control", GroundMap.ControlTexture());
        mat.SetFloat("_WorldHalf", size * 0.5f);

        float chunkSize = size / chunks;
        int n = Mathf.RoundToInt(chunkSize / cell);
        for (int cx = 0; cx < chunks; cx++)
        for (int cz = 0; cz < chunks; cz++)
        {
            float x0 = -size * 0.5f + cx * chunkSize, z0 = -size * 0.5f + cz * chunkSize;
            var mesh = ChunkMesh(x0, z0, n);
            var go = new GameObject($"Chunk_{cx}_{cz}");
            go.transform.SetParent(transform, false);
            go.isStatic = true;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // gentle hills shadow nothing within 40 m; saves GPU
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
        }
        BuildSurfaces();
    }

    Mesh ChunkMesh(float x0, float z0, int n)
    {
        int row = n + 1;
        var v = new Vector3[row * row];
        var nrm = new Vector3[v.Length];
        var col = new Color[v.Length];   // R: Countryside (GroundLit: strip parcels, drier grass)
        for (int j = 0; j <= n; j++)
        for (int i = 0; i <= n; i++)
        {
            float x = x0 + i * cell, z = z0 + j * cell;
            int k = j * row + i;
            v[k] = new Vector3(x, Height(x, z), z);
            nrm[k] = Normal(x, z);
            col[k] = new Color(Countryside(x, z), 0f, 0f, 0f);
        }
        var tris = new int[n * n * 6];
        int t = 0;
        for (int j = 0; j < n; j++)
        for (int i = 0; i < n; i++)
        {
            int a = j * row + i, b = a + 1, c = a + row, d = c + 1;
            tris[t++] = a; tris[t++] = c; tris[t++] = b;
            tris[t++] = b; tris[t++] = c; tris[t++] = d;
        }
        var m = new Mesh { name = "TerrainChunk", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        m.vertices = v;
        m.normals = nrm;
        m.colors = col;
        m.triangles = tris;
        m.RecalculateBounds();
        return m;
    }

    // ------------------------------------------------------------------ yard + roads
    void BuildSurfaces()
    {
        var parent = new GameObject("Surfaces").transform;
        parent.SetParent(transform, false);
        var y = WorldLayout.Yard;
        AddMesh(parent, "Yard", Patch(y.x0, y.z0, y.x1, y.z1, 2f, 0.03f), concrete);
        foreach (var a in WorldLayout.PracticeArea)
            AddMesh(parent, a.name, Patch(a.x0, a.z0, a.x1, a.z1, 2f, 0.03f), concrete);
        if (water != null)
        {
            var stream = Ribbon(WorldLayout.Stream, WorldLayout.StreamHalfWidth * 2f, 0.35f);
            var white = new Color[stream.vertexCount];   // EnvLit reads its baked AO from the vertex colour
            for (int i = 0; i < white.Length; i++) white[i] = Color.white;
            stream.colors = white;
            AddMesh(parent, "Stream", stream, water);
        }
        foreach (var r in WorldLayout.Roads)
        {
            bool tarmac = r.type == Ground.Asphalt;
            // Higher offset for the asphalt so it wins where a track joins it; the yard is lowest.
            AddMesh(parent, r.name, Ribbon(r.points, r.width, tarmac ? 0.07f : 0.05f), tarmac ? asphalt : gravel);
        }
    }

    static void AddMesh(Transform parent, string name, Mesh mesh, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.isStatic = true;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // Flat grid over a rectangle, following the terrain.
    static Mesh Patch(float x0, float z0, float x1, float z1, float step, float lift)
    {
        int nx = Mathf.CeilToInt((x1 - x0) / step), nz = Mathf.CeilToInt((z1 - z0) / step);
        var v = new List<Vector3>();
        var t = new List<int>();
        for (int j = 0; j <= nz; j++)
        for (int i = 0; i <= nx; i++)
        {
            float x = Mathf.Lerp(x0, x1, i / (float)nx), z = Mathf.Lerp(z0, z1, j / (float)nz);
            v.Add(new Vector3(x, SurfaceHeight(x, z) + lift, z));
        }
        for (int j = 0; j < nz; j++)
        for (int i = 0; i < nx; i++)
        {
            int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
            t.AddRange(new[] { a, c, b, b, c, d });
        }
        return Finish(v, t, "Patch");
    }

    // Strip of the given width along a polyline, sampled every 2 m (mitred at the corners), following the terrain.
    static Mesh Ribbon(Vector2[] pts, float width, float lift)
    {
        var centre = new List<Vector2>();
        var side = new List<Vector2>();
        for (int s = 0; s + 1 < pts.Length; s++)
        {
            Vector2 a = pts[s], b = pts[s + 1];
            int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / 2f));
            for (int k = (s == 0 ? 0 : 1); k <= n; k++)
            {
                Vector2 p = Vector2.Lerp(a, b, k / (float)n);
                Vector2 dir = (b - a).normalized;
                if (k == n && s + 2 < pts.Length) dir = ((b - a).normalized + (pts[s + 2] - b).normalized).normalized;   // corner
                centre.Add(p);
                side.Add(new Vector2(dir.y, -dir.x));
            }
        }
        var v = new List<Vector3>();
        var t = new List<int>();
        for (int i = 0; i < centre.Count; i++)
        {
            foreach (float sgn in new[] { -1f, 1f })
            {
                Vector2 q = centre[i] + side[i] * (sgn * width * 0.5f);
                v.Add(new Vector3(q.x, SurfaceHeight(q.x, q.y) + lift, q.y));
            }
            if (i > 0)
            {
                int a = (i - 1) * 2, b = a + 1, c = a + 2, d = a + 3;
                t.AddRange(new[] { a, c, b, b, c, d });
            }
        }
        return Finish(v, t, "Ribbon");
    }

    static Mesh Finish(List<Vector3> v, List<int> t, string name)
    {
        var m = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        m.SetVertices(v);
        m.SetTriangles(t, 0);
        m.RecalculateNormals();
        // Face up (the winding depends on the strip direction).
        var nrm = m.normals;
        if (nrm.Length > 0 && nrm[0].y < 0f)
        {
            for (int i = 0; i < t.Count; i += 3) { int tmp = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = tmp; }
            m.SetTriangles(t, 0);
            m.RecalculateNormals();
        }
        m.RecalculateBounds();
        return m;
    }
}
