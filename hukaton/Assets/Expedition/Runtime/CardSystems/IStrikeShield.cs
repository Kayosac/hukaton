using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// A card zone that keeps creatures' strikes off whoever stands inside it (the Bubble). Asked by
    /// <see cref="WorldCreature"/> when a strike connects: true blocks the blow (no damage, no push), and the shield
    /// shows the blow on itself.
    /// </summary>
    public interface IStrikeShield
    {
        bool BlocksStrike(WorldCreature attacker, ICombatant victim, Vector3 point);
    }
}
