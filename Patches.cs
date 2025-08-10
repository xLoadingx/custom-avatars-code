using System.Collections;
using UnityEngine;
using HarmonyLib;
using Il2CppRUMBLE.MeshGeneration;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Subsystems;
using MelonLoader;
using RumbleModdingAPI;

namespace CustomAvatars;

public class Patches
{
    [HarmonyPatch(typeof(PlayerController), nameof(PlayerController.Initialize), new[] { typeof(Player) })]
    public static class PlayerSpawn
    {
        private static void Postfix(ref PlayerController __instance, ref Player player)
        {
            if (!Calls.Players.IsHost() || Calls.Scene.GetSceneName() != "Park") return;
            MelonCoroutines.Start(RigManager.LoadRigForPlayer(player, null));
        }
    }

    [HarmonyPatch(typeof(PlayerController), nameof(PlayerController.OnDestroy))]
    public static class PlayerRemove
    {
        private static void Prefix(ref PlayerController __instance)
        {
            if (!Main.instance.sceneInitialized || __instance == null) return;
            string leftId = __instance.assignedPlayer.Data.GeneralData.PlayFabMasterId;

            if (RigManager.rigs.TryGetValue(leftId, out var rigObj))
            {
                GameObject.Destroy(rigObj.Root);
                RigManager.rigs.Remove(leftId);
            }
        }
    }

    [HarmonyPatch(typeof(PlayerVisuals), nameof(PlayerVisuals.ApplyPlayerVisuals))]
    public static class ApplyPlayerVisuals
    {
        private static void Prefix(PlayerCharacterBaker.GeneratedPlayerVisuals generatedVisuals)
        {
            if (Main.instance.sceneInitialized && (bool)(Main.instance.toggleLocal?.SavedValue ?? false)) {}
                // MelonCoroutines.Start(DelayedInitialize());
        }

        private static IEnumerator DelayedInitialize()
        {
            yield return new WaitForEndOfFrame();
            Main.instance.Initialize();
        }
    }
}