using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Il2CppRootMotion.FinalIK;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Subsystems;
using MelonLoader;
using MelonLoader.Utils;
using Newtonsoft.Json;
using RumbleModdingAPI.RMAPI;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CustomAvatars;

public class RigLoader
{
    private static Transform referenceSkeleton;
    private static Material loadingMaterial;

    private static MelonLogger.Instance logger => Melon<Main>.Logger;

    public static void EnsureReferenceObjects()
    {
        referenceSkeleton ??= GameObject.Instantiate(Resources
            .FindObjectsOfTypeAll<PlayerController>()
            .FirstOrDefault(p => !p.gameObject.scene.IsValid())
            ?.PlayerVisuals.transform.GetChild(1).gameObject).transform;
        
        Object.DontDestroyOnLoad(referenceSkeleton.gameObject);
        
        loadingMaterial ??= new Material(
            GameObjects.Gym.INTERACTABLES.PoseGhost.Ghost.StaticGhost.Visuals.Poseghostbody.GetGameObject().GetComponent<Renderer>().material
        );

        loadingMaterial.hideFlags = HideFlags.DontUnloadUnusedAsset;
    }
    
    // Thanks so much to Orangenal for finding this method of forcing T-Pose
    
    public static void CopySkeleton(Transform target, Transform source)
    {
        target.localRotation = source.localRotation;

        for (int i = 0; i < target.childCount; i++)
        {
            Transform targetChild = target.GetChild(i);
            Transform sourceChild = source.Find(targetChild.name);
            if (sourceChild != null)
                CopySkeleton(targetChild, sourceChild);
        }
    }
    
    public static void ToggleTPose(GameObject targetController, bool toggle)
    {
        var playerIK = targetController.GetComponentInChildren<PlayerIK>(true);
        var vrik = targetController.GetComponentInChildren<VRIK>(true);
        var animator = targetController.GetComponentInChildren<Animator>(true);

        if (playerIK != null)
            playerIK.enabled = !toggle;
        
        if (vrik != null)
            vrik.enabled = !toggle;
        
        if (animator != null)
            animator.enabled = !toggle;
        
        if (toggle && animator != null)
        {
            var playerSkeleton = animator.transform.GetChild(1);

            CopySkeleton(playerSkeleton, referenceSkeleton);
        }
    }
    
    // ----------------------------------------------------------------

    public static IEnumerator LoadAndApplyAvatarForPlayer(
        Player player, 
        int avatarIdx = 0, 
        GameObject overrideController = null,
        Action<bool> onDone = null)
    {
        bool isLocal = player == Main.LocalPlayer;

        var controller = overrideController ?? player.Controller.gameObject;

        // if already has rig loaded, unload.
        var customRig = controller.GetComponent<CustomRig>();
        if (customRig != null)
        {
            if (customRig.IsLoading)
            {
                logger.Warning("Avatar loading is already running for this instance.");
                onDone?.Invoke(false);
                yield break;
            }

            Object.Destroy(customRig);
        }

        // Check for avatar
        
        string avatarPath = null;
        
        if (isLocal) {
            avatarPath = GetLocalAvatarPath(avatarIdx);
            if (avatarPath == null)
            {
                onDone?.Invoke(false);
                yield break;
            }
        }
        else
        {
            bool hasAvatar = false;
            yield return RemoteAvatarNetworking.GetAvatarAsset(player, (avatarStatus) => hasAvatar = avatarStatus.Exists);

            if (!hasAvatar)
            {
                onDone?.Invoke(false);
                yield break;
            }
        }
        
        // ------------------------------------------------
        
        customRig = controller.AddComponent<CustomRig>();
        customRig.IsLoading = true;
        customRig.path = avatarPath;

        var playerRenderer = controller.GetComponentInChildren<SkinnedMeshRenderer>();
        customRig.OriginalMaterial = new Material(playerRenderer.material);
        customRig.OriginalMesh = playerRenderer.sharedMesh;
        customRig.OriginalBones = playerRenderer.bones;
        customRig.OriginalRootBone = playerRenderer.rootBone;

        customRig.OriginalMesh.hideFlags = HideFlags.DontUnloadUnusedAsset;
        customRig.OriginalMaterial.hideFlags = HideFlags.DontUnloadUnusedAsset;

        playerRenderer.material = loadingMaterial;

        // ------------------------------------------------
        
        GameObject avatarInstance = null;
        AvatarDescriptorExport config = null;
        yield return LoadAvatarInstanceAsync(avatarPath, (avatar, settings) => { avatarInstance = avatar; config = settings; });

        void Fail(string msg)
        {
            logger.Error(msg);

            playerRenderer.material = customRig.OriginalMaterial;

            Object.Destroy(customRig);
            
            if (avatarInstance != null)
                Object.Destroy(avatarInstance);
            
            onDone?.Invoke(false);
        }
        
        if (avatarInstance == null)
        {
            Fail($"Avatar could not be loaded from path: {avatarPath} | Player: {player.Data.GeneralData.PublicUsername}");
            yield break;
        } 
        
        if (config == null)
        {
            Fail($"Avatar does not have valid config | Player: {player.Data.GeneralData.PublicUsername}");
            yield break;
        }
        
        customRig.Root = avatarInstance;
        customRig.config = config;

        playerRenderer.material = customRig.OriginalMaterial;

        try
        {
            ApplyInstanceToPlayer(customRig);

            customRig.IsLoading = false;
            onDone?.Invoke(true);
        }
        catch (Exception e)
        {
            Fail($"Failed to apply avatar instance to player: {e.Message}");
        }
    }

