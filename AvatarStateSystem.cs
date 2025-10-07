using UnityEngine;
using static UnityEngine.Mathf;

namespace CustomAvatars
{
    public class AvatarStateSystem
    {
        public event Action<string, bool> OnBoolChanged;
        public event Action<string, float> OnFloatChanged;

        private readonly Dictionary<string, bool> bools = new();
        private readonly Dictionary<string, float> floats = new();

        public void SetBool(string name, bool value)
        {
            if (bools.TryGetValue(name, out var old) && old == value)
                return;

            bools[name] = value;
            OnBoolChanged?.Invoke(name, value);
        }

        public bool GetBool(string name)
            => bools.TryGetValue(name, out var v) && v;

        public void SetFloat(string name, float value)
        {
            if (floats.TryGetValue(name, out var old) && Approximately(old, value))
                return;

            floats[name] = value;
            OnFloatChanged?.Invoke(name, value);
        }

        public float GetFloat(string name)
            => floats.TryGetValue(name, out var v) ? v : 0f;
    }

    [Serializable]
    public class ShaderState
    {
        public Renderer renderer;
        public string property = "_EmissionStrength";
        // Applies to all materials with the valid property if -1
        public int materialIndex = -1;

        public void ApplyFloat(float value)
        {
            if (!renderer) return;

            var mats = renderer.materials;
            if (materialIndex >= 0 && materialIndex < mats.Length)
            {
                if (mats[materialIndex] != null && mats[materialIndex].HasFloat(property))
                    mats[materialIndex].SetFloat(property, value);
            }
            else
            {
                foreach (var m in mats)
                {
                    if (m.HasProperty(property))
                        m.SetFloat(property, value);
                }
            }
        }

        public void ApplyBool(bool value) => ApplyFloat(value ? 1f : 0f);
    }

    [Serializable]
    public class BlendshapeState
    {
        public SkinnedMeshRenderer smr;
        public int index = -1;

        public void Apply(float weight)
        {
            if (smr && index >= 0)
                smr.SetBlendShapeWeight(index, Clamp(weight, 0f, 100f));
        }

        public void Apply(bool on) => Apply(on ? 100f : 0f);
    }
}