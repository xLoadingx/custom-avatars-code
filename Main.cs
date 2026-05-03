using System.IO;
using Il2CppExitGames.Client.Photon;
using Il2CppPhoton.Pun;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.Players;
using MelonLoader;
using MelonLoader.Preferences;
using RumbleModdingAPI.RMAPI;
using UIFramework;
using UnityEngine;
using BuildInfo = CustomAvatars.BuildInfo;
using Main = CustomAvatars.Main;

[assembly: MelonInfo(typeof(Main), BuildInfo.Name, BuildInfo.Version, BuildInfo.Author)]
[assembly: MelonGame("Buckethead Entertainment", "RUMBLE")]
[assembly: MelonColor(255, 255, 0, 0), MelonAuthorColor(255, 255, 0, 0)]
[assembly: MelonAdditionalDependencies("RumbleModdingAPI","UIFramework")]
[assembly: UIInfo("Custom Avatars")]

namespace CustomAvatars;

public static class BuildInfo
{
    public const string Name = "CustomAvatars";
    public const string Author = "ERROR";
    public const string Version = "2.0.0";
}
    
public class Main : MelonMod
{
    private const string USER_DATA = "UserData/CustomAvatars";
    private const string CONFIG_FILE = "config.cfg";

    public string currentScene = "Loader";
    public static Player LocalPlayer => PlayerManager.instance.LocalPlayer;

    public static Main instance;

    public Main() => instance = this;
    
    // Settings
    public MelonPreferences_Entry<KeyCode> ReloadKeybind;
    
    public MelonPreferences_Entry<int> AvatarIndex;

    public MelonPreferences_Entry<bool> ToggleForSelf;
    public MelonPreferences_Entry<bool> ToggleForOthers;
    public MelonPreferences_Entry<bool> LetOthersSeeMyAvatar;
    
    public MelonPreferences_Entry<bool> ToggleSelfInMatch;
    public MelonPreferences_Entry<bool> ToggleOthersInMatch;

    public MelonPreferences_Entry<bool> LogAvatarStatisticsSelf;
    public MelonPreferences_Entry<bool> DebugMode;

    public MelonPreferences_Entry<float> MaxFileDownloadSize;
    public MelonPreferences_Entry<int> MaxConcurrentDownloads;

    public static void DebugLog(string msg)
    {
        if (instance.DebugMode.Value)
            instance.LoggerInstance.Msg($"Debug | {msg}");
    }
    
    // ---------------------------------------------------------
    
    public override void OnInitializeMelon()
    {
        UIInit();
        Actions.onMapInitialized += _ => OnMapInit();
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName) => currentScene = sceneName;

    public void UIInit()
    {
        Directory.CreateDirectory(USER_DATA);

        var generalCategory = MelonPreferences.CreateCategory("CustomAvatars_General", "General");
        UI.CreateButtonEntry(generalCategory, "", "Reload Avatar", "Reloads your currently selected avatar.", () => {
            MelonCoroutines.Start(RigManager.LoadAndApplyAvatarForPlayer(LocalPlayer, AvatarIndex.Value));
        });
        
        ReloadKeybind = generalCategory.CreateEntry("Reload_Keybind", KeyCode.R, "Reload Keybind", "The key that reloads your currently selected avatar.");
        AvatarIndex = generalCategory.CreateEntry("Avatar_Index", 0, "The index of the avatar that should be loaded for the local player.\nApplied on reload.");
        
        var visibilityCategory = MelonPreferences.CreateCategory("CustomAvatars_Visibility", "Visibility");
        ToggleForSelf = visibilityCategory.CreateEntry("Toggle_For_Self", true, "Toggle For Self", "Toggles your current avatar on or off.");
        ToggleForOthers = visibilityCategory.CreateEntry("Toggle_For_Others", true, "Toggle For Others", "Locally hides all remote avatars.");
        LetOthersSeeMyAvatar = visibilityCategory.CreateEntry("Let_Others_See_My_Avatar", true, "Let Others See My Avatar",
            "Toggles whether other people with the mod can see your current avatar ***If Uploaded***.");
        
        ToggleSelfInMatch = visibilityCategory.CreateEntry("Toggle_Self_In_Match", true, "Toggle For Self (In Match)", "Toggles whether your local avatar is visible in matches.\nOnly applies if `Toggle For Self` is enabled.");
        ToggleOthersInMatch = visibilityCategory.CreateEntry("Toggle_Others_In_Match", true, "Toggle For Others (In Match)", "Toggles whether you can see your opponents avatar in a match.\nOnly applies if `Toggle For Others` is enabled");
        
        var statisticsCategory = MelonPreferences.CreateCategory("CustomAvatars_Statistics", "Statistics");
        LogAvatarStatisticsSelf = statisticsCategory.CreateEntry("Log_Avatar_Statistics_Self", true, "Log Avatar Statistics (self)",
            "Displays information about the local avatar when loaded.\nDetails include material count, renderer count, and other notices.");
        DebugMode = statisticsCategory.CreateEntry("Debug_Mode", false, "Debug Mode", "Toggles on debug mode for the avatar loading/downloading framework.");
        
        var networkingCategory = MelonPreferences.CreateCategory("CustomAvatars_Networking", "Networking");
        MaxFileDownloadSize = networkingCategory.CreateEntry("Max_File_Download_Size", 50f, "Max File Download Size (MB)", "The max file size for downloading someone else's avatar.");
        MaxConcurrentDownloads = networkingCategory.CreateEntry("Max_Concurrent_Downloads", 3, "Max Concurrent Downloads", "The number of avatars that can download at the same time.");
        UI.CreateButtonEntry(networkingCategory, "", "Upload Avatar", "Opens a local website that allows you to drag in your avatar bundle.", () =>
        {
            Application.OpenURL($"https://xLoadingx.github.io/custom-avatars-code/upload.html?id=" +
                                $"{RemoteAvatarNetworking.Xor(LocalPlayer.Data.GeneralData.PlayFabMasterId, RemoteAvatarNetworking.KEY)}");
        });
        
        LetOthersSeeMyAvatar.OnEntryValueChanged.Subscribe((_, newValue) => UpdateLocalParams(newValue));
        
        ToggleForOthers.OnEntryValueChanged.Subscribe((_, newValue) =>
        {
            // Handled in OnUpdate
            if (newValue) return;
            
            foreach (var player in PlayerManager.instance.AllPlayers)
            {
                if (player == LocalPlayer) continue;
                
                var rig = player.Controller.GetComponent<CustomRig>();
                if (rig != null)
                    Object.Destroy(rig);
            }
        });
        
        ToggleForSelf.OnEntryValueChanged.Subscribe((_, newValue) =>
        {
            if (newValue)
            {
                MelonCoroutines.Start(RigManager.LoadAndApplyAvatarForPlayer(LocalPlayer, AvatarIndex.Value));
            }
            else
            {
                var rig = LocalPlayer.Controller.GetComponent<CustomRig>();
                if (rig != null)
                    Object.Destroy(rig);
            }
        });
        
        UI.Register((MelonBase)this, generalCategory, visibilityCategory, statisticsCategory, networkingCategory);
    }

