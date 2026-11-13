using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.SpecialItems
{
    /// <summary>One item in the Q menu: a plate (the dialogue button's sprite), the item's picture and its name.</summary>
    public sealed class SpecialItemMenuSlot : MonoBehaviour
    {
        public Image plate, icon;
        public TMP_Text label;
        [Tooltip("The plate's colour: resting, pointed at, and the item already in the hand.")]
        public Color restColour = new Color(1, 1, 1, .75f), highlightColour = Color.white, inHandColour = new Color(.75f, 1, .75f, .9f);
        [Min(1)] public float highlightScale = 1;

        bool inHand;
        public RectTransform Rect => (RectTransform)transform;

        public void Show(SpecialItemDefinition item, bool inTheHand)
        {
            inHand = inTheHand;
            if (icon) { icon.sprite = item.menuIcon ? item.menuIcon : item.tableSprite; icon.enabled = icon.sprite; }
            if (label) label.text = item.displayName;
            SetHighlighted(false);
        }

        public void SetHighlighted(bool on)
        {
            if (plate) plate.color = on ? highlightColour : inHand ? inHandColour : restColour;
            // Whole-pixel growth would wobble; a plain scale keeps the art's grid on the 1080p reference.
            transform.localScale = Vector3.one * (on ? highlightScale : 1);
        }
    }
}
