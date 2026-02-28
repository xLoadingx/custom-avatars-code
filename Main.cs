using System.Collections;
using System.Reflection;
using Il2CppPhoton.Pun;
using Il2CppRUMBLE.CharacterCreation.Interactable;
using Il2CppRUMBLE.Interactions.InteractionBase;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Subsystems;
using Il2CppSmartLocalization.Editor;
using Il2CppTMPro;
using UnityEngine;
using RumbleModdingAPI;
using MelonLoader;
using MelonLoader.Utils;
using RumbleModdingAPI.RMAPI;
using RumbleModUI;
using UnityEngine.Events;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using Hashtable = Il2CppExitGames.Client.Photon.Hashtable;
using Main = CustomAvatars.Main;
using Object = UnityEngine.Object;
using static UnityEngine.Mathf;
using VisibilityResult = CustomAvatars.RigManager.VisibilityResult;

[assembly: MelonInfo(typeof(Main), "CustomAvatars", "1.4.0", "ERROR")]
[assembly: MelonGame("Buckethead Entertainment", "RUMBLE")]
[assembly: MelonOptionalDependencies("RumbleHud")]
[assembly: MelonColor(255, 255, 0, 0)]
[assembly: MelonAuthorColor(255, 255, 0, 0)]

namespace CustomAvatars
{
    public static class BuildInfo
    {
        public const string Name = "CustomAvatars";
        public const string Version = "1.4.0";
    }
    
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

    [RegisterTypeInIl2Cpp]
    public class CustomRigBone : MonoBehaviour { }

    public class Main : MelonMod
    {
        public string currentScene = "Loader";
        public bool sceneInitialized;
        public static Main instance;

        public CustomRig localRig;

        public GameObject rigParent;
        public GameObject avatarOptimizationParent;
        public GameObject refreshAvatarButton;
        public GameObject tryoutModeButton;
        public GameObject uploadAvatarButton;
        public GameObject uploadProgressBar;
        public TextMeshPro serverStatusText;
        public GameObject tagObject;
        public (Color color, string status) serverStatus = (Color.cyan, "Up To Date");

        public Mod mod = new();
        public ModSetting<string> reloadKeybind;
        public ModSetting<bool> reloadToggle;
        public ModSetting<bool> toggleLocal;
        public ModSetting<bool> toggleOthers;
        public ModSetting<bool> toggleVisibleToOthers;
        public ModSetting<bool> toggleInMatch;
        public ModSetting<bool> toggleIfNewerVersion;
        public ModSetting<bool> toggleInRockCam;
        public ModSetting<bool> logAvatarStats;
        public ModSetting<bool> logOtherAvatarStats;
        public ModSetting<int> downloadLimitMB;
        public ModSetting<int> maxConcurrentDownloads;
        public ModSetting<bool> uploadAvatar;

        public ModSetting<bool> perPlayerHeader;
        public Dictionary<string, PlayerEntry> perPlayerSettings = new();
        private Dictionary<int, Hashtable> lastProps = new();

        public static Material poseGhostMaterial;

        public List<Transform> previewScanList = new();

        public GameObject bodyDouble;
        public GameObject currentRig;

        public Main()
        {
            instance = this;
        }

        // TODO:
        // Add avatar settings (along with making Animator Controllers work with it)
        
