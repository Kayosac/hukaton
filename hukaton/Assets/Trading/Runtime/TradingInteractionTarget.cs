using UnityEngine;

namespace Hakaton.Trading.Unity
{
    public enum TradingTargetKind { Counter, DisplaySlot, Door }

    /// <summary>Authored interaction affordance. Focus requires a clear camera ray within reach.</summary>
    public sealed class TradingInteractionTarget : MonoBehaviour
    {
        public TradingTargetKind kind;
        [Min(.1f)] public float reach = 4.5f;
    }
}
