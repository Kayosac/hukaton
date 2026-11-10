using TMPro;
using UnityEngine;

namespace Hakaton.Computer
{
    /// <summary>The yellow XP tooltip that follows the pointer: card name, level, power and description.</summary>
    public sealed class PcTooltip : MonoBehaviour
    {
        public RectTransform panel;
        public CanvasGroup group;
        public TMP_Text title, body;
        [Tooltip("The canvas area the tooltip must stay inside.")]
        public RectTransform bounds;
        public Vector2 cursorOffset = new Vector2(12, -20);
        [Min(0)] public float delay = .3f;

        float shownAt = -1;
        bool wanted;

        public void Show(string heading, string text)
        {
            if (title) title.text = heading;
            if (body) body.text = text;
            if (!wanted) shownAt = Time.unscaledTime;
            wanted = true;
        }

        public void Hide()
        {
            wanted = false;
            Apply(0);
        }

        /// <summary>Puts the tooltip next to the pointer (a local point in <see cref="bounds"/>).</summary>
        public void Follow(Vector2 pointer)
        {
            if (!wanted) return;
            Apply(Time.unscaledTime - shownAt >= delay ? 1 : 0);
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
            var size = panel.rect.size;
            var r = bounds.rect;
            var p = pointer + cursorOffset;
            if (p.x + size.x > r.xMax - 2) p.x = pointer.x - size.x - 4;
            if (p.y - size.y < r.yMin + 2) p.y = pointer.y + size.y + 4;
            p.x = Mathf.Clamp(p.x, r.xMin + 2, r.xMax - size.x - 2);
            p.y = Mathf.Clamp(p.y, r.yMin + size.y + 2, r.yMax - 2);
            panel.anchoredPosition = new Vector2(Mathf.Round(p.x), Mathf.Round(p.y));
        }

        void Apply(float alpha)
        {
            if (!group) return;
            group.alpha = alpha;
            group.blocksRaycasts = group.interactable = false;
        }
    }
}
