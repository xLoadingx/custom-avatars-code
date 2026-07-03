using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppRUMBLE.MeshGeneration;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Subsystems;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

namespace CustomAvatars;

public class Patches
{
    public static readonly HashSet<string> loadingRemoteAvatars = new();
    public static readonly HashSet<string> attemptedRemoteLoads = new();

    [HarmonyPatch(typeof(PlayerVisuals), nameof(PlayerVisuals.ApplyPlayerVisuals))]
    public static class Patch_PlayerVisuals_ApplyPlayerVisuals
    {
        static void Postfix(
            PlayerVisuals __instance,
            PlayerCharacterBaker.GeneratedPlayerVisuals generatedVisuals
        )
        {
            var player = __instance?.parentController?.assignedPlayer;

            if (player == null)
            {
                Main.DebugLog("ApplyPlayerVisuals postfix: assignedPlayer was null");
                return;
            }

            TryLoadRemoteAvatar(player, "ApplyPlayerVisuals");
        }
    }

    [HarmonyPatch(typeof(PlayerController), nameof(PlayerController.DeActivate))]
    public static class Patch_PlayerController_DeActivate
    {
        static void Prefix(PlayerController __instance)
        {
            if (__instance == null)
                return;

            var customRig = __instance.GetComponent<CustomRig>();

            if (customRig != null)
                Object.Destroy(customRig);

            string id = __instance.assignedPlayer?.Data?.GeneralData?.PlayFabMasterId;

            if (!string.IsNullOrEmpty(id))
            {
                loadingRemoteAvatars.Remove(id);
                attemptedRemoteLoads.Remove(id);

                Main.DebugLog($"Player deactivated. Cleared remote avatar state for {id}");
            }
        }
    }

    [HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.BeginCameraRendering))]
    public static class Patch_PlayerCamera_BeginCameraRendering
    {
        private static readonly int IsLocalPlayerId = Shader.PropertyToID("_IsLocalPlayer");

        static void Postfix(PlayerCamera __instance, ScriptableRenderContext context, Camera cam)
        {
            if (cam == null)
                return;

            var renderer = Main.LocalPlayer?.Controller?.PlayerVisuals?.renderer;
            if (renderer == null)
                return;

            bool hideHead = __instance.DontRenderPlayerHeadCameras.Contains(cam);

            foreach (var mat in renderer.materials)
            {
                if (mat != null && mat.HasProperty(IsLocalPlayerId))
                    mat.SetFloat(IsLocalPlayerId, hideHead ? 1f : 0f);
            }
        }
    }

    public static bool TryLoadRemoteAvatar(Player player, string reason)
    {
        if (player == null)
        {
            Main.DebugLog($"Skip remote avatar load: player null. Reason: {reason}");
            return false;
        }

        string id = player.Data?.GeneralData?.PlayFabMasterId;

        if (string.IsNullOrEmpty(id))
        {
            Main.DebugLog($"Skip remote avatar load: PlayFabMasterId null/empty. Reason: {reason}");
            return false;
        }

        if (player.Controller?.ControllerType == ControllerType.Local)
        {
            MelonCoroutines.Start(RigManager.LoadAndApplyAvatarForPlayer(player, Main.instance.AvatarIndex.Value));
            return false;
        }

        if (loadingRemoteAvatars.Contains(id))
            return false;

        if (!attemptedRemoteLoads.Add(id))
            return false;

        loadingRemoteAvatars.Add(id);

        Main.DebugLog($"Starting remote avatar load for {id}. Reason: {reason}");

        MelonCoroutines.Start(LoadRemoteAvatarRoutine(player, id));

        return true;
    }

    private static IEnumerator LoadRemoteAvatarRoutine(Player player, string id)
    {
        bool doneCalled = false;

        yield return RigManager.LoadAndApplyAvatarForPlayer(player, onDone: success =>
        {
            doneCalled = true;
            Main.DebugLog($"Remote avatar load done for {id} | success: {success}");
        });

        if (!doneCalled)
            Main.DebugLog($"Remote avatar load ended without onDone for {id}");

        loadingRemoteAvatars.Remove(id);
    }
}