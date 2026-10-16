using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildQuest
{
    const string ApkPath = "Builds/TractorVR.apk";

    [MenuItem("Tractor VR/Build Quest APK")]
    public static void Build()
    {
        var options = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/TractorFlat.unity" },
            locationPathName = ApkPath,
            target = BuildTarget.Android,
            options = BuildOptions.None,
        };
        BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"[Build] {report.summary.result}  {report.summary.totalSize / (1024 * 1024)} MB  errors={report.summary.totalErrors}");
        if (Application.isBatchMode && report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
    }
}
