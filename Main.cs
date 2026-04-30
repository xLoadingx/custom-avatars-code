using System.IO;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.Players;
using MelonLoader;
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
    
    // Settings
    private MelonPreferences_Entry<KeyCode> ReloadKeybind;

    private MelonPreferences_Entry<bool> ToggleForSelf;
    private MelonPreferences_Entry<bool> ToggleForOthers;
    private MelonPreferences_Entry<bool> LetOthersSeeMyAvatar;
    private MelonPreferences_Entry<bool> ToggleInMatch;

    private MelonPreferences_Entry<bool> LogAvatarStatisticsSelf;

    private MelonPreferences_Entry<float> MaxFileDownloadSize;
    private MelonPreferences_Entry<int> MaxConcurrentDownloads;

    private MelonPreferences_Entry<int> UploadSizeTest;

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
        UI.CreateButtonEntry(generalCategory, "", "Reload Avatar", "Reloads your currently selected avatar.", LoadLocalAvatars);
        ReloadKeybind = generalCategory.CreateEntry("Reload_Keybind", KeyCode.R, "Reload Keybind", "The key that reloads your currently selected avatar.");
        
        var visibilityCategory = MelonPreferences.CreateCategory("CustomAvatars_Visibility", "Visibility");
        ToggleForSelf = visibilityCategory.CreateEntry("Toggle_For_Self", true, "Toggle For Self", "Toggles your current avatar on or off.\nReloads the avatar when toggled back on.");
        ToggleForOthers = visibilityCategory.CreateEntry("Toggle_For_Others", true, "Toggle For Others", "Toggles whether you can see other peoples avatars or not.\nReloads all remote player avatars when toggled back on.");
        LetOthersSeeMyAvatar = visibilityCategory.CreateEntry("Let_Others_See_My_Avatar", true, "Let Others See My Avatar",
            "Toggles whether other people with the mod can see your current avatar ***If Uploaded***.\nReloads your avatar for all remote players when turned on.");
        ToggleInMatch = visibilityCategory.CreateEntry("Toggle_In_Match", true, "Toggle In Match", "Toggles whether your local avatar is visible in matches.");
        
        var statisticsCategory = MelonPreferences.CreateCategory("CustomAvatars_Statistics", "Statistics");
        LogAvatarStatisticsSelf = statisticsCategory.CreateEntry("Log_Avatar_Statistics_Self", true, "Log Avatar Statistics (self)",
            "Displays information about the local avatar when loaded.\nDetails include material count, renderer count, and other notices.");
        
        var networkingCategory = MelonPreferences.CreateCategory("CustomAvatars_Networking", "Networking");
        MaxFileDownloadSize = networkingCategory.CreateEntry("Max_File_Download_Size", 50f, "Max File Download Size (MB)", "The max file size for downloading someone else's avatar.");
        MaxConcurrentDownloads = networkingCategory.CreateEntry("Max_Concurrent_Donwloads", 3, "Max Concurrent Downloads", "The number of avatars that can download at the same time.");
        UploadSizeTest = networkingCategory.CreateEntry("AJfgaf", 1000, "Upload Size Test (B)");
        UI.CreateButtonEntry(networkingCategory, "", "Upload Avatar", "Opens a local website that allows you to drag in your avatar bundle.", () => { Application.OpenURL($"https://xLoadingx.github.io/custom-avatars-code/upload.html?id={LocalPlayer.Data.GeneralData.PlayFabMasterId}");});
        
        UI.Register((MelonBase)this, generalCategory, visibilityCategory, statisticsCategory, networkingCategory);
    }

    public void OnMapInit()
    {
        if (currentScene == "Gym")
            RigLoader.EnsureReferenceObjects();
        
        LoadLocalAvatars();
    }

    public void LoadLocalAvatars()
    {
        MelonCoroutines.Start(RigLoader.LoadAndApplyAvatarForPlayer(LocalPlayer, onDone: _ =>
        {
            if (currentScene == "Gym")
            {
                MelonCoroutines.Start(RigLoader.LoadAndApplyAvatarForPlayer(
                    LocalPlayer, 
                    overrideController: GameObjects.Gym.INTERACTABLES.DressingRoom.PreviewPlayerController.GetGameObject())
                );
            }
        }));
    }
}