#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEditor.XR.Management.Metadata;

public static class XRAutoConfigurator
{
    [InitializeOnLoadMethod]
    static void ConfigureXR()
    {
        EditorUserBuildSettings.selectedBuildTargetGroup = BuildTargetGroup.Android;

        var generalSettings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);

        if (generalSettings == null)
        {
            Debug.LogWarning("XR General Settings for Android not found. XR Management may not be initialized yet.");
            return;
        }

        if (generalSettings.Manager == null)
        {
            var manager = ScriptableObject.CreateInstance<XRManagerSettings>();
            AssetDatabase.CreateAsset(manager, "Assets/XRManagerSettings.asset");
            generalSettings.Manager = manager;
            EditorUtility.SetDirty(generalSettings);
        }

        XRPackageMetadataStore.AssignLoader(
            generalSettings.Manager,
            "UnityEngine.XR.ARCore.ARCoreLoader",
            BuildTargetGroup.Android
        );

        EditorUtility.SetDirty(generalSettings.Manager);
        AssetDatabase.SaveAssets();

        Debug.Log("ARCore Loader configured for Android.");
    }
}
#endif