using System.Collections;
using System.Reflection;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Subsystems;
using MelonLoader;
using MelonLoader.Utils;
using Newtonsoft.Json;
using RumbleModdingAPI;
using UnityEngine;

namespace CustomAvatars;

public static class RigManager
{
    private static Main instance;
    public static readonly Dictionary<string, CustomRig> rigs = new();
    private static readonly HashSet<string> loadingPlayers = new();
    private static int activeLoads = 0;

    public static void Initialize(Main mainInstance)
    {
        instance = mainInstance;
    }

    public static void LogStatsForAvatar(GameObject rig)
    {
        var LoggerInstance = Main.instance.LoggerInstance;
        
        if (rig == null)
        {
            LoggerInstance.Warning("LogStatsForAvatar: rig is null");
            return;
        }
        
        var smrs = rig.GetComponentsInChildren<SkinnedMeshRenderer>();
        if (smrs.Length == 0)
        {
            LoggerInstance.Warning("LogStatsForAvatar: No SkinnedMeshRenderer or mesh found");
            return;
        }

        int vertexCount = 0;
        int materialCount = 0;
        int totalTextures = 0;
        int totalPasses = 0;
        bool hasHugeTextures = false;
        bool hasHeavyShaders = false;

        foreach (var smr in smrs)
        {
            if (smr.sharedMesh != null)
                vertexCount += smr.sharedMesh.vertexCount;

            var materials = smr.sharedMaterials;
            materialCount += materials.Length;

            foreach (var mat in materials)
            {
                if (mat == null) continue;

                foreach (var texName in mat.GetTexturePropertyNames())
                {
                    var tex = mat.GetTexture(texName);
                    if (tex != null)
                    {
                        totalTextures++;

                        if (tex.width >= 4096 || tex.height >= 4096)
                        {
                            hasHugeTextures = true;
                            LoggerInstance.Warning($"[Avatar Optimization] Huge texture on '{mat.name}' ({texName}): {tex.width}x{tex.height}");
                        }
                    }
                }

                int passCount = mat.shader?.passCount ?? 0;
                totalPasses += passCount;
                if (passCount > 7)
                    LoggerInstance.Warning($"[Avatar Optimiziation] Shader '{mat.shader.name}' has {passCount} passes.");

                if (mat.shader.name.ToLower().Contains("tessellation"))
                {
                    hasHeavyShaders = true;
                    Main.instance.LoggerInstance.Warning($"[Avatar Optimization] Shader '{mat.shader.name}' uses expensive features.");
                }
            }
        }

        ConsoleColor color;
        string rating;
        string warnings = "";
        if (vertexCount > 70000) warnings += " High vertex count;";
        if (materialCount > 5) warnings += " Too many materials;";
        if (hasHugeTextures) warnings += " Huge Textures;";
        if (hasHeavyShaders) warnings += " Heavy Shaders;";
        if (totalPasses >= 32) warnings += " Many Shader Passes;";
        
        if (vertexCount > 70000 || materialCount > 5 || hasHugeTextures || hasHeavyShaders)
        {
            color = ConsoleColor.Red;
            rating = "BAD";
        }
        else if (vertexCount > 50000 || materialCount > 3)
        {
            color = ConsoleColor.Yellow;
            rating = "MEDIUM";
        }
        else
        {
            color = ConsoleColor.Green;
            rating = "GOOD";
        }
        
        MelonLogger.MsgPastel(color, "-------------------------------------------------------------");
        LoggerInstance.MsgPastel(color, $"[Avatar Optimization] {rating}: {vertexCount} verts, {materialCount} mat(s), {totalTextures} texture(s).");
        LoggerInstance.MsgPastel(ConsoleColor.Yellow, $"WARNINGS: {(String.IsNullOrEmpty(warnings) ? "None" : warnings.TrimEnd(';'))}");
        MelonLogger.MsgPastel(color, "-------------------------------------------------------------");
    }

    public static void ClearRigs()
    {
        foreach (var rig in rigs.Values)
            GameObject.Destroy(rig.Root);
            
        rigs.Clear();
    }