    public static string GetLocalAvatarPath(int idx = 0)
    {
        var files = Directory.GetFiles(
            Path.Combine(MelonEnvironment.UserDataDirectory, "CustomAvatars"),
            "*.rumbleavatar"
        );

        if (files.Length == 0)
            return null;

        idx = Mathf.Clamp(idx, 0, files.Length - 1);
        return files[idx];
    }
    
    public static IEnumerator LoadAvatarInstanceAsync(string path, Action<GameObject, AvatarDescriptorExport> callback = null)
    {
        if (!File.Exists(path))
        {
            logger.Error($"Avatar file not found: {path}");
            yield break;
        }

        var request = AssetBundle.LoadFromFileAsync(path);
        yield return request;

        var bundle = request.assetBundle;
        if (bundle == null)
        {
            logger.Error("Failed to load AssetBundle.");
            yield break;
        }
        
        var prefab = bundle.LoadAllAssets<GameObject>().FirstOrDefault();

        var json = bundle.LoadAsset<TextAsset>("Config");

        AvatarDescriptorExport avatarDescriptor = null;
        
        if (json != null)
            avatarDescriptor = JsonConvert.DeserializeObject<AvatarDescriptorExport>(json.text);
        
        var instance = Object.Instantiate(prefab);

        callback?.Invoke(instance, avatarDescriptor);

        bundle.Unload(false);
    }

    public static void ApplyInstanceToPlayer(CustomRig avatar)
    {
        var targetController = avatar.gameObject;
        
        ToggleTPose(targetController, true);
        
        avatar.IsLocal = targetController.GetComponent<PlayerController>()?.assignedPlayer == Main.LocalPlayer;
        
        var playerRenderer = targetController.GetComponentInChildren<SkinnedMeshRenderer>(true);
        avatar.MainRenderer = avatar.Root.GetComponentInChildren<SkinnedMeshRenderer>();

        ApplyBones(targetController, avatar);
        
        ApplyMesh(avatar, playerRenderer, avatar.config.swapOriginalMesh);

        ApplyMaterials(avatar, playerRenderer, avatar.IsLocal);

        if (avatar.config.swapOriginalMesh)
        {
            Object.Destroy(avatar.MainRenderer);
            avatar.MainRenderer = playerRenderer;
        }
        
        // Default blendshapes

        foreach (var b in avatar.config.defaultBlendshapes)
        {
            if (b.index < 0 || b.index >= avatar.MainRenderer.sharedMesh.blendShapeCount)
                continue;
            
            avatar.MainRenderer.SetBlendShapeWeight(b.index, b.weight);
        }

        avatar.blinkRoutine = MelonCoroutines.Start(avatar.BlinkRoutine());
        
        ToggleTPose(targetController, false);
    }

