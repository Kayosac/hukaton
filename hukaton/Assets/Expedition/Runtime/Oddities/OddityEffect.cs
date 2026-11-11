using System;
using Hakaton.Character;
using Hakaton.Expeditions.Cards;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// What an oddity (a ringing payphone, a hissing television) does to the player once used. One asset per effect in
    /// Game/Data/Oddities; <see cref="OddityTable"/> picks one by weight. Add a new effect by deriving from this class.
    /// </summary>
    public abstract class OddityEffect : ScriptableObject
    {
        [Tooltip("Shown on the screen when it happens: short, in Russian.")]
        public string notice;
        [Min(0)] public float noticeSeconds = 3.5f;
        [Tooltip("Played on the player when it happens.")]
        public SfxCue cue;
        [Tooltip("Retro Arsenal effect on the player when it happens.")]
        public VfxBurst burst;
        public Color tint = Color.white;
        [Min(.01f)] public float burstScale = 1;

        /// <summary>Whether it would do anything now (healing a player at full health would not).</summary>
        public virtual bool CanHappen(ExpeditionWorld world) => world && world.Running;

        /// <summary>Makes it happen, with its sound, effect and notice. True when it did something.</summary>
        public bool Apply(ExpeditionWorld world, Oddity source)
        {
            if (!world || !world.Running || !Happen(world, source)) return false;
            Vector3 at = world.motor ? world.motor.transform.position + Vector3.up * .9f : source.transform.position;
            world.Play(cue, at);
            world.Spawn(burst, at, Quaternion.identity, tint, burstScale);
            if (!string.IsNullOrEmpty(notice)) world.Announce(notice, noticeSeconds);
            return true;
        }

        protected abstract bool Happen(ExpeditionWorld world, Oddity source);
    }
}
