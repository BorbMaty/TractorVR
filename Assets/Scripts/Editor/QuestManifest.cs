using System.IO;
using System.Xml;
using UnityEditor.Android;

/// <summary>
/// Adds hand tracking to the Quest's Android manifest: the HAND_TRACKING permission and the (optional) handtracking feature.
/// Without them the Quest never hands tracked hands to the app. The OpenXR package only adds these for its Meta hand mesh feature.
/// </summary>
public class QuestManifest : IPostGenerateGradleAndroidProject
{
    public int callbackOrder => 100;

    const string AndroidNs = "http://schemas.android.com/apk/res/android";

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        string file = Path.Combine(path, "src", "main", "AndroidManifest.xml");
        if (!File.Exists(file)) return;
        var doc = new XmlDocument();
        doc.Load(file);
        var manifest = doc.DocumentElement;
        Ensure(doc, manifest, "uses-permission", "com.oculus.permission.HAND_TRACKING", null);
        Ensure(doc, manifest, "uses-feature", "oculus.software.handtracking", "false");
        doc.Save(file);
    }

    static void Ensure(XmlDocument doc, XmlElement manifest, string tag, string name, string required)
    {
        foreach (XmlElement e in manifest.GetElementsByTagName(tag))
            if (e.GetAttribute("name", AndroidNs) == name) return;
        var el = doc.CreateElement(tag);
        el.SetAttribute("name", AndroidNs, name);
        if (required != null) el.SetAttribute("required", AndroidNs, required);
        manifest.AppendChild(el);
    }
}
