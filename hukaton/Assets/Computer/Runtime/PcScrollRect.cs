using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Hakaton.Computer
{
    /// <summary>A scroll view on the PC: the wheel scrolls; dragging belongs to the cards (merging), not to the view.</summary>
    public sealed class PcScrollRect : ScrollRect
    {
        public override void OnBeginDrag(PointerEventData eventData) { }
        public override void OnDrag(PointerEventData eventData) { }
        public override void OnEndDrag(PointerEventData eventData) { }
        public override void OnInitializePotentialDrag(PointerEventData eventData) { }
    }
}
