using System.Collections;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppPhoton.Pun;
using Il2CppRootMotion.FinalIK;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.MoveSystem;
using Il2CppRUMBLE.MoveSystem.Testing;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Scaling;
using Il2CppRUMBLE.Players.Subsystems;
using Il2CppTMPro;
using MelonLoader;
using MelonLoader.Utils;
using Newtonsoft.Json;
using RumbleModdingAPI;
using RumbleModdingAPI.RMAPI;
using RumbleModUI;
using UnityEngine;
using Hashtable = Il2CppExitGames.Client.Photon.Hashtable;
using Stack = System.Collections.Stack;

namespace CustomAvatars;

public static class RigManager
{
    private static Main instance;
    public static readonly Dictionary<string, CustomRig> rigs = new();
    public static readonly HashSet<string> loadingPlayers = new();
    public static int activeLoads;

    public static ModSetting<bool> avatarSettingsHeader;
    public static List<ModSetting<bool>> avatarSettingBools = new();
    public static List<Transform> scanList = new();

    public static string OpponentID;

    public enum VisibilityResult
    {
        Visible,
        Hidden,
        Unknown // no visibility key found, probably not using the mod
    }

    public static void Initialize(Main mainInstance)
    {
        instance = mainInstance;
    }

    public static void Log(string message, ConsoleColor color = default)
    {
        if (color == default)
            color = ConsoleColor.White;
        
        instance?.LoggerInstance?.MsgPastel(color, message);
    }
    
    public static void Error(string message) => instance?.LoggerInstance?.Error(message);
    public static void Warning(string message) => instance?.LoggerInstance?.Warning(message);

    // Logs optimization stats (verts, mats, textures)
    // A simple version of VRCs system, but it works
    public static void LogStatsForAvatar(GameObject rig)
    {
        if (rig == null)
        {
            Warning("LogStatsForAvatar: rig is null");
            return;
        }
        
        var smrs = rig.GetComponentsInChildren<SkinnedMeshRenderer>();
        if (smrs.Length == 0)
        {
            Warning("LogStatsForAvatar: No SkinnedMeshRenderer or mesh found");
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

                        if (tex.width >= 8192 || tex.height >= 8192)
                        {
                            hasHugeTextures = true;
                            Warning($"[Avatar Optimization] Huge texture on '{mat.name}' ({texName}): {tex.width}x{tex.height}");
                        }
                    }
                }

                int passCount = mat.shader?.passCount ?? 0;
                totalPasses += passCount;
                if (passCount > 7)
                    if (mat.shader != null)
                        Warning($"[Avatar Optimization] Shader '{mat.shader.name}' has {passCount} passes.");

                if (mat.shader != null && mat.shader.name.ToLower().Contains("tessellation"))
                {
                    hasHeavyShaders = true;
                    Warning($"[Avatar Optimization] Shader '{mat.shader.name}' uses expensive features.");
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
        
        Log("-------------------------------------------------------------", color);
        Log($"[Avatar Optimization] {rating}: {vertexCount} verts, {materialCount} mat(s), {totalTextures} texture(s).", color);
        Log($"WARNINGS: {(String.IsNullOrEmpty(warnings) ? "None" : warnings.TrimEnd(';'))}", ConsoleColor.Yellow);
        Log("-------------------------------------------------------------", color);

        if (Main.instance.currentScene == "Gym")
        {
            Color textColor = (color switch
            {
                ConsoleColor.Green => new Color(0f, 0.5f, 0f),
                ConsoleColor.Yellow => Color.yellow,
                _ => Color.red
            });
            
            var avatarDetails = Main.instance.avatarOptimizationParent;
            avatarDetails.transform.GetChild(1).GetComponent<TextMeshPro>().text = rating;
            avatarDetails.transform.GetChild(1).GetComponent<TextMeshPro>().color = textColor;
            
            avatarDetails.transform.GetChild(2).GetComponent<TextMeshPro>().text = $"{vertexCount} verts, {materialCount} mat(s), {totalTextures} texture(s).";
            avatarDetails.transform.GetChild(2).GetComponent<TextMeshPro>().color = textColor;
            
            avatarDetails.transform.GetChild(3).GetComponent<TextMeshPro>().text = $"WARNINGS: {(String.IsNullOrEmpty(warnings) ? "None" : warnings.TrimEnd(';'))}";
        }
    }
    
