using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;
using System;
using System.Diagnostics;

public class AppBuilder
{
    public static string AssetsRoot => Application.dataPath;
    public static string BuildRoot => Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(AssetsRoot)), "Builds");
    public static string PatchTool => Path.Combine(Path.GetDirectoryName(AssetsRoot), "Patcher", "UnityApplicationPatcherCLI.exe");

    public static string AppName => Renderite.Shared.Helper.PROCESS_NAME;
    public static string Subfolder => Renderite.Shared.Helper.FOLDER_PATH;

    public static string BuildModifier => "";

    public static string WindowsRoot => Path.Combine(BuildRoot, $"Windows{BuildModifier}", Subfolder);
    public static string WindowsIL2CPP_Root => Path.Combine(BuildRoot, $"Windows{BuildModifier}IL2CPP", Subfolder);
    public static string WindowsDebugRoot => Path.Combine(BuildRoot, $"Windows{BuildModifier}Debug", Subfolder);
    public static string LinuxRoot => Path.Combine(BuildRoot, $"Linux{BuildModifier}", Subfolder);
    public static string AndroidRoot => Path.Combine(BuildRoot, $"Android{BuildModifier}");

    public static string OculusApkFile => Path.Combine(AndroidRoot, "Oculus.apk");
    public static string ScreenApkFile => Path.Combine(AndroidRoot, "Screen.apk");

    static void PatchBinary(string path)
    {
        var startInfo = new ProcessStartInfo(PatchTool);
        startInfo.Arguments = $"-windows -applicationPath \"{path}\"";

        var process = Process.Start(startInfo);

        process.WaitForExit();
    }

    static void UpdateDefines(ref string defines)
    {
        if (Directory.Exists(Path.Combine(AssetsRoot, "UniversalMediaPlayer")))
        {
            UnityEngine.Debug.Log($"Compiling with UMP support: ON");
            defines += "UMP_SUPPORTED;";
        }
        else
            UnityEngine.Debug.Log($"Compiling with UMP support: OFF");
    }

    [MenuItem("Build/Windows")]
    public static void BuildWindows()
    {
        BuildWindows(WindowsRoot, ScriptingImplementation.Mono2x);
    }

    [MenuItem("Build/Windows (IL2CPP) - Release")]
    public static void BuildWindowsIL2CPP_Release()
    {
        PlayerSettings.SetIl2CppCompilerConfiguration(BuildTargetGroup.Standalone, Il2CppCompilerConfiguration.Release);
        BuildWindows(WindowsIL2CPP_Root, ScriptingImplementation.IL2CPP);
    }

    [MenuItem("Build/Windows (IL2CPP) - Master")]
    public static void BuildWindowsIL2CPP_Master()
    {
        PlayerSettings.SetIl2CppCompilerConfiguration(BuildTargetGroup.Standalone, Il2CppCompilerConfiguration.Release);
        BuildWindows(WindowsIL2CPP_Root, ScriptingImplementation.IL2CPP);
    }

    public static void BuildWindows(string path, ScriptingImplementation runtime)
    {
        UpdateVersionInfo();

        var executable = Path.Combine(path, $"{AppName}.exe");
        var dataFolder = Path.Combine(path, $"{AppName}_Data");

        var defines = @"POST_PROCESSING_STACK_V2;";

        UpdateDefines(ref defines);

        PlayerSettings.virtualRealitySupported = true;
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, runtime);

        PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone, defines);

        var buildReport = BuildPipeline.BuildPlayer(new BuildPlayerOptions()
        {
            scenes = new string[] { "Assets/Engine.unity" },
            locationPathName = executable,
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
        });

        if (buildReport.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            return;

        // Copy the steam file into the folder so the renderer is recognized as Resonite by Steam
        File.Copy("steam_appid.txt", Path.Combine(path, "steam_appid.txt"), true);

        PatchBinary(path);
    }

    [MenuItem("Build/Windows (DEBUG)")]
    public static void BuildWindowsDebug()
    {
        UpdateVersionInfo();

        var path = WindowsDebugRoot;

        var executable = Path.Combine(path, $"{AppName}.exe");
        var dataFolder = Path.Combine(path, $"{AppName}_Data");

        var defines = @"POST_PROCESSING_STACK_V2;PROFILE;";

        UpdateDefines(ref defines);

        PlayerSettings.virtualRealitySupported = true;
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);

        PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone, defines);

        var buildReport = BuildPipeline.BuildPlayer(new BuildPlayerOptions()
        {
            scenes = new string[] { "Assets/Engine.unity" },
            locationPathName = executable,
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.AllowDebugging | BuildOptions.ConnectWithProfiler | BuildOptions.Development
        });

        if (buildReport.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            return;

        // Copy the steam file into the folder so the renderer is recognized as Resonite by Steam
        File.Copy("steam_appid.txt", Path.Combine(path, "steam_appid.txt"), true);

        PatchBinary(path);
    }

    static void RemoveIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    [MenuItem("Build/Linux")]
    public static void BuildLinux()
    {
        UpdateVersionInfo();

        var path = LinuxRoot;

        RemoveIfExists(Path.Combine(path, "UMPInstaller.sh"));
        RemoveIfExists(Path.Combine(path, "UMPRemover.sh"));

        var executable = Path.Combine(path, $"{AppName}.x86_64");
        var dataFolder = Path.Combine(path, $"{AppName}_Data");

        var defines = @"POST_PROCESSING_STACK_V2;DISABLESTEAMWORKS";

        UpdateDefines(ref defines);

        PlayerSettings.virtualRealitySupported = true;
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);

        PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone, defines);

        var buildReport = BuildPipeline.BuildPlayer(new BuildPlayerOptions()
        {
            scenes = new string[] { "Assets/Engine.unity" },
            locationPathName = executable,
            target = BuildTarget.StandaloneLinux64,
            targetGroup = BuildTargetGroup.Standalone
        });

        if (buildReport.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            return;

        PatchBinary(path);
    }

    static void UpdateVersionInfo()
    {
        PlayerSettings.productName = AppName;

        // Version the renderer depending on when it was built

        var now = DateTime.UtcNow;
        PlayerSettings.bundleVersion = $"{now.Year}.{now.Month}.{now.Day}.{now.Minute + now.Hour * 60}";
    }
}
