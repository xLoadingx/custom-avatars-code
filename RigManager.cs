using System;
using System.Collections;
using System.IO;
using System.Linq;
using Il2CppPhoton.Pun;
using Il2CppRootMotion.FinalIK;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Subsystems;
using Il2CppTMPro;
using MelonLoader;
using MelonLoader.Utils;
using Newtonsoft.Json;
using RumbleModdingAPI.RMAPI;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CustomAvatars;

public class RigManager
{
    private static Transform referenceSkeleton;
    private static Material loadingMaterial;
    public static GameObject loadingBarPrefab;
    public static GameObject avatarIconPrefab;

    private static GameObject RigParent;

    private static MelonLogger.Instance logger => Melon<Main>.Logger;

    public static void EnsureStaticObjects()
    {
        referenceSkeleton ??= GameObject.Instantiate(Resources
            .FindObjectsOfTypeAll<PlayerController>()
            .FirstOrDefault(p => !p.gameObject.scene.IsValid())
            ?.PlayerVisuals.transform.GetChild(1).gameObject).transform;
        
        Object.DontDestroyOnLoad(referenceSkeleton.gameObject);

        if (Main.instance.currentScene == "Gym")
        {
            loadingMaterial ??= new Material(
                GameObjects.Gym.INTERACTABLES.PoseGhost.Ghost.StaticGhost.Visuals.Poseghostbody.GetGameObject().GetComponent<Renderer>().material
            );
            
            if (loadingBarPrefab == null)
            {
                loadingBarPrefab = new GameObject("[Custom Avatars] Loading Bar Prefab");
                loadingBarPrefab.SetActive(false);
                Object.DontDestroyOnLoad(loadingBarPrefab);
                
                var bar = Object.Instantiate(
                        GameObjects.Gym.INTERACTABLES.ProgressTracker.ProgressPanel.StatusBar.GetGameObject(),
                        loadingBarPrefab.transform,
                        false
                    );

                var text = Create.NewText("0%", 1f, Color.white, Vector3.zero, Quaternion.identity);
                text.transform.SetParent(loadingBarPrefab.transform);
                text.GetComponent<TextMeshPro>().enableWordWrapping = false;

                text.name = "[Custom Avatars] Download Progress Text";
                text.transform.localPosition = new Vector3(0, -0.1142f, 0);
                text.transform.localRotation = Quaternion.Euler(0, 180, 0);
                text.transform.localScale = Vector3.one * 0.7f;

                bar.name = "[Custom Avatars] Download Progress Bar";
                bar.transform.localPosition = new Vector3(0, -0.1831f, 0);
                bar.transform.localRotation = Quaternion.Euler(0, 180, 0);
                bar.transform.localScale = new Vector3(0.6f, 0.05f, 0.6f);

                var mat = bar.GetComponent<MeshRenderer>().material;
                mat.SetFloat("_RC_Target", 1f);
                mat.SetFloat("_RC_Current", 0f);
            }
        }
        
        loadingMaterial.hideFlags = HideFlags.DontUnloadUnusedAsset;

        if (RigParent == null)
            RigParent = new GameObject("[CustomAvatar] Rigs");

        if (avatarIconPrefab == null)
        {
            var bundle = AssetBundles.LoadAssetBundleFromStream(Main.instance, "CustomAvatars.Resources.avatarthingies");
            var tagIcon = bundle.LoadAsset<Sprite>("icon");
            avatarIconPrefab = new("[Custom Avatars] Tag");
        
            var renderer = avatarIconPrefab.AddComponent<SpriteRenderer>();
            renderer.sprite = tagIcon;
            
            GameObject.DontDestroyOnLoad(avatarIconPrefab);
            avatarIconPrefab.SetActive(false);

            bundle.Unload(false);
        }
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
        Action<bool> onDone = null,
        Func<bool> waitUntil = null)
    {
        void Trace(string msg) {
            if (Main.instance.DebugMode.Value) Main.instance.LoggerInstance.Msg($"[Avatar:{player.Data.GeneralData.PlayFabMasterId}] {msg}");
        }

        Trace("Start load");
        
        var controller = overrideController ?? player.Controller.gameObject;

        // if already has rig loaded, unload.
        var customRig = controller.GetComponent<CustomRig>();
        if (customRig != null)
        {
            if (customRig.IsLoading)
            {
                logger.Warning("Avatar loading is already running for this instance.");
                customRig.CachePlayerVisuals(controller.GetComponentInChildren<SkinnedMeshRenderer>());
                onDone?.Invoke(false);
                yield break;
            }

            Object.Destroy(customRig);
            yield return null;
        }
        
        // -----------------------------------------------------

        Trace("Wait for photon view");
        
        var view = controller.GetComponent<PhotonView>();
        while (PhotonNetwork.InRoom && view != null && view.Owner == null)
        {
            if (player == null || controller == null)
            {
                onDone?.Invoke(false);
                yield break;
            }

            yield return null;
        }
        
        Trace("Done with photon view check");

        // if (!ShouldAttemptLoadForPlayer(player))
        // {
        //     onDone?.Invoke(false);
        //     yield break;
        // }

        while (waitUntil?.Invoke() ?? false)
            yield return null;
        
        Trace("After wait until");
        
        AvatarLoadContext ctx = null;

        Trace("Start get context");
        var contextRoutine = GetContextForAvatar(player, avatarIdx, c =>
        {
            Trace($"Context callback fired. Null? {c == null}");
            ctx = c;
        });

        while (contextRoutine.MoveNext())
            yield return contextRoutine.Current;
        
        Trace("After get context");
        if (ctx == null)
        {
            Trace("Context null");
            onDone?.Invoke(false);
            yield break;
        }
        
        Trace(ctx.IsLocal ? "Local avatar path found" : "Remote avatar exists");
        
        // -----------------------------------------------------
        
        // Has avatar, continue with loading / downloading if remote

        customRig = controller.AddComponent<CustomRig>();
        customRig.IsLoading = true;
        customRig.path = ctx.AvatarPath; /* Null if remote */

        if (PhotonNetwork.InRoom)
            customRig.photonPlayer = player.Controller.GetComponent<PhotonView>().Owner;

        var playerRenderer = controller.GetComponentInChildren<SkinnedMeshRenderer>();

        while (playerRenderer.sharedMesh == null || playerRenderer.rootBone == null)
            yield return null;

        customRig.CachePlayerVisuals(playerRenderer);

        if (!ctx.IsLocal)
        {
            playerRenderer.material = loadingMaterial;
            customRig.EnsureLoadingBar();
        }
        
        // -----------------------------------------------------
        
        // Remote download

        byte[] avatarData = null;
        
        if (!ctx.IsLocal)
        {
            Trace("Downloading avatar");

            customRig.loadingBar?.SetActive(true);
            
            yield return RemoteAvatarIO.GetAvatarAsset(
                player.Data.GeneralData.PlayFabMasterId,
                data => avatarData = data,
                (progress) =>
                {
                    Trace($"{progress * 100f:F}%");
                    
                    customRig.UpdateLoadingProgress(progress);
                },
                () => customRig == null
            );

            customRig.loadingBar?.SetActive(false);

            Trace(avatarData != null ? "Download complete" : "Download failed");

            // Download failed or (rarely) avatar deleted between check and download
            if (avatarData == null)
            {
                playerRenderer.material = customRig.OriginalMaterial;
                Object.Destroy(customRig);

                onDone?.Invoke(false);

                yield break;
            }
        }
        
        // -----------------------------------------------------
        
        GameObject avatarInstance = null;
        AvatarDescriptorExport config = null;
        yield return LoadAvatarInstanceAsync(ctx.AvatarPath, avatarData, (avatar, settings) => { avatarInstance = avatar; config = settings; });

        Trace("Avatar instance created");

        void Fail(string msg)
        {
            logger.Error(msg);

            playerRenderer.material = customRig.OriginalMaterial;

            ToggleTPose(controller.gameObject, false);
            
            if (customRig != null)
                Object.Destroy(customRig);
            
            if (avatarInstance != null)
                Object.Destroy(avatarInstance);
            
            onDone?.Invoke(false);
        }
        
        if (avatarInstance == null)
        {
            Fail($"Avatar could not be loaded from path: {ctx.AvatarPath} | Player: {player.Data.GeneralData.PublicUsername}");
            yield break;
        } 
        
        if (config == null)
        {
            Fail($"Avatar does not have valid config | Player: {player.Data.GeneralData.PublicUsername}");
            yield break;
        }

        if (RigParent == null)
        {
            Fail("Rig parent could not be found.");
            yield break;
        }
        
        avatarInstance.transform.SetParent(RigParent.transform);
        avatarInstance.name = $"Avatar | {player.Data.GeneralData.PlayFabMasterId} | {player.Data.GeneralData.PublicUsername}";
        
        customRig.Root = avatarInstance;
        customRig.config = config;

        playerRenderer.material = customRig.OriginalMaterial;

        try
        {
            Trace("Applying avatar");
            ApplyInstanceToPlayer(customRig);

            customRig.IsLoading = false;
            
            onDone?.Invoke(true);
        }
        catch (Exception e)
        {
            Fail($"Failed to apply avatar instance to player: {e.Message}");
        }
    }
    