    public static void ClearRigs()
    {
        foreach (var rig in rigs.Values)
            ClearRig(rig);
            
        rigs.Clear();
    }

    public static void ClearRig(CustomRig rig)
    {
        rig.Apply(CustomRig.RigState.Original);
            
        if (Main.instance.perPlayerSettings.ContainsKey(rig.PlayerId))
            Main.instance.RemovePlayerFromList(rig.Player);
            
        GameObject.Destroy(rig.Root);
    }
    
    // Converts managed stream to Il2Cpp stream
    public static Il2CppSystem.IO.Stream ConvertToIl2CppStream(Stream stream)
    {
        Il2CppSystem.IO.MemoryStream il2CppStream = new Il2CppSystem.IO.MemoryStream();
        byte[] numArray = new byte[4096];
        int count;
        while ((count = stream.Read(numArray, 0, numArray.Length)) > 0)
        {
            Il2CppStructArray<byte> buffer = numArray;
            il2CppStream.Write(buffer, 0, count);
        }
        il2CppStream.Flush();
        return il2CppStream;
    }
    
    // Main rig loader
    public static IEnumerator LoadRigForPlayer(Player player, Action<GameObject> onLoaded, bool log = true, string remoteSha = null)
    {
        string playerID = player?.Data?.GeneralData?.PlayFabMasterId;
        if (string.IsNullOrEmpty(playerID))
        {
            MelonLogger.Warning("LoadRigForPlayer: playerID is null or empty");
            yield break;
        }

        if (player.Controller.ControllerType != ControllerType.Local)
        {
            if (!loadingPlayers.Add(playerID))
            {
                MelonLogger.Msg($"LoadRigForPlayer: player {playerID} is already loading");
                yield break;
            }

            while (activeLoads >= (int)Main.instance.maxConcurrentDownloads.SavedValue)
                yield return null;

            activeLoads++;
        }

        try
        {
            bool isLocal = player == PlayerManager.instance.LocalPlayer;

            string opponentPath = Path.Combine(MelonEnvironment.UserDataDirectory, "CustomAvatars", "Opponents");
            if (!Directory.Exists(opponentPath)) Directory.CreateDirectory(opponentPath);

            string basePath = Path.Combine(MelonEnvironment.UserDataDirectory, "CustomAvatars");

            string filePath = isLocal
                ? Directory.GetFiles(basePath, "*.rumbleavatar").FirstOrDefault()
                : Path.Combine(opponentPath, playerID);
            
            // Hands tend to break when loading rig because the hands have different finger rotatations
            // than base pose
            player.Controller.GetSubsystem<PlayerHandPresence>().enabled = false;
            
            if (!isLocal)
            {
                if (File.Exists(filePath) && !string.IsNullOrEmpty(remoteSha))
                {
                    if (RemoteAvatarLoader.ShaMatchesLocal(remoteSha, filePath))
                    {
                        if (log) Log($"Using cached avatar for {playerID}.");
                    }
                    else
                    {
                        if (log)
                            Log($"Avatar for {playerID} outdated, downloading fresh...");
                        File.Delete(filePath);
                        yield return MelonCoroutines.Start(RemoteAvatarLoader.DownloadToFile(playerID, filePath));
                    }
                }
                else
                {
                    if (log) Log($"No cached avatar for {playerID}, downloading...");
                    yield return MelonCoroutines.Start(RemoteAvatarLoader.DownloadToFile(playerID, filePath));
                }
            }

            string rigPath = isLocal
                ? Directory.GetFiles(basePath, "*.rumbleavatar").FirstOrDefault()
                : Path.Combine(basePath, "Opponents", playerID);

            if (string.IsNullOrEmpty(rigPath) || !File.Exists(rigPath))
            {
                Warning(
                    $"No custom avatar found for {(isLocal ? "you" : player.Data.GeneralData?.PublicUsername ?? "unknown")} at {basePath}");

                player.Controller.GetSubsystem<PlayerHandPresence>().enabled = true;
                yield break;
            }


            AssetBundleCreateRequest request = null;

            if (isLocal)
            {
                request = AssetBundle.LoadFromFileAsync(rigPath);
            }
            else
            {
                byte[] decrypted = RemoteAvatarLoader.XorCrypt(File.ReadAllBytes(rigPath));
                request = AssetBundle.LoadFromMemoryAsync(decrypted);
            }
            
            yield return request;
            var rigBundle = request.assetBundle;
            
            if (rigBundle == null)
            {
                Error("Failed to load AssetBundle.");
                Error($"Path: {rigPath}");
                Error($"Player: {(isLocal ? "Local Player" : player.Data?.GeneralData?.PublicUsername ?? "Unknown")}");
                Error("Possible causes:");
                Error("  - The file is corrupted or incomplete.");
                Error("  - It is not an AssetBundle built for the correct Unity version.");
                Error("  - The file was not fully downloaded or unpacked.");
                
                if (File.Exists(rigPath) && !isLocal)
                    File.Delete(rigPath);
                
                player.Controller.GetSubsystem<PlayerHandPresence>().enabled = true;
                yield break;
            }

            if (!isLocal)
                yield return new WaitForSeconds(2f);
            
            GameObject rigPrefab = rigBundle?.LoadAsset<GameObject>("Rig");
            if (rigPrefab == null)
            {
                Error("'Rig' GameObject missing in AssetBundle.");
                Error($"  Bundle Path: {rigPath}");
                Error($"  Player: {(isLocal ? "Local Player" : player.Data?.GeneralData?.PublicUsername ?? "Unknown")}");
                Error("   Possible causes:");
                Error("      - The AssetBundle was built incorrectly (missing 'Rig' root object).");
                Error("      - The prefab name is different or nested incorrectly.");
                
                player.Controller.GetSubsystem<PlayerHandPresence>().enabled = true;
                rigBundle.Unload(true);
                yield break;
            }
            
            var rigInstance = GameObject.Instantiate(rigPrefab, Main.instance.rigParent.transform, true);
            rigInstance.name = $"RIG - {playerID}";

            // Function seems to only work with these null checks
            // despite never saying they are null, it likes them here either way
            if (player.Controller == null)
            {
                Error("player.Controller is null");
                player.Controller.GetSubsystem<PlayerHandPresence>().enabled = true;
                rigBundle.Unload(true);
                yield break;
            }

            if (player.Controller.gameObject == null)
            {
                Error("player.Controller.gameObject is null");
                player.Controller.GetSubsystem<PlayerHandPresence>().enabled = true;
                rigBundle.Unload(true);
                yield break;
            }

            var customRig = player.Controller.gameObject.GetOrAddComponent<CustomRig>();
            if (customRig == null)
            {
                Error("Failed to get or add CustomRig component");
                player.Controller.GetSubsystem<PlayerHandPresence>().enabled = true;
                rigBundle.Unload(true);
                yield break;
            }

            rigs[playerID] = customRig;

            scanList = Scan(rigInstance.transform);

            if (isLocal)
            {
                if (avatarSettingsHeader != null)
                    Main.instance.mod.Settings.Remove(avatarSettingsHeader);
                avatarSettingsHeader = null;

                foreach (var param in avatarSettingBools)
                    Main.instance.mod.Settings.Remove(param);
                avatarSettingBools.Clear();
            }
            
            TextAsset jsonAsset = rigBundle.LoadAsset<TextAsset>("Config");

            if (jsonAsset == null)
            {
                Warning(
                    "Config.json not found in rig bundle. Make sure your avatar has a AvatarDescriptor that was exported.");
            }
            else
            {
                try
                {
                    AvatarDescriptorExport config =
                        JsonConvert.DeserializeObject<AvatarDescriptorExport>(jsonAsset.text);
                    customRig.Config = config;

                    if (!(bool)Main.instance.toggleInRockCam.SavedValue && isLocal)
                        customRig.Config.swapOriginalMesh = false;
                }
                catch (Exception ex)
                {
                    Error($"Failed to parse avatar config: {ex.Message}");
                }
            }

            if (customRig.Config != null)
            {
                foreach (var blendshape in customRig.Config.defaultBlendshapes)
                {
                    if (blendshape.index >= 0)
                        customRig.MeshRenderer.SetBlendShapeWeight(blendshape.index, blendshape.weight);
                    else
                        Warning(
                            $"Blendshape '{blendshape.name}' not found on mesh '{customRig.MeshRenderer.sharedMesh.name}'");
                }

                if (isLocal)
                {
                    foreach (var param in customRig.Config.parameters)
                    {
                        avatarSettingsHeader ??= Main.instance.mod.AddToList("<b><#a600ff>Avatar Settings", false, 0, "", new Tags { DoNotSave = true });
                    
                        if (param.type == ParamType.Bool)
                        {
                            if (param.targetIndex < 0 || param.targetIndex >= scanList.Count)
                            {
                                Warning($"Parameter '{param.uiLabel}' refers to invalid index {param.targetIndex}");
                                continue;
                            }
                            
                            var setting = Main.instance.mod.AddToList($"- {param.uiLabel}", param.defaultToggle, 0, $"Toggle for '{param.uiLabel}'", new Tags());
                            Main.instance.mod.GetFromFile();
                            
                            GameObject toggleObject = scanList[param.targetIndex].gameObject;
                            toggleObject.SetActive((bool)setting.Value);
                            setting.SavedValueChanged += (sender, args) =>
                            {
                                toggleObject.SetActive((bool)setting.Value);

                                if (Main.instance.currentScene != "Gym")
                                {
                                    var toggles = avatarSettingBools.Select(s => (bool)s.Value).ToList();
                                    Hashtable ht = new Hashtable();
                                    ht["Ca_Params"] = PackParams(toggles);
                                    PhotonNetwork.LocalPlayer.SetCustomProperties(ht);
                                }
                            };
                        
                            avatarSettingBools.Add(setting);
                        }
                    }
                }
            }

            customRig.PlayerName = player.Data.GeneralData.PublicUsername;
            customRig.AvatarFilePath = filePath;

            rigBundle.Unload(false);

            if (log)
                Log($"Loading rig for player {playerID}");

            if (rigInstance != null && log && (
                    ((bool)Main.instance.logAvatarStats.SavedValue && isLocal)
                    || ((bool)Main.instance.logOtherAvatarStats.SavedValue && !isLocal))
               )
                LogStatsForAvatar(rigInstance);

            // It only gets deeper
            // I wouldn't recommend going here
            ApplyRigToPlayer(player, rigInstance, log);

            player.Controller.GetSubsystem<PlayerHandPresence>().enabled = true;

            if (!isLocal)
            {
                Main.instance.AddPlayerToList(player);
                ResolveRigState(player, customRig);
            }

            onLoaded?.Invoke(rigInstance);
        }
        finally
        {
            activeLoads--;
            loadingPlayers.Remove(playerID);
        }
    }
    
