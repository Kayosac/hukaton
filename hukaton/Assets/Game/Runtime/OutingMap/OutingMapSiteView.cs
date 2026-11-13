using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Hakaton.Game
{
    /// <summary>
    /// One point on the outing map: the charcoal icon and the red ring when pointed at or chosen. A new point (until the
    /// player clicks it for the first time) burns and blinks in the icon itself: its ember twin (the same drawing in
    /// ember, from the same atlas) fades in over it, and the drawing swells a little. Everything uses the map's one
    /// material; only vertex alpha changes.
    /// </summary>
    public sealed class OutingMapSiteView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {

        public Image icon;
        [Tooltip("The ember twin of the icon, laid over it (child of the icon, same rect).")]
        public Image ember;
        public Image ring;
        [Header("New point: burns and blinks")]
        [Min(.1f)] public float blinkPeriod = .9f;
        [Range(0, 1)] public float flicker = .35f;
        [Tooltip("Icon scale at the dark and bright ends of a blink.")]
        public Vector2 breath = new Vector2(1f, 1.08f);
        [Header("Appearing")]
        [Min(.01f)] public float popSeconds = .2f;

        public string Id { get; private set; }
        public bool IsNew { get; private set; }
        Action<OutingMapSiteView> clicked;
        bool hovered, selected;
        float appearAt = -1, noiseSeed;

        /// <param name="pixelMaterial">The map's one pixel material (shared by everything on the parchment).</param>
        public void Bind(string id, Sprite sprite, Sprite emberSprite, bool isNew, float appearDelay, Action<OutingMapSiteView> onClick, Material pixelMaterial)
        {
            Id = id;
            clicked = onClick;
            noiseSeed = UnityEngine.Random.value * 100;
            if (icon) { icon.sprite = sprite; icon.material = pixelMaterial; }
            if (ember) { ember.sprite = emberSprite; ember.material = pixelMaterial; SetEmber(0); }
            SetNew(isNew);
            appearAt = Time.unscaledTime + appearDelay;
            transform.localScale = appearDelay > 0 ? Vector3.zero : Vector3.one;
            Refresh();
        }

        /// <summary>Still waiting for its turn or popping in.</summary>
        public bool Appearing => appearAt >= 0;

        /// <summary>Skips the wait and the pop: the point simply stands on the map.</summary>
        public void FinishAppearing()
        {
            appearAt = -1;
            transform.localScale = Vector3.one;
        }

        public void SetNew(bool isNew)
        {
            IsNew = isNew;
            if (isNew) return;
            SetEmber(0);
            if (icon) icon.rectTransform.localScale = Vector3.one;
        }

        public void SetSelected(bool value) { selected = value; Refresh(); }

        public void OnPointerEnter(PointerEventData eventData) { hovered = true; Refresh(); }
        public void OnPointerExit(PointerEventData eventData) { hovered = false; Refresh(); }
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left && !Appearing) clicked?.Invoke(this);
        }

        void OnDisable() { hovered = false; }
        void SetEmber(float amount)
        {
            if (!ember) return;
            ember.enabled = amount > 0 && ember.sprite;
            var c = ember.color; c.a = amount; ember.color = c;
        }

        void Refresh() { if (ring) ring.enabled = hovered || selected; }

        void Update()
        {
            float now = Time.unscaledTime;
            if (appearAt >= 0)
            {
                float t = popSeconds > 0 ? Mathf.Clamp01((now - appearAt) / popSeconds) : 1;
                // A small overshoot, like a stamp pressed onto the paper.
                float s = t <= 0 ? 0 : t >= 1 ? 1 : 1 + 1.7f * Mathf.Pow(t - 1, 3) + .7f * Mathf.Pow(t - 1, 2);
                transform.localScale = Vector3.one * s;
                if (t >= 1) appearAt = -1;
            }
            if (!IsNew) return;
            // Burning: an uneven flame-like flicker riding on a steady on/off blink.
            float blink = Mathf.SmoothStep(0, 1, .5f + .5f * Mathf.Cos(now * Mathf.PI * 2 / blinkPeriod));
            float noise = Mathf.PerlinNoise(noiseSeed, now * 7f);
            float heat = Mathf.Clamp01(blink * (1 - flicker + flicker * noise * 1.6f));
            SetEmber(heat);
            if (icon) icon.rectTransform.localScale = Vector3.one * Mathf.Lerp(breath.x, breath.y, heat);
        }
    }
}
