using System.Collections;
using System.Reflection;
using Il2CppPhoton.Pun;
using Il2CppRUMBLE.CharacterCreation.Interactable;
using Il2CppRUMBLE.Interactions.InteractionBase;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Subsystems;
using Il2CppSmartLocalization.Editor;
using Il2CppTMPro;
using UnityEngine;
using RumbleModdingAPI;
using MelonLoader;
using MelonLoader.Utils;
using RumbleModUI;
using UnityEngine.Events;
using Hashtable = Il2CppExitGames.Client.Photon.Hashtable;
using Main = CustomAvatars.Main;
using Object = UnityEngine.Object;
using static UnityEngine.Mathf;

[assembly: MelonInfo(typeof(Main), "CustomAvatars", "1.1.0", "ERROR")]
[assembly: MelonGame("Buckethead Entertainment", "RUMBLE")]
[assembly: MelonOptionalDependencies("RumbleHud")]
[assembly: MelonColor(255, 255, 0, 0)]
[assembly: MelonAuthorColor(255, 255, 0, 0)]

namespace CustomAvatars
{
    public static class Extensions
    {
        public static string TrimString(this string str) => System.Text.RegularExpressions.Regex.Replace(str, "<.*?>|\\(.*?\\)|[^a-zA-Z0-9_ ]", "").Trim();
        
        public static T GetOrAddComponent<T>(this GameObject gameObject) where T : Component
        {
            T component = gameObject.GetComponent<T>();
            if (component == null)
            {
                component = gameObject.AddComponent<T>();
            }
            return component;
        }

        public static T GetSavedValue<T>(this ModSetting<T> modSetting)
        {
            return (T)(modSetting?.SavedValue ?? default(T));
        }
        
        public static T GetValue<T>(this ModSetting<T> modSetting) 
        {
            return (T)(modSetting?.Value ?? default(T));
        }
    }

    public static class TransformExtensions
    {
        public static Transform FindDeep(this Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                if (parent.GetChild(i).name == name)
                    return parent.GetChild(i);

                Transform found = parent.GetChild(i).FindDeep(name);
                if (found != null)
                    return found;
            }