    public static List<Transform> Scan(Transform root)
    {
        var list = new List<Transform>();

        void Recurse(Transform t)
        {
            list.Add(t);
            for (int i = 0; i < t.childCount; i++)
                Recurse(t.GetChild(i));
        }

        Recurse(root);
        return list;
    }

    public static int PackParams(List<bool> list)
    {
        int mask = 0;

        for (int i = 0; i < list.Count; i++)
        {
            if (list[i])
                mask |= (1 << i);
        }

        return mask;
    }

    public static void ApplyRemoteParams(CustomRig rig, int mask)
    {
        var config = rig.Config;
        var scan = rig.rigScan;

        for (int i = 0; i < config.parameters.Count; i++)
        {
            var param = config.parameters[i];
            if (param.type != ParamType.Bool)
                continue;

            bool value = (mask & (1 << i)) != 0;

            int idx = param.targetIndex;
            if (idx >= 0 && idx < scan.Count && scan[idx] != null)
                scan[idx].gameObject.SetActive(value);
        }
    }

    public static IEnumerator FixHUDCamera(string masterID, Action done = null)
    {
        // Fixes clipping issue with (most) rigs close to camera
        var camObj = GameObject.Find($"RumbleHud_{masterID}_portraitCamera");
        var cam = camObj?.GetComponent<Camera>();
        if (cam != null)
            cam.nearClipPlane = 0.0001f;

        var scene = Main.instance.currentScene;
        if ((scene is "Map0" or "Map1" && masterID != OpponentID) || scene is not ("Map0" or "Map1"))
        {
            OpponentID = masterID;
            
            // Only works if RumbleHud actually exists, so thats neat.
            var hudType = Type.GetType("RumbleHud.Hud, RumbleHud");
            var method = hudType?.GetMethod("RegeneratePortraits", BindingFlags.Static | BindingFlags.Public);
            method?.Invoke(null, new object[] { Main.instance.currentScene == "Gym" });
        }

        yield return new WaitForSeconds(2f);

        done?.Invoke();
    }