    public static IEnumerator LoadRigForPlayer(Player player, Action<GameObject> onLoaded, bool log = true)
    {
        string playerID = player?.Data?.GeneralData?.PlayFabMasterId;
        if (string.IsNullOrEmpty(playerID))
        {
            MelonLogger.Warning("LoadRigForPlayer: playerID is null or empty");
            yield break;
        }

        if (!loadingPlayers.Add(playerID))
        {
            MelonLogger.Msg($"LoadRigForPlayer: player {playerID} is already loading");
            yield break;
        }

        while (activeLoads >= (int)Main.instance.maxConcurrentDownloads.SavedValue)
            yield return null;

        activeLoads++;

        try
        {
            bool isLocal = player == Calls.Players.GetLocalPlayer();

            string opponentPath = Path.Combine(MelonEnvironment.UserDataDirectory, "CustomAvatars", "Opponents");
            if (!Directory.Exists(opponentPath)) Directory.CreateDirectory(opponentPath);

            if (!isLocal && !File.Exists(Path.Combine(opponentPath, playerID)))
            {
                if (log)
                    Main.instance.LoggerInstance.Msg($"Downloading avatar for path {opponentPath}");
                yield return MelonCoroutines.Start(
                    RemoteAvatarLoader.DownloadToFile(playerID, Path.Combine(opponentPath, playerID)));
            }

            string basePath = Path.Combine(MelonEnvironment.UserDataDirectory, "CustomAvatars");
            string rigPath = isLocal
                ? Directory.GetFiles(basePath).FirstOrDefault()
                : Path.Combine(basePath, "Opponents", playerID);

            if (string.IsNullOrEmpty(rigPath) || !File.Exists(rigPath))
            {
                Main.instance.LoggerInstance.Warning($"No custom avatar found for {(isLocal ? "you" : player?.Data?.GeneralData?.PublicUsername ?? "unknown")} at {rigPath}");
                yield break;
            }

            AssetBundle rigBundle = Calls.LoadAssetBundleFromFile(rigPath);
            GameObject rigPrefab = rigBundle.LoadAsset<GameObject>("Rig");
            if (rigPrefab == null)
            {
                Main.instance.LoggerInstance.Error(
                    $"Failed to load 'Rig' GameObject for {(isLocal ? "local player" : player?.Data?.GeneralData?.PublicUsername ?? "unknown")} from path {rigPath}");
                yield break;
            }

            var rigInstance = GameObject.Instantiate(rigPrefab);
            rigInstance.name = $"RIG - {playerID}";
            rigInstance.transform.SetParent(Main.instance.rigParent.transform, true);

            if (player?.Controller == null)
            {
                Main.instance.LoggerInstance.Error("player.Controller is null");
                yield break;
            }

            if (player.Controller.gameObject == null)
            {
                Main.instance.LoggerInstance.Error("player.Controller.gameObject is null");
                yield break;
            }

            var customRig = player.Controller.gameObject.GetOrAddComponent<CustomRig>();
            if (customRig == null)
            {
                Main.instance.LoggerInstance.Error("Failed to get or add CustomRig component");
                yield break;
            }

            try
            {
                customRig.grabbableObjects =
                    customRig.ParseGrabbableObjectsRecursive(rigInstance.transform, player);
            }
            catch (Exception ex)
            {
                Main.instance.LoggerInstance.Error("Exception while parsing grabbable objects: " + ex);
            }

            rigs[playerID] = customRig;

            TextAsset jsonAsset = rigBundle.LoadAsset<TextAsset>("Config");

            if (jsonAsset == null)
            {
                Main.instance.LoggerInstance.Warning(
                    "Config.json not found in rig bundle. Make sure your avatar has a AvatarDescriptor that was exported.");
            }
            else
            {
                try
                {
                    AvatarDescriptorExport config = JsonConvert.DeserializeObject<AvatarDescriptorExport>(jsonAsset.text);
                    customRig.Config = config;
                }
                catch (Exception ex)
                {
                    Main.instance.LoggerInstance.Error($"Failed to parse avatar config: {ex.Message}");
                }
            }

            if (customRig.Config != null)
            {
                foreach (var blendshape in customRig.Config.defaultBlendshapes)
                {
                    if (blendshape.index >= 0)
                        customRig.MeshRenderer.SetBlendShapeWeight(blendshape.index, blendshape.weight);
                    else
                        Main.instance.LoggerInstance.Warning($"Blendshape '{blendshape.name}' not found on mesh '{customRig.MeshRenderer.sharedMesh.name}'");
                }
            }

            rigBundle.Unload(false);

            if (log)
                Main.instance.LoggerInstance.Msg($"Loading rig for player {playerID}");

            if (rigInstance != null && (
                    ((bool)Main.instance.logAvatarStats.SavedValue && isLocal)
                    || ((bool)Main.instance.logOtherAvatarStats.SavedValue && !isLocal))
               )
                LogStatsForAvatar(rigInstance);

            ApplyRigToPlayer(player, rigInstance);

            if (!isLocal)
            {
                string path = Path.Combine(basePath, "Opponents", playerID);
                if (File.Exists(path))
                    File.Delete(path);

                if ((bool)Main.instance.toggleOthers.SavedValue)
                    player.Controller.GetComponent<CustomRig>().Apply(CustomRig.RigState.Original);
            }

            var camObj = GameObject.Find($"RumbleHud_{playerID}_portraitCamera");
            var cam = camObj?.GetComponent<Camera>();
            if (cam != null)
                cam.nearClipPlane = 0.01f;

            var hudType = Type.GetType("RumbleHud.Hud, RumbleHud");
            var method = hudType?.GetMethod("RegeneratePortraits", BindingFlags.Static | BindingFlags.Public);
            method?.Invoke(null, new object[] { Main.instance.currentScene == "Gym" });

            onLoaded?.Invoke(rigInstance);
        }
        finally
        {
            activeLoads--;
            loadingPlayers.Remove(playerID);
        }
    }

