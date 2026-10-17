using UnityEngine;

/// <summary>
/// Rolling farmland, built at startup from a height function (nothing stored in the repo):
/// 2 × 2 km in 8 × 8 chunks of 250 m (frustum culling), 5 m grid, each chunk with a MeshCollider.
/// Two octaves of Perlin noise give gentle hills (slopes up to ~7°). The area around the start is flat so the
/// tractor begins on level ground. Normals come from the height function, so chunk edges are seamless.
/// UVs are world XZ / 10, so the grid texture tiles every 10 m like the phase 1 ground.
/// </summary>
public class TerrainGenerator : MonoBehaviour
{
    public Material material;
    public float size = 2000f;
    public int chunks = 8;
    public float cell = 5f;

    public const float FlatRadius = 60f, BlendRadius = 160f;

    /// <summary>Ground height (m) at world x, z.</summary>
    public static float Height(float x, float z)
    {
        float h = 34f * (Mathf.PerlinNoise(x / 380f + 13.7f, z / 380f + 4.1f) - 0.5f)
                + 9f * (Mathf.PerlinNoise(x / 140f + 71.3f, z / 140f + 22.9f) - 0.5f);
        float r = Mathf.Sqrt(x * x + z * z);
        float t = Mathf.Clamp01((r - FlatRadius) / (BlendRadius - FlatRadius));
        return h * t * t * (3f - 2f * t);
    }

    public static Vector3 Normal(float x, float z, float e = 0.5f)
    {
        float dx = Height(x + e, z) - Height(x - e, z);
        float dz = Height(x, z + e) - Height(x, z - e);
        return new Vector3(-dx, 2f * e, -dz).normalized;
    }

    void Awake() => Build();

    /// <summary>Creates the chunk meshes and colliders (also called by the editor for preview renders and tests).</summary>
    public void Build()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var c = transform.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
        }
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
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
        }
    }

    Mesh ChunkMesh(float x0, float z0, int n)
    {
        int row = n + 1;
        var v = new Vector3[row * row];
        var nrm = new Vector3[v.Length];
        var uv = new Vector2[v.Length];
        for (int j = 0; j <= n; j++)
        for (int i = 0; i <= n; i++)
        {
            float x = x0 + i * cell, z = z0 + j * cell;
            int k = j * row + i;
            v[k] = new Vector3(x, Height(x, z), z);
            nrm[k] = Normal(x, z);
            uv[k] = new Vector2(x / 10f, z / 10f);
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
        var m = new Mesh { name = "TerrainChunk" };
        m.vertices = v;
        m.normals = nrm;
        m.uv = uv;
        m.triangles = tris;
        m.RecalculateBounds();
        return m;
    }
}
