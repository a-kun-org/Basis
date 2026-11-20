using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

/// <summary>Android XR (XREAL AURA) 実験ビルド入口。upstream 向けではない。</summary>
public static class BasisAndroidXrBuild
{
    static readonly string OutputPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Builds", "AndroidXR", "BasisVR-AndroidXR.apk"));
    const string OpenXRLoaderType = "UnityEngine.XR.OpenXR.OpenXRLoader";

    // 有効にする機能 (featureId)
    static readonly string[] Enable =
    {
        "com.unity.openxr.feature.androidxr-support",
        "com.unity.openxr.feature.input.handtrackingsubsystem",
        "com.unity.openxr.feature.input.handinteraction",
        "com.unity.openxr.feature.input.handinteractionposes",
    };

    // 無効にする機能 (featureId / 型名 / 表示名のいずれかにマッチ)
    static readonly Regex Disable = new Regex("meta|oculus|pico|vive|valve|htc|spacewarp|foveat|bodytracking|microsofthand|eyetracking|eyegaze", RegexOptions.IgnoreCase);

    public static void Build()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            Debug.Log($"[BasisAndroidXr] SwitchActiveBuildTarget => {EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android)}");
        ConfigureOpenXR();
        ConfigureXRGeneralSettings();

        EditorUserBuildSettings.buildAppBundle = false;
        PlayerSettings.Android.useCustomKeystore = false;
        Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
        AssetDatabase.SaveAssets();

        AddressableAssetSettings addr = AddressableAssetSettingsDefaultObject.Settings;
        var originalOption = AddressableAssetSettings.PlayerBuildOption.PreferencesValue;
        if (addr != null)
        {
            originalOption = addr.BuildAddressablesWithPlayerBuild;
            AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
            Debug.Log($"[BasisAndroidXr] Addressables error='{result.Error}' output='{result.OutputPath}'");
            if (!string.IsNullOrWhiteSpace(result.Error)) throw new BuildFailedException($"Addressables build failed: {result.Error}");
            addr.BuildAddressablesWithPlayerBuild = AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer;
        }
        try
        {
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = OutputPath, target = BuildTarget.Android, targetGroup = BuildTargetGroup.Android, options = BuildOptions.None
            });
            Debug.Log($"[BasisAndroidXr] Build result={report.summary.result} output={report.summary.outputPath} errors={report.summary.totalErrors} warnings={report.summary.totalWarnings} size={report.summary.totalSize}");
            if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException($"Player build failed: {report.summary.result}");
        }
        finally
        {
            if (addr != null) addr.BuildAddressablesWithPlayerBuild = originalOption;
        }
        Debug.Log("[BasisAndroidXr] Build succeeded!");
    }

    public static void ConfigureOpenXR()
    {
        UnityEditor.XR.OpenXR.Features.FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
        OpenXRSettings settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
        if (settings == null) throw new BuildFailedException("OpenXRSettings for Android not found.");
        foreach (OpenXRFeature f in settings.GetFeatures<OpenXRFeature>())
        {
            string id = GetFeatureId(f);
            string ident = id + " " + f.GetType().FullName + " " + f.name;
            bool want = f.enabled;
            if (Disable.IsMatch(ident) && !id.Contains("androidxr")) want = false;
            else if (Enable.Contains(id)) want = true;
            if (want != f.enabled) { f.enabled = want; EditorUtility.SetDirty(f); }
            Debug.Log($"[BasisAndroidXr] feature enabled={f.enabled} id={id} type={f.GetType().FullName}");
        }
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
    }

    static string GetFeatureId(OpenXRFeature f)
    {
        var field = typeof(OpenXRFeature).GetField("featureIdInternal", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        return (field?.GetValue(f) as string) ?? "";
    }

    static void ConfigureXRGeneralSettings()
    {
        XRGeneralSettings general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
        if (general == null) throw new BuildFailedException("XRGeneralSettings for Android not found.");
        general.InitManagerOnStart = true;
        if (general.Manager == null) throw new BuildFailedException("XRManagerSettings for Android is null.");
        bool ok = XRPackageMetadataStore.AssignLoader(general.Manager, OpenXRLoaderType, BuildTargetGroup.Android);
        Debug.Log($"[BasisAndroidXr] AssignLoader OpenXR => {ok}; loaders=[{string.Join(", ", general.Manager.activeLoaders.Select(l => l.GetType().Name))}] initOnStart={general.InitManagerOnStart}");
        EditorUtility.SetDirty(general);
        EditorUtility.SetDirty(general.Manager);
        AssetDatabase.SaveAssets();
    }
}