    public static void ApplyRigToPlayer(Player player, GameObject rig, bool log = true)
    {
        if (player == null || rig == null) return;
        
        player.Controller.GetComponent<CustomRig>().CaptureRig(rig);

        string playerUsername = player.Data.GeneralData.PublicUsername.TrimString();
        var playerRenderer = player.Controller.transform.GetChild(1).GetChild(0).GetComponent<SkinnedMeshRenderer>();
        var rigRenderer = rig.GetComponentInChildren<SkinnedMeshRenderer>(true);

        if (playerRenderer == null || rigRenderer == null) return;

        var playerRigRoot = player.Controller.transform.GetChild(1).GetChild(1);
        ApplyRigToSMR(playerRigRoot, rig, player.Controller.GetComponent<CustomRig>(), visuals: player.Controller.GetSubsystem<PlayerVisuals>());
        
        
        if (log)
            instance.LoggerInstance.Msg($"Applied custom rig to player {playerUsername}.");
    }

    public static void ApplyRigBones(Transform rigRoot, Transform playerRigRoot)
    {
        var playerBones = new Dictionary<string, Transform>();
        foreach (var bone in playerRigRoot.GetComponentsInChildren<Transform>(true))
            playerBones[bone.name] = bone;
        
        foreach (var rigBone in rigRoot.GetComponentsInChildren<Transform>(true))
        {
            rigBone.gameObject.layer = LayerMask.NameToLayer("Default");

            if (playerBones.TryGetValue(rigBone.name, out var playerBone))
            {
                rigBone.SetParent(playerBone, true);
                rigBone.localPosition = Vector3.zero;
                rigBone.localRotation = Quaternion.identity;

                rigBone.gameObject.AddComponent<CustomRigBone>();
            }
        }
    }

