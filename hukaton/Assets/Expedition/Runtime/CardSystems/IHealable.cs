using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// A fighter other than the player that «Лечение» restores (the awakened tree; owner, 09.11 evening). Health units:
    /// 1 unit = half a heart of the player = 1 HP of a creature or ally, so a Heal worth N half hearts restores N units.
    /// Found by <see cref="HealEffect"/> among <see cref="ExpeditionWorld.Allies"/>.
    /// </summary>
    public interface IHealable : ICombatant
    {
        /// <summary>Restores up to <paramref name="units"/>, never above the maximum. Returns the units restored (0: full or dead).</summary>
        int Heal(int units);

        /// <summary>Does a healing splash of <paramref name="radius"/> at <paramref name="point"/> reach this body?</summary>
        bool WithinHealReach(Vector3 point, float radius);
    }

    /// <summary>Pure healing rules (EditMode-tested).</summary>
    public static class HealRules
    {
        /// <summary>Units actually restored: up to the maximum, nothing to the dead.</summary>
        public static int Restored(int health, int maximum, int units)
            => health <= 0 ? 0 : Mathf.Clamp(maximum - health, 0, Mathf.Max(0, units));

        /// <summary>
        /// The splash reaches a standing body when its vertical axis, from <paramref name="feet"/> up to
        /// <paramref name="height"/>, comes within <paramref name="radius"/> + <paramref name="bodyRadius"/> of the point
        /// (the player: card radius + capsule radius from the capsule's centre).
        /// </summary>
        public static bool Reaches(Vector3 point, float radius, Vector3 feet, float height, float bodyRadius)
        {
            var closest = new Vector3(feet.x, Mathf.Clamp(point.y, feet.y, feet.y + Mathf.Max(0, height)), feet.z);
            float reach = Mathf.Max(0, radius) + Mathf.Max(0, bodyRadius);
            return (point - closest).sqrMagnitude <= reach * reach;
        }
    }
}
