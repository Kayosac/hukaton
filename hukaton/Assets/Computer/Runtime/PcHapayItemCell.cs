using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Hakaton.Computer
{
    /// <summary>One wanted item on a хаPAY lot: its picture (or its name when it has none), «есть / нужно», a mark when
    /// some of it would come off the display, the name in the yellow hint under the pointer.</summary>
    public sealed class PcHapayItemCell : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Image icon;
        [Tooltip("Shown instead of the picture when the item has none (its first letters).")]
        public TMP_Text fallback;
        public TMP_Text count;
        [Tooltip("Lit when some of the pieces would be taken from the display.")]
        public GameObject displayMark;
        public CanvasGroup group;
        [Range(0, 1)] public float missingAlpha = .35f;
        public Color enoughColour = new Color(.05f, .45f, .05f, 1), missingColour = new Color(.7f, .1f, .05f, 1);

        public PcTooltip Tooltip { get; set; }
        string itemName, hint;

        public void Bind(Sprite picture, string name, int have, int need, bool fromDisplay)
        {
            gameObject.SetActive(true);
            itemName = name;
            if (icon) { icon.sprite = picture; icon.enabled = picture; icon.preserveAspect = true; }
            if (fallback) { fallback.text = picture ? "" : Short(name); }
            if (count)
            {
                count.text = $"{Mathf.Min(have, need)}/{need}";
                count.color = have >= need ? enoughColour : missingColour;
            }
            if (displayMark) displayMark.SetActive(fromDisplay && have >= 1);
            if (group) group.alpha = have >= need ? 1 : missingAlpha;
            hint = (have >= need ? "Есть" : $"Есть {have} из {need}") + (fromDisplay ? ". Часть заберут с витрины." : ".");
        }

        public void Hide() => gameObject.SetActive(false);

        static string Short(string name)
        {
            if (string.IsNullOrEmpty(name)) return "?";
            // The first word, cut to fit the little cell (an item with no picture of its own).
            string word = name.Split(' ')[0];
            return word.Length <= 5 ? word : word.Substring(0, 4) + ".";
        }

        public void OnPointerEnter(PointerEventData eventData) { if (Tooltip && !string.IsNullOrEmpty(itemName)) Tooltip.Show(itemName, hint); }
        public void OnPointerExit(PointerEventData eventData) { if (Tooltip) Tooltip.Hide(); }
    }
}