        public override void OnLateInitializeMelon()
        {
            Actions.onMapInitialized += (scene) => MelonCoroutines.Start(Initialize(scene));
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

            if (currentScene is "Gym" or "Park")
                RigManager.OpponentID = string.Empty;
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
        public IEnumerator Initialize(string scene)
        {
            yield return new WaitForSeconds(1f);
            
            RigManager.ClearRigs();
            lastProps.Clear();

            string filePath = Path.Combine(MelonEnvironment.UserDataDirectory, "CustomAvatars", "Opponents");
            Directory.CreateDirectory(filePath);

            // Making objects in code is fun looking
            if (currentScene == "Gym" && !sceneInitialized)
            {
                tryoutModeButton = GameObjects.Gym.INTERACTABLES.DressingRoom.Controlpanel.Controls.Frameattachment.TryOutModePanel.GetGameObject();

                uploadAvatarButton = GameObject.Instantiate(tryoutModeButton, tryoutModeButton.transform.parent, false);
                uploadAvatarButton.name = "Upload Avatar Panel";
                uploadAvatarButton.transform.localPosition = new Vector3(0.1069f, 0.1962f, -0.1014f);
                
                refreshAvatarButton = GameObject.Instantiate(tryoutModeButton, tryoutModeButton.transform.parent, false);
                refreshAvatarButton.name = "Refresh Avatar Panel";
                refreshAvatarButton.transform.localPosition = new Vector3(-0.1164f, 0.1962f, -0.1014f);
                
                InteractionButton interactionButton = refreshAvatarButton.transform.GetChild(1).GetChild(0).GetComponent<InteractionButton>();
                interactionButton.onPressed.RemoveAllListeners();
                interactionButton.onPressed.AddListener((UnityAction)(() => { if ((bool)toggleLocal.SavedValue) MelonCoroutines.Start(Initialize(scene)); }));
                interactionButton.interactionAnimParameter = "nan";
                interactionButton.InteractionAnimParameterL = "nan";
                interactionButton.InteractionAnimParameterR = "nan";
                
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
                
                uploadProgressBar = GameObject.Instantiate(GameObjects.Gym.INTERACTABLES.
                    ProgressTracker.ProgressPanel.StatusBar.GetGameObject(), avatarOptimizationParent.transform, false);
                uploadProgressBar.name = "Upload Progress Bar";
                uploadProgressBar.transform.localScale = new Vector3(0.8562f, 0.0544f, 0.854f);
                uploadProgressBar.transform.localPosition = new Vector3(-1.9034f, 0.2507f, -0.4614f);
                uploadProgressBar.transform.localRotation = Quaternion.Euler(354.8412f, 324.0485f, 3.7311f);
                uploadProgressBar.GetComponent<MeshRenderer>().material.SetFloat("_RC_Target", 1f);
                uploadProgressBar.SetActive(false);
                
                var summary = Create.NewText("GOOD", 1f, new Color(0f, 0.5f, 0f), Vector3.zero, Quaternion.identity);
                summary.name = "Summary";
                summary.transform.SetParent(avatarOptimizationParent.transform, false);
                summary.transform.localPosition = new Vector3(0f, 0.0919f, 0f);
                summary.GetComponent<TextMeshPro>().enableWordWrapping = false;
                summary.GetComponent<TextMeshPro>().alignment = TextAlignmentOptions.Center;
                
                var details = Create.NewText("0 verts, 0 mat(s), 0 texture(s)", 1f, new Color(0f, 0.5f, 0f), Vector3.zero, Quaternion.identity);
                details.name = "Details";
                details.transform.SetParent(avatarOptimizationParent.transform, false);
                details.GetComponent<TextMeshPro>().enableWordWrapping = false;
                details.GetComponent<TextMeshPro>().alignment = TextAlignmentOptions.Center;
                
                var warnings = Create.NewText("WARNINGS:", 1f, new Color(1, 1, 0), Vector3.zero, Quaternion.identity);
                warnings.name = "Warnings";
                warnings.transform.SetParent(avatarOptimizationParent.transform, false);
                warnings.transform.localPosition = new Vector3(0, -0.0919f, 0f);
                warnings.GetComponent<TextMeshPro>().enableWordWrapping = false;
                warnings.GetComponent<TextMeshPro>().alignment = TextAlignmentOptions.Center;

                var newServerStatus = Create.NewText("Up To Date",1f, new Color(0, 1, 1), Vector3.zero, Quaternion.identity);
                newServerStatus.name = "AvatarServerStatus";
                newServerStatus.transform.SetParent(avatarOptimizationParent.transform, false);
                newServerStatus.transform.localPosition = new Vector3(-1.9309f, 0.2499f, -0.4545f);
                newServerStatus.transform.localRotation = Quaternion.Euler(352.9085f, 321.6011f, 3.3454f);
                serverStatusText = newServerStatus.GetComponent<TextMeshPro>();
                serverStatusText.enableWordWrapping = false;
                serverStatusText.alignment = TextAlignmentOptions.Center;
                
                SetObjectsActive();
            }
            
            // Mod version
            if (PhotonNetwork.InRoom)
            {
                var props = new Hashtable();
                props["CA_ModVersion"] = BuildInfo.Version;
                
                var toggles = RigManager.avatarSettingBools.Select(s => (bool)s.Value).ToList();
                props["Ca_Params"] = RigManager.PackParams(toggles);
                
                PhotonNetwork.LocalPlayer.SetCustomProperties(props);
            }
            
            ApplyAvatars();
            
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
        
        public void CheckClonebending(CustomRig customRig, GameObject rig, bool log)
        {
            // Thanks to oreotrollturbo for the type shenanigans
            var mainClassType = Type.GetType("CloneBending2.Core, CloneBending2");
            if (mainClassType == null) return;
            
            var instanceField = mainClassType.GetField("instance", BindingFlags.Public | BindingFlags.Static);
            var cloneInstance = instanceField?.GetValue(null);
            
            var bodyDoubleField = mainClassType.GetField("bodyDouble", BindingFlags.Instance | BindingFlags.NonPublic);
            bodyDouble = (GameObject)bodyDoubleField?.GetValue(cloneInstance);

            if (bodyDouble != null)
            {
                var path = Directory
                    .GetFiles(Path.Combine(MelonEnvironment.UserDataDirectory, "CustomAvatars"), "*.rumbleavatar")
                    .FirstOrDefault();

                var bundle = AssetBundle.LoadFromFile(path);
                GameObject newRig = bundle.LoadAsset<GameObject>("Rig");
                bundle.Unload(false);

                newRig.name = "RIG - CloneBending Clone";
                newRig.transform.SetParent(rigParent.transform, true);
                
                var smr = bodyDouble.transform.GetChild(1).GetChild(0).GetComponent<SkinnedMeshRenderer>();
                var cloneCustomRig = bodyDouble.GetComponent<CustomRig>();
                if (cloneCustomRig != null)
                {
                    if (cloneCustomRig.blinkCoroutine != null)
                        MelonCoroutines.Stop(cloneCustomRig.blinkCoroutine);
                }
                else
                {
                    cloneCustomRig = bodyDouble.AddComponent<CustomRig>();
                    cloneCustomRig.PlayerName = "CloneBending Clone";
                    cloneCustomRig.IsPreview = true;
                    cloneCustomRig.CaptureOriginal("CloneBending Clone", false, smr, log);
                }

                cloneCustomRig.CaptureRig(newRig);
                
                cloneCustomRig.Config = customRig.Config;
                if (!cloneCustomRig.Config.swapOriginalMesh)
                    rig.transform.SetParent(bodyDouble.transform, true);
            
                RigManager.ApplyRigToSMR(bodyDouble.transform.GetChild(1).GetChild(1), newRig, bodyDouble.transform.GetChild(1).GetComponent<Animator>(), customRig: cloneCustomRig);
                RigManager.rigs["CloneBending Clone"] = cloneCustomRig;
                
                if (!(bool)toggleLocal.SavedValue)
                    cloneCustomRig.Apply(CustomRig.RigState.Original);
                else
                    cloneCustomRig.Apply(CustomRig.RigState.Rigged);
                
                var runtimeAnimator = rig.GetComponent<Animator>();
                var cloneRigAnimator = newRig.GetComponent<Animator>();

                if (runtimeAnimator != null && cloneRigAnimator != null && cloneRigAnimator.isHuman)
                {
                    foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
                    {
                        if (bone == HumanBodyBones.LastBone) continue;

                        Transform playerBone = runtimeAnimator.GetBoneTransform(bone);
                        Transform rigBone = cloneRigAnimator.GetBoneTransform(bone);

                        if (playerBone != null && rigBone != null)
                        {
                            rigBone.localRotation = playerBone.localRotation;
                        }
                    }
                }
            }
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
                if (currentScene == "Gym")
                {
                    uploadProgressBar.SetActive(true);
                    progressBarMat = uploadProgressBar.GetComponent<MeshRenderer>().material;
                    progressBarMat.SetFloat("_RC_Current", 0f);

                    serverStatusText.gameObject.transform.localPosition = new Vector3(-1.9309f, 0.359f, -0.4545f);
                    serverStatusText.color = Color.yellow;
                }
            }, (success, skipped) =>
            {
                if (currentScene == "Gym")
                {
                    uploadProgressBar.SetActive(false);
                    serverStatus = success ? (Color.cyan, "Up To Date") : (Color.red, "Not Uploaded");
                    serverStatusText.color = serverStatus.color;
                    serverStatusText.text = serverStatus.status;
                    serverStatusText.gameObject.transform.localPosition = new Vector3(-1.9309f, 0.2499f, -0.4545f);
                }
                
                if (skipped) return;
                LoggerInstance.Msg($"{(success ? "File uploaded successfully!" : "Upload failed.")}");
            }, progress =>
            {
                if (progressBarMat == null || currentScene != "Gym") return;
                displayedProgress = Lerp(displayedProgress, progress, Time.deltaTime * 10f);
                progressBarMat.SetFloat("_RC_Current", displayedProgress);
            }, serverStatusText);
        }

