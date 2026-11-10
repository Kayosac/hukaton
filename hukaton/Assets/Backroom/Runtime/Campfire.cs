using UnityEngine;

namespace Hakaton.Backroom
{
    /// <summary>
    /// A campfire for the brazier's bowl, put together from the Retro Arsenal pixel flames: one tall flame in the
    /// middle (with its sparks) and a ring of shorter tongues around it, each on its own beat and leaning its own
    /// way, and a warm light that breathes with them. Built on first enable; scale the object to size it.
    /// </summary>
    public sealed class Campfire : MonoBehaviour
    {
        [Tooltip("The tall flame in the middle: Retro Arsenal FireTorch (flame and sparks; its smoke is switched off).")]
        public GameObject heart;
        [Tooltip("The shorter tongues around it: a bare flame (Candle flame).")]
        public GameObject tongue;

        [Header("Shape")]
        [Min(0)] public int tongues = 6;
        [Tooltip("How far the tongues spread from the middle, metres at scale 1.")]
        [Min(.01f)] public float radius = .15f;
        [Tooltip("Heart flame scale against the FireTorch prefab.")]
        [Min(.01f)] public float heartScale = .95f;
        [Tooltip("Tongue scale against the FireTorch prefab (Candle flame is 0.09 of it).")]
        [Min(.01f)] public float tongueScale = .55f;
        public int seed = 7;

        [Header("Light")]
        public Color lightColor = new Color(1, .55f, .22f);
        [Min(0)] public float lightIntensity = 2.2f, lightRange = 2.4f;

        readonly System.Collections.Generic.List<Transform> flames = new System.Collections.Generic.List<Transform>();
        readonly System.Collections.Generic.List<float> sizes = new System.Collections.Generic.List<float>();
        Light glow;
        float lightBase = 1;
        bool built;

        /// <summary>The flames as a whole: 0 out, 1 the resting size, above 1 a flare.</summary>
        public float Strength { get; set; } = 1;

        void OnEnable() => Build();

        public void Build()
        {
            if (built) return;
            built = true;
            var random = new System.Random(seed);
            float R() => (float)random.NextDouble();

            // Flames: the heart, then the tongues around it, all out of step.
            if (heart) Flame(heart, Vector3.zero, heartScale, R);
            for (int i = 0; i < tongues && tongue; i++)
            {
                // Two rings: the inner tongues tall, the outer ones lower, toward the rim.
                bool outer = i % 2 == 1;
                float around = (i + R() * .6f) / tongues * Mathf.PI * 2;
                float reach = radius * (outer ? .8f + R() * .25f : .35f + R() * .2f);
                var at = new Vector3(Mathf.Cos(around), 0, Mathf.Sin(around)) * reach;
                float size = tongueScale * (outer ? .6f + R() * .25f : .85f + R() * .3f);
                Flame(tongue, at, size, R);
            }

            var lightObject = new GameObject("Glow");
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.localPosition = new Vector3(0, radius * 1.6f, 0);
            glow = lightObject.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = lightColor;
            glow.shadows = LightShadows.None;
            glow.range = lightRange * transform.lossyScale.x;
            glow.intensity = lightIntensity;
            lightBase = lightIntensity;
        }

        void Flame(GameObject prefab, Vector3 at, float scale, System.Func<float> random)
        {
            var flame = Instantiate(prefab, transform, false).transform;
            flame.localPosition = at;
            flame.localRotation = Quaternion.identity;
            flame.localScale = Vector3.one * scale;
            flames.Add(flame);
            sizes.Add(scale);
            foreach (var sound in flame.GetComponentsInChildren<AudioSource>(true)) sound.enabled = false;
            // A grey ball of smoke reads as a stone over a pixel fire: no smoke.
            foreach (Transform child in flame)
                if (child.name.Contains("Smoke")) child.gameObject.SetActive(false);
            foreach (var system in flame.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = system.main;
                main.simulationSpeed *= .85f + random() * .3f;
            }
            // Out of step with the others: start somewhere inside its loop.
            var root = flame.GetComponent<ParticleSystem>();
            if (root)
            {
                root.Simulate(random() * .4f, true, true);
                root.Play(true);
            }
        }

        void Update() => Apply();

        /// <summary>Sizes the flames and the light to <see cref="Strength"/> (called every frame; tools call it directly).</summary>
        public void Apply()
        {
            for (int i = 0; i < flames.Count; i++)
                if (flames[i]) flames[i].localScale = Vector3.one * sizes[i] * Mathf.Max(0, Strength);
            if (!glow) return;
            float t = Time.time * 7;
            float flicker = .8f + (Mathf.PerlinNoise(t, 1.3f) - .5f) * .5f + (Mathf.PerlinNoise(t * 2.3f, 7) - .5f) * .25f;
            glow.intensity = lightBase * flicker * Mathf.Clamp(Strength, 0, 2.5f);
            glow.range = lightRange * transform.lossyScale.x;
        }
    }
}
