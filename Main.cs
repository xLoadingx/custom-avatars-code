using System.Collections;
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
            rigParent = null;
        }

        public void Initialize()
        {
            if (ranOnce)
                MelonCoroutines.Start(WaitThenApplyAvatars());
            else
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
                interactionButton.onPressed.AddListener((UnityAction)(() => { Initialize(); }));

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
            ApplyAvatars();
            yield return new WaitForEndOfFrame();
            ApplyAvatars(false);
        }

        public void ApplyAvatars(bool log = true)
        {
            RigManager.ClearRigs();
            
            var localPlayer = Calls.Players.GetLocalPlayer();

            if (rigParent == null)
                rigParent = new GameObject("Rigs");
            
            if ((bool)toggleLocal.SavedValue)
            {
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
                        var previewController =
                            Calls.GameObjects.Gym.LOGIC.DressingRoom.PreviewPlayerController.Visuals.GetGameObject();
                
                        GameObject newRig = Calls.LoadAssetBundleGameObjectFromFile(
                            Path.Combine(MelonEnvironment.UserDataDirectory, "CustomAvatars", "rig"), "Rig");

                        newRig.name = "RIG - Preview Controller (Dressing Room)";
                        newRig.transform.SetParent(rigParent.transform, true);

                        var previewCustomRig = previewController.AddComponent<CustomRig>();
                        var smr = previewController.transform.GetChild(0).GetComponent<SkinnedMeshRenderer>();
                        previewCustomRig.CaptureOriginal("Preview Controller (Dressing Room)", true, smr);
                        previewCustomRig.CaptureRig(newRig);
                    
                        RigManager.ApplyRigToSMR(previewController.transform.GetChild(1), newRig, renderer: smr);
                        RigManager.rigs["PreviewController"] = previewCustomRig;
                    }
                }, log));
            }

            if (currentScene == "Gym" && poseGhostMaterial == null)
                poseGhostMaterial = Calls.GameObjects.Gym.LOGIC.Heinhouserproducts.
                    MoveLearning.Ghost.Ghost_.Visuals.
                    Poseghostbody.GetGameObject().
                    GetComponent<SkinnedMeshRenderer>().material;

            foreach (var player in Calls.Players.GetAllPlayers())
            {
                if (player != localPlayer)
                
                MelonCoroutines.Start(
                    RemoteAvatarLoader.PlayerHasAvatar(player.Data.GeneralData.PlayFabMasterId, hasAvatar =>
                    {
                        if (!hasAvatar) return;
            
                        var visuals = player.Controller.GetSubsystem<PlayerVisuals>();
                        
                        var customRig = player.Controller.gameObject.AddComponent<CustomRig>();
                        customRig.CaptureOriginal(player.Data.GeneralData.PlayFabMasterId, false, visuals.renderer);
                        
                        visuals.renderer.material = poseGhostMaterial;
                        MelonCoroutines.Start(RigManager.LoadRigForPlayer(player, null, log));
                    })
                );
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

            if (Input.GetKeyDown(KeyCode.R))
                Initialize();
            
            foreach (var rig in RigManager.rigs)
            {
                var customRig = rig.Value;
                if (customRig == null) continue;
                
                SetBonePair(customRig.Pelvis, customRig.PlayerPelvis, customRig.PelvisRb);
                SetBonePair(customRig.Head, customRig.PlayerHead, customRig.HeadRb);
            }
        }

        bool IsValidAssetBundle(string path)
        {
            if (!File.Exists(path)) return false;

            var b = Calls.LoadAssetBundleFromFile(path);
            
            try
            {
                var rigPath = b.GetAllAssetNames()
                    .FirstOrDefault(n => n.Replace('\\', '/')
                    .EndsWith("/rig.prefab", StringComparison.OrdinalIgnoreCase));
                if (rigPath == null) return false;

                var rig = b.LoadAsset<GameObject>(rigPath);
                return rig != null;
            }
            catch { return false; }
            finally { b.Unload(true); }
        }

        public void OnUIInitialized()
        {
            var mod = new Mod
            {
                ModName = "Custom Avatars",
                ModVersion = "1.0.0"
            };
            mod.AddToList("Description", "", "Allows custom avatars for you or specific people.", new Tags());
            toggleOthers = mod.AddToList("Toggle for Others", true, 0, "Toggles custom avatars for others.", new Tags());
            toggleLocal = mod.AddToList("Toggle for Self", true, 0, "Toggles custom avatars for yourself.", new Tags());
            downloadLimitMB = mod.AddToList("Max File Download Size", 50, "The max download size for other avatars in MB.", new Tags());
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
                LoggerInstance.Msg(
                    $"Toggle Others set to {(bool)toggleOthers.SavedValue}. Will take effect on next scene load.");
            
            toggleLocal.SavedValueChanged += (sender, args) =>
                LoggerInstance.Msg(
                    $"Toggle Local set to {(bool)toggleLocal.SavedValue}. Will take effect on next scene load.");
            
            downloadLimitMB.SavedValueChanged += (sender, args) =>
                LoggerInstance.Msg(
                    $"Max File Download Size set to {(int)downloadLimitMB.SavedValue}.");
            
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
        
        public Transform Pelvis;
        public Transform PlayerPelvis;
        public Rigidbody PelvisRb;
        
        public Transform Head;
        public Transform PlayerHead;
        public Rigidbody HeadRb;
        
        public Transform LeftHand;
        public Transform RightHand;

        public Material OriginalMaterial;
        public Mesh OriginalMesh;

        public Material RigMaterial;
        public Mesh RigMesh;

        public SkinnedMeshRenderer MeshRenderer;

        public void CaptureOriginal(string playerId, bool isLocal, SkinnedMeshRenderer renderer)
        {
            PlayerId = playerId;
            IsLocal = isLocal;

            OriginalMesh = Instantiate(renderer.sharedMesh);
            OriginalMesh.hideFlags = HideFlags.DontUnloadUnusedAsset | HideFlags.HideAndDontSave;

            OriginalMaterial = Instantiate(renderer.material);
            OriginalMaterial.hideFlags = HideFlags.DontUnloadUnusedAsset | HideFlags.HideAndDontSave;
            
            PlayerPelvis = renderer.bones.FirstOrDefault(b => b.name == "Bone_Pelvis");
            PlayerHead = renderer.bones.FirstOrDefault(b => b.name == "Bone_Head");

            MeshRenderer = renderer;
        }

        public void CaptureRig(GameObject rig)
        {
            Root = rig;

            var smr = rig.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr == null) return;

            var pelvisBone = smr.bones.FirstOrDefault(b => b.name == "Bone_Pelvis");
            if (pelvisBone != null)
            {
                Pelvis = pelvisBone;
                PelvisRb = pelvisBone.gameObject.GetOrAddComponent<Rigidbody>();
            }
                

            var headBone = smr.bones.FirstOrDefault(b => b.name == "Bone_Head");
            if (headBone != null)
            {
                Head = headBone;
                HeadRb = headBone.gameObject.GetOrAddComponent<Rigidbody>();
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
