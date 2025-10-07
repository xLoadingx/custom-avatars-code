using UnityEditor;
using UnityEngine;

public class AssetBundleTools
{
    [MenuItem("Assets/Asset Bundles/Clear All Bundle Assignments")]
    private static void ClearAllBundleAssignments()
    {
        string[] allBundleNames = AssetDatabase.GetAllAssetBundleNames();
        foreach (string bundle in allBundleNames)
        {
            AssetDatabase.RemoveAssetBundleName(bundle, true);
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"✅ Cleared {allBundleNames.Length} AssetBundle assignments.");
    }

    [MenuItem("Assets/Asset Bundles/Build All Bundles")]
    private static void BuildAllBundles()
    {
        string outputPath = "Assets/AssetBundles";

        if (!System.IO.Directory.Exists(outputPath))
            System.IO.Directory.CreateDirectory(outputPath);

        BuildPipeline.BuildAssetBundles(
            outputPath,
            BuildAssetBundleOptions.None,
            EditorUserBuildSettings.activeBuildTarget
        );

        AssetDatabase.Refresh();
        Debug.Log("✅ Asset Bundles built successfully at: " + outputPath);
    }
}