            return null;
        }
    }

    [RegisterTypeInIl2Cpp]
    public class CustomRigBone : MonoBehaviour { }

    public class Main : MelonMod
    {
        public string currentScene = "Loader";
        public bool sceneInitialized;
        public static Main instance;

        public GameObject rigParent;
        public GameObject avatarOptimizationParent;
        public GameObject refreshAvatarButton;
        public GameObject tryoutModeButton;
        public GameObject uploadAvatarButton;
        public GameObject uploadProgressBar;
        public TextMeshPro serverStatusText;
        public (Color color, string status) serverStatus = (Color.cyan, "Up To Date");

        public Mod mod = new();
        public ModSetting<string> reloadKeybind;
        public ModSetting<bool> toggleLocal;
        public ModSetting<bool> toggleOthers;
        public ModSetting<bool> toggleVisibleToOthers;
        public ModSetting<bool> toggleInMatch;
        public ModSetting<bool> logAvatarStats;
        public ModSetting<bool> logOtherAvatarStats;
        public ModSetting<int> downloadLimitMB;
        public ModSetting<int> maxConcurrentDownloads;
        public ModSetting<bool> uploadAvatar;

        public ModSetting<bool> perPlayerHeader;
        public Dictionary<CustomRig, ModSetting<bool>> perPlayerToggles = new();
        private Dictionary<int, object> lastAvatars = new();

        public static Material poseGhostMaterial;

        public Main()
        {
            instance = this;
        }

        // TODO:
        // Add base avatars you can choose from and customize
        // Fix remote speaking
        // Add avatar settings (along with making Animator Controllers work with it)
        // Try and figure out if I can fiddle with RockCam to show more than one material
        
        public override void OnLateInitializeMelon()
        {
            Calls.onMapInitialized += Initialize;
            UI.instance.UI_Initialized += OnUIInitialized;
            LoggerInstance.Msg("Custom Avatars Initialized");
            RigManager.Initialize(this);
        }

        // Clears state and rigs when a scene loads
        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            currentScene = sceneName;
            sceneInitialized = false;

            RigManager.rigs.Clear();
            Patches.loadedPlayers.Clear();
            rigParent = null;
        }

        // Deletes cached avatars from disk when you close the game
        public override void OnDeinitializeMelon()
        {
            string filePath = Path.Combine(MelonEnvironment.UserDataDirectory, "CustomAvatars", "Opponents");
            if (!Directory.Exists(filePath)) return;

            foreach (var file in Directory.GetFiles(filePath))
            {
                try
                {
                    File.Delete(file);
                } 
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        // Resets rigs and builds a few things in Gym
        // Loads the Optimization text and the reload button
        public void Initialize()
        {
            RigManager.ClearRigs();
            lastAvatars.Clear();

            string filePath = Path.Combine(MelonEnvironment.UserDataDirectory, "CustomAvatars", "Opponents");
            Directory.CreateDirectory(filePath);
            
            ApplyAvatars();

            // Making objects in code is fun looking
            if (currentScene == "Gym" && !sceneInitialized)
            {
                tryoutModeButton = Calls.GameObjects.Gym.LOGIC.DressingRoom.Controlpanel.Controls
                    .Frameattachment.TryOutModePanel.GetGameObject();

                uploadAvatarButton = GameObject.Instantiate(tryoutModeButton, tryoutModeButton.transform.parent, false);
                uploadAvatarButton.name = "Upload Avatar Panel";
                uploadAvatarButton.transform.localPosition = new Vector3(0.1069f, 0.1962f, -0.1014f);
                
                refreshAvatarButton = GameObject.Instantiate(tryoutModeButton, tryoutModeButton.transform.parent, false);
                refreshAvatarButton.name = "Refresh Avatar Panel";
                refreshAvatarButton.transform.localPosition = new Vector3(-0.1164f, 0.1962f, -0.1014f);
                
                InteractionButton interactionButton = refreshAvatarButton.transform.GetChild(1).GetChild(0).GetComponent<InteractionButton>();
                interactionButton.onPressed.RemoveAllListeners();
                interactionButton.onPressed.AddListener((UnityAction)(() => { if ((bool)toggleLocal.SavedValue) Initialize(); }));
                
                InteractionButton interactionButtonUpload = uploadAvatarButton.transform.GetChild(1).GetChild(0).GetComponent<InteractionButton>();
                interactionButtonUpload.onPressed.RemoveAllListeners();
                interactionButtonUpload.onPressed.AddListener((UnityAction)(() => { UploadAvatar(); }));

                TextMeshPro text = refreshAvatarButton.transform.GetChild(1).GetChild(1).GetComponent<TextMeshPro>();
                Object.Destroy(text.transform.GetComponent<LocalizedTextTMPro>());
                text.m_text = "Refresh Avatar";
                text.fontSize = 0.25f;
                text.ForceMeshUpdate();
                
                TextMeshPro textUpload = uploadAvatarButton.transform.GetChild(1).GetChild(1).GetComponent<TextMeshPro>();
                Object.Destroy(textUpload.transform.GetComponent<LocalizedTextTMPro>());
                textUpload.m_text = "Upload Avatar";
                textUpload.fontSize = 0.25f;
                textUpload.ForceMeshUpdate();

                avatarOptimizationParent = new GameObject("AvatarDetails");
                avatarOptimizationParent.transform.localPosition = new Vector3(-3.5418f, 1.2327f, -0.8255f);
                avatarOptimizationParent.transform.localScale = Vector3.one * 0.5f;
                avatarOptimizationParent.transform.localRotation = Quaternion.Euler(6.3636f, 241.8925f, 0f);
                
                uploadProgressBar = GameObject.Instantiate(Calls.GameObjects.Gym.LOGIC.Heinhouserproducts.
                    ProgressTracker.ProgressPanel.StatusBar.GetGameObject(), avatarOptimizationParent.transform, false);
                uploadProgressBar.name = "Upload Progress Bar";
                uploadProgressBar.transform.localScale = new Vector3(0.8562f, 0.0544f, 0.854f);
                uploadProgressBar.transform.localPosition = new Vector3(-1.9034f, 0.2507f, -0.4614f);
                uploadProgressBar.transform.localRotation = Quaternion.Euler(354.8412f, 324.0485f, 3.7311f);
                uploadProgressBar.GetComponent<MeshRenderer>().material.SetFloat("_RC_Target", 1f);
                uploadProgressBar.SetActive(false);
                
                var summary = Calls.Create.NewText("GOOD", 1f, new Color(0f, 0.5f, 0f), Vector3.zero, Quaternion.identity);
                summary.name = "Summary";
                summary.transform.SetParent(avatarOptimizationParent.transform, false);
                summary.transform.localPosition = new Vector3(0f, 0.0919f, 0f);
                summary.GetComponent<TextMeshPro>().enableWordWrapping = false;
                summary.GetComponent<TextMeshPro>().alignment = TextAlignmentOptions.Center;
                
                var details = Calls.Create.NewText("0 verts, 0 mat(s), 0 texture(s)", 1f, new Color(0f, 0.5f, 0f), Vector3.zero, Quaternion.identity);
                details.name = "Details";
                details.transform.SetParent(avatarOptimizationParent.transform, false);
                details.GetComponent<TextMeshPro>().enableWordWrapping = false;
                details.GetComponent<TextMeshPro>().alignment = TextAlignmentOptions.Center;
                
                var warnings = Calls.Create.NewText("WARNINGS:", 1f, new Color(1, 1, 0), Vector3.zero, Quaternion.identity);
                warnings.name = "Warnings";
                warnings.transform.SetParent(avatarOptimizationParent.transform, false);
                warnings.transform.localPosition = new Vector3(0, -0.0919f, 0f);
                warnings.GetComponent<TextMeshPro>().enableWordWrapping = false;
                warnings.GetComponent<TextMeshPro>().alignment = TextAlignmentOptions.Center;

                var newServerStatus = Calls.Create.NewText("Up To Date",1f, new Color(0, 1, 1), Vector3.zero, Quaternion.identity);
                newServerStatus.name = "AvatarServerStatus";
                newServerStatus.transform.SetParent(avatarOptimizationParent.transform, false);
                newServerStatus.transform.localPosition = new Vector3(-1.9309f, 0.2499f, -0.4545f);
                newServerStatus.transform.localRotation = Quaternion.Euler(352.9085f, 321.6011f, 3.3454f);
                serverStatusText = newServerStatus.GetComponent<TextMeshPro>();
                serverStatusText.enableWordWrapping = false;
                serverStatusText.alignment = TextAlignmentOptions.Center;
                
                SetObjectsActive();
            }
            
            sceneInitialized = true;
        }

        public void SetObjectsActive()
        {
            bool enabled = (bool)toggleLocal.Value && Directory.GetFiles(Path.Combine(MelonEnvironment.UserDataDirectory, "CustomAvatars"), "*.rumbleavatar").Length > 0;
            tryoutModeButton.SetActive(!enabled);
            uploadAvatarButton.SetActive(enabled);
            refreshAvatarButton.SetActive(enabled);
            avatarOptimizationParent.SetActive(enabled);
        }

        public void UploadAvatar()
        {
            string rigBundle = Path.Combine(MelonEnvironment.UserDataDirectory, "CustomAvatars", Directory.GetFiles(Path.Combine(MelonEnvironment.UserDataDirectory, "CustomAvatars"), "*.rumbleavatar").FirstOrDefault() ?? string.Empty);
            string masterId = Calls.Players.GetLocalPlayer().Data.GeneralData.PlayFabMasterId;
            if (!File.Exists(rigBundle))
            {
                LoggerInstance.Error($"Invalid bundle found at path: {rigBundle}");
                return;
            }
            
            float displayedProgress = 0f;
            Material progressBarMat = null;
            
            LoggerInstance.Msg($"Uploading file at path '{rigBundle}' for MasterID {masterId}");
            
            // Actions are cool
            RemoteAvatarLoader.UploadBundle(masterId, rigBundle, () =>
            {
                uploadProgressBar.SetActive(true);
                progressBarMat = uploadProgressBar.GetComponent<MeshRenderer>().material;
                progressBarMat.SetFloat("_RC_Current", 0f);

                serverStatusText.gameObject.transform.localPosition = new Vector3(-1.9309f, 0.359f, -0.4545f);
                serverStatusText.color = Color.yellow;
            }, (success, skipped) =>
            {
                uploadProgressBar.SetActive(false);
                serverStatus = success ? (Color.cyan, "Up To Date") : (Color.red, "Not Uploaded");
                serverStatusText.color = serverStatus.color;
                serverStatusText.text = serverStatus.status;
                serverStatusText.gameObject.transform.localPosition = new Vector3(-1.9309f, 0.2499f, -0.4545f);
                
                if (skipped) return;
                LoggerInstance.Msg($"{(success ? "File uploaded successfully!" : "Upload failed.")}");
            }, progress =>
            {
                if (progressBarMat == null) return;
                displayedProgress = Mathf.Lerp(displayedProgress, progress, Time.deltaTime * 10f);
                progressBarMat.SetFloat("_RC_Current", displayedProgress);
            }, serverStatusText);
        }

        // Applies local & preview rigs, also SHA-checks against GitHub
        // Might need to make the warning a bit more visible
        public void ApplyAvatars(bool log = true)
        {
            RigManager.ClearRigs();
            
            var localPlayer = Calls.Players.GetLocalPlayer();

            if (rigParent == null)
                rigParent = new GameObject("Rigs");
            
            var customRig = localPlayer.Controller.gameObject.GetComponent<CustomRig>();
            if (customRig == null)
            {
                customRig = localPlayer.Controller.gameObject.AddComponent<CustomRig>();
                customRig.CaptureOriginal(
                    localPlayer.Data.GeneralData.PlayFabMasterId, 
                    true, 
                    localPlayer.Controller.GetSubsystem<PlayerVisuals>().renderer,
                    log
                );
            }
            else
            {
                if (customRig.blinkCoroutine != null)
                    MelonCoroutines.Stop(customRig.blinkCoroutine);
            }
            
            MelonCoroutines.Start(RigManager.LoadRigForPlayer(localPlayer, (rig) =>
            {
                serverStatus = (Color.yellow, "Checking...");
                serverStatusText.text = serverStatus.status;
                serverStatusText.color = serverStatus.color;
                
                MelonCoroutines.Start(RemoteAvatarLoader.GetSha(Calls.Players.GetLocalPlayer().Data.GeneralData.PlayFabMasterId, (sha) =>
                {
                    if (sha == null)
                    {
                        serverStatus = (Color.red, "Not Uploaded");
                        if (log)
                            LoggerInstance.MsgPastel(System.ConsoleColor.Red, "An avatar has not been uploaded. Make sure to upload your avatar when you're done!");
                    } else if (!RemoteAvatarLoader.ShaMatchesLocal(sha, customRig.AvatarFilePath, false))
                    {
                        serverStatus = (Color.red, "Not Uploaded");
                        if (log)
                            LoggerInstance.MsgPastel(System.ConsoleColor.Red, "Uploaded file is different from local file. Make sure to reupload your avatar when you're done!");
                    }
                    else
                    {
                        serverStatus = (Color.cyan, "Up To Date");
                        if (log)
                            LoggerInstance.MsgPastel(System.ConsoleColor.Cyan, "Avatar is up to date on the server.");
                    }
                        
                    serverStatusText.text = serverStatus.status;
                    serverStatusText.color = serverStatus.color;
                }, false));

                UpdateAvatarVisibility();
                
                if (currentScene == "Gym" && rig != null)
                {
                    var previewController =
                        Calls.GameObjects.Gym.LOGIC.DressingRoom.PreviewPlayerController.Visuals.GetGameObject();

                    GameObject newRig = Calls.LoadAssetBundleGameObjectFromFile(
                        Directory.GetFiles(Path.Combine(MelonEnvironment.UserDataDirectory, "CustomAvatars"), "*.rumbleavatar").FirstOrDefault(), "Rig");

                    newRig.name = "RIG - Preview Controller (Dressing Room)";
                    newRig.transform.SetParent(rigParent.transform, true);
                    
                    var smr = previewController.transform.GetChild(0).GetComponent<SkinnedMeshRenderer>();
                    var previewCustomRig = previewController.transform.parent.GetComponent<CustomRig>();
                    if (previewCustomRig != null)
                    {
                        if (previewCustomRig.blinkCoroutine != null)
                            MelonCoroutines.Stop(previewCustomRig.blinkCoroutine);
                    }
                    else
                    {
                        previewCustomRig = previewController.transform.parent.gameObject.AddComponent<CustomRig>();
                        previewCustomRig.IsPreview = true;
                        previewCustomRig.PlayerName = "Preview Controller (Dressing Room)";
                        previewCustomRig.CaptureOriginal("Preview Controller (Dressing Room)", false, smr, log);
                    }

                    previewCustomRig.CaptureRig(newRig);
                    
                    previewCustomRig.Config = customRig.Config;
                
                    RigManager.ApplyRigToSMR(previewController.transform.GetChild(1), newRig, previewController.GetComponent<Animator>(), customRig: previewCustomRig);
                    RigManager.rigs["Preview Controller (Dressing Room)"] = previewCustomRig;
                    
                    if (!(bool)toggleLocal.SavedValue)
                        previewCustomRig.Apply(CustomRig.RigState.Original);
                    else
                        previewCustomRig.Apply(CustomRig.RigState.Rigged);

                    // LMAO I have no idea how I came up with this
                    // but it works somehow so im not touching it
                    var runtimeAnimator = rig.GetComponent<Animator>();
                    var previewRigAnimator = newRig.GetComponent<Animator>();

                    if (runtimeAnimator != null && previewRigAnimator != null)
                    {
                        foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
                        {
                            if (bone == HumanBodyBones.LastBone) continue;

                            Transform playerBone = runtimeAnimator.GetBoneTransform(bone);
                            Transform rigBone = previewRigAnimator.GetBoneTransform(bone);

                            if (playerBone != null && rigBone != null)
                            {
                                rigBone.localRotation = playerBone.localRotation;
                            }
                        }
                    }
                }
            }, log));

            if (currentScene == "Gym" && poseGhostMaterial == null)
            {
                poseGhostMaterial = new Material(Calls.GameObjects.Gym.LOGIC.Heinhouserproducts.
                    MoveLearning.Ghost.Ghost_.Visuals.
                    Poseghostbody.GetGameObject().
                    GetComponent<SkinnedMeshRenderer>().material);
                poseGhostMaterial.hideFlags = HideFlags.DontUnloadUnusedAsset | HideFlags.HideAndDontSave;
            }
        }

        // Esnures rigParent stays active
        // Mostly because FlatLand
        public override void OnFixedUpdate()
        {
            if (currentScene == "Loader") return;

            if (rigParent && !rigParent.activeSelf)
                rigParent.SetActive(true);
        }

        // Reload keybind + rig refresh
        // Reload for other players might need some tweaking
        public override void OnUpdate()
        {
            if (reloadKeybind != null && Enum.TryParse((string)reloadKeybind.SavedValue, true, out KeyCode parsed))
            {
                if (Input.GetKeyDown(parsed))
                {
                    Initialize();

                    var rigsSnapshot = RigManager.rigs.ToArray();

                    foreach (var kv in rigsSnapshot)
                    {
                        var id = kv.Key;
                        var rig = kv.Value;

                        if (rig == null || rig.IsLocal) continue;

                        var player = rig.GetComponent<PlayerController>()?.assignedPlayer ?? Calls.Players.GetAllPlayers()
                            .ToArray().FirstOrDefault(p => p.Data.GeneralData.PlayFabMasterId == id);
                        if (player == null) continue;
                    
                        if (!string.IsNullOrEmpty(rig.AvatarFilePath) && File.Exists(rig.AvatarFilePath))
                            try { File.Delete(rig.AvatarFilePath); } catch {}

                        RigManager.ClearRig(rig);
                        Object.Destroy(rig);
                    
                        var visuals = player.Controller.GetSubsystem<PlayerVisuals>();
        
                        var customRig = player.Controller.gameObject.AddComponent<CustomRig>();
                        customRig.CaptureOriginal(player.Data.GeneralData.PlayFabMasterId, false, visuals.renderer);
        
                        visuals.renderer.material = Main.poseGhostMaterial;
                    
                        MelonCoroutines.Start(RigManager.LoadRigForPlayer(player, null));
                    }
                }
            }

            // Checks if other players want to be seen, for the canOthersSeeMyAvatar toggle.
            if (currentScene != "Gym" && (bool)(toggleOthers?.SavedValue ?? false))
            {
                foreach (var player in PhotonNetwork.PlayerList)
                {
                    if (player.CustomProperties == null || player == PhotonNetwork.LocalPlayer) continue;

                    if (player.CustomProperties.TryGetValue("CA_Avatar", out var value))
                    {
                        if (!lastAvatars.TryGetValue(player.ActorNumber, out var old) || !Equals(old, value))
                        {
                            lastAvatars[player.ActorNumber] = value;
                            
                            Player rumblePlayer = Calls.Players.GetPlayerByActorNo(player.ActorNumber);
                            if (rumblePlayer?.Controller == null) continue;
                            
                            var rig = rumblePlayer.Controller.GetComponent<CustomRig>();
                            if (rig == null) continue;

                            RigManager.ResolveRigState(rumblePlayer, rig);
                        }
                    }
                }
            }
        }

        // Adds per-player toggle for ModUI
        public void AddRigToList(CustomRig rig)
        {
            try
            {
                if (string.IsNullOrEmpty(rig.PlayerName)) return;

                if (perPlayerToggles.Count == 0)
                    perPlayerHeader = mod.AddToList("<b><#FFB347>- Per Player Toggles", false, 0, "", new Tags { DoNotSave = true });

                var setting = mod.AddToList($"{rig.PlayerName} <#FFFFFF>({rig.PlayerId})", true, 0, $"Toggles the avatar for {rig.PlayerName}.", new Tags());

                setting.SavedValueChanged += (sender, args) =>
                {
                    if (toggleOthers.GetValue())
                        rig.Apply(setting.GetValue() ? CustomRig.RigState.Rigged : CustomRig.RigState.Original);
                };

                perPlayerToggles[rig] = setting;

                mod.GetFromFile();
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"Failed to add rig to ModUI: {ex}");
            }
        }

        // Removes per-player toggle (cleanup if player leaves)
        public void RemoveRigFromList(CustomRig rig)
        {
            if (perPlayerToggles.TryGetValue(rig, out var setting))
            {
                mod.Settings.Remove(setting);
                perPlayerToggles.Remove(rig);
            }
            
            if (perPlayerHeader != null && perPlayerToggles.Count == 0)
            {
                mod.Settings.Remove(perPlayerHeader);
                perPlayerHeader = null;
            }
        }

        // Refreshes HUD portraits to show new avatar, if the mod exists
        public void RegeneratePortraits()
        {
            var hudType = Type.GetType("RumbleHud.Hud, RumbleHud");
            var method = hudType?.GetMethod("RegeneratePortraits", BindingFlags.Static | BindingFlags.Public);
            method?.Invoke(null, new object[] { currentScene == "Gym" });
        }

        // Similar to ResolveRigState
        // Merges a bunch of settings into a value
        // Also syncs across the network
        private void UpdateAvatarVisibility()
        {
            if (toggleVisibleToOthers == null || toggleInMatch == null || toggleLocal == null)
                return;
            
            bool showToOthers = (bool)toggleVisibleToOthers.Value;
            bool showInMatch = (bool)toggleInMatch.Value;
            bool showLocal = (bool)toggleLocal.Value;

            var localRig = Calls.Players.GetLocalPlayer()?.Controller?.GetComponent<CustomRig>();
            if (localRig == null)
                return;

            bool visibleLocally = showLocal;
            if (currentScene is "Map0" or "Map1")
                visibleLocally = showLocal && showInMatch;
            
            localRig.Apply(visibleLocally
                ? CustomRig.RigState.Rigged
                : CustomRig.RigState.Original);

            if (currentScene != "Gym" && PhotonNetwork.LocalPlayer != null)
            {
                bool visibleToOthers = showToOthers && showInMatch;
                var props = new Hashtable();
                props["CA_Avatar"] = visibleToOthers;
                PhotonNetwork.LocalPlayer.SetCustomProperties(props);
            }
        }

        public void OnUIInitialized()
        {
            mod.ModName = "<b><#6A5ACD>Custom Avatars</color></b>";
            mod.ModVersion = "1.0.0";
                
            mod.SetFolder("CustomAvatars");
            mod.AddToList("Description", "", "Allows custom avatars for you or specific people.", new Tags());
            reloadKeybind = mod.AddToList("Reload Keybind", nameof(KeyCode.R), "The key that reloads your and other's avatars.", new Tags());
            
            mod.AddToList("<b><#114F11>- Avatar Visibility</color></b>", false, 0, "", new Tags { DoNotSave = true });
            toggleLocal = mod.AddToList("Toggle for Self", true, 0, "Toggles whether you see your custom avatar locally. This does not affect what other players see.", new Tags());
            toggleOthers = mod.AddToList("Toggle for Others", true, 0, "Toggles whether you can see other players' custom avatars.", new Tags());
            toggleVisibleToOthers = mod.AddToList("Let Others See My Avatar", true, 0, "Controls whether other players can see your custom avatar. This setting is networked.", new Tags());
            toggleInMatch = mod.AddToList("Toggle In Match", true, 0, "Toggles whether or not you and other players can see your custom avatar in a match. This setting is networked.", new Tags());

            mod.AddToList("<b><#FFED29>- Statistics</color></b>", false, 0, "", new Tags { DoNotSave = true });
            logAvatarStats = mod.AddToList("Log Avatar Statistics (self)", true, 0, "If enabled, logs mesh info like vertex count, material count, etc. when the local player's avatar is loaded.", new Tags());
            logOtherAvatarStats = mod.AddToList("Log Avatar Statistics (other)", true, 0, "If enabled, logs mesh info like vertex count, material count, etc. when a remote player's avatar is loaded.", new Tags());

            mod.AddToList("<b><#305CDE>- Download & Upload</color></b>", false, 0, "", new Tags { DoNotSave = true });
            downloadLimitMB = mod.AddToList("Max File Download Size", 50, "The max download size for other avatars in MB.", new Tags());
            maxConcurrentDownloads = mod.AddToList("Max Concurrent Downloads", 3, "The maximum number of downloads that can be ran at the same time.", new Tags());
            uploadAvatar = mod.AddToList("Upload Avatar", false, 0, "Uploads the avatar in the folder when the button is clicked and saved.", new Tags
            {
                DoNotSave = true
            });
            uploadAvatar.SavedValueChanged += (sender, args) => UploadAvatar();

            toggleOthers.SavedValueChanged += (sender, args) =>
            {
                foreach (var rig in RigManager.rigs)
                {
                    if (rig.Key == Calls.Players.GetLocalPlayer().Data.GeneralData.PlayFabMasterId) continue;
                    if (rig.Key == "Preview Controller (Dressing Room)") continue;

                    var player = Calls.Players.GetAllPlayers().ToArray().FirstOrDefault(p => p.Data.GeneralData.PlayFabMasterId == rig.Key);
                    RigManager.ResolveRigState(player, rig.Value);
                }
                
                RegeneratePortraits();
            };
            
            toggleLocal.SavedValueChanged += (sender, args) =>
            {
                bool enabled = (bool)toggleLocal.Value;

                UpdateAvatarVisibility();

                if (currentScene == "Gym")
                {
                    if (!enabled)
                        Calls.GameObjects.Gym.LOGIC.DressingRoom.GetGameObject().GetComponent<DressingRoom>().UpdatePlayerVisuals();
                    
                    SetObjectsActive();
                    
                    Calls.GameObjects.Gym.LOGIC.DressingRoom.PreviewPlayerController.GetGameObject().GetComponent<CustomRig>()?
                        .Apply(enabled ? CustomRig.RigState.Rigged : CustomRig.RigState.Original);
                }

                RegeneratePortraits();
            };

            toggleInMatch.SavedValueChanged += (sender, args) =>
            {
                if (currentScene is not ("Map0" or "Map1"))
                    return;
                
                UpdateAvatarVisibility();
                RegeneratePortraits();
            };

            toggleVisibleToOthers.SavedValueChanged += (sender, args) =>
            {
                if (currentScene != "Gym")
                    UpdateAvatarVisibility();
            };

            logAvatarStats.SavedValueChanged += (sender, args) =>
            {
                bool enabled = (bool)logAvatarStats.Value;
                
                if (enabled)
                    LoggerInstance.Msg("Will log on next Avatar Refresh.");
            };
            
            mod.GetFromFile();
            UI.instance.AddMod(mod);
        }
    }

    [RegisterTypeInIl2Cpp]
    public class CustomRig : MonoBehaviour
    {
        public string PlayerId;
        public bool IsLocal;
        public bool IsPreview;
        public string PlayerName;
        public string AvatarFilePath;

        public AvatarDescriptorExport Config;

        public GameObject Root;
        public GameObject PlayerRoot;
        public Transform Head;
        public Animator animator;

        public PlayerVoiceSystem voiceSystem;
        
        public PlayerEyeSystem eyeSystem;
        public LookatAttentionPoint lastAttentionPoint;
        public object lookAtCoroutine;

        public object blinkCoroutine;

        // --- Toggles for the two ---
        public Material OriginalMaterial;
        public Material OriginalVisualsMaterial;
        public Mesh OriginalMesh;
        public Transform[] OriginalBones;

        public Material[] RigMaterials;
        public Material RigVisualsMaterial;
        public Mesh RigMesh;
        public Transform[] RigBones;

        public enum RigState
        {
            Original,
            Rigged
        }
        
        public SkinnedMeshRenderer MeshRenderer;
        public PlayerVisuals playerVisuals;

        void Update()
        {
            if (OriginalMaterial == null || OriginalMesh == null)
            {
                OriginalMaterial = new Material(MeshRenderer.material);
                OriginalMaterial.hideFlags = HideFlags.DontUnloadUnusedAsset | HideFlags.HideAndDontSave;
                OriginalMesh = Instantiate(MeshRenderer.sharedMesh);
                OriginalMesh.hideFlags = HideFlags.DontUnloadUnusedAsset | HideFlags.HideAndDontSave;
            }

            if (Config != null && MeshRenderer != null)
            {
                if (voiceSystem != null)
                {
                    int idx = Config.jawOpenBlendshape;
                    if (idx < MeshRenderer.sharedMesh.blendShapeCount)
                        MeshRenderer.SetBlendShapeWeight(idx, voiceSystem.currentJawOpenPercentage * 100f);
                }

                if (Head != null && eyeSystem != null && eyeSystem.CurrentAttentionPoint != null)
                {
                    var settings = Config.eyeSettings;
                    if (settings.eyeUpBlendshape == -1 || settings.eyeDownBlendshape == -1 ||
                        settings.eyeLeftBlendshape == -1 || settings.eyeRightBlendshape == -1)
                        return;

                    var newPoint = eyeSystem.CurrentAttentionPoint;
                    if (lastAttentionPoint != newPoint)
                    {
                        lastAttentionPoint = newPoint;

                        if (lookAtCoroutine != null)
                            MelonCoroutines.Stop(lookAtCoroutine);

                        lookAtCoroutine = MelonCoroutines.Start(FollowEyes(settings, newPoint.transform, 0.02f, settings.eyeGain));
                    }
                }
            }
        }

        IEnumerator FollowEyes(EyeSettings settings, Transform target, float duration, float gain = 1f)
        {
            while (target != null)
            {
                Vector4 newWeights = CalculateEyeWeights(Head, target);

                float targetUp = newWeights.x * gain;
                float targetDown = newWeights.y * gain;
                float targetLeft = newWeights.z * gain;
                float targetRight = newWeights.w * gain;

                float currentUp = MeshRenderer.GetBlendShapeWeight(settings.eyeUpBlendshape);
                float currentDown = MeshRenderer.GetBlendShapeWeight(settings.eyeDownBlendshape);
                float currentLeft = MeshRenderer.GetBlendShapeWeight(settings.eyeLeftBlendshape);
                float currentRight = MeshRenderer.GetBlendShapeWeight(settings.eyeRightBlendshape);

                MeshRenderer.SetBlendShapeWeight(settings.eyeUpBlendshape, Lerp(currentUp,    targetUp,    Time.deltaTime / duration));
                MeshRenderer.SetBlendShapeWeight(settings.eyeDownBlendshape, Lerp(currentDown,  targetDown,  Time.deltaTime / duration));
                MeshRenderer.SetBlendShapeWeight(settings.eyeLeftBlendshape, Lerp(currentLeft,  targetLeft,  Time.deltaTime / duration));
                MeshRenderer.SetBlendShapeWeight(settings.eyeRightBlendshape, Lerp(currentRight, targetRight, Time.deltaTime / duration));

                yield return null;
            }
        }

        public Vector4 CalculateEyeWeights(Transform head, Transform target)
        {
            Vector3 dir = (target.position - head.position).normalized;
            Vector3 dirLocal = head.InverseTransformDirection(dir);

            float up = Clamp01(dirLocal.y) * 100f;
            float down = Clamp01(-dirLocal.y) * 100f;
            float left = Clamp01(-dirLocal.x) * 100f;
            float right = Clamp01(dirLocal.x) * 100f;

            return new Vector4(up, down, left, right);
        }

        public void CaptureOriginal(string playerId, bool isLocal, SkinnedMeshRenderer renderer, bool log = true)
        {
            if (renderer == null)
            {
                if (log)
                    Main.instance.LoggerInstance.Warning($"CaptureOriginal: Renderer is null for player {playerId ?? "Unknown"}, skipping.");
                return;
            }

            if (renderer.sharedMesh == null)
            {
                if (log)
                    Main.instance.LoggerInstance.Warning($"CaptureOriginal: SharedMesh is null for player {playerId ?? "Unknown"}, skipping.");
                return;
            }

            PlayerId = playerId;
            IsLocal = isLocal;

            var parent = renderer.transform.parent;
            if (parent != null && parent.childCount > 1)
            {
                PlayerRoot = parent.GetChild(1).gameObject;
            }
            else
            {
                if (log)
                    Main.instance.LoggerInstance.Warning($"CaptureOriginal: Could not find 'Skelington' (index 1) under renderer's parent for player {playerId ?? "Unknown"}.");
                return;
            }

            playerVisuals = parent.GetComponent<PlayerVisuals>();
            if (playerVisuals != null && isLocal)
            {
                OriginalVisualsMaterial = Instantiate(playerVisuals.NonHeadClippedMaterial);
                OriginalVisualsMaterial.hideFlags = HideFlags.HideAndDontSave | HideFlags.DontUnloadUnusedAsset;
            }
            
            animator = parent.GetComponent<Animator>();
            if (animator != null)
                Head = RigManager.GetBone(animator, HumanBodyBones.Head);

            OriginalMesh = Instantiate(renderer.sharedMesh);
            OriginalMesh.hideFlags = HideFlags.DontUnloadUnusedAsset | HideFlags.HideAndDontSave;

            if (renderer.material != null)
            {
                OriginalMaterial = Instantiate(renderer.material);
                OriginalMaterial.hideFlags = HideFlags.DontUnloadUnusedAsset | HideFlags.HideAndDontSave;
            }
            else
            {
                if (log)
                    Main.instance.LoggerInstance.Warning("CaptureOriginal: Renderer material is null.");
            }

            OriginalBones = renderer.bones ?? Array.Empty<Transform>();
            MeshRenderer = renderer;

            if (!IsPreview)
            {
                var headset = renderer.transform.root.GetChild(2).GetChild(0).GetChild(0);
                voiceSystem = headset.GetChild(2).GetComponent<PlayerVoiceSystem>();
                eyeSystem = headset.GetComponent<PlayerEyeSystem>();
            }
        }

        public void CaptureRig(GameObject rig)
        {
            Root = rig;

            var anim = rig.GetComponent<Animator>();

            if ((bool)Main.instance.logAvatarStats.SavedValue || (!IsLocal && (bool)Main.instance.logOtherAvatarStats.SavedValue))
            {
                if (anim == null)
                    Main.instance.LoggerInstance.Msg("Rig has no Animator, using raw transform capture.");
                else if (!anim.isHuman)
                    Main.instance.LoggerInstance.Msg("Rig Animator is not humanoid, using name-based capture.");
            }

            RigBones = rig.GetComponentsInChildren<Transform>();
        }

        public List<GrabbableObject> ParseGrabbableObjectsRecursive(Transform root, Player player)
        {
            var grabbableObjects = new List<GrabbableObject>();

            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child.GetComponent<Collider>() != null && child.name.Contains(":Grabbable"))
                {
                    var grabbableObject = child.gameObject.AddComponent<GrabbableObject>();
                    MelonCoroutines.Start(grabbableObject.Init(player));
                    child.gameObject.layer = LayerMask.NameToLayer("InteractionBase");
                    grabbableObjects.Add(grabbableObject);
                }
                
                grabbableObjects.AddRange(ParseGrabbableObjectsRecursive(child, player));
            }
            
            return grabbableObjects;
        }

        public void Apply(RigState state)
        {
            switch (state)
            {
                case RigState.Original:
                    MeshRenderer.materials = new[] { OriginalMaterial };
                    MeshRenderer.bones = OriginalBones;
                    MeshRenderer.sharedMesh = OriginalMesh;
                    Root.SetActive(false);

                    foreach (var bone in RigBones)
                        bone.gameObject.SetActive(false);
                    
                    if (playerVisuals != null && IsLocal)
                        playerVisuals.NonHeadClippedMaterial = OriginalVisualsMaterial;
                    
                    if (blinkCoroutine != null) MelonCoroutines.Stop(blinkCoroutine); blinkCoroutine = null;
                    break;
                case RigState.Rigged:
                    if (Config.swapOriginalMesh)
                    {
                        MeshRenderer.materials = RigMaterials;
                        MeshRenderer.bones = RigBones;
                        MeshRenderer.sharedMesh = RigMesh;
                        
                        if (playerVisuals != null && IsLocal)
                            playerVisuals.NonHeadClippedMaterial = RigVisualsMaterial;
                    }
                    Root.SetActive(true);

                    foreach (var bone in RigBones)
                        bone.gameObject.SetActive(true);
                    
                    if (Config != null)
                    {
                        if (Config.swapOriginalMesh)
                            MelonCoroutines.Start(ApplyDefaultBlendshapes());
                        
                        if (Config.eyeSettings.blinkType != (int)AvatarDescriptorExport.BlinkType.None)
                        {
                            if (blinkCoroutine != null)
                                MelonCoroutines.Stop(blinkCoroutine);

                            blinkCoroutine = MelonCoroutines.Start(AutoBlinkCoroutine());
                        }
                    }

                    if (!Main.instance.perPlayerToggles.ContainsKey(this) && !IsLocal && !IsPreview)
                        Main.instance.AddRigToList(this);
                    
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(state), state, null);
            }
        }

        private IEnumerator ApplyDefaultBlendshapes()
        {
            yield return null;
            if (MeshRenderer == null || MeshRenderer.sharedMesh == null) yield break;

            foreach (var blendshape in Config.defaultBlendshapes)
            {
                if (blendshape.index >= 0 && blendshape.index < MeshRenderer.sharedMesh.blendShapeCount)
                    MeshRenderer.SetBlendShapeWeight(blendshape.index, blendshape.weight);
            }
        }

        private IEnumerator AutoBlinkCoroutine()
        {
            while (true)
            {
                float waitTime = UnityEngine.Random.Range(Config.eyeSettings.blinkInterval.x, Config.eyeSettings.blinkInterval.y);
                yield return new WaitForSeconds(waitTime);

                float blinkDuration = Config.eyeSettings.blinkSpeed;

                switch ((AvatarDescriptorExport.BlinkType)Config.eyeSettings.blinkType)
                {
                    case AvatarDescriptorExport.BlinkType.Single:
                        int singleIdx = Config.eyeSettings.blinkBlendshape;
                        if (singleIdx >= 0)
                        {
                            yield return MelonCoroutines.Start(BlinkBlendshapeLerp(singleIdx, 100f, blinkDuration));
                            yield return MelonCoroutines.Start(BlinkBlendshapeLerp(singleIdx, 0f, blinkDuration));
                        }
                        break;

                    case AvatarDescriptorExport.BlinkType.LeftRight:
                        int leftIdx = Config.eyeSettings.blinkLeftBlendshape;
                        int rightIdx = Config.eyeSettings.blinkRightBlendshape;

                        if (leftIdx >= 0) MelonCoroutines.Start(BlinkBlendshapeLerp(leftIdx, 100f, blinkDuration));
                        if (rightIdx >= 0) MelonCoroutines.Start(BlinkBlendshapeLerp(rightIdx, 100f, blinkDuration));
                        yield return new WaitForSeconds(blinkDuration);

                        if (leftIdx >= 0) MelonCoroutines.Start(BlinkBlendshapeLerp(leftIdx, 0f, blinkDuration));
                        if (rightIdx >= 0) MelonCoroutines.Start(BlinkBlendshapeLerp(rightIdx, 0f, blinkDuration));
                        yield return new WaitForSeconds(blinkDuration);

                        break;
                    
                    case AvatarDescriptorExport.BlinkType.None:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        private IEnumerator BlinkBlendshapeLerp(int index, float targetWeight, float duration)
        {
            float startWeight = MeshRenderer.GetBlendShapeWeight(index);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                float t = elapsed / duration;
                MeshRenderer.SetBlendShapeWeight(index, Mathf.Lerp(startWeight, targetWeight, t));
                elapsed += Time.deltaTime;
                yield return null;
            }

            MeshRenderer.SetBlendShapeWeight(index, targetWeight);
        }

        public void OnDestroy()
        {
            if (OriginalMesh) Destroy(OriginalMesh);
            if (OriginalMaterial) Destroy(OriginalMaterial);
            if (RigMesh) Destroy(RigMesh);

            if (RigMaterials != null)
            {
                foreach (var mat in RigMaterials)
                    Destroy(mat);
            }
            
            if (blinkCoroutine != null) MelonCoroutines.Stop(blinkCoroutine); blinkCoroutine = null;
        }
    }
}