    public static void ApplyBones(GameObject targetController, CustomRig avatar)
    {
        var playerAnimator = targetController.GetComponentInChildren<Animator>();
        var avatarAnimator = avatar.Root.GetComponentInChildren<Animator>(true);
        
        avatar.Root.transform.rotation = targetController.transform.rotation;

        foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
        {
            if (bone == HumanBodyBones.LastBone) continue;
            
            var playerBone = playerAnimator.GetBoneTransform(bone);
            var avatarBone = avatarAnimator.GetBoneTransform(bone);

            if (playerBone == null || avatarBone == null)
                continue;

            if (bone == HumanBodyBones.Hips) {
                avatarBone.transform.position = playerBone.position;
                avatarBone.transform.rotation = playerBone.rotation;
            }
            
            avatarBone.transform.localScale = Vector3.Scale(avatarBone.localScale, playerBone.localScale);
            avatarBone.SetParent(playerBone, true);
            avatarBone.gameObject.AddComponent<CustomRigBone>();
        }
    }
    
    public static void ApplyMesh(
        CustomRig avatar,
        SkinnedMeshRenderer playerRenderer,
        bool swap
    )
    {
        if (!swap) return;

        var rigRenderer = avatar.MainRenderer;
        if (rigRenderer == null) return;
        
        playerRenderer.sharedMesh = rigRenderer.sharedMesh;
        playerRenderer.bones = rigRenderer.bones;
        playerRenderer.rootBone = rigRenderer.rootBone;
    }

    public static void ApplyMaterials(
        CustomRig avatar,
        SkinnedMeshRenderer playerRenderer,
        bool isLocal
    )
    {
        var renderers = avatar.Root.GetComponentsInChildren<Renderer>(true);
        var rigRenderer = avatar.MainRenderer;

        int globalIndex = 0;

        foreach (var r in renderers)
        {
            var sourceMats = r.materials;
            var newMats = new Material[sourceMats.Length];

            for (int i = 0; i < sourceMats.Length; i++, globalIndex++)
            {
                var original = sourceMats[i];
                Material mat;

                bool usePlayerShader = avatar.config.playerShaderSlots.Contains(globalIndex);

                if (usePlayerShader)
                {
                    var tex = original.GetTexture("_BaseMap") ?? original.GetTexture("_MainTex");

                    mat = new Material(playerRenderer.material);

                    if (tex != null)
                        mat.SetTexture("_ColorAtlas", tex);
                }
                else
                {
                    mat = new Material(original);
                }

                if (mat.HasProperty("_IsLocal"))
                    mat.SetFloat("_IsLocal", isLocal ? 1f : 0f);

                newMats[i] = mat;
            }

            if (!avatar.config.swapOriginalMesh)
            {
                r.materials = newMats;
            }
            else
            {
                if (r == rigRenderer)
                    playerRenderer.materials = newMats;
                else
                    r.materials = newMats;
            }
        }
    }
}

[RegisterTypeInIl2Cpp]
public class CustomRig : MonoBehaviour
{
    public GameObject Root;
    public SkinnedMeshRenderer MainRenderer;
    public PlayerController playerController;
    public AvatarDescriptorExport config;

    public bool IsLoading;
    public string path;

    public Mesh OriginalMesh;
    public Material OriginalMaterial;
    public Transform[] OriginalBones;
    public Transform OriginalRootBone;

    public bool IsLocal;

    public object blinkRoutine;

    public void Awake()
    {
        playerController = GetComponent<PlayerController>();
    }

    public void OnDestroy()
    {
        var playerRenderer = transform.GetComponentInChildren<SkinnedMeshRenderer>();

        var bones = playerRenderer.GetComponentsInChildren<CustomRigBone>(true);

        foreach (var b in bones)
        {
            if (b != null)
                Destroy(b.gameObject);
        }
        
        playerRenderer.sharedMesh = OriginalMesh;
        playerRenderer.material = OriginalMaterial;
        playerRenderer.bones = OriginalBones;
        playerRenderer.rootBone = OriginalRootBone;

        if (blinkRoutine != null)
            MelonCoroutines.Stop(blinkRoutine);

        if (Root != null)
            Destroy(Root);
    }

