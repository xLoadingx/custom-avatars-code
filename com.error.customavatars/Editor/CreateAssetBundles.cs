using UnityEngine;
using UnityEditor;
using System.IO;
using CustomAvatars;

public class AvatarBundleBuilder : EditorWindow
{
    private AvatarDescriptor avatarDescriptor;
    private string outputPath;

    [MenuItem("Custom Avatars/RUMBLE Avatar Builder")]
    public static void ShowWindow()
    {
        GetWindow<AvatarBundleBuilder>("Avatar Builder");
    }

    private void OnEnable()
    {
        outputPath = EditorPrefs.GetString("RUMBLE_UserDataPath", "E:\\SteamLibrary\\steamapps\\common\\RUMBLE\\UserData\\");
    }

    private void OnGUI()
    {
        GUILayout.Label("Avatar Builder", EditorStyles.boldLabel);

        avatarDescriptor = (AvatarDescriptor)EditorGUILayout.ObjectField(
            "Avatar Descriptor",
            avatarDescriptor,
            typeof(AvatarDescriptor),
            true
        );

        EditorGUILayout.BeginHorizontal();
        outputPath = EditorGUILayout.TextField("Output Path", outputPath);
        if (GUILayout.Button("...", GUILayout.Width(30)))
        {
            string selected = EditorUtility.OpenFolderPanel("Select Output Folder", outputPath, "");
            if (!string.IsNullOrEmpty(selected))
            {
                outputPath = selected;
                EditorPrefs.SetString("RUMBLE_UserDataPath", outputPath);
                Debug.Log($"Saved UserData path: {outputPath}");
            }
        }
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(10);

        if (GUILayout.Button("Build Bundle"))
        {
            if (avatarDescriptor == null)
            {
                Debug.LogError("No AvatarDescriptor assigned!");
                return;
            }

            if (!Directory.Exists(outputPath))
                Directory.CreateDirectory(outputPath);

            AvatarDescriptorEditor.BuildConfig(avatarDescriptor);

            BuildPipeline.BuildAssetBundles(outputPath, BuildAssetBundleOptions.None, EditorUserBuildSettings.activeBuildTarget);

            var catalog = Path.Combine(outputPath, Path.GetFileName(outputPath));
            var catalogManifest = catalog + ".manifest";
            var bundleManifest = Path.Combine(outputPath, "rig.rumbleavatar.manifest");

            void TryDelete(string path)
            {
                if (File.Exists(path)) File.Delete(path);
            }

            TryDelete(catalog);
            TryDelete(catalogManifest);
            TryDelete(bundleManifest);

            Debug.Log($"Built bundle for {avatarDescriptor.name} at {outputPath}");
        }
    }

    
}