    public static void ApplyRigToSMR(Transform skeletonRoot, GameObject rig, CustomRig customRig = null, SkinnedMeshRenderer renderer = null, PlayerVisuals visuals = null)
    {
        void ApplyRig(Transform customRigTransform, SkinnedMeshRenderer rigRenderer, SkinnedMeshRenderer playerRenderer, Material originalMaterial)
        {
            if (customRig == null)
            {
                Main.instance.LoggerInstance.Error("customRig is null");
                return;
            }
            if (skeletonRoot == null)
            {
                Main.instance.LoggerInstance.Error("skeletonRoot is null");
                return;
            }
            if (playerRenderer == null)
            {
                Main.instance.LoggerInstance.Error("playerRenderer is null");
                return;
            }
            if (rigRenderer == null)
            {
                Main.instance.LoggerInstance.Error("rigRenderer is null");
                return;
            }

            var bones = skeletonRoot.GetComponentsInChildren<CustomRigBone>(true);
            if (bones != null)
            {
                foreach (var customBone in bones)
                {
                    if (customBone != null && customBone.gameObject != null)
                        UnityEngine.Object.DestroyImmediate(customBone.gameObject);
                }
            }

            playerRenderer.enabled = false;

            ApplyRigBones(customRigTransform, skeletonRoot);

            customRig.triggerColliders = customRig.ParseTriggerCollidersRecursive(skeletonRoot, skeletonRoot);

            if (rigRenderer.sharedMesh != null)
                playerRenderer.sharedMesh = rigRenderer.sharedMesh;
            else
                Main.instance.LoggerInstance.Warning("rigRenderer.sharedMesh is null");

            if (rigRenderer.bones is { Length: > 0 })
            {
                playerRenderer.bones = rigRenderer.bones;
                customRig.RigBones = rigRenderer.bones;
            }
            else
            {
                Main.instance.LoggerInstance.Warning("rigRenderer.bones array is null or empty");
            }

            if (playerRenderer.material == null)
            {
                Main.instance.LoggerInstance.Error("playerRenderer.material is null");
                return;
            }
            if (rigRenderer.material == null)
            {
                Main.instance.LoggerInstance.Error("rigRenderer.material is null");
                return;
            }

            var rigMats = rigRenderer.materials;
            var newMats = new Material[rigMats.Length];

            if (customRig.Config != null)
            {
                for (int i = 0; i < rigMats.Length; i++)
                {
                    if (customRig.Config.playerShaderSlots.Contains(i))
                    {
                        newMats[i] = new Material(customRig.OriginalMaterial);
                        var baseMapTex = rigMats[i].GetTexture("_BaseMap");
                        if (baseMapTex != null)
                        {
                            newMats[i].SetTexture("_ColorAtlas", baseMapTex);
                            
                            if (visuals != null && visuals.NonHeadClippedMaterial != null && customRig.Config.bodyShaderSlot == i)
                                visuals.NonHeadClippedMaterial.SetTexture("_ColorAtlas", baseMapTex);
                        }
                        else
                        {
                            Main.instance.LoggerInstance.Warning($"_BaseMap texture is null for material {rigMats[i].name}");
                        }
                    }
                    else
                    {
                        newMats[i] = new Material(rigMats[i]);
                    }
                }
                
                foreach (var blendshape in customRig.Config.defaultBlendshapes)
                {
                    if (blendshape.index >= 0)
                        customRig.MeshRenderer.SetBlendShapeWeight(blendshape.index, blendshape.weight);
                    else
                        Main.instance.LoggerInstance.Warning($"Blendshape '{blendshape.name}' not found on mesh '{customRig.MeshRenderer.sharedMesh.name}'");
                }
            }
            else
            {
                newMats = rigMats;
            }

            playerRenderer.materials = newMats;

            for (int i = 0; i < playerRenderer.materials.Length; i++)
            {
                var mat = playerRenderer.materials[i];

                if (mat.HasFloat("_IsLocal"))
                    mat.SetFloat("_IsLocal", customRig.IsLocal ? 1f : 0f);
            }

            if (customRig != null)
            {
                if (playerRenderer.material != null)
                {
                    customRig.RigMaterial = new Material(playerRenderer.material);
                    customRig.RigMaterial.hideFlags = HideFlags.DontUnloadUnusedAsset | HideFlags.HideAndDontSave;
                }
                else
                {
                    Main.instance.LoggerInstance.Warning("playerRenderer.material is null while assigning to customRigComp");
                }

                if (rigRenderer.sharedMesh != null)
                {
                    customRig.RigMesh = UnityEngine.Object.Instantiate(rigRenderer.sharedMesh);
                    customRig.RigMesh.hideFlags = HideFlags.DontUnloadUnusedAsset | HideFlags.HideAndDontSave;
                }
                else
                {
                    Main.instance.LoggerInstance.Warning("rigRenderer.sharedMesh is null while assigning to customRigComp");
                }
            }

            if (rigRenderer.gameObject != null)
                GameObject.Destroy(rigRenderer.gameObject);

            playerRenderer.enabled = true;
        }

        if (skeletonRoot == null || rig == null) return;

        var rigRenderer = rig.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (rigRenderer == null) return;

        if (renderer == null)
        {
            if (customRig != null && customRig.MeshRenderer != null)
                ApplyRig(customRig.Root.transform, rigRenderer, customRig.MeshRenderer, customRig.OriginalMaterial);
        }
        else
        {
            ApplyRig(rig.transform, rigRenderer, renderer, renderer.material);
        }
    }

