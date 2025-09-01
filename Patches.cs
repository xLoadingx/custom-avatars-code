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

namespace CustomAvatars;

public class Patches
{
    public static List<string> loadedPlayers = new();
    
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

    [HarmonyPatch(typeof(MonoBehaviourPunCallbacks), nameof(MonoBehaviourPunCallbacks.OnPlayerPropertiesUpdate))]
    public static class PlayerPropsChanged
    {
        static void Postfix(Il2CppPhoton.Realtime.Player targetPlayer, Hashtable changedProps)
        {
            if (changedProps.ContainsKey("CA_Avatar"))
            {
                MelonLogger.Msg($"Player with rig has changed props.");
                
                var rumblePlayer = Calls.Players.GetPlayerByActorNo(targetPlayer.actorNumber);

                if (rumblePlayer != null)
                {
                    var rig = rumblePlayer.Controller.GetComponent<CustomRig>();
                    if (rig != null)
                    {
                        RigManager.ResolveRigState(rumblePlayer, rig);
                    }
                }
            }
        }
    }
    
    [HarmonyPatch(typeof(PlayerController), nameof(PlayerController.Initialize), new[] { typeof(Player) })]
    public static class PlayerSpawn
    {
        private static void Postfix(ref PlayerController __instance, ref Player player)
        {
            if (!(bool)(Main.instance?.toggleOthers?.SavedValue ?? false) && __instance.controllerType == ControllerType.Remote) return;
            
            string masterId = player.Data.GeneralData.PlayFabMasterId;
            if (__instance.controllerType == ControllerType.Local || CustomAvatars.Patches.loadedPlayers.Contains(masterId)) return;
            if (!CustomAvatars.Patches.loadedPlayers.Contains(masterId))
                loadedPlayers.Add(masterId);
            
            ApplyRig(player);
        }
    }

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
                    
                    if (File.Exists(rigObj.AvatarFilePath) && __instance.ControllerType == ControllerType.Remote)
                        File.Delete(rigObj.AvatarFilePath);
                }

                RigManager.rigs.Remove(leftId);
            }
        }
    }

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