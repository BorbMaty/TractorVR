using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Windows 64-bit build (PC VR over Quest Link / Air Link; quality level "PC") → Builds/PC/TractorVR.exe.
///   CLI: Unity.exe -batchmode -quit -projectPath . -buildTarget StandaloneWindows64 -executeMethod BuildPC.Build
/// (-buildTarget switches the editor to Windows; the next Android run switches back, both imports are cached.)
/// </summary>
public static class BuildPC
{
    const string ExePath = "Builds/PC/TractorVR.exe";

    [MenuItem("Tractor VR/Build PC (Windows)")]
    public static void Build()
    {
        var options = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/TractorFlat.unity" },
            locationPathName = ExePath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        };
        BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"[Build] PC {report.summary.result}  {report.summary.totalSize / (1024 * 1024)} MB  errors={report.summary.totalErrors}");
        if (Application.isBatchMode && report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
    }
}
