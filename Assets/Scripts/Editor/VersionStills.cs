using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Image-quality comparison of the two quality levels (phase 4): the same fixed viewpoints rendered at "Quest" and at "PC"
/// (PC VR and the desktop version share the PC level). Unlike the benchmark stills these don't depend on where a headset looks.
///   Menu: Tractor VR > Render Quality Comparison
///   CLI : Unity.exe -batchmode -quit -projectPath . -executeMethod VersionStills.Run   → Logs/Quality/&lt;level&gt;_&lt;view&gt;.png
/// Then: python tools/compare_stills.py (side-by-side sheets in docs/images).
/// </summary>
public static class VersionStills
{
    const string OutDir = "Logs/Quality";

    [MenuItem("Tractor VR/Render Quality Comparison")]
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/TractorFlat.unity");
        ShaderUtil.allowAsyncCompilation = false;
        Object.FindAnyObjectByType<TerrainGenerator>().Build();
        var tractor = GameObject.Find("Tractor").transform;
        var seat = tractor.Find("DriverEye");
        var tier = tractor.GetComponent<QualityTier>();
        foreach (var name in new[] { "Left Controller", "Right Controller" }) { var go = GameObject.Find(name); if (go) go.SetActive(false); }
        var cam = Camera.main;
        cam.fieldOfView = 60f;
        cam.farClipPlane = 1500f;
        Directory.CreateDirectory(OutDir);

        // The sun shines from the front left (Euler 35, -35): the 3/4 view from the front right shows the tractor's shadow.
        Vector3 pivot = tractor.position + Vector3.up * 1.6f;
        var views = new (string name, Vector3 pos, Vector3 look)[]
        {
            ("cab",   seat.position, seat.position + Quaternion.Euler(12f, 0f, 0f) * Vector3.forward),
            ("third", tractor.position + new Vector3(0f, 4.2f, -9.5f), pivot),
            ("close", tractor.position + new Vector3(4.5f, 2.2f, 6.5f), pivot + Vector3.down * 0.4f),
            ("far",   tractor.position + new Vector3(-18f, 5f, 22f), pivot),
        };

        int level = QualitySettings.GetQualityLevel();
        foreach (var (levelName, index, t) in new[] { ("quest", 0, QualityTier.Tier.Quest), ("pc", 1, QualityTier.Tier.PC) })
        {
            QualitySettings.SetQualityLevel(index, true);
            tier.Apply(t);
            Shot(cam, "warmup", views[0].pos, views[0].look, false);   // the first batch-mode render of a level comes out grey
            foreach (var v in views) Shot(cam, $"{levelName}_{v.name}", v.pos, v.look, true);
        }
        QualitySettings.SetQualityLevel(level, true);
        tier.Apply(QualityTier.Current);
        EditorSceneManager.OpenScene("Assets/Scenes/TractorFlat.unity");   // drop the changes (the scene isn't saved)
        Debug.Log("[Quality] Stills in " + OutDir);
    }

    static void Shot(Camera cam, string name, Vector3 pos, Vector3 look, bool save)
    {
        cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos));
        var rt = new RenderTexture(1600, 900, 24);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        if (save) File.WriteAllBytes($"{OutDir}/{name}.png", tex.EncodeToPNG());
        cam.targetTexture = null;
        RenderTexture.active = null;
        Object.DestroyImmediate(tex);
        rt.Release();
    }
}