    public static Transform FindDeepChild(Transform parent, string name)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);

            if (child.name == name)
                return child;

            var result = FindDeepChild(child, name);
            if (result != null)
                return result;
        }

        return null;
    }
}

[Serializable]
public class AvatarDescriptorExport
{
    public enum BlinkType
    {
        None,
        Single,
        LeftRight
    }
    
    public List<int> playerShaderSlots;
    public int bodyShaderSlot;
    public List<BlendshapeDefault> defaultBlendshapes;
    public int blinkType;
    public int blinkBlendshape;
    public int blinkLeftBlendshape;
    public int blinkRightBlendshape;
    public int jawOpenBlendshape;
    public bool autoBlink;
    public Vector2 blinkInterval;
}

[Serializable]
public class BlendshapeDefault
{
    public string name;
    public int index;
    public float weight;
}

[RegisterTypeInIl2Cpp]
public class GrabbableObject : MonoBehaviour
{
    public Transform leftHand;
    public Transform rightHand;
    public Player player;

    public Vector3 originalPosition;
    public Quaternion originalRotation;
    public Transform originalParent;

    private Transform currentHand = null;
    private bool isGrabbed = false;
    
    private bool isLeftTouching = false;
    private bool isRightTouching = false;
    private bool wasLeftGripHeldLastFrame = false;
    private bool wasRightGripHeldLastFrame = false;

    public IEnumerator Init(Player Player)
    {
        if (Player == null) yield break;
        
        player = Player;
        originalParent = transform.parent;
        originalPosition = transform.localPosition;
        originalRotation = transform.localRotation;
        
        while (player.Controller == null)
            yield return null;

        leftHand = player.Controller.transform.GetChild(2).GetChild(1).GetChild(1);
        rightHand = player.Controller.transform.GetChild(2).GetChild(2).GetChild(1);
    }

    private void Update()
    {
        bool leftGrip = player.Controller.GetSubsystem<PlayerHandPresence>().leftHandGripInput.ReadValue<float>() > 0.5f;
        bool rightGrip = player.Controller.GetSubsystem<PlayerHandPresence>().rightHandGripInput.ReadValue<float>() > 0.5f;
        
        if (isGrabbed)
        {
            bool stillGripping = 
                (currentHand == leftHand && Calls.ControllerMap.LeftController.GetGrip() > 0.5f) ||
                (currentHand == rightHand && Calls.ControllerMap.RightController.GetGrip() > 0.5f);

            if (!stillGripping)
                Release();
        }
        else
        {
            if (isLeftTouching && leftGrip && !wasLeftGripHeldLastFrame)
                Grab(leftHand);

            else if (isRightTouching && rightGrip && !wasRightGripHeldLastFrame)
                Grab(rightHand);
        }
        
        wasLeftGripHeldLastFrame = leftGrip;
        wasRightGripHeldLastFrame = rightGrip;
    }

    private void Grab(Transform hand)
    {
        isGrabbed = true;
        currentHand = hand;

        transform.SetParent(hand.transform);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
    }

    private void Release()
    {
        isGrabbed = false;
        isLeftTouching = false;
        isRightTouching = false;
        currentHand = null;
        transform.SetParent(originalParent);
        transform.localPosition = originalPosition;
        transform.localRotation = originalRotation;
    }

    private void OnTriggerEnter(Collider other)
    {
        MelonLogger.Msg(other.name);
        
        if (other.transform == leftHand) isLeftTouching = true;
        else if (other.transform == rightHand) isRightTouching = true;
    }

    private void OnTriggerExit(Collider other)
    {
        MelonLogger.Msg(other.name);
        
        if (other.transform == leftHand) isLeftTouching = false;
        else if (other.transform == rightHand) isRightTouching = false;
    }
}

[Flags]
public enum TriggerActionType
{
    Blendshape,
    ToggleOn,
    ToggleOff,
    Toggle,
    ToggleTemp
}

public struct TriggerAction
{
    public TriggerActionType Type;
    public string TargetName;
    public GameObject TargetGameObject;
    public float Duration;

    public TriggerAction(TriggerActionType type, string targetName, float duration, GameObject targetGameObject = null)
    {
        Type = type;
        TargetName = targetName;
        Duration = duration;
        TargetGameObject = targetGameObject;
    }
}