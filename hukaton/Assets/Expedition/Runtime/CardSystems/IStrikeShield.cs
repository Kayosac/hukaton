using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// A card zone that keeps fighters' blows off whoever stands inside it (the Bubble). Asked by every attacker of
    /// the world — <see cref="WorldCreature"/> strikes, the chest mimic's bite — when its blow connects: true blocks
    /// it (no damage, no push), and the shield shows the blow on itself.
    /// </summary>
    public interface IStrikeShield
    {
        bool BlocksStrike(ICombatant attacker, ICombatant victim, Vector3 point);
    }

    /// <summary>
    /// A card zone that fighters moving by their own script (not a <see cref="WorldCreature"/> body, e.g. the chest
    /// mimic's hops) may not enter: their move targets are pushed out of it.
    /// </summary>
    public interface ICardBarrier
    {
        /// <summary>The point moved out of the zone for a body of this radius (unchanged when already outside).</summary>
        Vector3 KeepOut(Vector3 point, float bodyRadius);
    }

    /// <summary>Asks every shield and barrier among the world's card zones.</summary>
    public static class CardBarriers
    {
        /// <summary>A card shield takes this blow (the attacker deals no damage).</summary>
        public static bool BlockStrike(ExpeditionWorld world, ICombatant attacker, ICombatant victim, Vector3 point)
        {
            if (!world || attacker == null || victim == null) return false;
            var zones = world.Zones.All;
            for (int i = 0; i < zones.Count; i++)
                if (zones[i] is IStrikeShield shield && shield.BlocksStrike(attacker, victim, point)) return true;
            return false;
        }

        /// <summary>A move target kept out of every card barrier.</summary>
        public static Vector3 KeepOut(ExpeditionWorld world, Vector3 point, float bodyRadius)
        {
            if (!world) return point;
            var zones = world.Zones.All;
            for (int i = 0; i < zones.Count; i++)
                if (zones[i] is ICardBarrier barrier) point = barrier.KeepOut(point, bodyRadius);
            return point;
        }
    }
}
