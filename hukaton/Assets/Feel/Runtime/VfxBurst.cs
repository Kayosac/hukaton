using UnityEngine;

namespace Hakaton.Feel
{
    /// <summary>
    /// One-shot authored effect (mostly Retro Arsenal particle prefabs): plays its particle systems, an optional
    /// light flash, optionally follows a target, and destroys itself when its lifetime runs out.
    /// </summary>
    public sealed class VfxBurst : MonoBehaviour
    {
        public ParticleSystem[] systems;
        [Tooltip("Systems whose start colour follows the tint passed to Play (e.g. the card's element).")]
        public ParticleSystem[] tinted;
        public Light flash;
        [Min(0)] public float flashIntensity = 3, flashSeconds = .25f;
        [Min(.1f)] public float lifetime = 2;
        float age;
        Transform follow;
        Vector3 followOffset;

        /// <summary>Instantiates and plays an effect; returns null when no prefab is assigned.</summary>
        public static VfxBurst Spawn(VfxBurst prefab, Vector3 position, Quaternion rotation, Transform parent, Color tint, float scale = 1)
        {
            if (!prefab) return null;
            var burst = Instantiate(prefab, position, rotation, parent);
            burst.Play(tint, scale);
            return burst;
        }

        public void Play(Color tint, float scale = 1)
        {
            transform.localScale = Vector3.one * scale;
            if (tinted != null)
                foreach (var system in tinted)
                {
                    if (!system) continue;
                    var main = system.main;
                    main.startColor = new ParticleSystem.MinMaxGradient(tint, Color.Lerp(tint, Color.white, .45f));
                }
            if (flash) { flash.color = tint; flash.intensity = flashIntensity; }
            foreach (var system in systems) if (system) system.Play(true);
        }

        /// <summary>Keeps the effect alive for at least this many more seconds (e.g. while a creature is stunned).</summary>
        public void HoldFor(float seconds) => lifetime = Mathf.Max(lifetime, age + seconds);

        /// <summary>Stops emitting and lets the live particles fade before the effect is destroyed.</summary>
        public void Release(float fadeSeconds = .6f)
        {
            foreach (var system in systems) if (system) system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            lifetime = Mathf.Min(lifetime, age + fadeSeconds);
        }

        /// <summary>Rides along with a moving target (a creature's head, a visitor).</summary>
        public void Follow(Transform target, Vector3 offset)
        {
            follow = target;
            followOffset = offset;
            if (target) transform.position = target.position + offset;
        }

        void LateUpdate()
        {
            if (follow) transform.position = follow.position + followOffset;
        }

        void Update()
        {
            age += Time.deltaTime;
            if (flash) flash.intensity = Mathf.Max(0, flashIntensity * (1 - age / Mathf.Max(.01f, flashSeconds)));
            if (age >= lifetime) Destroy(gameObject);
        }
    }
}
