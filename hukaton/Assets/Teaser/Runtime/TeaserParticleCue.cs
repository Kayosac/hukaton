using UnityEngine;

namespace Hakaton.Teaser
{
    /// <summary>Plays a particle effect once at a teaser time (fixed random seed, so every render is the same).</summary>
    public sealed class TeaserParticleCue : TeaserTrack
    {
        public ParticleSystem effect;
        public float playSeconds = 1.6f;
        public uint seed = 7;

        bool played;

        void Awake()
        {
            if (!effect) return;
            effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (var ps in effect.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.useAutoRandomSeed = false;
                ps.randomSeed = seed;
                var main = ps.main;
                main.playOnAwake = false;
            }
        }

        public override void Evaluate(float seconds)
        {
            if (!effect || played || seconds < playSeconds) return;
            effect.Play(true);
            played = true;
        }
    }
}
