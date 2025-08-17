using System.Collections;
using CustomAvatars;
using Il2CppPhoton.Pun;
using Il2CppRUMBLE.Interactions.InteractionBase;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Subsystems;
using Il2CppRUMBLE.Utilities.SmartLocalization;
using Il2CppSmartLocalization.Editor;
using Il2CppTMPro;
using UnityEngine;
using RumbleModdingAPI;
using MelonLoader;
using MelonLoader.Utils;
using RumbleModUI;
using UnityEngine.Events;
using Main = CustomAvatars.Main;

[assembly: MelonInfo(typeof(CustomAvatars.Main), "CustomAvatars", "1.0.0", "ERROR")]
[assembly: MelonGame("Buckethead Entertainment", "RUMBLE")]
[assembly: MelonColor(255, 255, 0, 0)]

namespace CustomAvatars
{
    public static class GameObjectExtensions
    {
        public static T GetOrAddComponent<T>(this GameObject gameObject) where T : Component
        {
            T component = gameObject.GetComponent<T>();
            if (component == null)
            {
                component = gameObject.AddComponent<T>();
            }
            return component;
        }
    }

    public static class StringExtensions
    {
        public static string TrimString(this string str) => System.Text.RegularExpressions.Regex.Replace(str, "<.*?>|\\(.*?\\)|[^a-zA-Z0-9_ ]", "").Trim();
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
    public class CustomRigBone : MonoBehaviour {}

    public class Main : MelonMod
    {
        public string currentScene = "Loader";
        public bool sceneInitialized = false;
        public static Main instance;

        public GameObject rigParent;

        public ModSetting<bool> toggleLocal;
        public ModSetting<bool> toggleOthers;
        public ModSetting<int> downloadLimitMB;
        public ModSetting<int> maxConcurrentDownloads;
        
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
        // Make player shader an option based on the SMR
        
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
                catch (IOException e) { }
                catch (UnauthorizedAccessException e) { }
            }
        }

        public void Initialize()
        {
            RigManager.ClearRigs();

            string filePath = Path.Combine(MelonEnvironment.UserDataDirectory, "CustomAvatars", "Opponents");
            Directory.CreateDirectory(filePath);

            ApplyAvatars();

            if (currentScene == "Gym" && !sceneInitialized)
            {
                GameObject tryOutModePanel = Calls.GameObjects.Gym.LOGIC.DressingRoom.Controlpanel.Controls
                    .Frameattachment.TryOutModePanel.GetGameObject();

                tryOutModePanel.transform.localPosition = new Vector3(-0.1164f, 0.1962f, -0.1014f);
                
                GameObject RefreshAvatar = GameObject.Instantiate(tryOutModePanel);
                RefreshAvatar.transform.SetParent(tryOutModePanel.transform.parent, false);
                RefreshAvatar.name = "Refresh Avatar Panel";
                RefreshAvatar.transform.localPosition = new Vector3(0.1069f, 0.1962f, -0.1014f);
                
                InteractionButton interactionButton = RefreshAvatar.transform.GetChild(1).GetChild(0).GetComponent<InteractionButton>();
                interactionButton.onPressed.RemoveAllListeners();
                interactionButton.onPressed.AddListener((UnityAction)(() => { if ((bool)toggleLocal.SavedValue) Initialize(); }));

                TextMeshPro text = RefreshAvatar.transform.GetChild(1).GetChild(1).GetComponent<TextMeshPro>();
                UnityEngine.Object.Destroy(text.transform.GetComponent<LocalizedTextTMPro>());
                text.m_text = "Refresh Avatar";
                text.fontSize = 0.25f;
                text.ForceMeshUpdate();
            }
            
            sceneInitialized = true;
        }

