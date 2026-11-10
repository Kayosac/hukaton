using TMPro;
using UnityEngine;

namespace Hakaton.DialogueUI
{
    /// <summary>
    /// «E — дальше» in the bottom-right corner while a line waits: big letters with a dark drop and a warm glow
    /// around the letters themselves — rings of the same text in the glow colour, a few pixels out — that breathes.
    /// Pixel fonts have no soft outline, so the glow is built from the glyphs. Shown and hidden by whoever owns the line.
    /// </summary>
    public sealed class ContinueHint : MonoBehaviour
    {
        public TMP_Text label;
        [Tooltip("The same text, dark, a few pixels down-right: gives the letters weight.")]
        public TMP_Text drop;
        [Tooltip("Copies of the text around the letters, inner ring first: the glow.")]
        public TMP_Text[] halo;
        [Tooltip("How many copies make the inner ring; the rest are the outer ring.")]
        [Min(1)] public int innerRing = 8;
        public Color glowColour = new Color(1f, .7f, .2f, 1);
        [Range(0, 1)] public float innerLow = .55f, innerHigh = .95f, outerLow = .2f, outerHigh = .5f;
        [Min(.1f)] public float breathsPerSecond = .8f;

        public bool Visible => gameObject.activeSelf;

        public void Show(bool visible) => gameObject.SetActive(visible);

        void OnEnable() => Breathe();

        void Update() => Breathe();

        void Breathe()
        {
            if (halo == null) return;
            float k = .5f - .5f * Mathf.Cos(Time.unscaledTime * breathsPerSecond * Mathf.PI * 2);
            for (int i = 0; i < halo.Length; i++)
            {
                if (!halo[i]) continue;
                bool inner = i < innerRing;
                var colour = glowColour;
                colour.a = inner ? Mathf.Lerp(innerLow, innerHigh, k) : Mathf.Lerp(outerLow, outerHigh, k);
                halo[i].color = colour;
            }
        }
    }
}
