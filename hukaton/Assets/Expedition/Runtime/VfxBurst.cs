using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>One-shot authored effect: particle systems, an optional light flash. Destroys itself.</summary>
    public sealed class VfxBurst : MonoBehaviour
    {
        public ParticleSystem[] systems;
        [Tooltip("Systems whose start colour follows the tint passed to Play (e.g. the card's element).")]
        public ParticleSystem[] tinted;
        public Light flash;
        [Min(0)] public float flashIntensity = 3, flashSeconds = .25f;
        [Min(.1f)] public float lifetime = 2;
        float age;

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

        void Update()
        {
            age += Time.deltaTime;
            if (flash) flash.intensity = Mathf.Max(0, flashIntensity * (1 - age / Mathf.Max(.01f, flashSeconds)));
            if (age >= lifetime) Destroy(gameObject);
        }
    }
}
