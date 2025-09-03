using System.Collections;
using UnityEngine;
using HarmonyLib;
using Il2CppPhoton.Pun;
using Il2CppRUMBLE.CharacterCreation.Interactable;
using Il2CppRUMBLE.MeshGeneration;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Subsystems;
using Il2CppTMPro;
using MelonLoader;
using RumbleModdingAPI;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Hashtable = Il2CppExitGames.Client.Photon.Hashtable;

namespace CustomAvatars;

public class Patches
{
    public static List<string> loadedPlayers = new();
    
    // Applies rig to a remote player if they have one uploaded
    // The PlayerHasAvatar function seems to be the only reason this works due to its delay
    public static void ApplyRig(Player player)
    {
        MelonCoroutines.Start(
            RemoteAvatarLoader.PlayerHasAvatar(player.Data.GeneralData.PlayFabMasterId, avatarDetails =>
            {
                if (avatarDetails.hasAvatar)
                {
                    var visuals = player.Controller.GetSubsystem<PlayerVisuals>();
        
                    var customRig = player.Controller.gameObject.AddComponent<CustomRig>();
                    customRig.CaptureOriginal(player.Data.GeneralData.PlayFabMasterId, false, visuals.renderer);
        
                    visuals.renderer.material = Main.poseGhostMaterial;
                    
                    MelonCoroutines.Start(RigManager.LoadRigForPlayer(player, null, true, avatarDetails.returnedSha));
                }
            })
        );
    }
    
    // Adds a rig to a newly spawned remote player
    [HarmonyPatch(typeof(PlayerController), nameof(PlayerController.Initialize), new[] { typeof(Player) })]
    public static class PlayerSpawn
    {
        private static void Postfix(ref PlayerController __instance, ref Player player)
        {
            string masterId = player.Data.GeneralData.PlayFabMasterId;
            if (__instance.controllerType == ControllerType.Local || CustomAvatars.Patches.loadedPlayers.Contains(masterId)) return;
            if (!CustomAvatars.Patches.loadedPlayers.Contains(masterId))
                loadedPlayers.Add(masterId);
            
            ApplyRig(player);
        }
    }

    // Cleans up rig + deletes file when a player leaves
    [HarmonyPatch(typeof(PlayerController), nameof(PlayerController.OnDestroy))]
    public static class PlayerRemove
    {
        private static void Prefix(PlayerController __instance)
        {
            if (!Main.instance.sceneInitialized || __instance == null) return;

            var assigned = __instance.assignedPlayer;
            if (assigned?.Data?.GeneralData == null) return;

            string leftId = assigned.Data.GeneralData.PlayFabMasterId;

            if (RigManager.rigs.TryGetValue(leftId, out var rigObj))
            {
                if (rigObj != null)
                {
                    GameObject.Destroy(rigObj.Root);
                    Main.instance.RemoveRigFromList(rigObj);
                }

                RigManager.rigs.Remove(leftId);
                loadedPlayers.Remove(leftId);
            }
        }
    }

    // Stops default visuals from overriding cvustom rigs in Gym
    [HarmonyPatch(typeof(DressingRoom), nameof(DressingRoom.UpdatePlayerVisuals))]
    public static class DressingRoomVisuals
    {
        private static bool Prefix(bool saveChanges)
        {
            if (Main.instance.sceneInitialized && (bool)(Main.instance.toggleLocal?.SavedValue ?? false))
                return false;

            return true;
        }
    }
}