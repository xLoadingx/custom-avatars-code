using System.Collections;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Subsystems;
using MelonLoader;
using MelonLoader.Utils;
using RumbleModdingAPI;
using UnityEngine;

namespace CustomAvatars;

public static class RigManager
{
    private static Main instance;
    public static readonly Dictionary<string, CustomRig> rigs = new();

    public static void Initialize(Main mainInstance)
    {
        instance = mainInstance;
    }

    public static void ClearRigs()
    {
        foreach (var rig in rigs.Values)
            GameObject.Destroy(rig.Root);
        rigs.Clear();
    }

    public static IEnumerator LoadRigForPlayer(Player player, Action<GameObject> onLoaded, bool log = true)
    {
        string playerID = player.Data.GeneralData.PlayFabMasterId;
        if (string.IsNullOrEmpty(playerID)) yield break;

        bool isLocal = player == Calls.Players.GetLocalPlayer();

        if (isLocal && !(bool)instance.toggleLocal.SavedValue) yield break;
        if (!isLocal && !(bool)instance.toggleOthers.SavedValue) yield break;

        string opponentPath = Path.Combine(MelonEnvironment.UserDataDirectory, "CustomAvatars", "Opponents");
        if (!Directory.Exists(opponentPath)) Directory.CreateDirectory(opponentPath);
        
        if (!File.Exists(Path.Combine(opponentPath, playerID)))
        {
            if (log)
                MelonLogger.Msg($"Downloading avatar for path {opponentPath}");
            yield return MelonCoroutines.Start(RemoteAvatarLoader.DownloadToFile(playerID, Path.Combine(opponentPath, playerID)));
        }
        
        string basePath = Path.Combine(MelonEnvironment.UserDataDirectory, "CustomAvatars");
        string rigPath = isLocal
            ? Directory.GetFiles(basePath).FirstOrDefault()
            : Path.Combine(basePath, "Opponents", playerID);

        if (string.IsNullOrEmpty(rigPath) || !File.Exists(rigPath))
        {
            instance.LoggerInstance.Warning($"No custom avatar found for {(isLocal ? "you" : player.Data.GeneralData.PublicUsername.TrimString())}");
            yield break;
        }

        GameObject rigPrefab = Calls.LoadAssetFromFile<GameObject>(rigPath, "Rig");
        if (rigPrefab == null)
        {
            instance.LoggerInstance.Error($"Failed to load 'Rig' GameObject for {(isLocal ? "local player" : player.Data.GeneralData.PublicUsername.TrimString())}");
            yield break;
        }

        var rigInstance = GameObject.Instantiate(rigPrefab);
        rigInstance.name = $"RIG - {playerID}";
        rigInstance.transform.SetParent(Main.instance.rigParent.transform, true);
        var customRig = player.Controller.gameObject.GetOrAddComponent<CustomRig>();
        customRig.CaptureRig(rigInstance);
        
        rigs[playerID] = customRig;
        
        if (log)
            instance.LoggerInstance.Msg($"Loading rig for player {playerID}");
        
        ApplyRigToPlayer(player, rigInstance);

        onLoaded?.Invoke(rigInstance);
    }

    public static void ApplyRigToPlayer(Player player, GameObject rig, bool log = true)
    {
        if (player == null || rig == null) return;

        string playerUsername = player.Data.GeneralData.PublicUsername.TrimString();
        var playerRenderer = player.Controller.transform.GetChild(1).GetChild(0).GetComponent<SkinnedMeshRenderer>();
        var rigRenderer = rig.GetComponentInChildren<SkinnedMeshRenderer>(true);

        if (playerRenderer == null || rigRenderer == null) return;

        var playerRigRoot = player.Controller.transform.GetChild(1).GetChild(1);
        ApplyRigToSMR(playerRigRoot, rig, player.Controller.GetComponent<CustomRig>());
        
        if (log)
            instance.LoggerInstance.Msg($"Applied custom rig to player {playerUsername}.");
    }

    public static void ApplyRigBones(Transform rigRoot, Transform playerRigRoot, CustomRig customRigComp = null)
    {
        foreach (var old in playerRigRoot.GetComponentsInChildren<CustomRigBone>(true))
            UnityEngine.Object.Destroy(old.gameObject);
        
        var playerBones = new Dictionary<string, Transform>();
        foreach (var bone in playerRigRoot.GetComponentsInChildren<Transform>(true))
            playerBones[bone.name] = bone;

        foreach (var rigBone in rigRoot.transform.GetComponentsInChildren<Transform>(true))
        {
            rigBone.gameObject.layer = LayerMask.NameToLayer("Default");
            if (playerBones.TryGetValue(rigBone.name, out var playerBone))
            {
                if (playerBone.name is "Bone_Pelvis" or "Bone_Head")
                {
                    rigBone.transform.SetParent(rigRoot.transform, true);
                    continue;
                }
                
                rigBone.SetParent(playerBone, true);
                rigBone.localPosition = Vector3.zero;
                rigBone.localRotation = Quaternion.identity;

                rigBone.gameObject.AddComponent<CustomRigBone>();
            }
        }
    }

    public static void ApplyRigToSMR(Transform skeletonRoot, GameObject rig, CustomRig customRig = null, SkinnedMeshRenderer renderer = null)
    {
        void ApplyRig(Transform customRig, SkinnedMeshRenderer rigRenderer, SkinnedMeshRenderer playerRenderer, Material originalMaterial, CustomRig customRigComp = null)
        {
            ApplyRigBones(customRig, skeletonRoot, customRigComp);
            
            playerRenderer.sharedMesh = rigRenderer.sharedMesh;
            playerRenderer.bones = rigRenderer.bones;

            if (originalMaterial != null)
                playerRenderer.material = originalMaterial;
            playerRenderer.material.SetTexture("_ColorAtlas", rigRenderer.material.GetTexture("_BaseMap"));

            if (customRigComp != null)
            {
                customRigComp.RigMaterial = new Material(playerRenderer.material);
                customRigComp.RigMaterial.hideFlags = HideFlags.DontUnloadUnusedAsset | HideFlags.HideAndDontSave;

                customRigComp.RigMesh = UnityEngine.Object.Instantiate(rigRenderer.sharedMesh);
                customRigComp.RigMesh.hideFlags = HideFlags.DontUnloadUnusedAsset | HideFlags.HideAndDontSave;
            }
                
            GameObject.Destroy(rigRenderer.gameObject);
        }
        
        if (skeletonRoot == null || rig == null) return;

        var rigRenderer = rig.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (rigRenderer == null) return;
        
        if (renderer == null)
        {
            if (customRig != null && customRig.MeshRenderer != null)
                ApplyRig(customRig.Root.transform, rigRenderer, customRig.MeshRenderer, customRig.OriginalMaterial, customRig);
        }
        else
        {
            ApplyRig(rig.transform, rigRenderer, renderer, renderer.material);
        }
    }
    
    static Transform FindDeep(Transform t, string name)
    {
        if (t.name.Equals(name, StringComparison.Ordinal)) return t;
        for (int i = 0; i < t.childCount; i++)
        {
            var r = FindDeep(t.GetChild(i), name);
            if (r) return r;
        }
        return null;
    }
}