using System.Collections;
using System.Reflection;
using CustomAvatars;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
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
        public ModSetting<bool> toggleLocal;
        public ModSetting<bool> toggleOthers;
        public ModSetting<bool> logAvatarStats;
        public ModSetting<bool> logOtherAvatarStats;
        public ModSetting<int> downloadLimitMB;
        public ModSetting<int> maxConcurrentDownloads;

        public ModSetting<bool> perPlayerHeader;
        public Dictionary<CustomRig, ModSetting<bool>> perPlayerToggles = new();
        
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
            ranOnce = false;

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

            string filePath = Path.Combine(MelonEnvironment.UserDataDirectory, "CustomAvatars", "Opponents");
            Directory.CreateDirectory(filePath);

            MelonCoroutines.Start(WaitAndApplyAvatars());

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
                avatarOptimizationParent.transform.SetParent(rigParent.transform);
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
        
        // Oh god this is so bad but it works
        public IEnumerator WaitAndApplyAvatars()
        {
            ApplyAvatars(true);
            yield return new WaitForEndOfFrame();
            ApplyAvatars(false);
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
            
            MelonCoroutines.Start(RigManager.LoadRigForPlayer(localPlayer, (rig) =>
            {
                if (!(bool)toggleLocal.SavedValue)
                    customRig.Apply(CustomRig.RigState.Original);
                else
                    customRig.Apply(CustomRig.RigState.Rigged);
                
                if (currentScene == "Gym" && rig != null)
                {
                    var previewController =
                        Calls.GameObjects.Gym.LOGIC.DressingRoom.PreviewPlayerController.Visuals.GetGameObject();

                    GameObject newRig = Calls.LoadAssetBundleGameObjectFromFile(
                        Path.Combine(MelonEnvironment.UserDataDirectory, "CustomAvatars", "rig"), "Rig");

                    newRig.name = "RIG - Preview Controller (Dressing Room)";
                    newRig.transform.SetParent(rigParent.transform, true);
                    
                    var smr = previewController.transform.GetChild(0).GetComponent<SkinnedMeshRenderer>();
                    var previewCustomRig = previewController.transform.parent.GetComponent<CustomRig>();
                    if (previewCustomRig != null)
                        GameObject.Destroy(previewCustomRig);
                    
                    previewCustomRig = previewController.transform.parent.gameObject.AddComponent<CustomRig>();
                    previewCustomRig.PlayerName = "Preview Controller (Dressing Room)";
                    previewCustomRig.CaptureOriginal("Preview Controller (Dressing Room)", false, smr, log);
                    previewCustomRig.CaptureRig(newRig);
                    previewCustomRig.Config = customRig.Config;
                
                    RigManager.ApplyRigToSMR(previewController.transform.GetChild(1), newRig, customRig: previewCustomRig);
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
            if (Input.GetKeyDown(KeyCode.R))
            {
                Initialize();

                if (Calls.Players.GetAllPlayers().Count > 1)
                {
                    var players = Calls.Players.GetAllPlayers().ToArray();
                    foreach (var rig in RigManager.rigs)
                    {
                        if (rig.Value?.IsLocal ?? true) return;
                    
                        if (File.Exists(rig.Value.AvatarFilePath))
                            File.Delete(rig.Value.AvatarFilePath);

                        var player = players.FirstOrDefault(p => p.Data.GeneralData.PlayFabMasterId == rig.Key);
                        if (player != null)
                            Patches.ApplyRig(player);
                    }
                }
            }
        }

        public void AddRigToList(CustomRig rig)
        {
            try
            {
                string playerName = string.IsNullOrEmpty(rig.PlayerName) ? "Unnamed Player" : rig.PlayerName;

                if (perPlayerToggles.Count == 0)
                    perPlayerHeader = mod.AddToList("<b><#FFB347>- Per Player Toggles", false, 0, "", new Tags { DoNotSave = true });

                var setting = mod.AddToList($"{playerName} ({rig.PlayerId})", true, 0, $"Toggles the avatar for {playerName}.", new Tags());

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

        public void OnUIInitialized()
        {
            mod.ModName = "<b><#6A5ACD>Custom Avatars</color></b>";
            mod.ModVersion = "1.0.0";
                
            mod.SetFolder("CustomAvatars");
            mod.AddToList("Description", "", "Allows custom avatars for you or specific people.", new Tags());
            
            mod.AddToList("<b><#114F11>- Avatar Visibility</color></b>", false, 0, "", new Tags { DoNotSave = true });
            toggleLocal = mod.AddToList("Toggle for Self", true, 0, "Toggles custom avatars for yourself.", new Tags());
            toggleOthers = mod.AddToList("Toggle for Others", true, 0, "Toggles custom avatars for others.", new Tags());

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
                string rigBundle = Path.Combine(MelonEnvironment.UserDataDirectory, "CustomAvatars", "rig");
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

                    var setting = perPlayerToggles.FirstOrDefault(s => s.Key.PlayerId == rig.Key).Value;
                    rig.Value?.Apply(enabled && (setting?.GetValue() ?? false) ? CustomRig.RigState.Rigged : CustomRig.RigState.Original);
                }
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
                    

                var hudType = Type.GetType("RumbleHud.Hud, RumbleHud");
                var method = hudType?.GetMethod("RegeneratePortraits", BindingFlags.Static | BindingFlags.Public);
                method?.Invoke(null, new object[] { currentScene == "Gym" });
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
        public string PlayerName;
        public string AvatarFilePath;

        public AvatarDescriptorExport Config;

        public GameObject Root;
        public GameObject PlayerRoot;

        private object blinkCoroutine;

        private Speaker remoteSpeaker;
        private AudioSource remoteAudioSource;
        
        private Recorder localRecorder;

        public float volumeMultiplier = 30f;

        public List<TriggerCollider> triggerColliders = new();
        public List<GrabbableObject> grabbableObjects = new();

        // --- Toggles for the two ---
        public Material OriginalMaterial;
        public Material OriginalVisualsMaterial;
        public Mesh OriginalMesh;
        public Transform[] OriginalBones;

        public Material RigMaterial;
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
                weight = Mathf.Clamp01(volume * volumeMultiplier) * 100f;
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

                weight = Mathf.Clamp01(volume * volumeMultiplier) * 100f;
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

        public void CaptureRig(GameObject rig)
        {
            Root = rig;

            var anim = rig.GetComponent<Animator>();
            if (anim == null) { Main.instance.LoggerInstance.Error($"Animator doesn't exist"); return; };
            if (!anim.isHuman)
            {
                Main.instance.LoggerInstance.Error($"Loaded rig not marked as humanoid.");
                return;
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
 
        public List<TriggerCollider> ParseTriggerCollidersRecursive(Transform root, Transform rig)
        {
            var colliders = new List<TriggerCollider>();

            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child.GetComponent<Collider>() != null)
                {
                    var triggerActions = new List<TriggerAction>();
                    
                    for (int j = 0; j < child.childCount; j++)
                    {
                        if (TriggerCollider.TryParseActionFromName(child.GetChild(j).name, out var action, rig.gameObject))
                            triggerActions.Add(action);
                    }

                    if (triggerActions.Count > 0)
                    {
                        var collider = child.gameObject.AddComponent<TriggerCollider>();
                        child.gameObject.layer = LayerMask.NameToLayer("InteractionBase");
                        collider.meshRenderer = MeshRenderer;
                        collider.triggerActions.AddRange(triggerActions);
                        
                        colliders.Add(collider);
                    }
                }
                
                colliders.AddRange(ParseTriggerCollidersRecursive(child, rig));
            }

            return colliders;
        }

        public void Apply(RigState state)
        {
            switch (state)
            {
                case RigState.Original:
                    MeshRenderer.materials = new Material[] { OriginalMaterial };
                    MeshRenderer.bones = OriginalBones;
                    MeshRenderer.sharedMesh = OriginalMesh;
                    
                    if (playerVisuals != null && IsLocal)
                        playerVisuals.NonHeadClippedMaterial = OriginalVisualsMaterial;
                    
                    if (blinkCoroutine != null) MelonCoroutines.Stop(blinkCoroutine); blinkCoroutine = null;
                    break;
                case RigState.Rigged:
                    MeshRenderer.material = RigMaterial;
                    MeshRenderer.bones = RigBones;
                    MeshRenderer.sharedMesh = RigMesh;
                    
                    if (playerVisuals != null && IsLocal)
                        playerVisuals.NonHeadClippedMaterial = RigVisualsMaterial;

                    if (Config != null)
                    {
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

                float blinkDuration = 0.05f;

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
            if (RigMaterial) Destroy(RigMaterial);
            if (blinkCoroutine != null) MelonCoroutines.Stop(blinkCoroutine); blinkCoroutine = null;
        }
    }
}

[RegisterTypeInIl2Cpp]
public class TriggerCollider : MonoBehaviour
{
    public SkinnedMeshRenderer meshRenderer;
    public List<TriggerAction> triggerActions = new();
    public HashSet<GameObject> runningTempToggles = new();

    public static bool TryParseActionFromName(string name, out TriggerAction action, GameObject rig = null)
    {
        action = default;
        
        if (string.IsNullOrEmpty(name)) { return false; }

        var parts = name.Split(':');
        if (parts.Length < 2)
            return false;

        if (!Enum.TryParse(parts[0], true, out TriggerActionType type))
            return false;

        string targetName = parts[1];

        Transform target = null;
        if (rig != null &&
            type is (TriggerActionType.ToggleOff | TriggerActionType.ToggleOn | TriggerActionType.ToggleTemp))
        {
            target = RigManager.FindDeepChild(rig.transform, targetName);
            if (target == null)
                Main.instance.LoggerInstance.Warning($"TriggerAction target '{targetName}' not found under rig '{rig.name}'");
        }
        
        float duration = (parts.Length >= 3 && float.TryParse(parts[2], out var d)) ? d : 0f;

        action = new TriggerAction(type, targetName, duration, target?.gameObject);
        return true;
    }

    public void OnTriggerEnter(Collider other)
    {
        MelonLogger.Msg($"OnTriggerEnter for collider {other.name}");
        
        foreach (var action in triggerActions)
        {
            if (action.Type is (TriggerActionType.ToggleOff | TriggerActionType.ToggleOn | TriggerActionType.ToggleTemp)
                && action.TargetGameObject == null)
            {
                Main.instance.LoggerInstance.Warning($"TriggerAction of type {action.Type} has null target on {gameObject.name}");
                continue;
            }

            switch (action.Type)
            {
                case TriggerActionType.ToggleOn:
                    action.TargetGameObject.SetActive(true);
                    break;
                
                case TriggerActionType.ToggleOff:
                    action.TargetGameObject.SetActive(false);
                    break;
                
                case TriggerActionType.ToggleTemp:
                    MelonCoroutines.Start(ToggleTemp(action));
                    break;
                
                case TriggerActionType.Blendshape:
                    int index = meshRenderer.sharedMesh.GetBlendShapeIndex(action.TargetName);
                    if (index == -1)
                        Main.instance.LoggerInstance.Warning($"Blendshape '{action.TargetName}' not found on {meshRenderer.name}");
                    else
                        meshRenderer.SetBlendShapeWeight(index, 100f);

                    break;
                
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    public IEnumerator ToggleTemp(TriggerAction action)
    {
        if (!runningTempToggles.Add(action.TargetGameObject))
            yield break;

        action.TargetGameObject.SetActive(!action.TargetGameObject.activeSelf);
        yield return new WaitForSeconds(action.Duration);
        action.TargetGameObject.SetActive(!action.TargetGameObject.activeSelf);
        
        runningTempToggles.Remove(action.TargetGameObject);
    }
}
