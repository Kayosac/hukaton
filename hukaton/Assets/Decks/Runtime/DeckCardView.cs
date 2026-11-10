using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Hakaton.Decks.Unity
{
    /// <summary>Authored card tile. Hover moves only the illustration; text keeps its pixel grid.</summary>
    public sealed class DeckCardView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Button button;
        public Image art, outline, selectionMark;
        public TMP_Text caption, status;
        public CanvasGroup group;
        public Color normalColor = new Color(.27f, .31f, .27f), selectedColor = new Color(.83f, .68f, .40f);
        public int CopyId { get; private set; }
        public string DefinitionId { get; private set; }
        bool hovered, selected, dimmed;
        Vector2 artHome;
        float celebrate;
        void Awake() { artHome = art.rectTransform.anchoredPosition; }

        /// <summary>A short pop of the whole tile (a fresh merge).</summary>
        public void Celebrate() { celebrate = 1; }

        /// <summary>Fades the tile while it cannot be chosen (merging looks for a partner).</summary>
        public void SetDimmed(bool value)
        {
            dimmed = value;
            if (group) group.alpha = value ? .35f : 1;
            button.interactable = !value;
        }
        public void Bind(
            Sprite sprite, string captionText, string statusText, int copyId, string definitionId, bool isSelected, bool equipped)
        {
            gameObject.SetActive(true);
            CopyId = copyId;
            DefinitionId = definitionId;
            selected = isSelected;
            art.sprite = sprite;
            caption.text = captionText;
            status.text = statusText;
            outline.color = isSelected ? selectedColor : normalColor;
            selectionMark.gameObject.SetActive(equipped);
            SetDimmed(false);
        }

        public void OnPointerEnter(PointerEventData e) { hovered = true; }
        public void OnPointerExit(PointerEventData e) { hovered = false; }
        void Update()
        {
            var p = artHome + Vector2.up * (hovered ? 6 : 0);
            art.rectTransform.anchoredPosition = Vector2.Lerp(art.rectTransform.anchoredPosition, p, Time.unscaledDeltaTime * 18);
            outline.color = selected ? selectedColor : hovered && !dimmed ? Color.Lerp(normalColor, selectedColor, .55f) : normalColor;
            // Overshooting pop: 1 → 1.18 → settle, in about a third of a second.
            celebrate = Mathf.MoveTowards(celebrate, 0, Time.unscaledDeltaTime * 3f);
            float pop = 1 + .18f * Mathf.Sin(celebrate * Mathf.PI) * celebrate;
            transform.localScale = new Vector3(pop, pop, 1);
        }

        void OnDisable()
        {
            hovered = false;
            celebrate = 0;
            transform.localScale = Vector3.one;
            if (art) art.rectTransform.anchoredPosition = artHome;
        }
    }
}