    // Helpers

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

    public static IEnumerator GetContextForAvatar(
        Player player,
        int avatarIdx = 0,
        Action<AvatarLoadContext> done = null
    )
    {
        Main.instance.LoggerInstance.Warning("Start GetContext");
        var ctx = new AvatarLoadContext();
        ctx.IsLocal = player == Main.LocalPlayer;

        if (ctx.IsLocal)
        {
            Main.instance.LoggerInstance.Warning("Is Local Player");
            ctx.AvatarPath = GetLocalAvatarPath(avatarIdx);

            if (ctx.AvatarPath == null)
            {
                Main.instance.LoggerInstance.Warning("Avatar path null");
                done?.Invoke(null);
                yield break;
            }
            
            done?.Invoke(ctx);
            yield break;
        }

        Main.instance.LoggerInstance.Warning("Is Remote Player");
        bool exists = false;

        Main.instance.LoggerInstance.Warning("RemoteAvatarExists check");
        yield return RemoteAvatarIO.RemoteAvatarExists(
            player.Data.GeneralData.PlayFabMasterId,
            e => exists = e
        );
        Main.instance.LoggerInstance.Warning("After RAE Check");

        if (!exists)
        {
            Main.instance.LoggerInstance.Warning("Not exist");
            done?.Invoke(null);
            yield break;
        }

        Main.instance.LoggerInstance.Warning("Exists");
        ctx.Exists = true;
        done?.Invoke(ctx);
    }

