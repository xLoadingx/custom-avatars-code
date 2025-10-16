using System.Collections;
using UnityEngine;
using HarmonyLib;
using Il2CppPhoton.Pun;
using Il2CppRUMBLE.CharacterCreation.Interactable;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Subsystems;
using Il2CppTMPro;
using MelonLoader;
using RumbleModdingAPI;

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
                if (!avatarDetails.hasAvatar)
                    return;
                
                var visuals = player.Controller.GetSubsystem<PlayerVisuals>();
        
                var customRig = player.Controller.gameObject.AddComponent<CustomRig>();
                customRig.CaptureOriginal(player.Data.GeneralData.PlayFabMasterId, false, visuals.renderer);
        
                visuals.renderer.material = Main.poseGhostMaterial;
                    
                MelonCoroutines.Start(RigManager.LoadRigForPlayer(player, (rig) =>
                {
                    MelonCoroutines.Start(RigManager.FixHUDCamera(player.Data.GeneralData.PlayFabMasterId));
                }, true, avatarDetails.returnedSha));
            })
        );
    }
    
    // Adds a rig to a newly spawned remote player
    [HarmonyPatch(typeof(PlayerController), nameof(PlayerController.Initialize), typeof(Player))]
    public static class PlayerSpawn
    {
        private static void Postfix(ref PlayerController __instance, ref Player player)
        {
            string masterId = player.Data.GeneralData.PlayFabMasterId;
            if (__instance.controllerType == ControllerType.Local || loadedPlayers.Contains(masterId)) return;
            if (!loadedPlayers.Contains(masterId))
                loadedPlayers.Add(masterId);
            
            ApplyRig(player);

            MelonCoroutines.Start(CheckForMod(player));
        }

        private static IEnumerator CheckForMod(Player player)
        {
            yield return new WaitForSeconds(1f);
            
            var photonPlayer = PhotonNetwork.CurrentRoom?.GetPlayer(player.Data.GeneralData.ActorNo);
            var props = photonPlayer?.CustomProperties;
            bool isNewerVersion = false;
                
            if (props != null)
            {
                if (props.TryGetValue("CA_ModVersion", out var remoteVerObj) &&
                    PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue("CA_ModVersion", out var localVerObj))
                {
                    string remoteVer = remoteVerObj?.ToString() ?? "Unknown";
                    string localVer = localVerObj?.ToString() ?? "Unknown";

                    var tag = Calls.Create.NewText(
                        "[CustomAvatars]",
                        0.5f,
                        new Color(0, 0.8f, 0, 1),
                        Vector3.zero,
                        Quaternion.identity
                    );

                    tag.transform.SetParent(player.Controller.transform.GetChild(9));

                    tag.name = "[CustomAvatars]";
                    tag.transform.localRotation = Quaternion.Euler(0, 180, 0);
                    tag.transform.localPosition = new Vector3(-0.148f, 0.1484f, 0f);

                    if (Version.TryParse(remoteVer, out var rV) && Version.TryParse(localVer, out var lV))
                    {
                        if (rV > lV)
                        {
                            tag.GetComponent<TextMeshPro>().color = Color.cyan;
                            isNewerVersion = true;
                        }
                    }
                }
            }
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

    // Stops default visuals from overriding custom rigs in Gym
    [HarmonyPatch(typeof(DressingRoom), nameof(DressingRoom.UpdatePlayerVisuals))]
    public static class DressingRoomVisuals
    {
        private static bool Prefix(bool saveChanges)
        {
            return !IsEnabled();
        }

        private static void Postfix(bool saveChanges)
        {
            if (IsEnabled())
                return;
            
            var localPlayer = Calls.Players.GetLocalPlayer()?.Controller;
            if (localPlayer?.TryGetComponent<CustomRig>(out var rig) ?? false)
            {
                rig.OriginalVisualsMaterial = new Material(localPlayer.GetSubsystem<PlayerVisuals>().NonHeadClippedMaterial);
                rig.OriginalVisualsMaterial.hideFlags = HideFlags.DontUnloadUnusedAsset | HideFlags.HideAndDontSave;
            }
        }

        private static bool IsEnabled()
        {
            return Main.instance.sceneInitialized && (bool)(Main.instance.toggleLocal?.SavedValue ?? false);
        }
    }
}