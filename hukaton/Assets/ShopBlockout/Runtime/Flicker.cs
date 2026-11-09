using UnityEngine;

namespace Hakaton.ShopBlockout
{
    /// <summary>
    /// Living brightness for fire and screens: wobbles a light's intensity and the emission of glowing renderers.
    /// The curve depends only on the settings, the seed and time, so a lantern's light and its glass
    /// (or the CRT light and the screen) given the same values flicker together.
    /// </summary>
    public sealed class Flicker : MonoBehaviour
    {
        [Tooltip("Optional light whose intensity follows the curve.")]
        public Light target;
        [Tooltip("Renderers whose emission follows the curve (lantern glass, screen).")]
        public Renderer[] emissive = new Renderer[0];

        [Header("Curve")]
        [Range(0, 1)] public float amount = .15f;
        [Tooltip("How fast the slow wobble moves.")]
        [Min(0)] public float speed = 1.5f;
        [Tooltip("Share of the fast, nervous layer on top of the slow wobble.")]
        [Range(0, 1)] public float jitter = .35f;
        [Tooltip("Chance of a short brightness dip (CRT); 0 for fire.")]
        [Range(0, 1)] public float dipChance;
        [Range(0, 1)] public float dipDepth = .35f;
        [Tooltip("Light position wobble in metres: moving shadows of a live flame.")]
        [Min(0)] public float drift;
        public float seed = 1;

        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        float baseIntensity;
        Vector3 basePosition;
        Color[] baseEmission;
        MaterialPropertyBlock block;

        void OnEnable()
        {
            if (target)
            {
                baseIntensity = target.intensity;
                basePosition = target.transform.localPosition;
            }
            block ??= new MaterialPropertyBlock();
            baseEmission = new Color[emissive.Length];
            for (int i = 0; i < emissive.Length; i++)
                baseEmission[i] = emissive[i] && emissive[i].sharedMaterial ? emissive[i].sharedMaterial.GetColor(EmissionColor) : Color.black;
        }

        void OnDisable()
        {
            if (target)
            {
                target.intensity = baseIntensity;
                target.transform.localPosition = basePosition;
            }
            foreach (var renderer in emissive) if (renderer) renderer.SetPropertyBlock(null);
        }

        /// <summary>Brightness multiplier at a moment in time (1 = authored brightness).</summary>
        public float Evaluate(float time)
        {
            float t = time * speed;
            float slow = Mathf.PerlinNoise(seed, t) * 2 - 1;
            float fast = Mathf.PerlinNoise(seed + 17.3f, t * 4.7f) * 2 - 1;
            float value = 1 + amount * Mathf.Lerp(slow, fast, jitter);
            if (dipChance > 0 && Mathf.PerlinNoise(seed + 41.9f, t * 2.3f) > 1 - dipChance * .5f)
                value *= 1 - dipDepth;
            return Mathf.Max(0, value);
        }

        void LateUpdate()
        {
            float factor = Evaluate(Time.time);
            if (target)
            {
                target.intensity = baseIntensity * factor;
                if (drift > 0)
                {
                    float t = Time.time * speed * 1.3f;
                    target.transform.localPosition = basePosition + drift * new Vector3(
                        Mathf.PerlinNoise(seed + 3.1f, t) * 2 - 1, Mathf.PerlinNoise(seed + 5.7f, t) * 2 - 1, Mathf.PerlinNoise(seed + 9.2f, t) * 2 - 1);
                }
            }
            for (int i = 0; i < emissive.Length; i++)
            {
                if (!emissive[i]) continue;
                emissive[i].GetPropertyBlock(block);
                block.SetColor(EmissionColor, baseEmission[i] * factor);
                emissive[i].SetPropertyBlock(block);
            }
        }
    }
}