    // Basically has to merge like 4 settings into one
    // toggleOthers, perPlayerToggles, and canOthersSeeMyAvatar, and toggleIfNewerVersion
    public static CustomRig.RigState ResolveRigState(Player player, CustomRig rig, bool setRig = true)
    {
        var state = CustomRig.RigState.Rigged;

        if (!(bool)Main.instance.toggleOthers.Value)
        {
            state = CustomRig.RigState.Original;
        }
        else
        {
            bool isFake =
                player?.Controller?.GetComponent<PlayerSessionStateSystem>() == null;

            if (isFake)
            {
                state = CustomRig.RigState.Rigged;
            }
            else
            {
                var view = player?.Controller?.GetComponent<PhotonView>();
                var props = view?.Controller?.CustomProperties;

                if (props == null ||
                    !props.TryGetValue("CA_Avatar", out var val) ||
                    !val.Unbox<bool>())
                {
                    state = CustomRig.RigState.Original;
                }
                else if (Main.instance.perPlayerSettings.TryGetValue(rig.PlayerId, out var settings) &&
                         !(bool)settings.Toggle.SavedValue)
                {
                    state = CustomRig.RigState.Original;
                }
                else if (!(bool)Main.instance.toggleIfNewerVersion.Value &&
                         Version.TryParse(rig.ModVersion, out var otherVersion) &&
                         Version.TryParse(BuildInfo.Version, out var localVersion) &&
                         otherVersion > localVersion)
                {
                    state = CustomRig.RigState.Original;
                }
            }
        }

        if (setRig)
            rig.Apply(state);

        return state;
    }
    
