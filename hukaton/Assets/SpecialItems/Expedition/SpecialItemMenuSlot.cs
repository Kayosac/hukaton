using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.SpecialItems
{
    /// <summary>One item in the Q menu: a linen plate (the game's button frame), the item's picture and its name.</summary>
    public sealed class SpecialItemMenuSlot : MonoBehaviour
    {
        [Tooltip("The plate's pieces (the button's linen frame), tinted together.")]
        public Graphic[] plate = new Graphic[0];
        public Image icon;
        public TMP_Text label;
        [Tooltip("The plate's colour: resting, pointed at, and the item already in the hand.")]
        public Color restColour = new Color(.78f, .74f, .68f, 1), highlightColour = Color.white, inHandColour = new Color(.8f, 1, .8f, 1);
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
            var colour = on ? highlightColour : inHand ? inHandColour : restColour;
            foreach (var piece in plate) if (piece) piece.color = colour;
            // Whole-pixel growth would wobble; a plain scale keeps the art's grid on the 1080p reference.
            transform.localScale = Vector3.one * (on ? highlightScale : 1);
        }
    }
}
