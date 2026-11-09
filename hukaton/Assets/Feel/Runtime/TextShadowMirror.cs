using TMPro;
using UnityEngine;

namespace Hakaton.Feel
{
    /// <summary>
    /// A hard one-pixel drop shadow for bitmap TMP labels (the pixel font material has no underlay):
    /// this label copies the source's text, alpha and position with a fixed offset. Keep it a sibling
    /// placed just before the source so it draws underneath.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(TMP_Text))]
    public sealed class TextShadowMirror : MonoBehaviour
    {
        public TMP_Text source;
        public Vector2 offset = new Vector2(1, -1);
        TMP_Text shadow;
        DrivenRectTransformTracker tracker;

        void OnEnable()
        {
            shadow = GetComponent<TMP_Text>();
            tracker.Clear();
            tracker.Add(this, shadow.rectTransform, DrivenTransformProperties.Anchors | DrivenTransformProperties.Pivot
                | DrivenTransformProperties.AnchoredPosition | DrivenTransformProperties.SizeDelta);
        }

        void OnDisable() => tracker.Clear();

        void LateUpdate()
        {
            if (!source || !shadow) return;
            if (shadow.text != source.text) shadow.text = source.text;
            var colour = shadow.color;
            if (!Mathf.Approximately(colour.a, source.color.a)) { colour.a = source.color.a; shadow.color = colour; }
            var target = source.rectTransform;
            var own = shadow.rectTransform;
            // Assign only on change so an idle HUD never dirties the scene or prefab in Edit mode.
            if (own.anchorMin != target.anchorMin) own.anchorMin = target.anchorMin;
            if (own.anchorMax != target.anchorMax) own.anchorMax = target.anchorMax;
            if (own.pivot != target.pivot) own.pivot = target.pivot;
            if (own.sizeDelta != target.sizeDelta) own.sizeDelta = target.sizeDelta;
            var position = target.anchoredPosition + offset;
            if (own.anchoredPosition != position) own.anchoredPosition = position;
            if (shadow.gameObject.activeSelf != source.gameObject.activeSelf) shadow.gameObject.SetActive(source.gameObject.activeSelf);
        }
    }
}
