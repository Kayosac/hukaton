using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// The noise source left by «Манок» (prefab <c>Assets/Game/Prefabs/Cards/Lure/Lure.prefab</c>): chirps at the point
    /// of impact for 6 / 12 / 20 s. Every call it draws every creature within hearing (20 / 40 / 70 m), whatever its
    /// temperament and even mid-fight (<see cref="WorldCreature.Investigate"/>): out of its territory, to the noise,
    /// deaf to the player while it sounds. A creature hurt while listening ignores this noise afterwards. A small ring on
    /// the ground and a sound wave at each chirp mark it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LureNoise : CardZoneBehaviour
    {
        [Header("Noise")]
        [Tooltip("Time between two chirps, s.")]
        [Min(.1f)] public float chirpInterval = .7f;
        [Tooltip("How often calm creatures in hearing are called, s.")]
        [Min(.05f)] public float callInterval = .5f;
        public Vector2 chirpPitch = new Vector2(1.2f, 1.55f);
        public SfxCue chirp;

        [Header("Look")]
        [Tooltip("Looping marker on the ground (Retro Arsenal), stopped when the noise ends.")]
        public ParticleSystem marker;
        [Tooltip("A sound wave at each chirp (Retro Arsenal).")]
        public VfxBurst pulse;
        [Min(0)] public float pulseScale = .6f;
        public Color pulseTint = new Color(1f, .9f, .45f);
        [Min(0)] public float fadeSeconds = .8f;

        float until, hearing = 20, nextChirp, nextCall, endedAt = -1;
        int called;

        public override CardZoneKind Kind => CardZoneKind.Lure;
        public bool Sounding => World && endedAt < 0;
        public float Remaining => Sounding ? Mathf.Max(0, until - Time.time) : 0;
        public float HearingRadius => hearing;
        /// <summary>Creatures that answered the last call (for tests and tuning).</summary>
        public int Answered => called;

        /// <summary>Numbers from the card asset, right after spawning.</summary>
        public void Setup(float seconds, float hearingRadius)
        {
            until = Time.time + Mathf.Max(0, seconds);
            hearing = Mathf.Max(0, hearingRadius);
            nextChirp = nextCall = 0;
        }

        void Update()
        {
            if (!World) return;
            if (Sounding)
            {
                if (Time.time >= nextChirp)
                {
                    nextChirp = Time.time + chirpInterval * Random.Range(.85f, 1.15f);
                    World.Play(chirp, transform.position, 1, Random.Range(chirpPitch.x, chirpPitch.y));
                    World.Spawn(pulse, transform.position + Vector3.up * .15f, Quaternion.identity, pulseTint, pulseScale);
                }
                if (Time.time >= nextCall)
                {
                    nextCall = Time.time + callInterval;
                    Call();
                }
                if (Time.time >= until)
                {
                    endedAt = Time.time;
                    World.Zones.Remove(this);
                    if (marker) marker.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }
            else if (Time.time - endedAt >= fadeSeconds && (!marker || !marker.IsAlive(true))) Destroy(gameObject);
        }

        void Call()
        {
            called = 0;
            float remaining = until - Time.time;
            if (remaining <= 0 || World.ActiveCreatures == null) return;
            Vector3 here = transform.position;
            foreach (var creature in World.ActiveCreatures)
            {
                if (!creature || !creature.isActiveAndEnabled || !creature.Alive) continue;
                if (Vector3.Distance(creature.Position, here) > hearing) continue;
                // Every creature, aggressive and attacking ones too; the card pulls them out of their territory.
                if (creature.Investigate(here, remaining, pull: true, distract: true)) called++;
            }
        }
    }
}
