using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>Side in a fight: the player with their allies (awakened trees) against the valley's creatures.</summary>
    public enum Faction { Player, Wild }

    public enum DamageKind { Hit, Fire, Lightning, Fall, Other }

    /// <summary>
    /// One hit. <see cref="Amount"/> in the board's damage units: for the player 1 = ½ heart, for creatures and
    /// allies 1 = 1 HP. <see cref="Impulse"/> in impulse units I (Δv = I / μ): a physics impulse for bodies, a
    /// normalized impulse for the player.
    /// </summary>
    public readonly struct DamageInfo
    {
        public readonly int Amount;
        public readonly Vector3 Impulse;
        /// <summary>Where the hit lands.</summary>
        public readonly Vector3 Point;
        /// <summary>Where it came from (the player's camera kicks away from it); null for no direction.</summary>
        public readonly Vector3? Source;
        public readonly DamageKind Kind;
        /// <summary>Who dealt it, when it was a fighter (a creature, an ally); null for the player's cards and hazards.</summary>
        public readonly ICombatant Instigator;

        public DamageInfo(int amount, Vector3 impulse, Vector3 point, Vector3? source = null, DamageKind kind = DamageKind.Hit,
            ICombatant instigator = null)
        {
            Amount = amount;
            Impulse = impulse;
            Point = point;
            Source = source;
            Kind = kind;
            Instigator = instigator;
        }
    }

    /// <summary>Anything that takes damage.</summary>
    public interface IDamageable
    {
        /// <summary>Returns true when the hit was taken (false: dead, invulnerable, not in an outing).</summary>
        bool TakeDamage(in DamageInfo damage);
    }

    /// <summary>
    /// A fighter: the player (<see cref="PlayerVitals"/>), a creature (<see cref="WorldCreature"/>), an ally. Creatures
    /// attack their <see cref="WorldCreature.Target"/>; chain lightning and burning tar hit every fighter regardless of side.
    /// </summary>
    public interface ICombatant : IDamageable
    {
        Faction Faction { get; }
        bool Alive { get; }
        /// <summary>Feet position.</summary>
        Vector3 Position { get; }
        /// <summary>Middle of the body: aim point for chains and strikes.</summary>
        Vector3 Centre { get; }
        /// <summary>Root of the body: statuses (<see cref="WetStatus"/>) live here; hits on its children count as hits on it.</summary>
        Transform Root { get; }
    }

    /// <summary>
    /// A body that is not a <see cref="WorldCreature"/> but takes the creature cards like one: Strike, Heavy strike
    /// and Fire hurt it, Ice freezes it (a chest mimic). Registers itself in <see cref="ExpeditionWorld.Wild"/> so
    /// chain lightning and burning tar find it too.
    /// </summary>
    public interface ICardCreature : ICombatant
    {
        void Freeze(float seconds);
    }

    /// <summary>Fighters a card added to the outing (allies). The player and the scene's creatures are always counted
    /// by <see cref="ExpeditionWorld.Combatants"/> without registering.</summary>
    public sealed class CombatantRegistry
    {
        readonly List<ICombatant> fighters = new List<ICombatant>();

        public IReadOnlyList<ICombatant> All
        {
            get
            {
                fighters.RemoveAll(f => !Combat.IsLive(f));
                return fighters;
            }
        }

        public void Add(ICombatant fighter)
        {
            if (Combat.IsLive(fighter) && !fighters.Contains(fighter)) fighters.Add(fighter);
        }

        public bool Remove(ICombatant fighter) => fighters.Remove(fighter);
        public void Clear() => fighters.Clear();
    }

    public static class Combat
    {
        /// <summary>Not null, not a destroyed Unity object (the object still exists, alive or not).</summary>
        public static bool IsLive(ICombatant fighter) => fighter != null && !(fighter is Object unityObject && !unityObject);

        /// <summary>Exists and is alive.</summary>
        public static bool IsAlive(ICombatant fighter) => IsLive(fighter) && fighter.Alive;

        /// <summary>The fighter a collider belongs to, or null.</summary>
        public static ICombatant Of(Component part) => part ? part.GetComponentInParent<ICombatant>() : null;
    }
}