    public static bool ShouldAttemptLoadForPlayer(Player player)
    {
        var m = Main.instance;

        bool isInMatch = m.currentScene is "Map0" or "Map1";

        if (player == Main.LocalPlayer)
        {
            if (!m.ToggleForSelf.Value)
                return false;

            if (isInMatch && !m.ToggleSelfInMatch.Value)
                return false;

            return true;
        }

        if (!m.ToggleForOthers.Value)
            return false;
        
        if (isInMatch && !m.ToggleOthersInMatch.Value)
            return false;

        if (PhotonNetwork.InRoom)
        {
            var photonPlayer = player.Controller.GetComponent<PhotonView>().Owner;

            if (!CAParams.Visibility.Get(photonPlayer))
                return false;
        }
        
        return true;
    }
    
    // -----------------------------------------------------
    
    public static IEnumerator LoadAvatarInstanceAsync(string path = null, byte[] data = null, Action<GameObject, AvatarDescriptorExport> callback = null)
    {
        AssetBundleCreateRequest request;
        
        if (path != null)
        {
            if (!File.Exists(path))
            {
                logger.Error($"Avatar file not found: {path}");
                yield break; 
            }
            
            request = AssetBundle.LoadFromFileAsync(path);
        } else if (data != null)
        {
            request = AssetBundle.LoadFromMemoryAsync(data);
        }
        else
        {
            logger.Error("LoadAvatarInstanceAsync: Must supply either a path or bundle data.");
            yield break;
        }

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

        avatar.blinkRoutine ??= MelonCoroutines.Start(avatar.BlinkRoutine());
        
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

                if (mat.HasProperty("_IsLocalPlayer"))
                    mat.SetFloat("_IsLocalPlayer", isLocal ? 1f : 0f);

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

public class AvatarLoadContext
{
    public bool IsLocal;
    public string AvatarPath;
    public byte[] RemoteData;
    public bool Exists;
}