        // Applies local & preview rigs, also SHA-checks against GitHub
        // Might need to make the warning a bit more visible
        public void ApplyAvatars(bool log = true, bool clearAll = true)
        {
            if (clearAll)
                RigManager.ClearRigs();
            
            PlayerManager.instance.LocalPlayer.Controller.GetSubsystem<PlayerCamera>().camera.cullingMask |= (1 << 2);
            GameObjects.DDOL.GameInstance.Initializable.RecordingCamera.GetGameObject().GetComponent<Camera>()
                .cullingMask |= (1 << 2);
            
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

                localRig = customRig;
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
                        GameObjects.Gym.INTERACTABLES.DressingRoom.PreviewPlayerController.Visuals.GetGameObject();
                    
                    currentRig = rig;
                    LoadPreviewController();
                    
                    // CloneBending Clone
                    CheckClonebending(customRig, currentRig, log);
                    
                    MelonCoroutines.Start(RigManager.FixHUDCamera(localPlayer.Data.GeneralData.PlayFabMasterId, () =>
                    {
                        if (!(bool)toggleInRockCam.SavedValue)
                            previewController.transform.GetChild(0).gameObject.layer = 2;
                        else
                            previewController.transform.GetChild(0).gameObject.layer = 23;
                    }));
                }
            }, log));

            if (currentScene == "Gym" && poseGhostMaterial == null)
            {
                poseGhostMaterial = new Material(GameObjects.Gym.INTERACTABLES.
                    PoseGhost.Ghost.StaticGhost.Visuals.
                    Poseghostbody.GetGameObject().
                    GetComponent<SkinnedMeshRenderer>().material);
                poseGhostMaterial.hideFlags = HideFlags.DontUnloadUnusedAsset | HideFlags.HideAndDontSave;
            }
        }

        public void LoadPreviewController()
        {
            // Preview Controller in Dressing Room
            var previewController =
                GameObjects.Gym.INTERACTABLES.DressingRoom.PreviewPlayerController.Visuals.GetGameObject();

            var path = Directory
                .GetFiles(Path.Combine(MelonEnvironment.UserDataDirectory, "CustomAvatars"), "*.rumbleavatar")
                .FirstOrDefault();

            var bundle = AssetBundle.LoadFromFile(path);
            GameObject newRig = bundle.LoadAsset<GameObject>("Rig");
            bundle.Unload(false);

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
                previewCustomRig.CaptureOriginal("Preview Controller (Dressing Room)", false, smr, false);
            }

            previewCustomRig.CaptureRig(newRig);
            
            previewCustomRig.Config = localRig.Config;
            previewScanList = RigManager.Scan(newRig.transform);

            for (int i = 0; i < previewCustomRig.Config.parameters.Count; i++)
            {
                var param = previewCustomRig.Config.parameters[i];
                var setting = RigManager.avatarSettingBools[i];
                
                GameObject previewObj = previewScanList[param.targetIndex].gameObject;

                previewObj.SetActive((bool)setting.Value);

                setting.SavedValueChanged += (sender, args) =>
                {
                    previewObj.SetActive((bool)setting.Value);
                };
            }
        
            RigManager.ApplyRigToSMR(previewController.transform.GetChild(1), newRig, previewController.GetComponent<Animator>(), customRig: previewCustomRig);
            RigManager.rigs["Preview Controller (Dressing Room)"] = previewCustomRig;
            
            if (!(bool)toggleLocal.SavedValue)
                previewCustomRig.Apply(CustomRig.RigState.Original);
            else
                previewCustomRig.Apply(CustomRig.RigState.Rigged);

            // LMAO I have no idea how I came up with this
            // but it works somehow so im not touching it
            var runtimeAnimator = currentRig.GetComponent<Animator>();
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

        // Ensures rigParent stays active
        // Mostly because FlatLand
        // Also handles CustomProperties
        public override void OnFixedUpdate()
        {
            if (currentScene == "Loader") return;

            if (rigParent && !rigParent.activeSelf)
                rigParent.SetActive(true);

            if (refreshAvatarButton && !refreshAvatarButton.activeSelf && currentScene == "Gym" && (bool)toggleLocal.SavedValue)
                refreshAvatarButton.SetActive(true);

            if (currentScene == "Gym")
            {
                var dressingRoom = GameObjects.Gym.INTERACTABLES.DressingRoom.GetGameObject();
                if (!dressingRoom.activeSelf)
                {
                    dressingRoom.SetActive(true);
                    dressingRoom.transform.GetChild(0).gameObject.SetActive(false);
                    dressingRoom.transform.GetChild(1).gameObject.SetActive(false);
                    dressingRoom.transform.GetChild(2).GetChild(1).gameObject.SetActive(false);
                    dressingRoom.transform.position = new Vector3(-0.4274f, 0.093f, -3.1731f);
                    avatarOptimizationParent.transform.position = new Vector3(-1.3643f, 1.2603f, -3.4911f);
                    avatarOptimizationParent.SetActive(true);
                }
            }
            
            if (currentScene != "Gym" && (bool)(toggleOthers?.SavedValue ?? false))
            {
                foreach (var player in PhotonNetwork.PlayerList)
                {
                    if (player.CustomProperties == null || player == PhotonNetwork.LocalPlayer) 
                        continue;

                    Player rumblePlayer = Calls.Players.GetPlayerByActorNo(player.ActorNumber);
                    
                    var props = player.CustomProperties;
                    if (props != null)
                    {
                        // Checks if other players want to be seen, for the canOthersSeeMyAvatar toggle.
                        if (props.ContainsKey("CA_Avatar") && localRig != null)
                        {
                            if (rumblePlayer?.Controller?.TryGetComponent<CustomRig>(out var rig) ?? false)
                                RigManager.ResolveRigState(rumblePlayer, rig);
                        }

                        if (props.ContainsKey($"{Calls.Players.GetLocalPlayer().Data.GeneralData.PlayFabMasterId}_CAVisibility"))
                        {
                            VisibilityResult canSeeMe = RigManager.CanPlayerSeeMe(player);

                            if (rumblePlayer?.Controller?.GetComponent<CustomRig>() ?? false)
                            {
                                var tagObj = rumblePlayer.Controller.transform.GetChild(9).Find("CustomAvatarTag");

                                if (tagObj != null && tagObj.TryGetComponent<SpriteRenderer>(out var sr))
                                {
                                    sr.color = canSeeMe switch
                                    {
                                        VisibilityResult.Visible => Color.white,
                                        VisibilityResult.Hidden => new Color(1f, 0.5f, 0.5f, 1f),
                                        VisibilityResult.Unknown => Color.grey, // Local player doesn't have avatar
                                        _ => sr.color
                                    };
                                }
                            }
                        }

                        if (props.ContainsKey("CA_Params") && rumblePlayer.Controller.TryGetComponent<CustomRig>(out var customRig))
                        {
                            int mask = Convert.ToInt32(props["CA_Params"]);
                            RigManager.ApplyRemoteParams(customRig, mask);
                        }
                    }
                }
            }
        }

        // Reload keybind + rig refresh
        public override void OnUpdate()
        {
            if (reloadKeybind != null && Enum.TryParse((string)reloadKeybind.SavedValue, true, out KeyCode parsed))
            {
                if (Input.GetKeyDown(parsed))
                    MelonCoroutines.Start(Initialize(currentScene));
            }
        }

        // Adds per-player toggle for ModUI
        public void AddPlayerToList(Player player)
        {
            try
            {
                if (player?.Controller?.TryGetComponent<CustomRig>(out var rig) ?? false)
                {
                    if (string.IsNullOrEmpty(rig.PlayerId)) return;

                    if (perPlayerSettings.Count == 0)
                        perPlayerHeader = mod.AddToList("<b><#FFB347>- Per Player Settings", false, 0, "", new Tags { DoNotSave = true });

                    var entry = new PlayerEntry();
                
                    entry.Toggle = mod.AddToList($"{rig.PlayerName} <#FFF>({rig.PlayerId})", true, 0, $"Toggles the avatar for {rig.PlayerName}.", new Tags());
                    entry.Toggle.SavedValueChanged += (sender, args) =>
                    {
                        if (toggleOthers.GetValue())
                            rig.Apply(entry.Toggle.GetValue() ? CustomRig.RigState.Rigged : CustomRig.RigState.Original);
                    
                        RigManager.UpdateVisibilityProps();
                    };

                    entry.ReloadButton = mod.AddToList("   - Reload", false, 0, $"Reloads the avatar for {rig.PlayerName} <#FFF>upon clicking the button.", new Tags { DoNotSave = true });
                    entry.ReloadButton.CurrentValueChanged += (sender, args) =>
                    {
                        if (RigManager.loadingPlayers.Contains(player.Data.GeneralData.PlayFabMasterId))
                            return;
                    
                        LoggerInstance.Msg($"Reloading avatar for {rig.PlayerName}");
                    
                        GameObject.Destroy(rig);
                        Patches.loadedPlayers.Remove(player.Data.GeneralData.PlayFabMasterId);
                        Patches.ApplyRig(player);
                    };

                    perPlayerSettings[rig.PlayerId] = entry;

                    mod.GetFromFile();
                }
            }
            catch (Exception) { }
        }

        // Removes per-player toggle (cleanup if player leaves)
        public void RemovePlayerFromList(Player player)
        {
            var id = player.Data.GeneralData.PlayFabMasterId;
            
            if (perPlayerSettings.TryGetValue(id, out var settings))
            {
                mod.Settings.Remove(settings.Toggle);
                mod.Settings.Remove(settings.ReloadButton);
                
                perPlayerSettings.Remove(id);
            }
            
            if (perPlayerHeader != null && perPlayerSettings.Count == 0)
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
            mod.ModVersion = BuildInfo.Version;
                
            mod.SetFolder("CustomAvatars");
            mod.AddToList("Description", "", "Allows custom avatars for you or specific people.", new Tags());
            reloadKeybind = mod.AddToList("Reload Keybind", nameof(KeyCode.R), "The key that reloads your and other's avatars.", new Tags());
            reloadToggle = mod.AddToList("Reload Avatar", false, 0, "Reloads your avatar on toggle.", new Tags { DoNotSave = true });
            
            mod.AddToList("<b><#114F11>- Avatar Visibility</color></b>", false, 0, "", new Tags { DoNotSave = true });
            toggleLocal = mod.AddToList("Toggle for Self", true, 0, "Toggles whether you see your custom avatar locally. This does not affect what other players see.", new Tags());
            toggleOthers = mod.AddToList("Toggle for Others", true, 0, "Toggles whether you can see other players' custom avatars.", new Tags());
            toggleVisibleToOthers = mod.AddToList("Let Others See My Avatar", true, 0, "Controls whether other players can see your custom avatar. This setting is networked.", new Tags());
            toggleInMatch = mod.AddToList("Toggle In Match", true, 0, "Toggles whether or not you and other players can see your custom avatar in a match. This setting is networked.", new Tags());
            toggleIfNewerVersion = mod.AddToList("Load Higher Version Avatars", false, 0, "Toggles whether or not people that have a higher mod version than you will have their avatars loaded.", new Tags());
            toggleInRockCam = mod.AddToList("Toggle in Rock Cam", true, 0, "Toggles whether or not you can see your custom avatar in Rock Cam or not.\nThis setting takes effect when your avatar is reloaded.", new Tags());

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

            reloadToggle.CurrentValueChanged += (sender, args) => ApplyAvatars(clearAll: false);

            void UpdateAllPlayers()
            {
                foreach (var rig in RigManager.rigs)
                {
                    if (rig.Key == Calls.Players.GetLocalPlayer().Data.GeneralData.PlayFabMasterId) continue;
                    if (rig.Key == "Preview Controller (Dressing Room)") continue;
                    if (rig.Key == "CloneBending Clone") continue;

                    var player = Calls.Players.GetAllPlayers().ToArray().FirstOrDefault(p => p.Data.GeneralData.PlayFabMasterId == rig.Key);
                    RigManager.ResolveRigState(player, rig.Value);
                }
                
                RegeneratePortraits();
            }

            toggleOthers.SavedValueChanged += (sender, args) =>
            {
                UpdateAllPlayers();
                RigManager.UpdateVisibilityProps();
            };

            toggleIfNewerVersion.SavedValueChanged += (sender, args) => { UpdateAllPlayers(); };
            
            toggleLocal.SavedValueChanged += (sender, args) =>
            {
                bool enabled = (bool)toggleLocal.Value;

                UpdateAvatarVisibility();

                if (currentScene == "Gym")
                {
                    if (!enabled)
                        GameObjects.Gym.INTERACTABLES.DressingRoom.GetGameObject().GetComponent<DressingRoom>().UpdatePlayerVisuals();
                    
                    SetObjectsActive();
                    
                    GameObjects.Gym.INTERACTABLES.DressingRoom.PreviewPlayerController.GetGameObject().GetComponent<CustomRig>()?
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
        public Player Player;
        public string AvatarFilePath;
        public string ModVersion;

        public AvatarDescriptorExport Config;

        public GameObject Root;
        public GameObject PlayerRoot;
        public Transform Head;
        public Animator animator;
        public List<Transform> rigScan;

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
                        MeshRenderer.SetBlendShapeWeight(idx, Clamp(voiceSystem.currentJawOpenPercentage * 100f * Config.voiceMultiplier, 0, 100));
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

            var foundPlayer = Calls.Players.GetAllPlayers().ToArray().FirstOrDefault(p => p.Data.GeneralData.PlayFabMasterId == playerId);
            if (foundPlayer != null)
                Player = foundPlayer;

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
                // OriginalVisualsMaterial = Instantiate(playerVisuals.NonHeadClippedMaterial);
                // OriginalVisualsMaterial.hideFlags = HideFlags.HideAndDontSave | HideFlags.DontUnloadUnusedAsset;
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
                var headset = renderer.transform.parent.parent.GetChild(2).GetChild(0).GetChild(0);
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
            rigScan = RigManager.Scan(rig.transform);
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
            try
            {
                switch (state)
                {
                    case RigState.Original:
                        MeshRenderer.materials = new[] { OriginalMaterial };
                        MeshRenderer.bones = OriginalBones;
                        MeshRenderer.sharedMesh = OriginalMesh;

                        foreach (var bone in RigBones)
                            bone.gameObject.SetActive(false);
                        
                        if (blinkCoroutine != null) MelonCoroutines.Stop(blinkCoroutine); blinkCoroutine = null;
                        break;
                    case RigState.Rigged:
                        if (Config.swapOriginalMesh && (bool)Main.instance.toggleInRockCam.SavedValue)
                        {
                            MeshRenderer.materials = RigMaterials;
                            MeshRenderer.bones = RigBones;
                            MeshRenderer.sharedMesh = RigMesh;
                        }
                        Root.SetActive(true);

                        if (IsLocal)
                        {
                            if (!(bool)Main.instance.toggleInRockCam.SavedValue)
                            {
                                MeshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                                MeshRenderer.gameObject.layer = 2;
                                PlayerManager.instance.LocalPlayer.Controller.GetSubsystem<PlayerCamera>().camera.cullingMask &= ~(1 << 2);
                                GameObjects.DDOL.GameInstance.Initializable.RecordingCamera.GetGameObject().GetComponent<Camera>()
                                    .cullingMask &= ~(1 << 2);
                            
                                foreach (var renderer in Root.GetComponentsInChildren<Renderer>())
                                    renderer.gameObject.layer = 23;
                            }
                            else
                            {
                                MeshRenderer.shadowCastingMode = ShadowCastingMode.On;
                                MeshRenderer.gameObject.layer = 23;
                                PlayerManager.instance.LocalPlayer.Controller.GetSubsystem<PlayerCamera>().camera.cullingMask |= (1 << 2);
                                GameObjects.DDOL.GameInstance.Initializable.RecordingCamera.GetGameObject().GetComponent<Camera>()
                                    .cullingMask |= (1 << 2);
                            }
                        }

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

                        var player = GetComponent<PlayerController>().assignedPlayer;
                        
                        if (!Main.instance.perPlayerSettings.ContainsKey(player.Data.GeneralData.PlayFabMasterId) && !IsLocal && !IsPreview)
                            Main.instance.AddPlayerToList(player);
                        
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(state), state, null);
                }
            } catch {}
        }

        private IEnumerator ApplyDefaultBlendshapes()
        {
            yield return null;

            SkinnedMeshRenderer renderer;
            renderer = Config.swapOriginalMesh ? MeshRenderer : Root.GetComponentInChildren<SkinnedMeshRenderer>();
            
            if (renderer == null || MeshRenderer.sharedMesh == null) yield break;

            foreach (var blendshape in Config.defaultBlendshapes)
            {
                if (blendshape.index >= 0 && blendshape.index < MeshRenderer.sharedMesh.blendShapeCount)
                    renderer.SetBlendShapeWeight(blendshape.index, blendshape.weight);
            }
        }

        private IEnumerator AutoBlinkCoroutine()
        {
            while (true)
            {
                float waitTime = UnityEngine.Random.Range(Config.eyeSettings.blinkInterval.x, Config.eyeSettings.blinkInterval.y);
                yield return new WaitForSeconds(waitTime);

                float blinkDuration = Config.eyeSettings.blinkSpeed;

                switch (Config.eyeSettings.blinkType)
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
            if (!MeshRenderer || !MeshRenderer.sharedMesh)
                yield break;

            if (index < 0 || index >= MeshRenderer.sharedMesh.blendShapeCount)
                yield break;

            float startWeight;
            try
            {
                startWeight = MeshRenderer.GetBlendShapeWeight(index);
            }
            catch
            {
                yield break;
            }
            
            float elapsed = 0f;

            while (elapsed < duration)
            {
                float t = elapsed / duration;
                MeshRenderer.SetBlendShapeWeight(index, Lerp(startWeight, targetWeight, t));
                elapsed += Time.deltaTime;
                yield return null;
            }

            MeshRenderer.SetBlendShapeWeight(index, targetWeight);
        }

        public void OnDestroy()
        {
            Apply(RigState.Original);
            
            if (RigMesh) Destroy(RigMesh);
            if (Root) Destroy(Root);

            if (RigMaterials != null)
            {
                foreach (var mat in RigMaterials)
                    Destroy(mat);
            }

            if (blinkCoroutine != null)
            {
                MelonCoroutines.Stop(blinkCoroutine); 
                blinkCoroutine = null;
            }
        }
    }

    public class PlayerEntry
    {
        public ModSetting<bool> Toggle;
        public ModSetting<bool> ReloadButton;

        public IEnumerable<ModSetting> GetAllSettings()
        {
            if (Toggle != null) yield return Toggle;
            if (ReloadButton != null) yield return ReloadButton;
        }
    }
}
