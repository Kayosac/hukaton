using System;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// Marks an ordinary tree of the world that «Оживление дерева» can bring to life (on the prefab root: Pine.prefab).
    /// The card hides this scene object for the rest of the outing (<see cref="ExpeditionWorld.HideForOuting"/>) and
    /// puts an ally in its place; the world shows the tree again when the outing ends.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AwakenableTree : MonoBehaviour
    {
        [Tooltip("Where the ally stands up: the foot of the trunk. Empty: this transform.")]
        public Transform foot;

        /// <summary>
        /// Raised when a tree comes alive, right BEFORE its scene object is hidden: whatever a card fastened to the tree
        /// (a rope) lets go here. Arguments: the tree, the ally that replaces it. See tmp/new-cards/API.md § 13.
        /// </summary>
        public static event Action<AwakenableTree, ICombatant> Awakened;

        /// <summary>The tree is in the world now (not alive and walking this outing).</summary>
        public bool Standing => isActiveAndEnabled;

        public Vector3 FootPosition => foot ? foot.position : transform.position;

        /// <summary>Does this collider (or transform) belong to this tree?</summary>
        public bool Owns(Component part) => part && part.transform.IsChildOf(transform);

        internal static void RaiseAwakened(AwakenableTree tree, ICombatant ally)
        {
            var handlers = Awakened;
            if (handlers == null) return;
            foreach (Action<AwakenableTree, ICombatant> handler in handlers.GetInvocationList())
            {
                try { handler(tree, ally); }
                catch (Exception exception) { Debug.LogException(exception, tree); }
            }
        }
    }
}
