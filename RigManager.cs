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
    public static readonly Dictionary<string, (Transform, GameObject)> rigs = new();

    public static void Initialize(Main mainInstance)
    {
        instance = mainInstance;
    }

    public static void ClearRigs()
    {
        foreach (var rig in rigs.Values)
            GameObject.Destroy(rig.Item2);
        rigs.Clear();
    }

    public static GameObject LoadRigForPlayer(Player player)
    {
        string playerID = player.Data.GeneralData.PlayFabMasterId;
        if (string.IsNullOrEmpty(playerID)) return null;

        bool isLocal = player == Calls.Players.GetLocalPlayer();

        if (isLocal && !(bool)instance.toggleLocal.SavedValue) return null;
        if (!isLocal && (bool)instance.toggleOthers.SavedValue) return null;

        string basePath = Path.Combine(MelonEnvironment.UserDataDirectory, "CustomAvatars");
        string rigPath = isLocal
            ? Directory.GetFiles(basePath).FirstOrDefault()
            : Path.Combine(basePath, "Opponents", playerID);

        if (string.IsNullOrEmpty(rigPath) || !File.Exists(rigPath))
        {
            instance.LoggerInstance.Warning($"No custom avatar found for {(isLocal ? "you" : player.Data.GeneralData.PublicUsername.TrimString())}");
            return null;
        }

        GameObject rigPrefab = Calls.LoadAssetFromFile<GameObject>(rigPath, "Rig");
        if (rigPrefab == null)
        {
            instance.LoggerInstance.Error($"Failed to load 'Rig' GameObject for {(isLocal ? "local player" : player.Data.GeneralData.PublicUsername.TrimString())}");
            return null;
        }

        var rigInstance = GameObject.Instantiate(rigPrefab);
        rigs[playerID] = (player.Controller.transform.GetChild(1).GetChild(1).GetChild(0), rigInstance);
        
        ApplyRigToPlayer(player, rigInstance);

        return rigInstance;
    }

    public static void ApplyRigToPlayer(Player player, GameObject rig)
    {
        if (player == null || rig == null) return;

        string playerUsername = player.Data.GeneralData.PublicUsername.TrimString();
        var playerRenderer = player.Controller.transform.GetChild(1).GetChild(0).GetComponent<SkinnedMeshRenderer>();
        var rigRenderer = rig.GetComponentInChildren<SkinnedMeshRenderer>(true);

        if (playerRenderer == null || rigRenderer == null) return;

        var playerRigRoot = player.Controller.transform.GetChild(1).GetChild(1);
        ApplyRigToSMR(playerRenderer, playerRigRoot, rig);
        
        instance.LoggerInstance.Msg($"Applied custom rig to player {playerUsername}.");
    }

    public static void ApplyRigBones(Transform rigRoot, Transform playerRigRoot)
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
                if (playerBone.name == "Bone_Pelvis") continue;
                
                rigBone.SetParent(playerBone, true);
                rigBone.localPosition = Vector3.zero;
                rigBone.localRotation = Quaternion.identity;

                rigBone.gameObject.AddComponent<CustomRigBone>();
            }
        }
    }

    public static void ApplyRigToSMR(SkinnedMeshRenderer renderer, Transform skeletonRoot, GameObject rig)
    {
        if (renderer == null || skeletonRoot == null || rig == null) return;

        var rigRenderer = rig.GetComponentInChildren<SkinnedMeshRenderer>(true);

        if (rigRenderer == null) return;

        ApplyRigBones(rig.transform, skeletonRoot);

        renderer.sharedMesh = rigRenderer.sharedMesh;
        renderer.bones = rigRenderer.bones;
        renderer.material.SetTexture("_ColorAtlas", rigRenderer.material.GetTexture("_BaseMap"));
        
        GameObject.Destroy(rigRenderer.gameObject);
    }
}