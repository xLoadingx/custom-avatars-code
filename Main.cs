using System.Collections;
using Il2CppRUMBLE.Players.Subsystems;
using UnityEngine;
using RumbleModdingAPI;
using MelonLoader;
using MelonLoader.Utils;
using RumbleModUI;

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

        public ModSetting<bool> toggleLocal;
        public ModSetting<bool> toggleOthers;

        public bool ranOnce = false;

        public Main()
        {
            instance = this;
        }

        // TODO:
        // Make player use the pose ghost material for when the avatar is downloading
        // Allow avatar downloading when encountering one that hasn't been downloaded.
        // - Check if the player has an uploaded avatar
        // ALlow uploading of avatar in ModUI
        
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
        }

        public void Initialize()
        {
            if (ranOnce)
                MelonCoroutines.Start(WaitThenApplyAvatars());
            else
                ApplyAvatars();
        }

        // Oh god this is so bad but it works.
        public IEnumerator WaitThenApplyAvatars()
        {
            ApplyAvatars();
            yield return new WaitForEndOfFrame();
            ApplyAvatars();
        }

        public void ApplyAvatars()
        {
            if ((bool)toggleLocal.SavedValue)
            {
                RigManager.ClearRigs();
                var rig = RigManager.LoadRigForPlayer(Calls.Players.GetLocalPlayer());

                if (currentScene == "Gym" && rig != null)
                {
                    var previewController =
                        Calls.GameObjects.Gym.LOGIC.DressingRoom.PreviewPlayerController.Visuals.GetGameObject();

                    GameObject newRig = Calls.LoadAssetBundleGameObjectFromFile(
                        Path.Combine(MelonEnvironment.UserDataDirectory, "CustomAvatars", "rig"), "Rig");
                    
                    RigManager.ApplyRigToSMR(previewController.transform.GetChild(0).GetComponent<SkinnedMeshRenderer>(), previewController.transform.GetChild(1), newRig);
                    RigManager.rigs["PreviewController"] = (previewController.transform.GetChild(1).GetChild(0), newRig);
                }
            }

            foreach (var player in Calls.Players.GetAllPlayers())
            {
                if (player != Calls.Players.GetLocalPlayer())
                    RigManager.LoadRigForPlayer(player);
            }
            
            sceneInitialized = true;
            ranOnce = true;
        }

        public override void OnFixedUpdate()
        {
            if (currentScene == "Loader") return;

            if (Input.GetKeyDown(KeyCode.R))
                Initialize();

            foreach (var rig in RigManager.rigs)
            {
                var rigRoot = rig.Value.Item2.transform;
            
                Transform pelvisTransform = rigRoot.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Bone_Pelvis");
                if (pelvisTransform == null) continue;
            
                var rb = pelvisTransform.gameObject.GetOrAddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                
                rb.MovePosition(rig.Value.Item1.position);
                rb.MoveRotation(rig.Value.Item1.rotation);
            }
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

            toggleOthers.SavedValueChanged += (sender, args) =>
                LoggerInstance.Msg(
                    $"Toggle Others set to {(bool)toggleOthers.SavedValue}. Will take effect on next scene load.");
            
            toggleLocal.SavedValueChanged += (sender, args) =>
                LoggerInstance.Msg(
                    $"Toggle Local set to {(bool)toggleLocal.SavedValue}. Will take effect on next scene load.");
            
            mod.GetFromFile();
            UI.instance.AddMod(mod);
        }
    }
}
