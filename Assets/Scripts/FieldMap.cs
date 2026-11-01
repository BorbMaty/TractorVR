using UnityEngine;

/// <summary>
/// What the implements have done to the fields (phase 5 step 3), as a GPU texture over the fields' bounding box, 0.5 m per texel:
///   R = cut (the mower has been over the grass), G = swath (cut grass lying in a row behind the mower),
///   B = ploughed, A = furrow direction (heading angle mod 180°, / 180°).
/// Painted on the GPU with quads (<see cref="Paint"/>), read by GroundLit (furrows, stubble, swath) and the vegetation shader
/// (cut grass) through the global <c>_FieldMap</c> / <c>_FieldRect</c>. Starts empty every run. The physics side stays in
/// <see cref="GroundMap"/> (ploughed ground has its own grip and draft).
/// </summary>
public static class FieldMap
{
    public const float TexelSize = 0.5f;
    static RenderTexture rt;
    static Material brushMax, brushDir;
    static Rect area;

    public static bool Ready => rt != null;
    public static Rect Area => area;

    /// <summary>Creates the texture (once) over all <see cref="WorldLayout.Fields"/> plus a margin, cleared, and sets the globals.</summary>
    public static void Init(Shader brush)
    {
        if (rt != null || brush == null) return;
        float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
        foreach (var f in WorldLayout.Fields)
        {
            x0 = Mathf.Min(x0, f.x0); z0 = Mathf.Min(z0, f.z0); x1 = Mathf.Max(x1, f.x1); z1 = Mathf.Max(z1, f.z1);
        }
        area = Rect.MinMaxRect(x0 - 10f, z0 - 10f, x1 + 10f, z1 + 10f);
        rt = new RenderTexture(Mathf.CeilToInt(area.width / TexelSize), Mathf.CeilToInt(area.height / TexelSize), 0,
                               RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
        { name = "FieldMap", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, useMipMap = false };
        rt.Create();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(false, true, Color.clear);
        RenderTexture.active = prev;

        brushMax = new Material(brush) { name = "FieldBrush (max)" };          // R, G, B: only ever grows
        brushMax.SetFloat("_BlendOp", (float)UnityEngine.Rendering.BlendOp.Max);
        brushMax.SetFloat("_ColorMask", (float)(UnityEngine.Rendering.ColorWriteMask.Red | UnityEngine.Rendering.ColorWriteMask.Green
                                                | UnityEngine.Rendering.ColorWriteMask.Blue));
        brushDir = new Material(brush) { name = "FieldBrush (direction)" };    // A: the latest pass's furrow direction
        brushDir.SetFloat("_BlendOp", (float)UnityEngine.Rendering.BlendOp.Add);
        brushDir.SetFloat("_ColorMask", (float)UnityEngine.Rendering.ColorWriteMask.Alpha);

        Shader.SetGlobalTexture("_FieldMap", rt);
        Shader.SetGlobalVector("_FieldRect", new Vector4(area.xMin, area.yMin, 1f / area.width, 1f / area.height));
    }

    /// <summary>Paints a strip from <paramref name="a"/> to <paramref name="b"/> (world XZ), <paramref name="width"/> m wide:
    /// channels in <paramref name="value"/> (R cut, G swath, B ploughed) by max; with <paramref name="furrows"/> also the direction.</summary>
    public static void Paint(Vector3 a, Vector3 b, float width, Color value, bool furrows = false)
    {
        if (rt == null) return;
        Vector2 p = new Vector2(a.x, a.z), q = new Vector2(b.x, b.z), d = q - p;
        if (d.sqrMagnitude < 1e-6f) return;
        Vector2 dir = d.normalized, side = new Vector2(dir.y, -dir.x) * (width * 0.5f);
        p -= dir * 0.1f; q += dir * 0.1f;   // overlap the next segment: no gaps on curves
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        GL.PushMatrix();
        GL.LoadOrtho();
        Quad(brushMax, p, q, side, value);
        if (furrows)
        {
            float heading = Mathf.Repeat(Mathf.Atan2(dir.x, dir.y), Mathf.PI) / Mathf.PI;
            Quad(brushDir, p, q, side, new Color(0f, 0f, 0f, heading));
        }
        GL.PopMatrix();
        RenderTexture.active = prev;
    }

    static void Quad(Material m, Vector2 p, Vector2 q, Vector2 side, Color c)
    {
        m.SetPass(0);
        GL.Begin(GL.QUADS);
        GL.Color(c);
        GL.Vertex(UV(p - side)); GL.Vertex(UV(p + side)); GL.Vertex(UV(q + side)); GL.Vertex(UV(q - side));
        GL.End();
    }

    static Vector3 UV(Vector2 w) => new Vector3((w.x - area.xMin) / area.width, (w.y - area.yMin) / area.height, 0f);

    /// <summary>Back to untouched fields (tests).</summary>
    public static void Clear()
    {
        if (rt == null) return;
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(false, true, Color.clear);
        RenderTexture.active = prev;
    }

    /// <summary>The texel at a world point, read back from the GPU (slow: tests only).</summary>
    public static Color Sample(float x, float z)
    {
        if (rt == null) return Color.clear;
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var t = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
        int i = Mathf.Clamp(Mathf.FloorToInt((x - area.xMin) / TexelSize), 0, rt.width - 1);
        int j = Mathf.Clamp(Mathf.FloorToInt((z - area.yMin) / TexelSize), 0, rt.height - 1);
        t.ReadPixels(new Rect(i, j, 1, 1), 0, 0, false);
        t.Apply();
        RenderTexture.active = prev;
        var c = t.GetPixel(0, 0);
        Object.DestroyImmediate(t);
        return c;
    }
}
