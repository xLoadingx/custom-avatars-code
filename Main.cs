using System;
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
using Object = UnityEngine.Object;

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
    public MelonPreferences_Entry<string> ReloadKeybind;
    
    public MelonPreferences_Entry<int> AvatarIndex;

    public MelonPreferences_Entry<bool> ToggleForSelf;
    public MelonPreferences_Entry<bool> ToggleForOthers;
    public MelonPreferences_Entry<bool> LetOthersSeeMyAvatar;
    
    public MelonPreferences_Entry<bool> ToggleSelfInMatch;
    public MelonPreferences_Entry<bool> ToggleOthersInMatch;

    public MelonPreferences_Entry<bool> LogAvatarStatisticsSelf;
    public MelonPreferences_Entry<bool> DebugMode;

    public MelonPreferences_Entry<float> MaxFileDownloadSize;

    public MelonPreferences_Category paramCategory;

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

        string configPath = Path.Combine(USER_DATA, CONFIG_FILE);

        var generalCategory = MelonPreferences.CreateCategory("CustomAvatars_General", "General");
        generalCategory.SetFilePath(configPath);
        
        UI.CreateButtonEntry(generalCategory, "", "Reload Avatar", "Reloads your currently selected avatar.", () => {
            MelonCoroutines.Start(RigManager.LoadAndApplyAvatarForPlayer(LocalPlayer, AvatarIndex.Value));
        });
        
        ReloadKeybind = generalCategory.CreateEntry("Reload_Keybind", "R", "Reload Keybind", "The key that reloads your currently selected avatar.");
        AvatarIndex = generalCategory.CreateEntry("Avatar_Index", 0, "The index of the avatar that should be loaded for the local player.\nApplied on reload.");
        
        var visibilityCategory = MelonPreferences.CreateCategory("CustomAvatars_Visibility", "Visibility");
        visibilityCategory.SetFilePath(configPath);
        
        ToggleForSelf = visibilityCategory.CreateEntry("Toggle_For_Self", true, "Toggle For Self", "Toggles your current avatar on or off.");
        ToggleForOthers = visibilityCategory.CreateEntry("Toggle_For_Others", true, "Toggle For Others", "Locally hides all remote avatars.");
        LetOthersSeeMyAvatar = visibilityCategory.CreateEntry("Let_Others_See_My_Avatar", true, "Let Others See My Avatar",
            "Toggles whether other people with the mod can see your current avatar ***If Uploaded***.");
        
        ToggleSelfInMatch = visibilityCategory.CreateEntry("Toggle_Self_In_Match", true, "Toggle For Self (In Match)", "Toggles whether your local avatar is visible in matches.\nOnly applies if `Toggle For Self` is enabled.");
        ToggleOthersInMatch = visibilityCategory.CreateEntry("Toggle_Others_In_Match", true, "Toggle For Others (In Match)", "Toggles whether you can see your opponents avatar in a match.\nOnly applies if `Toggle For Others` is enabled");
        
        var statisticsCategory = MelonPreferences.CreateCategory("CustomAvatars_Statistics", "Statistics");
        statisticsCategory.SetFilePath(configPath);
        
        LogAvatarStatisticsSelf = statisticsCategory.CreateEntry("Log_Avatar_Statistics_Self", true, "Log Avatar Statistics (self)",
            "Displays information about the local avatar when loaded.\nDetails include material count, renderer count, and other notices.");
        DebugMode = statisticsCategory.CreateEntry("Debug_Mode", false, "Debug Mode", "Toggles on debug mode for the avatar loading/downloading framework.");
        
        var networkingCategory = MelonPreferences.CreateCategory("CustomAvatars_Networking", "Networking");
        networkingCategory.SetFilePath(configPath);
        
        MaxFileDownloadSize = networkingCategory.CreateEntry("Max_File_Download_Size", 50f, "Max File Download Size (MB)", "The max file size for downloading someone else's avatar.");
        UI.CreateButtonEntry(networkingCategory, "", "Upload Avatar", "Opens a local website that allows you to drag in your avatar bundle.", () =>
        {
            Application.OpenURL($"https://xLoadingx.github.io/custom-avatars-code/upload.html?id=" +
                                $"{RemoteAvatarIO.Xor(LocalPlayer.Data.GeneralData.PlayFabMasterId, RemoteAvatarIO.KEY)}");
        });
        
        LetOthersSeeMyAvatar.OnEntryValueChanged.Subscribe((_, _) => AvatarNetworking.UpdateLocalParams());
        
        ToggleForOthers.OnEntryValueChanged.Subscribe((_, newValue) => Patches.attemptedRemoteLoads.Clear());
        ToggleOthersInMatch.OnEntryValueChanged.Subscribe((_, newValue) => Patches.attemptedRemoteLoads.Clear());
        
        ToggleForSelf.OnEntryValueChanged.Subscribe((_, newValue) => UpdateLocalPlayerAvatar(newValue));
        ToggleSelfInMatch.OnEntryValueChanged.Subscribe((_, newValue) => UpdateLocalPlayerAvatar(newValue));
        
        UI.RegisterMelon(this, generalCategory, visibilityCategory, statisticsCategory, networkingCategory);
    }

    public void UpdateLocalPlayerAvatar(bool newValue)
    {
        if (newValue)
        {
            if (currentScene is "Map0" or "Map1" && !ToggleSelfInMatch.Value) return;
            
            MelonCoroutines.Start(RigManager.LoadAndApplyAvatarForPlayer(LocalPlayer, AvatarIndex.Value));
        }
        else
        {
            var rig = LocalPlayer.Controller.GetComponent<CustomRig>();
            if (rig != null)
                Object.Destroy(rig);
        }
    }

    public void OnMapInit()
    {
        RigManager.EnsureStaticObjects();
        
        if (currentScene == "Gym" && RigManager.GetLocalAvatarPath(AvatarIndex.Value) != null)
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

        AvatarNetworking.UpdateLocalParams();
    }

    public override void OnUpdate()
    {
        if (Enum.TryParse(ReloadKeybind.Value, out KeyCode reloadCode) && Input.GetKeyDown(reloadCode))
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

        if (player.Controller.ControllerType == ControllerType.Local)
            return;
                
        var controller = player.Controller;
        var photonPlayer = player.Controller.GetComponent<PhotonView>().Owner;

        bool remoteVisible = false;
        bool hasVersion = false;

        if (photonPlayer != null)
        {
            remoteVisible = CAParams.Visibility.Get(photonPlayer);
            hasVersion = CAParams.Version.Get(photonPlayer) != null;
        }
        
        // Replay mod does this
        if (controller.PlayerSessionStateSystem == null)
            remoteVisible = true;

        bool showOthers = ToggleForOthers.Value;
        bool matchOnly = ToggleOthersInMatch.Value;
        bool isInMatch = currentScene is "Map0" or "Map1";

        bool localAllows = showOthers && (!isInMatch || matchOnly);
        bool shouldShowAvatar = localAllows && remoteVisible;

        var tag = controller.GetComponentInChildren<AvatarTag>();

        // Has mod
        // Tag stuff
        if (hasVersion)
        {
            if (tag == null)
            {
                var obj = Object.Instantiate(RigManager.avatarIconPrefab, controller.PlayerNameTag.transform, false);
                var tagComp = obj.AddComponent<AvatarTag>();
                obj.SetActive(true);

                obj.transform.localPosition = new Vector3(0.236f, -0.1606f, 0);
                obj.transform.localRotation = Quaternion.Euler(0, 180, 0);
                obj.transform.localScale = Vector3.one * 0.04f;
            }
        }

        // Avatar
        var rig = controller.GetComponent<CustomRig>();

        if (!shouldShowAvatar)
        {
            if (rig != null)
                Object.Destroy(rig);
        
            return;
        }

        if (rig == null)
            Patches.TryLoadRemoteAvatar(player, "visibility update");
    }
}