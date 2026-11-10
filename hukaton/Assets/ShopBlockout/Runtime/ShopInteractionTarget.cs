using UnityEngine;

namespace Hakaton.ShopBlockout
{
    public enum ShopServiceKind
    {
        Computer,
        GoblinRope,
        Goblin,
        Brazier
    }

    /// <summary>A saved world target. The caller supplies the first unobstructed focus ray.</summary>
    public sealed class ShopInteractionTarget : MonoBehaviour
    {
        public ShopServiceKind kind;
        [Min(.1f)]
        public float reach = 2.2f;
    }
}
