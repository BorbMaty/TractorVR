using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Standing grass on the meadow and wheat on the wheat field (phase 5 step 3). Clumps are placed once, in 8 m tiles over each
/// field (only on the field's own ground type: no clumps on tracks, gates or mud), and each frame the tiles near the camera and in
/// its view are drawn with <see cref="Graphics.RenderMeshInstanced"/>. The shader (TractorVR/Vegetation) reads the FieldMap per
/// clump, so mown grass turns to stubble and swath without any CPU work. Distance and density come from the quality tier
/// (<see cref="QualityTier.Budget.grassDistance"/>, <see cref="QualityTier.Budget.vegetationDensity"/>).
/// </summary>
public class Vegetation : MonoBehaviour
{
    [System.Serializable]
    public class Kind
    {
        public string field;              // WorldLayout.Fields name
        public Mesh mesh;                 // one clump, origin at its root
        public Material material;         // TractorVR/Vegetation (instancing on)
        public float perSquareMetre = 2f; // clumps per m² on the PC tier (Quest: × vegetationDensity, bigger clumps)
        public float distanceScale = 1f;  // × the tier's grass distance
        [System.NonSerialized] public List<Tile> tiles;
    }

    public class Tile { public Bounds bounds; public Matrix4x4[] matrices; }

    public Kind[] kinds = new Kind[0];
    public const float TileSize = 8f;
    readonly Plane[] planes = new Plane[6];

    /// <summary>Instances drawn last frame (all kinds), for the HUD / benchmark.</summary>
    public int DrawnInstances { get; private set; }

    void Start() => Build();

    /// <summary>Places the clumps (deterministic: the same field always gets the same grass).</summary>
    public void Build()
    {
        if (!GroundMap.Built) GroundMap.Build();
        var budget = QualityTier.CurrentBudget;
        foreach (var k in kinds)
        {
            k.tiles = new List<Tile>();
            int fi = System.Array.FindIndex(WorldLayout.Fields, f => f.name == k.field);
            if (fi < 0 || k.mesh == null) continue;
            var area = WorldLayout.Fields[fi];
            float density = k.perSquareMetre * budget.vegetationDensity;
            float spacing = 1f / Mathf.Sqrt(density);
            float grow = 1f / Mathf.Sqrt(budget.vegetationDensity);   // fewer clumps on the Quest: each one covers more
            var rnd = new System.Random(fi * 7919 + 17);
            float R() => (float)rnd.NextDouble();
            var list = new List<Matrix4x4>();
            for (float tx = area.x0; tx < area.x1; tx += TileSize)
            for (float tz = area.z0; tz < area.z1; tz += TileSize)
            {
                list.Clear();
                float yMin = float.MaxValue, yMax = float.MinValue;
                for (float x = tx; x < Mathf.Min(tx + TileSize, area.x1); x += spacing)
                for (float z = tz; z < Mathf.Min(tz + TileSize, area.z1); z += spacing)
                {
                    float px = x + (R() - 0.5f) * spacing, pz = z + (R() - 0.5f) * spacing;
                    if (GroundMap.At(px, pz) != area.type) continue;
                    float y = TerrainGenerator.SurfaceHeight(px, pz);
                    float s = (0.8f + 0.4f * R()) * grow;
                    list.Add(Matrix4x4.TRS(new Vector3(px, y, pz), Quaternion.Euler(0f, R() * 360f, 0f), new Vector3(s, s * (0.85f + 0.3f * R()), s)));
                    yMin = Mathf.Min(yMin, y); yMax = Mathf.Max(yMax, y);
                }
                if (list.Count == 0) continue;
                var b = new Bounds();
                b.SetMinMax(new Vector3(tx - 0.5f, yMin - 0.2f, tz - 0.5f), new Vector3(tx + TileSize + 0.5f, yMax + 2.5f, tz + TileSize + 0.5f));
                k.tiles.Add(new Tile { bounds = b, matrices = list.ToArray() });
            }
        }
    }

    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam) Submit(cam, null);
    }

    /// <summary>Draws the tiles within reach of <paramref name="cam"/> (for <paramref name="only"/>: just that camera, as the editor
    /// previews need; null = every camera this frame).</summary>
    public void Submit(Camera cam, Camera only)
    {
        if (kinds.Length > 0 && kinds[0].tiles == null) Build();   // editor renders: the clumps aren't serialized
        DrawnInstances = 0;
        GeometryUtility.CalculateFrustumPlanes(cam, planes);
        Vector3 c = cam.transform.position;
        float reach = QualityTier.CurrentBudget.grassDistance;
        foreach (var k in kinds)
        {
            if (k.tiles == null || k.material == null) continue;
            float r = reach * k.distanceScale;
            k.material.SetFloat("_FadeEnd", r);
            k.material.SetFloat("_FadeWidth", Mathf.Max(3f, r * 0.15f));
            var rp = new RenderParams(k.material) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, camera = only };
            float r2 = (r + TileSize) * (r + TileSize);
            foreach (var t in k.tiles)
            {
                Vector3 d = t.bounds.center - c;
                if (d.x * d.x + d.z * d.z > r2) continue;
                var wide = t.bounds; wide.Expand(4f);   // VR: the culling camera's frustum is narrower than the two eyes together
                if (!GeometryUtility.TestPlanesAABB(planes, wide)) continue;
                Graphics.RenderMeshInstanced(rp, k.mesh, 0, t.matrices);
                DrawnInstances += t.matrices.Length;
            }
        }
    }
}
