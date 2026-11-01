using UnityEngine;

/// <summary>
/// The far horizon around Reghin (phase 5): two rings of silhouettes that travel with the camera (like a skybox, so they never get
/// closer), inside the far clip plane and hazed by their own shader (TractorVR/Horizon) instead of the scene fog:
///   far  (1.42 km)  the Görgényi / Gurghiu mountains in the north-east (up to ~3° above the horizon, as the real ones from ~30 km)
///                   and the Kelemen / Călimani range to the north, low hills elsewhere;
///   near (1.30 km)  Mezőség hills all round, which also hide the end of the 2 km terrain.
/// Bearing 0° = north (+Z), 90° = east (+X). Vertex colour: rgb = colour, a = haze (share of the fog colour).
/// </summary>
public class Horizon : MonoBehaviour
{
    public Material material;
    const int Segments = 360;

    void Start() => Build();

    public void Build()
    {
        var mf = GetComponent<MeshFilter>();
        if (!mf) mf = gameObject.AddComponent<MeshFilter>();
        var mr = GetComponent<MeshRenderer>();
        if (!mr) mr = gameObject.AddComponent<MeshRenderer>();
        mr.sharedMaterial = material;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        var m = new Mesh { name = "Horizon" };
        var v = new System.Collections.Generic.List<Vector3>();
        var c = new System.Collections.Generic.List<Color>();
        var t = new System.Collections.Generic.List<int>();
        Ring(v, c, t, 1420f, FarElevation, new Color(0.30f, 0.37f, 0.48f, 0.38f), new Color(0.45f, 0.52f, 0.60f, 0.5f));
        Ring(v, c, t, 1300f, NearElevation, new Color(0.28f, 0.37f, 0.20f, 0.32f), new Color(0.40f, 0.47f, 0.30f, 0.45f));
        m.SetVertices(v); m.SetColors(c); m.SetTriangles(t, 0);
        m.bounds = new Bounds(Vector3.zero, Vector3.one * 3000f);
        mf.sharedMesh = m;
    }

    static void Ring(System.Collections.Generic.List<Vector3> v, System.Collections.Generic.List<Color> c, System.Collections.Generic.List<int> t,
                     float radius, System.Func<float, float> elevation, Color top, Color bottom)
    {
        int first = v.Count;
        for (int i = 0; i <= Segments; i++)
        {
            float b = i * 360f / Segments, a = b * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            float y = Mathf.Tan(elevation(b) * Mathf.Deg2Rad) * radius;
            v.Add(dir * radius + Vector3.up * y);
            v.Add(dir * radius + Vector3.down * 200f);
            c.Add(top.linear); c.Add(bottom.linear);   // vertex colours are used as linear values (the colours here are sRGB)
        }
        for (int i = 0; i < Segments; i++)
        {
            int k = first + i * 2;
            t.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 });
        }
    }

    // Degrees above the horizon for a bearing (0 = north). Ridges from layered noise; peaks a little sharper (abs of noise).
    static float Ridge(float b, float scale, float seed) =>
        1f - Mathf.Abs(Mathf.PerlinNoise(b / scale + seed, seed * 0.37f) * 2f - 1f);

    static float Bump(float b, float centre, float halfWidth)
    {
        float d = Mathf.Abs(Mathf.DeltaAngle(b, centre)) / halfWidth;
        return d >= 1f ? 0f : 0.5f + 0.5f * Mathf.Cos(d * Mathf.PI);
    }

    static float FarElevation(float b)
    {
        // Exaggerated about twice (the real ridges stand ~2–3° above the horizon from Reghin): at that height the nearby
        // Mezőség hills hid them, and the eye judges mountains bigger than they measure anyway.
        float hills = 1.1f + 0.6f * Mathf.PerlinNoise(b / 14f + 3.1f, 0.5f);
        float gurghiu = Bump(b, 70f, 55f) * (2.6f + 2.0f * Ridge(b, 9f, 11.3f) + 0.7f * Ridge(b, 3.5f, 5.7f));
        float calimani = Bump(b, 10f, 28f) * (2.0f + 1.2f * Ridge(b, 7f, 23.9f));
        return hills + gurghiu + calimani;
    }

    static float NearElevation(float b) =>
        0.6f + 0.7f * Mathf.PerlinNoise(b / 11f + 7.7f, 1.3f) + 0.3f * Mathf.PerlinNoise(b / 4f + 2.2f, 4.1f);

    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam) Follow(cam);
    }

    /// <summary>Centres the rings on the camera (also used by the editor previews).</summary>
    public void Follow(Camera cam)
    {
        Vector3 p = cam.transform.position;
        transform.position = new Vector3(p.x, 0f, p.z);
    }
}
