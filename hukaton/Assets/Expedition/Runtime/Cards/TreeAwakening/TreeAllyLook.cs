using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    public enum TreeAllyPose { Awakening, Idle, Walk, Windup, Strike, Recover, Dying }

    /// <summary>What the awakened tree is doing this frame, for its look.</summary>
    public struct TreeAllyLookState
    {
        public TreeAllyPose Pose;
        /// <summary>0 → 1 through the timed poses (awakening, wind-up, strike, recovery, dying).</summary>
        public float Progress;
        /// <summary>Planar speed, m/s, and the gait phase in radians (a step every π).</summary>
        public float Speed, GaitPhase;
        /// <summary>1 right after a hit, fading to 0.</summary>
        public float Hurt;
        /// <summary>0 visible → 1 gone, at the very end of dying.</summary>
        public float Fade;
        /// <summary>World facing of the tree.</summary>
        public Vector3 Heading;
    }

    /// <summary>
    /// The swappable look of the awakened tree: a child prefab under the ally that only presents the state
    /// (gameplay, colliders and sizes stay on <see cref="TreeAlly"/>). The current look is the owner's «Зловещее ожившее
    /// дерево» drawn from its atlas (<see cref="SpriteTreeAllyLook"/>); another look is another child prefab with its own subclass.
    /// </summary>
    public abstract class TreeAllyLook : MonoBehaviour
    {
        /// <summary>Visible height above the feet, m (marks and drips sit above it).</summary>
        public abstract float Height { get; }

        public abstract void Present(in TreeAllyLookState state, float deltaTime);
    }
}