    public void Update()
    {
        if (config != null && MainRenderer != null && playerController != null)
        {
            // Jaw
            var idx = config.jawOpenBlendshape;
            if (idx < MainRenderer.sharedMesh.blendShapeCount)
            {
                var voiceSystem = playerController.PlayerVoiceSystem;

                if (voiceSystem != null)
                {
                    float weight = Mathf.Clamp(voiceSystem.currentJawOpenPercentage * 100f * config.voiceMultiplier, 0, 100);
                    MainRenderer.SetBlendShapeWeight(idx, weight);
                }
            }
            
            // Eyes
            var eyeSystem = playerController.PlayerEyeSystem;

            if (eyeSystem.CurrentAttentionPoint != null)
            {
                var settings = config.eyeSettings;
                
                if (settings.eyeUpBlendshape == -1 || 
                    settings.eyeDownBlendshape == -1 ||
                    settings.eyeLeftBlendshape == -1 || 
                    settings.eyeRightBlendshape == -1)
                    return;

                var head = playerController.PlayerAnimator.animator
                    .GetBoneTransform(HumanBodyBones.Head);

                if (head == null) return;

                Vector3 target = eyeSystem.CurrentAttentionPoint.transform.position;
                
                Vector3 dir = (target - head.position).normalized;
                
                Vector3 localDir = head.InverseTransformDirection(dir);

                float x = localDir.x;
                float y = localDir.y;

                float gain = settings.eyeGain;

                float up = Mathf.Clamp01(y * gain);
                float down = Mathf.Clamp01(-y * gain);
                float right = Mathf.Clamp01(x * gain);
                float left = Mathf.Clamp01(-x * gain);

                MainRenderer.SetBlendShapeWeight(settings.eyeUpBlendshape, up * 100f);
                MainRenderer.SetBlendShapeWeight(settings.eyeDownBlendshape, down * 100f);
                MainRenderer.SetBlendShapeWeight(settings.eyeLeftBlendshape, left * 100f);
                MainRenderer.SetBlendShapeWeight(settings.eyeRightBlendshape, right * 100f);
            }
        }
    }
    
    public IEnumerator BlinkRoutine()
    {
        while (true)
        {
            float wait = UnityEngine.Random.Range(
                config.eyeSettings.blinkInterval.x,
                config.eyeSettings.blinkInterval.y
            );

            yield return new WaitForSeconds(wait);

            yield return Blink();
        }
    }

    IEnumerator Blink()
    {
        var eyes = config.eyeSettings;

        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime / eyes.blinkSpeed;
            float w = Mathf.Lerp(0, 100, t);

            ApplyBlinkWeight(MainRenderer, eyes, w);

            yield return null;
        }

        t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime / eyes.blinkSpeed;
            float w = Mathf.Lerp(100, 0, t);

            ApplyBlinkWeight(MainRenderer, eyes, w);

            yield return null;
        }
    }

    void ApplyBlinkWeight(SkinnedMeshRenderer smr, EyeSettings eyes, float weight)
    {
        if (eyes.blinkType == AvatarDescriptorExport.BlinkType.Single)
        {
            smr.SetBlendShapeWeight(eyes.blinkBlendshape, weight);
        } else if (eyes.blinkType == AvatarDescriptorExport.BlinkType.LeftRight)
        {
            smr.SetBlendShapeWeight(eyes.blinkLeftBlendshape, weight);
            smr.SetBlendShapeWeight(eyes.blinkRightBlendshape, weight);
        }
    }
}

[RegisterTypeInIl2Cpp]
public class CustomRigBone : MonoBehaviour { }

public enum ParamType
{
    Bool,
    Float,
    Int
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

    public bool swapOriginalMesh = true;
    public List<int> playerShaderSlots = new();
    public int bodyShaderSlot = -1;
    public List<BlendshapeDefault> defaultBlendshapes = new();
    public int jawOpenBlendshape = -1;
    public float voiceMultiplier = 1f;
    public EyeSettings eyeSettings = new EyeSettings();
    public List<AvatarParam> parameters = new();
}

[Serializable]
public class EyeSettings
{
    public AvatarDescriptorExport.BlinkType blinkType;
    public int blinkBlendshape = -1;
    public int blinkLeftBlendshape = -1;
    public int blinkRightBlendshape = -1;

    public int eyeUpBlendshape = -1;
    public int eyeDownBlendshape = -1;
    public int eyeLeftBlendshape = -1;
    public int eyeRightBlendshape = -1;

    public float eyeGain = 1.0f;

    public Vector2 blinkInterval = new(2.5f, 5f);
    public float blinkSpeed = 0.05f;
}

[Serializable]
public class BlendshapeDefault
{
    public string name;
    public int index;
    public float weight;
}

[Serializable]
public class AvatarParam
{
    public ParamType type;
    public bool networked = true;
    public string uiLabel;

    // if bool
    public int targetIndex = -1;
    public bool defaultToggle = true;
}