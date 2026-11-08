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
        bool hovered, selected;
        Vector2 artHome;
        void Awake() { artHome = art.rectTransform.anchoredPosition; }
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
            button.interactable = true;
        }

        public void OnPointerEnter(PointerEventData e) { hovered = true; }
        public void OnPointerExit(PointerEventData e) { hovered = false; }
        void Update()
        {
            var p = artHome + Vector2.up * (hovered ? 6 : 0);
            art.rectTransform.anchoredPosition = Vector2.Lerp(art.rectTransform.anchoredPosition, p, Time.unscaledDeltaTime * 18);
            outline.color = selected ? selectedColor : hovered ? Color.Lerp(normalColor, selectedColor, .55f) : normalColor;
        }

        void OnDisable()
        {
            hovered = false;
            if (art) art.rectTransform.anchoredPosition = artHome;
        }
    }
}