    public void OnMapInit()
    {
        RigManager.EnsureStaticObjects();
        
        if (currentScene == "Gym")
        {
            MelonCoroutines.Start(RigManager.LoadAndApplyAvatarForPlayer(
                LocalPlayer, 
                overrideController: GameObjects.Gym.INTERACTABLES.DressingRoom.PreviewPlayerController.GetGameObject(),
                avatarIdx: AvatarIndex.Value,
                waitUntil: () =>
                {
                    var rig = LocalPlayer.Controller.GetComponent<CustomRig>();
                    return rig != null && !rig.IsLoading;
                })
            );
        }

        UpdateLocalParams(LetOthersSeeMyAvatar.Value);
    }

    public override void OnUpdate()
    {
        if (Input.GetKeyDown(ReloadKeybind.Value))
            MelonCoroutines.Start(RigManager.LoadAndApplyAvatarForPlayer(LocalPlayer, AvatarIndex.Value));

        // Handles settings
        // When turned back on, make sure to load unloaded avatars again
        if (PlayerManager.instance.AllPlayers.Count > 1)
        {
            foreach (var player in PlayerManager.instance.AllPlayers)
                HandlePlayerUpdate(player);
        }
    }

    public void HandlePlayerUpdate(Player player)
    {
        if (player?.Controller == null)
            return;

        if (player == LocalPlayer)
            return;
                
        var rig = player.Controller.GetComponent<CustomRig>();
        var photonPlayer = player.Controller.GetComponent<PhotonView>().Owner;
                
        bool remoteVisible = false;
        if (photonPlayer?.CustomProperties.TryGetValue("CA:visibility", out var v) ?? false)
            remoteVisible = v.Unbox<bool>();
        
        // Replay mod does this
        if (player.Controller.PlayerSessionStateSystem == null)
            remoteVisible = true;

        bool showOthers = ToggleForOthers.Value;
        bool matchOnly = ToggleOthersInMatch.Value;

        bool isInMatch = currentScene is "Map0" or "Map1";

        bool localAllows = showOthers && (!isInMatch || matchOnly);
        bool shouldShow = localAllows && remoteVisible;

        if (!shouldShow)
        {
            if (rig != null)
                Object.Destroy(rig);

            return;
        }
                
        // If they don't have a rig, but
        // their combined settings say they should
        if (rig == null)
        {
            string id = player.Data.GeneralData.PlayFabMasterId;

            if (!Patches.activeDownloads.Contains(id))
            {
                DebugLog($"Re-enqueue {id} after visibility ON");
                Patches.downloadQueue.Enqueue(player);
                Patches.ProcessQueue();
            }
        }
    }

    public void UpdateLocalParams(bool visibleValue)
    {
        if (!PhotonNetwork.InRoom) return;

        var props = new Hashtable();
        props["CA:visibility"] = visibleValue;
        
        PhotonNetwork.LocalPlayer.SetCustomProperties(props);
    }
}