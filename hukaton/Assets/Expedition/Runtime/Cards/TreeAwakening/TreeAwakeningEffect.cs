using Hakaton.Decks.Unity;
using UnityEngine;
using UnityEngine.AI;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Оживление дерева» (земля*, ранг 4/5*). A hit on an ordinary tree of the world (<see cref="AwakenableTree"/>)
    /// turns that very tree into an ally (<see cref="TreeAlly"/>) until the end of the outing or its death: the standing
    /// trunk is hidden for the outing and the ally stands up in its place, no extra copy. Every copy awakens another
    /// tree; there is no squad limit. A hit on anything else, or on a tree that is already walking, spends the card
    /// with the miss feedback (no second ally, no healing). Health 12 (upgraded 18), branch strike 2 within 2.5 m,
    /// cycle 2.5 s with a 0.8 s wind-up, μ = 3. The ally looks like the owner's «Зловещее ожившее дерево» (its own child prefab of the ally).
    /// Rules and numbers: tmp/new-cards/cards.md; shared systems: tmp/new-cards/API.md (§ 13 for this card).
    /// </summary>
    public sealed class TreeAwakeningEffect : CardEffect, ICardStatLine
    {
        [Header("Board numbers (I / II / III)")]
        [Tooltip("Ally health (board*: 12 → 18).")]
        public LevelInt health = new LevelInt(12, 20, 32);
        [Tooltip("Branch strike damage (board*).")]
        public LevelInt damage = new LevelInt(2, 3, 4);
        [Tooltip("Strike reach, m.")]
        [Min(0)] public float reach = 2.5f;
        [Tooltip("Attack cycle and its wind-up, s.")]
        [Min(0)] public float attackCycle = 2.5f, windup = .8f;
        [Tooltip("Ally mass μ.")]
        [Min(0)] public float mass = 3;
        [Header("Ally")]
        [Tooltip("The awakened tree (gameplay root; its look is a swappable child prefab).")]
        public TreeAlly allyPrefab;
        [Tooltip("How far from the foot of the trunk a walkable point for the ally is searched, m.")]
        [Min(.5f)] public float standUpSearch = 3;
        [Header("Notices")]
        public string notATreeNotice = "Нужно попасть в дерево";
        public string alreadyAliveNotice = "Это дерево уже живое";

        public override void Apply(in ImpactContext c)
        {
            var world = c.World;
            if (c.TargetAs<TreeAlly>())
            {
                Feedback(c, false);
                world.Announce(alreadyAliveNotice, 1.6f);
                return;
            }
            var tree = c.TargetAs<AwakenableTree>();
            if (!tree || !tree.Standing || !allyPrefab)
            {
                if (!allyPrefab) Debug.LogWarning("Tree awakening: no ally prefab assigned.", this);
                Feedback(c, false);
                world.Announce(notATreeNotice, 1.6f);
                return;
            }

            Vector3 foot = tree.FootPosition;
            Vector3 position = NavMesh.SamplePosition(foot, out var hit, standUpSearch, NavMesh.AllAreas) ? hit.position : foot;
            Vector3 toPlayer = c.Motor.transform.position - position;
            toPlayer.y = 0;
            var rotation = toPlayer.sqrMagnitude > .01f ? Quaternion.LookRotation(toPlayer) : tree.transform.rotation;
            var ally = world.SpawnForOuting(allyPrefab, position, rotation);
            if (!ally) { Feedback(c, false); return; }
            ally.Configure(c.At(health), c.At(damage), reach, attackCycle, windup, mass);
            // Ropes and other things fastened to the tree let go before it disappears.
            AwakenableTree.RaiseAwakened(tree, ally);
            world.HideForOuting(tree.gameObject);
            ally.Rise(tree, position);
            world.Allies.Add(ally);
            Feedback(c, true);
        }

        public bool TryGetStat(out string label, out LevelFloat values, out string unit)
        {
            label = "Здоровье";
            values = health;
            unit = "";
            return true;
        }
    }
}