    // Sets your custom properties for CanSeeMe to work
    public static void UpdateVisibilityProps()
    {
        if (!PhotonNetwork.InRoom)
            return;

        foreach (var player in PhotonNetwork.PlayerList)
        {
            if (player == PhotonNetwork.LocalPlayer || player == null)
                continue;

            if (!player.CustomProperties?.ContainsKey("CA_ModVersion") ?? true)
                continue;

            Player rumblePlayer = Calls.Players.GetPlayerByActorNo(player.ActorNumber);
            if (rumblePlayer?.Controller == null)
                continue;

            var state = rumblePlayer.Controller.TryGetComponent<CustomRig>(out var rig) 
                ? ResolveRigState(rumblePlayer, rig, false) 
                : CustomRig.RigState.Original;

            var props = new Hashtable();
            props[$"{rumblePlayer.Data.GeneralData.PlayFabMasterId}_CAVisibility"] = state == CustomRig.RigState.Rigged;
            PhotonNetwork.LocalPlayer.SetCustomProperties(props);
        }
    }

    // Checks if a specified player is able to see your avatar
    // with their custom properties
    public static VisibilityResult CanPlayerSeeMe(Il2CppPhoton.Realtime.Player viewer)
    {
        if (viewer.CustomProperties == null || Main.instance.localRig == null)
            return VisibilityResult.Unknown;
        
        string masterId = Calls.Players.GetLocalPlayer().Data.GeneralData.PlayFabMasterId;
        if (!viewer.CustomProperties.TryGetValue($"{masterId}_CAVisibility", out var val))
            return VisibilityResult.Unknown;
        
        return val.Unbox<bool>() ? VisibilityResult.Visible : VisibilityResult.Hidden;
    }

    // Wires a rig up to the player's renderer + animator
    public static void ApplyRigToPlayer(Player player, GameObject rig, bool log = true)
    {
        if (player == null || rig == null) return;
        
        player.Controller.GetComponent<CustomRig>().CaptureRig(rig);

        string playerUsername = player.Data.GeneralData.PublicUsername.TrimString();
        var playerRenderer = player.Controller.transform.GetChild(1).GetChild(0).GetComponent<SkinnedMeshRenderer>();
        var rigRenderer = rig.GetComponentInChildren<SkinnedMeshRenderer>(true);

        if (playerRenderer == null || rigRenderer == null) return;

        var playerRigRoot = player.Controller.transform.GetChild(1).GetChild(1);
        
        // It just gets worse
        ApplyRigToSMR(playerRigRoot, rig, player.Controller.transform.GetChild(1).GetComponent<Animator>(), player.Controller.GetComponent<CustomRig>(), visuals: player.Controller.GetSubsystem<PlayerVisuals>());
        
        if (log)
            Log($"Applied custom rig to player {playerUsername}.");
    }

