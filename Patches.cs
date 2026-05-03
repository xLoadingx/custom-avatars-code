using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppRUMBLE.MeshGeneration;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Subsystems;
using MelonLoader;
using UnityEngine;

namespace CustomAvatars;

public class Patches
{
    public static Queue<Player> downloadQueue = new();
    public static HashSet<string> activeDownloads = new();

    private static int MAX_CONCURRENT => Main.instance.MaxConcurrentDownloads.Value;
    
    [HarmonyPatch(typeof(PlayerVisuals), nameof(PlayerVisuals.ApplyPlayerVisuals))]
    public static class Patch_PlayerVisuals_ApplyPlayerVisuals
    {
        static void Postfix(PlayerVisuals __instance, PlayerCharacterBaker.GeneratedPlayerVisuals generatedVisuals)
        {
            var player = __instance.parentController.assignedPlayer;

            if (player == null) return;
            
            string id = player.Data.GeneralData.PlayFabMasterId;

            if (activeDownloads.Contains(id))
                return;
            
            Main.DebugLog($"Enqueue {id} for download.");

            downloadQueue.Enqueue(player);
            ProcessQueue();
        }
    }

    [HarmonyPatch(typeof(PlayerController), nameof(PlayerController.DeActivate))]
    public static class Patch_PlayerController_DeActivate
    {
        static void Prefix(PlayerController __instance)
        {
            var customRig = __instance.GetComponent<CustomRig>();

            if (customRig != null)
                Object.Destroy(customRig);
        }
    }

    public static void ProcessQueue()
    {
        Main.DebugLog($"ProcessQueue | active: {activeDownloads.Count}/{MAX_CONCURRENT}, queued: {downloadQueue.Count}");
        
        while (activeDownloads.Count < MAX_CONCURRENT && downloadQueue.Count > 0)
        {
            var player = downloadQueue.Dequeue();
            string id = player.Data.GeneralData.PlayFabMasterId;
            
            Main.DebugLog($"Dequeue {id}");

            if (!activeDownloads.Add(id))
                continue;

            MelonCoroutines.Start(HandleAvatarForRemotePlayer(player, id));
        }
    }

    private static IEnumerator HandleAvatarForRemotePlayer(Player player, string id)
    {
        yield return RigManager.LoadAndApplyAvatarForPlayer(player, onDone: success =>
        {
            Main.DebugLog($"Done {id} | success: {success}");
            
            activeDownloads.Remove(id);
            ProcessQueue();
        });
    }
}