using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CustomAvatars
{
    public enum ParamType
    {
        Bool,
        Float,
        Int
    }

    [CreateAssetMenu(menuName = "CustomAvatars/Avatar Descriptor", fileName = "NewAvatarDescriptor")]
    public class AvatarDescriptor : ScriptableObject
    {
        public enum BlinkType
        {
            None,
            Single,
            LeftRight
        }

        public bool blendshapesInitialized = false;

        public GameObject avatarPrefab;
        // public RuntimeAnimatorController animatorController;
        // public string animatorControllerName;
        public bool swapOriginalMesh = true;
        public List<int> playerShaderSlots = new();
        public int bodyShaderSlot = new();

        public List<BlendshapeDefault> defaultBlendshapes = new();

        public int jawOpenBlendshape;
        public float voiceMultiplier;
        public EyeSettings eyeSettings = new EyeSettings();

        public List<AnimatorParam> parameters = new();
    }

    [System.Serializable]
    public class AnimatorParam
    {
        public string name;
        public ParamType type;
        public bool networked = true;
        public string uiLabel;
    }

    [System.Serializable]
    public class EyeSettings
    {
        public AvatarDescriptor.BlinkType blinkType;
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

    [System.Serializable]
    public struct BlendshapeDefault
    {
        public string name;
        public int index;
        public float weight;
    }
}