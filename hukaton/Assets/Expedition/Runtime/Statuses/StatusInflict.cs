using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// A debuff an attack (a creature's strike, a projectile, a trap) puts on the player, with its own chance and,
    /// optionally, its own duration and strength instead of the status's defaults.
    /// </summary>
    [Serializable]
    public struct StatusInflict
    {
        public PlayerStatusDefinition status;
        [Tooltip("Chance per hit, 0–1.")]
        [Range(0, 1)] public float chance;
        [Tooltip("Seconds; 0: the status's own duration.")]
        [Min(0)] public float seconds;
        [Tooltip("0: the status's own strength (see PlayerStatusDefinition.strength for what it means).")]
        public float strength;

        public StatusInflict(PlayerStatusDefinition status, float chance, float seconds = 0, float strength = 0)
        {
            this.status = status;
            this.chance = chance;
            this.seconds = seconds;
            this.strength = strength;
        }

        /// <summary>Rolls every entry against <paramref name="roll"/> (0–1 values) and applies the ones that pass.</summary>
        public static int ApplyAll(IReadOnlyList<StatusInflict> list, PlayerStatuses statuses, Func<float> roll = null)
        {
            if (list == null || !statuses) return 0;
            int applied = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var entry = list[i];
                if (!entry.status || entry.chance <= 0) continue;
                float value = roll != null ? roll() : UnityEngine.Random.value;
                if (entry.chance < 1 && value >= entry.chance) continue;
                if (statuses.Apply(entry.status, entry.seconds, entry.strength)) applied++;
            }
            return applied;
        }
    }

    /// <summary>A card zone that keeps debuffs off the player standing inside it (the Bubble).</summary>
    public interface IPlayerStatusShield
    {
        bool ShieldsPlayer(Vector3 feet);
    }
}