    // Main backbone (literally) of the whole rig system
    // The humanoid system tends to make it a lot easier
    // but if you like pain you can still go the other route
    public static void ApplyRigBones(Animator rigAnimator, Animator rumbleAnimator, RigDefinition defaultBones, Transform rigRoot, Transform rumbleRoot)
    {
        rigRoot.rotation = Quaternion.LookRotation(rumbleRoot.forward, rumbleRoot.up);
        
        if (rigAnimator != null && rigAnimator.isHuman)
        {
            foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone) continue;
            
                var rigBone = rigAnimator.GetBoneTransform(bone);
                var rumbleBone = rumbleAnimator.GetBoneTransform(bone);

                if (rigBone == null || rumbleBone == null)
                    continue;

                // Animators tend to break this for some reason
                // I'll have to find another way if we really want avatar settings
                rigBone.SetParent(rumbleBone, true);
                rigBone.localPosition = Vector3.zero;
                rigBone.localRotation = rigBone.localRotation;
                // rigBone.localRotation = Quaternion.identity;
                rigBone.localScale = Vector3.Scale(rigBone.localScale, rumbleBone.localScale);

                rigBone.gameObject.AddComponent<CustomRigBone>();
            }
        }
        else
        {
            var rumbleBones = rumbleRoot.GetComponentsInChildren<Transform>(true)
                .GroupBy(t => t.name)
                .ToDictionary(g => g.Key, g => g.ToList());
            
            foreach (var rigBone in rigRoot.GetComponentsInChildren<Transform>(true))
            {
                if (rumbleBones.TryGetValue(rigBone.name, out var rumbleMatches))
                {
                    foreach (var rumbleBone in rumbleMatches)
                    {
                        rigBone.SetParent(rumbleBone, true);
                        rigBone.localPosition = Vector3.zero;
                        rigBone.localRotation = Quaternion.identity;
                        rigBone.localScale = Vector3.Scale(rigBone.localScale, rumbleBone.localScale);
                    
                        rigBone.gameObject.AddComponent<CustomRigBone>();
                    }
                }
            }
        }
    }

    public static Transform GetBone(Animator animator, HumanBodyBones bone, Transform rigRoot = null, string boneName = null)
    {
        if (animator != null && animator.isHuman)
            return animator.GetBoneTransform(bone);

        if (rigRoot != null && !string.IsNullOrEmpty(boneName))
        {
            return rigRoot.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name.Equals(boneName, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    // Basically swaps out the in game rig for the custom one
    // Very cursed way of doing it, but im not messing with the VRIK system ever again
    public static void ApplyRigToSMR(Transform skeletonRoot, GameObject rig, Animator rumbleAnimator = null, CustomRig customRig = null, SkinnedMeshRenderer renderer = null, PlayerVisuals visuals = null)
    {
        void ApplyRig(Transform customRigTransform, SkinnedMeshRenderer rigRenderer, SkinnedMeshRenderer playerRenderer, Material originalMaterial)
        {
            if (customRig == null)
            {
                Error("customRig is null");
                return;
            }
            if (skeletonRoot == null)
            {
                Error("skeletonRoot is null");
                return;
            }
            if (playerRenderer == null)
            {
                Error("playerRenderer is null");
                return;
            }
            if (rigRenderer == null)
            {
                Error("rigRenderer is null");
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

            rumbleAnimator.enabled = false;
            visuals?.GetComponent<VRIK>()?.FixTransforms();

            foreach (var t in rig.GetComponentsInChildren<Collider>(true))
                GameObject.Destroy(t);

            // The funny
            Animator customRigAnimator = customRigTransform.GetComponentInParent<Animator>();
            RigDefinition defaultBones = rumbleAnimator?.GetComponent<RigDefinition>();
            ApplyRigBones(
                customRigAnimator,
                rumbleAnimator,
                defaultBones,
                rig.transform,
                skeletonRoot
            );

            foreach (var collider in customRigTransform.GetComponentsInChildren<Collider>(true))
                GameObject.Destroy(collider);

            foreach (var obj in rig.GetComponentsInChildren<Transform>(true))
            {
                obj.gameObject.layer = customRig.Config.swapOriginalMesh ? 23 : 0;
            }

            if (rigRenderer.sharedMesh != null)
                if (customRig.Config.swapOriginalMesh) playerRenderer.sharedMesh = rigRenderer.sharedMesh;
            else
                Warning("rigRenderer.sharedMesh is null");

            if (rigRenderer.bones is { Length: > 0 })
            {
                if (customRig.Config.swapOriginalMesh)
                {
                    playerRenderer.bones = rigRenderer.bones;
                    customRig.RigBones = rigRenderer.bones;
                }
            }
            else
            {
                Warning("rigRenderer.bones array is null or empty");
            }

            if (playerRenderer.material == null)
            {
                Error("playerRenderer.material is null");
                return;
            }
            if (rigRenderer.material == null)
            {
                Error("rigRenderer.material is null");
                return;
            }

            var renderers = rig.GetComponentsInChildren<Renderer>(true);

            int globalIndex = 0;
            foreach (var r in renderers)
            {
                var mats = r.materials;
                var newMats = new Material[mats.Length];

                if (r is SkinnedMeshRenderer smr)
                    smr.updateWhenOffscreen = true;

                for (int localIndex = 0; localIndex < mats.Length; localIndex++, globalIndex++)
                {
                    Material original = mats[localIndex];
                    Material mat;

                    bool isPlayerShader = customRig.Config.playerShaderSlots.Contains(globalIndex);

                    if (isPlayerShader)
                    {
                        var baseMap = original.HasProperty("_BaseMap") ? original.GetTexture("_BaseMap") : null;
                        mat = new Material(customRig.OriginalMaterial);
                        if (baseMap != null)
                            mat.SetTexture("_ColorAtlas", baseMap);
                    }
                    else
                    {
                        mat = new Material(original);
                    }

                    if (mat.HasProperty("_IsLocal"))
                        mat.SetFloat("_IsLocal", customRig.IsLocal ? 1f : 0f);

                    newMats[localIndex] = mat;
                }

                if (r == rigRenderer && customRig.Config.swapOriginalMesh)
                    playerRenderer.materials = newMats;
                else
                    r.materials = newMats;
            }
            
            if (!customRig.Config.swapOriginalMesh)
                playerRenderer.material = customRig.OriginalMaterial;

            if (customRig != null)
            {
                if (playerRenderer.material != null)
                {
                    customRig.RigMaterials = new Material[playerRenderer.materials.Length];
                    for (var index = 0; index < playerRenderer.materials.Count; index++)
                    {
                        var mat = playerRenderer.materials[index];
                        customRig.RigMaterials[index] = mat;
                        
                        mat.hideFlags = HideFlags.DontUnloadUnusedAsset | HideFlags.HideAndDontSave;
                    }
                }
                else
                {
                    Warning("playerRenderer.material is null while assigning to customRigComp");
                }
            
                if (rigRenderer.sharedMesh != null)
                {
                    customRig.RigMesh = UnityEngine.Object.Instantiate(rigRenderer.sharedMesh);
                    customRig.RigMesh.hideFlags = HideFlags.DontUnloadUnusedAsset | HideFlags.HideAndDontSave;
                }
                else
                {
                    Warning("rigRenderer.sharedMesh is null while assigning to customRigComp");
                }
            }

            if (rigRenderer.gameObject != null && customRig.Config.swapOriginalMesh)
                GameObject.Destroy(rigRenderer.gameObject);
            
            rumbleAnimator.enabled = true;
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
}

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

// This doesn't seem to work anymore for some reason
// Not sure if ill add it back.
[RegisterTypeInIl2Cpp]
public class GrabbableObject : MonoBehaviour
{
    public Transform leftHand;
    public Transform rightHand;
    public Player player;

    public Vector3 originalPosition;
    public Quaternion originalRotation;
    public Transform originalParent;

    private Transform currentHand;
    private bool isGrabbed;
    
    private bool isLeftTouching;
    private bool isRightTouching;
    private bool wasLeftGripHeldLastFrame;
    private bool wasRightGripHeldLastFrame;

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
        bool leftGrip = Calls.ControllerMap.LeftController.GetGrip() > 0.5f;
        bool rightGrip = Calls.ControllerMap.RightController.GetGrip() > 0.5f;
        
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

// Though these I will definitely add back, when I have the chance
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