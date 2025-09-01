using System.Collections;
using System.Reflection;
using CustomAvatars;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppPhoton.Pun;
using Il2CppPhoton.Voice.Unity;
using Il2CppRUMBLE.CharacterCreation.Interactable;
using Il2CppRUMBLE.Interactions.InteractionBase;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Subsystems;
using Il2CppSmartLocalization.Editor;
using Il2CppTMPro;
using UnityEngine;
using RumbleModdingAPI;
using MelonLoader;
using MelonLoader.Logging;
using MelonLoader.Utils;
using RumbleModUI;
using UnityEngine.Events;
using Hashtable = Il2CppExitGames.Client.Photon.Hashtable;
using Main = CustomAvatars.Main;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

[assembly: MelonInfo(typeof(Main), "CustomAvatars", "1.0.0", "ERROR")]
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
    public class CustomRigBone : MonoBehaviour
    {
        public Quaternion rotationOffset = Quaternion.identity;
    }

    public class Main : MelonMod
    {
        public string currentScene = "Loader";
        public bool sceneInitialized = false;
        public static Main instance;

        public GameObject rigParent;
        public GameObject avatarOptimizationParent;
        public GameObject refreshAvatarButton;

        public Mod mod = new Mod();
        public ModSetting<string> reloadKeybind;
        public ModSetting<bool> toggleLocal;
        public ModSetting<bool> toggleOthers;
        public ModSetting<bool> toggleVisibleToOthers;
        public ModSetting<bool> toggleInMatch;
        public ModSetting<bool> logAvatarStats;
        public ModSetting<bool> logOtherAvatarStats;
        public ModSetting<int> downloadLimitMB;
        public ModSetting<int> maxConcurrentDownloads;

        public ModSetting<bool> perPlayerHeader;
        public Dictionary<CustomRig, ModSetting<bool>> perPlayerToggles = new();
        private Dictionary<int, object> lastAvatars = new();
        
        public ModSetting<bool> UploadAvatar;

        public static Material poseGhostMaterial;

        public bool ranOnce = false;

        public Main()
        {
            instance = this;
        }

        // TODO:
        // Add base avatars you can choose from and customize
        // Make tutorial on how to make custom avatars
        
        public override void OnLateInitializeMelon()
        {
            Calls.onMapInitialized += Initialize;
            UI.instance.UI_Initialized += OnUIInitialized;
            LoggerInstance.Msg("Custom Avatars Initialized");
            RigManager.Initialize(this);
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            currentScene = sceneName;
            sceneInitialized = false;

            RigManager.rigs.Clear();
            Patches.loadedPlayers.Clear();
            rigParent = null;
        }

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

        public void Initialize()
        {
            RigManager.ClearRigs();
            lastAvatars.Clear();

            string filePath = Path.Combine(MelonEnvironment.UserDataDirectory, "CustomAvatars", "Opponents");
            Directory.CreateDirectory(filePath);
            
            ApplyAvatars(true);

            if (currentScene == "Gym" && !sceneInitialized)
            {
                GameObject tryOutModePanel = Calls.GameObjects.Gym.LOGIC.DressingRoom.Controlpanel.Controls
                    .Frameattachment.TryOutModePanel.GetGameObject();

                tryOutModePanel.transform.localPosition = new Vector3(-0.1164f, 0.1962f, -0.1014f);
                
                refreshAvatarButton = GameObject.Instantiate(tryOutModePanel);
                refreshAvatarButton.transform.SetParent(tryOutModePanel.transform.parent, false);
                refreshAvatarButton.name = "Refresh Avatar Panel";
                refreshAvatarButton.transform.localPosition = new Vector3(0.1069f, 0.1962f, -0.1014f);
                
                InteractionButton interactionButton = refreshAvatarButton.transform.GetChild(1).GetChild(0).GetComponent<InteractionButton>();
                interactionButton.onPressed.RemoveAllListeners();
                interactionButton.onPressed.AddListener((UnityAction)(() => { if ((bool)toggleLocal.SavedValue) Initialize(); }));

                TextMeshPro text = refreshAvatarButton.transform.GetChild(1).GetChild(1).GetComponent<TextMeshPro>();
                Object.Destroy(text.transform.GetComponent<LocalizedTextTMPro>());
                text.m_text = "Refresh Avatar";
                text.fontSize = 0.25f;
                text.ForceMeshUpdate();

                avatarOptimizationParent = new GameObject("AvatarDetails");
                avatarOptimizationParent.transform.localPosition = new Vector3(-2.9091f, 1.4218f, -1.5964f);
                avatarOptimizationParent.transform.localScale = Vector3.one * 0.5f;
                avatarOptimizationParent.transform.localRotation = Quaternion.Euler(0f, 206.6199f, 0f);
                
                var summary = Calls.Create.NewText("GOOD", 1f, new Color(0f, 0.5f, 0f), Vector3.zero, Quaternion.identity);
                summary.name = "Summary";
                summary.transform.SetParent(avatarOptimizationParent.transform, false);
                summary.transform.localPosition = new Vector3(0f, 0.0919f, 0f);
                summary.GetComponent<TextMeshPro>().enableWordWrapping = false;
                
                var details = Calls.Create.NewText("0 verts, 0 mat(s), 0 texture(s)", 1f, new Color(0f, 0.5f, 0f), Vector3.zero, Quaternion.identity);
                details.name = "Details";
                details.transform.SetParent(avatarOptimizationParent.transform, false);
                details.GetComponent<TextMeshPro>().enableWordWrapping = false;
                
                var warnings = Calls.Create.NewText("WARNINGS:", 1f, new Color(1, 1, 0), Vector3.zero, Quaternion.identity);
                warnings.name = "Warnings";
                warnings.transform.SetParent(avatarOptimizationParent.transform, false);
                warnings.transform.localPosition = new Vector3(0, -0.0919f, 0f);
                warnings.GetComponent<TextMeshPro>().enableWordWrapping = false;
                warnings.GetComponent<TextMeshPro>().alignment = TextAlignmentOptions.Center;
            }
            
            sceneInitialized = true;
        }

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
                if ((currentScene is "Map0" or "Map1" && !(bool)toggleInMatch.SavedValue) || !(bool)toggleLocal.SavedValue)
                    customRig.Apply(CustomRig.RigState.Original);
                else
                    customRig.Apply(CustomRig.RigState.Rigged);

                if (currentScene != "Gym")
                {
                    var props = new Hashtable();
                    props["CA_Avatar"] = (bool)toggleVisibleToOthers.SavedValue;
                    
                    PhotonNetwork.LocalPlayer.SetCustomProperties(props);
                }
                
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
            
            ranOnce = true;
        }

        public override void OnFixedUpdate()
        {
            if (currentScene == "Loader") return;

            if (rigParent && !rigParent.activeSelf)
                rigParent.SetActive(true);
        }

        public override void OnUpdate()
        {
            if (reloadKeybind != null && Enum.TryParse((string)reloadKeybind.SavedValue, true, out KeyCode parsed))
            {
                if (Input.GetKeyDown(parsed))
                {
                    Initialize();

                    var players = Calls.Players.GetAllPlayers().ToArray();
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

                        rig.Apply(CustomRig.RigState.Original);
                        RigManager.rigs.Remove(id);
                        GameObject.Destroy(rig.Root);
                        GameObject.Destroy(rig);
                    
                        Patches.ApplyRig(player);
                    }
                }
            }

            if (currentScene != "Gym")
            {
                foreach (var player in PhotonNetwork.PlayerList)
                {
                    if (player.CustomProperties == null) continue;

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

        public void RegeneratePortraits()
        {
            var hudType = Type.GetType("RumbleHud.Hud, RumbleHud");
            var method = hudType?.GetMethod("RegeneratePortraits", BindingFlags.Static | BindingFlags.Public);
            method?.Invoke(null, new object[] { currentScene == "Gym" });
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
            toggleInMatch = mod.AddToList("Toggle In Match", true, 0, "Toggles whether or not Custom Avatars will be loaded in matches.", new Tags());

            mod.AddToList("<b><#FFED29>- Statistics</color></b>", false, 0, "", new Tags { DoNotSave = true });
            logAvatarStats = mod.AddToList("Log Avatar Statistics (self)", true, 0, "If enabled, logs mesh info like vertex count, material count, etc. when the local player's avatar is loaded.", new Tags());
            logOtherAvatarStats = mod.AddToList("Log Avatar Statistics (other)", true, 0, "If enabled, logs mesh info like vertex count, material count, etc. when a remote player's avatar is loaded.", new Tags());

            mod.AddToList("<b><#305CDE>- Download & Upload</color></b>", false, 0, "", new Tags { DoNotSave = true });
            downloadLimitMB = mod.AddToList("Max File Download Size", 50, "The max download size for other avatars in MB.", new Tags());
            maxConcurrentDownloads = mod.AddToList("Max Concurrent Downloads", 3, "The maximum number of downloads that can be ran at the same time.", new Tags());
            UploadAvatar = mod.AddToList("Upload Avatar", false, 0, "Uploads the avatar in the folder when the button is clicked and saved.",
                new Tags
                {
                    DoNotSave = true
                });

            UploadAvatar.SavedValueChanged += (sender, args) =>
            {
                string rigBundle = Path.Combine(MelonEnvironment.UserDataDirectory, "CustomAvatars", Directory.GetFiles(Path.Combine(MelonEnvironment.UserDataDirectory, "CustomAvatars"), "*.rumbleavatar").FirstOrDefault());
                string masterId = Calls.Players.GetLocalPlayer().Data.GeneralData.PlayFabMasterId;
                if (!File.Exists(rigBundle))
                {
                    LoggerInstance.Error($"Invalid bundle found at path: {rigBundle}");
                    return;
                }
                
                LoggerInstance.Msg($"Uploading file at path '{rigBundle}' for MasterID {masterId}");
                RemoteAvatarLoader.UploadBundle(masterId, rigBundle, (success, skipped) =>
                {
                    if (skipped) return; 
                    LoggerInstance.Msg($"{(success ? "File uploaded successfully!" : "Upload failed.")}");
                });
            };

            toggleOthers.SavedValueChanged += (sender, args) =>
            {
                bool enabled = (bool)toggleOthers.Value;

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

                foreach (var rig in RigManager.rigs)
                {
                    if (rig.Key == Calls.Players.GetLocalPlayer().Data.GeneralData.PlayFabMasterId ||
                        rig.Key == "Preview Controller (Dressing Room)")
                    {
                        if (rig.Value?.playerVisuals != null && enabled)
                            rig.Value.OriginalVisualsMaterial = rig.Value.playerVisuals.NonHeadClippedMaterial;
                        
                        rig.Value?.Apply(enabled ? CustomRig.RigState.Rigged : CustomRig.RigState.Original);
                    }
                }

                if (currentScene == "Gym")
                {
                    if (!enabled)
                        Calls.GameObjects.Gym.LOGIC.DressingRoom.GetGameObject().GetComponent<DressingRoom>().UpdatePlayerVisuals();
                    
                    refreshAvatarButton?.SetActive(enabled);
                    avatarOptimizationParent?.SetActive(enabled);
                }

                RegeneratePortraits();
            };

            toggleInMatch.SavedValueChanged += (sender, args) =>
            {
                if (currentScene is "Map0" or "Map1")
                {
                    bool enabled = (bool)toggleInMatch.Value;
                    
                    foreach (var rig in RigManager.rigs)
                    {
                        var player = Calls.Players.GetAllPlayers().ToArray().FirstOrDefault(p => p.Data.GeneralData.PlayFabMasterId == rig.Key);
                        RigManager.ResolveRigState(player, rig.Value);
                    }
                
                    RegeneratePortraits();
                }
            };

            toggleVisibleToOthers.SavedValueChanged += (sender, args) =>
            {
                if (currentScene != "Gym")
                {
                    var props = new Il2CppExitGames.Client.Photon.Hashtable();
                    props["CA_Avatar"] = (bool)toggleVisibleToOthers.Value;
                    PhotonNetwork.LocalPlayer.SetCustomProperties(props);
                }
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

        public object blinkCoroutine;

        private Speaker remoteSpeaker;
        private AudioSource remoteAudioSource;
        
        private Recorder localRecorder;

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
            
            if (Config == null || MeshRenderer == null || Config.jawOpenBlendshape < 0 || MeshRenderer.sharedMesh.blendShapeCount < 1)
                return;

            float weight = 0f;

            if (IsLocal && localRecorder?.LevelMeter != null)
            {
                float volume = localRecorder.LevelMeter.CurrentAvgAmp;
                weight = Mathf.Clamp01(volume * Config.voiceMultiplier) * 100f;
            }
            else if (!IsLocal && remoteSpeaker != null)
            {
                float volume = 0f;

                if (remoteAudioSource != null)
                {
                    float[] s = new float[64];
                    remoteAudioSource.GetSpectrumData(s, 0, FFTWindow.Rectangular);
                    for (int i = 0; i < s.Length; i++) volume += s[i];
                }

                if (volume <= 0.0001f)
                {
                    var prop = remoteSpeaker.GetType().GetProperty("LevelMeter", BindingFlags.Public|BindingFlags.Instance);
                    var lm = prop?.GetValue(remoteSpeaker);
                    var avgProp = lm?.GetType().GetProperty("CurrentAvgAmp");
                    if (avgProp != null) volume = (float)avgProp.GetValue(lm);
                }

                weight = Mathf.Clamp01(volume * Config.voiceMultiplier) * 100f;
            }
            
            MeshRenderer.SetBlendShapeWeight(Config.jawOpenBlendshape, weight);
        }
        
        private AudioClip GetMicClip(Recorder recorder)
        {
            if (recorder == null)
                return null;

            var micSourceField = recorder.GetType().GetField("microphoneSource", BindingFlags.NonPublic | BindingFlags.Instance);
            if (micSourceField == null)
                return null;

            var micSource = micSourceField.GetValue(recorder);

            var clipProp = micSource?.GetType().GetProperty("MicrophoneClip", BindingFlags.Public | BindingFlags.Instance);
            return clipProp?.GetValue(micSource) as AudioClip;
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
                try
                {
                    var target = transform.GetChild(2).GetChild(0).GetChild(0).GetChild(2);
                    if (IsLocal)
                    {
                        localRecorder = target.GetComponent<Recorder>();
                    }
                    else
                    {
                        remoteSpeaker = target.GetComponent<Speaker>();
                        remoteAudioSource = remoteSpeaker.GetComponent<AudioSource>();
                    }
                }
                catch
                {
                    if (log)
                        Main.instance.LoggerInstance.Warning("CaptureOriginal: Recorder/Speaker hierarchy is missing or malformed.");
                }
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
                    MeshRenderer.materials = new Material[] { OriginalMaterial };
                    MeshRenderer.bones = OriginalBones;
                    MeshRenderer.sharedMesh = OriginalMesh;
                    Root.SetActive(false);
                    
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

                    if (Config != null)
                    {
                        if (Config.swapOriginalMesh)
                            MelonCoroutines.Start(ApplyDefaultBlendshapes());
                        
                        if (Config.autoBlink)
                        {
                            if (blinkCoroutine != null)
                                MelonCoroutines.Stop(blinkCoroutine);

                            blinkCoroutine = MelonCoroutines.Start(AutoBlinkCoroutine());
                        }
                    }
                    
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
                float waitTime = UnityEngine.Random.Range(Config.blinkInterval.x, Config.blinkInterval.y);
                yield return new WaitForSeconds(waitTime);

                float blinkDuration = Config.blinkSpeed;

                switch ((AvatarDescriptorExport.BlinkType)Config.blinkType)
                {
                    case AvatarDescriptorExport.BlinkType.Single:
                        int singleIdx = Config.blinkBlendshape;
                        if (singleIdx >= 0)
                        {
                            yield return MelonCoroutines.Start(BlinkBlendshapeLerp(singleIdx, 100f, blinkDuration));
                            yield return MelonCoroutines.Start(BlinkBlendshapeLerp(singleIdx, 0f, blinkDuration));
                        }
                        break;

                    case AvatarDescriptorExport.BlinkType.LeftRight:
                        int leftIdx = Config.blinkLeftBlendshape;
                        int rightIdx = Config.blinkRightBlendshape;

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
