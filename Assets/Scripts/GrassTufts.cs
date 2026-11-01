using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Grass tufts on the pastures near the camera (phase 5 step 4): the flat grass texture gets some height where the eye is close.
/// The ground around the camera is cut into 3 m cells; each pasture cell (GroundMap: grass, not a field, a road, the yard, a
/// building or the practice pad) gets a few tufts at hashed positions, so the same spot always has the same tufts and nothing is
/// stored. The list is rebuilt when the camera has moved 2 m and drawn with one <see cref="Graphics.RenderMeshInstanced"/>.
/// Same clump mesh and shader as the meadow (<see cref="Vegetation"/>), smaller and darker; they sink into the ground at the edge
/// of their range. Quest: 14 m, 2 per cell; PC: 30 m, 4 per cell.
/// </summary>
public class GrassTufts : MonoBehaviour
{
    public Mesh mesh, meshLight;
    public Material material;
    const float Cell = 3f;

    readonly List<Matrix4x4> list = new List<Matrix4x4>();
    Vector3 builtAt = new Vector3(1e9f, 0f, 0f);

    public int Count => list.Count;

    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam) Submit(cam, null);
    }

    public void Submit(Camera cam, Camera only)
    {
        if (mesh == null || material == null) return;
        if (!GroundMap.Built) GroundMap.Build();
        bool pc = QualityTier.Current == QualityTier.Tier.PC;
        float range = pc ? 30f : 14f;
        int perCell = pc ? 4 : 2;
        Vector3 c = cam.transform.position;
        if ((c - builtAt).sqrMagnitude > 4f || only != null) Rebuild(c, range, perCell);
        material.SetFloat("_FadeEnd", range);
        material.SetFloat("_FadeWidth", range * 0.3f);
        var rp = new RenderParams(material) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, camera = only };
        var m = pc || meshLight == null ? mesh : meshLight;
        for (int start = 0; start < list.Count; start += 1023)
            Graphics.RenderMeshInstanced(rp, m, 0, list, Mathf.Min(1023, list.Count - start), start);
    }

    void Rebuild(Vector3 c, float range, int perCell)
    {
        builtAt = c;
        list.Clear();
        int n = Mathf.CeilToInt(range / Cell);
        int ci = Mathf.FloorToInt(c.x / Cell), cj = Mathf.FloorToInt(c.z / Cell);
        for (int i = ci - n; i <= ci + n; i++)
        for (int j = cj - n; j <= cj + n; j++)
        {
            float x0 = i * Cell, z0 = j * Cell;
            float dx = x0 + Cell * 0.5f - c.x, dz = z0 + Cell * 0.5f - c.z;
            if (dx * dx + dz * dz > (range + Cell) * (range + Cell)) continue;
            uint h = Hash((uint)i * 73856093u ^ (uint)j * 19349663u);
            for (int k = 0; k < perCell; k++)
            {
                h = Hash(h + (uint)k);
                float x = x0 + (h & 0xffff) / 65535f * Cell, z = z0 + (h >> 16) / 65535f * Cell;
                if (!Pasture(x, z)) continue;
                uint g = Hash(h);
                float s = 0.45f + 0.35f * ((g & 0xff) / 255f);
                list.Add(Matrix4x4.TRS(new Vector3(x, TerrainGenerator.SurfaceHeight(x, z), z),
                    Quaternion.Euler(0f, (g >> 8 & 0xff) * 1.41f, 0f), new Vector3(s, s * (0.7f + 0.5f * ((g >> 16 & 0xff) / 255f)), s)));
            }
        }
    }

    static bool Pasture(float x, float z)
    {
        if (GroundMap.At(x, z) != Ground.Grass) return false;
        foreach (var f in WorldLayout.Fields) if (f.Contains(x, z)) return false;
        if (WorldLayout.Barn.Contains(x, z) || WorldLayout.Shed.Contains(x, z) || WorldLayout.Farmhouse.Contains(x, z) || WorldLayout.TipPad.Contains(x, z))
            return false;
        foreach (var r in WorldLayout.Roads) if (WorldLayout.DistanceToRoad(r, x, z) < r.width * 0.5f + 0.6f) return false;
        return true;
    }

    static uint Hash(uint x)
    {
        x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16;
        return x;
    }
}
