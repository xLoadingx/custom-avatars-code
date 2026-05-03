using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Il2CppPhoton.Pun;
using Il2CppRUMBLE.Players;
using Il2CppTMPro;
using MelonLoader;
using RumbleModdingAPI.RMAPI;
using UnityEngine;
using Player = Il2CppPhoton.Realtime.Player;

namespace CustomAvatars;

[RegisterTypeInIl2Cpp]
public class CustomRig : MonoBehaviour
{
    public GameObject Root;
    public SkinnedMeshRenderer MainRenderer;
    public PlayerController playerController;
    public Player photonPlayer;
    public AvatarDescriptorExport config;

    public bool IsLoading;
    public string path;

    public GameObject loadingBar;
    public Material loadingBarMat;
    public TextMeshPro loadingBarText;
    public float displayedProgress;

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

        var bones = transform.GetComponentsInChildren<CustomRigBone>(true);

        foreach (var b in bones)
        {
            if (b != null)
                Destroy(b.gameObject);
        }
        
        playerRenderer.sharedMesh = OriginalMesh;
        playerRenderer.materials = new[] { OriginalMaterial };
        playerRenderer.bones = OriginalBones;
        playerRenderer.rootBone = OriginalRootBone;

        if (loadingBar != null)
            Destroy(loadingBar);
        
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
            
            // Make root follow player visibility
            Root.SetActive(playerController.gameObject.activeInHierarchy);
        }
    }

    public void UpdateLoadingProgress(float progress)
    {
        if (!IsLoading && (loadingBarMat == null || loadingBarText != null)) return;
        
        displayedProgress = Mathf.Lerp(
            displayedProgress,
            progress,
            Time.deltaTime * 10f
        );

        loadingBarText.text = $"{progress * 100f:F}%";
        loadingBarMat.SetFloat("_RC_Current", displayedProgress);
    }

    public void EnsureLoadingBar()
    {
        var barParent = GetComponent<PlayerController>().PlayerUI.transform.GetChild(1);

        var bar = Instantiate(RigManager.loadingBarPrefab, barParent, false);
        var mat = bar.transform.GetChild(0).GetComponent<MeshRenderer>().material;
        var text = bar.transform.GetChild(1).GetComponent<TextMeshPro>();
        
        loadingBar = bar;
        loadingBarMat = mat;
        loadingBarText = text;
        displayedProgress = 0f;
    }

    public bool IsVisibleToOthers()
    {
        if (!photonPlayer.CustomProperties.TryGetValue("CA:visibility", out var visible))
            return false;

        return visible.Unbox<bool>();
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
            try
            {
                smr.SetBlendShapeWeight(eyes.blinkBlendshape, weight);
            }
            catch (Exception e) {
            }
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
    public EyeSettings eyeSettings = new();
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