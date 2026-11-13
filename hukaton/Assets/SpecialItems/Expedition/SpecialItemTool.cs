using Hakaton.Expeditions;
using UnityEngine;

namespace Hakaton.SpecialItems
{
    /// <summary>
    /// What one special item does on an outing once it is taken out with Q. Every item is different; this is only how
    /// <see cref="SpecialItemsHand"/> takes it out and puts it away.
    /// </summary>
    public abstract class SpecialItemTool : MonoBehaviour
    {
        public SpecialItemDefinition definition;

        public SpecialItemsHand Hand { get; internal set; }
        protected ExpeditionWorld World => Hand ? Hand.world : null;
        protected SpecialItemsHost Items => Hand ? Hand.items : null;

        /// <summary>In the hand, or on its way there.</summary>
        public abstract bool IsOut { get; }

        /// <summary>How to use it while it is in the hand (HUD, bottom left), one action per line.</summary>
        public virtual string ControlHint => null;

        /// <summary>Take it out (animated).</summary>
        public abstract void Draw();

        /// <summary>Put it away (animated; <paramref name="instant"/>: at once, the outing is over).</summary>
        public abstract void Stow(bool instant = false);

        /// <summary>A new outing began: charges are full again, it starts put away.</summary>
        public virtual void OutingBegan() { }

        /// <summary>The outing is over: put away at once, nothing it did lasts.</summary>
        public virtual void OutingEnded() => Stow(true);
    }
}