        // Oh god this is so bad but it works.
        public IEnumerator WaitThenApplyAvatars()
        {
            ApplyAvatars(false);
            yield return new WaitForEndOfFrame();
            ApplyAvatars(true);
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
                    localPlayer.Controller.GetSubsystem<PlayerVisuals>().renderer
                );
            }
            
            MelonCoroutines.Start(RigManager.LoadRigForPlayer(localPlayer, (rig) =>
            {
                if (currentScene == "Gym" && rig != null)
                {
                    if (!(bool)toggleLocal.SavedValue)
                        customRig.Apply(CustomRig.RigState.Original);
                    
                    var previewController =
                        Calls.GameObjects.Gym.LOGIC.DressingRoom.PreviewPlayerController.Visuals.GetGameObject();
            
                    GameObject newRig = Calls.LoadAssetBundleGameObjectFromFile(
                        Path.Combine(MelonEnvironment.UserDataDirectory, "CustomAvatars", "rig"), "Rig");

                    newRig.name = "RIG - Preview Controller (Dressing Room)";
                    newRig.transform.SetParent(rigParent.transform, true);
                    
                    var smr = previewController.transform.GetChild(0).GetComponent<SkinnedMeshRenderer>();
                    var previewCustomRig = previewController.GetComponent<CustomRig>();
                    if (previewCustomRig == null)
                    {
                        previewCustomRig = previewController.AddComponent<CustomRig>();
                        previewCustomRig.CaptureOriginal("Preview Controller (Dressing Room)", false, smr);
                    }
                    previewCustomRig.CaptureRig(newRig);
                
                    RigManager.ApplyRigToSMR(previewController.transform.GetChild(1), newRig, customRig: previewCustomRig);
                    RigManager.rigs["Preview Controller"] = previewCustomRig;
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
            void SetBonePair(Transform target, Transform source, Rigidbody targetRb)
            {
                if (target == null || source == null) return;
                
                targetRb.isKinematic = true;
                targetRb.interpolation = RigidbodyInterpolation.Interpolate;
                targetRb.MovePosition(source.position);
                targetRb.MoveRotation(source.rotation);
            }
            
            if (currentScene == "Loader") return;

            if (rigParent && !rigParent.activeSelf)
                rigParent.SetActive(true);

            if (Input.GetKeyDown(KeyCode.R))
                Initialize();
        }

        bool IsValidAssetBundle(string path)
        {
            if (!File.Exists(path)) return false;
            
            return true;
        }

        public void OnUIInitialized()
        {
            var mod = new Mod
            {
                ModName = "CustomAvatars",
                ModVersion = "1.0.0"
            };
            mod.SetFolder("CustomAvatars");
            mod.AddToList("Description", "", "Allows custom avatars for you or specific people.", new Tags());
            toggleOthers = mod.AddToList("Toggle for Others", true, 0, "Toggles custom avatars for others.", new Tags());
            toggleLocal = mod.AddToList("Toggle for Self", true, 0, "Toggles custom avatars for yourself.", new Tags());
            downloadLimitMB = mod.AddToList("Max File Download Size", 50, "The max download size for other avatars in MB.", new Tags());
            maxConcurrentDownloads = mod.AddToList("Max Concurrent Downloads", 3, "The maximum number of downloads that can be ran at the same time.", new Tags());
            UploadAvatar = mod.AddToList("Upload Avatar", false, 0, "Uploads avatar when the button is clicked.",
                new Tags
                {
                    DoNotSave = true
                });

            UploadAvatar.CurrentValueChanged += (sender, args) =>
            {
                string rigBundle = Path.Combine(MelonEnvironment.UserDataDirectory, "CustomAvatars", "rig");
                string masterId = Calls.Players.GetLocalPlayer().Data.GeneralData.PlayFabMasterId;
                if (!IsValidAssetBundle(rigBundle))
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
                LoggerInstance.Msg($"Toggle Others set to {enabled}.");
            };
            
            toggleLocal.SavedValueChanged += (sender, args) =>
            {
                bool enabled = (bool)toggleLocal.Value;
                LoggerInstance.Msg($"Toggle Local set to {enabled}.");

                var localId = Calls.Players.GetLocalPlayer().Data.GeneralData.PlayFabMasterId;
                foreach (var kvp in RigManager.rigs)
                {
                    if (kvp.Key == localId)
                    {
                        kvp.Value.Apply(enabled
                            ? CustomRig.RigState.Rigged
                            : CustomRig.RigState.Original);
                    }
                }
            };
            
            downloadLimitMB.SavedValueChanged += (sender, args) =>
                LoggerInstance.Msg(
                    $"Max File Download Size set to {(int)downloadLimitMB.Value}.");

            maxConcurrentDownloads.SavedValueChanged += (sender, args) =>
                LoggerInstance.Msg(
                    $"Max Concurrent Downloads set to {(int)maxConcurrentDownloads.Value}.");
            
            mod.GetFromFile();
            UI.instance.AddMod(mod);
        }
    }

    [RegisterTypeInIl2Cpp]
    public class CustomRig : MonoBehaviour
    {
        public string PlayerId;
        public bool IsLocal;

        public GameObject Root;
        public GameObject PlayerRoot;

        public List<TriggerCollider> triggerColliders = new();
        public List<GrabbableObject> grabbableObjects = new();

        // --- Toggles for the two ---
        public Material OriginalMaterial;
        public Mesh OriginalMesh;
        public Transform[] OriginalBones;

        public Material RigMaterial;
        public Mesh RigMesh;
        public Transform[] RigBones;

        public enum RigState
        {
            Original,
            Rigged
        }
        
        public SkinnedMeshRenderer MeshRenderer;

        public void CaptureOriginal(string playerId, bool isLocal, SkinnedMeshRenderer renderer)
        {
            if (renderer == null)
            {
                Main.instance.LoggerInstance.Warning($"CaptureOriginal: Renderer is null for player {playerId ?? "Unknown"}, skipping.");
                return;
            }

            if (renderer.sharedMesh == null)
            {
                Main.instance.LoggerInstance.Warning($"CaptureOriginal: SharedMesh is null for player {playerId ?? "Unknown"}, skipping.");
                return;
            }

            PlayerId = playerId;
            IsLocal = isLocal;
            PlayerRoot = renderer.transform.parent.GetChild(1).gameObject;

            OriginalMesh = Instantiate(renderer.sharedMesh);
            OriginalMesh.hideFlags = HideFlags.DontUnloadUnusedAsset | HideFlags.HideAndDontSave;
            
            if (renderer.material != null)
            {
                OriginalMaterial = Instantiate(renderer.material);
                OriginalMaterial.hideFlags = HideFlags.DontUnloadUnusedAsset | HideFlags.HideAndDontSave;
            }
            else
            {
                Main.instance.LoggerInstance.Warning("CaptureOriginal: Renderer material is null.");
            }

            OriginalBones = renderer.bones ?? Array.Empty<Transform>();

            MeshRenderer = renderer;
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
                    MeshRenderer.material = OriginalMaterial;
                    MeshRenderer.bones = OriginalBones;
                    MeshRenderer.sharedMesh = OriginalMesh;
                    break;
                case RigState.Rigged:
                    MeshRenderer.material = RigMaterial;
                    MeshRenderer.bones = RigBones;
                    MeshRenderer.sharedMesh = RigMesh;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(state), state, null);
            }
        }

        public void OnDestroy()
        {
            if (OriginalMesh) Destroy(OriginalMesh);
            if (OriginalMaterial) Destroy(OriginalMaterial);
            if (RigMesh) Destroy(RigMesh);
            if (RigMaterial) Destroy(RigMaterial);
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
        if (runningTempToggles.Contains(action.TargetGameObject))
            yield break;
        
        runningTempToggles.Add(action.TargetGameObject);
        
        action.TargetGameObject.SetActive(!action.TargetGameObject.activeSelf);
        yield return new WaitForSeconds(action.Duration);
        action.TargetGameObject.SetActive(!action.TargetGameObject.activeSelf);
        
        runningTempToggles.Remove(action.TargetGameObject);
    }
}
