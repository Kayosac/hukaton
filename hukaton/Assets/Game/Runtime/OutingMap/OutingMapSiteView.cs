using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Hakaton.Game
{
    /// <summary>
    /// One point on the outing map: the charcoal icon and the red ring when pointed at or chosen. A new point (until the
    /// player clicks it for the first time) burns and blinks in the icon itself: the hand-drawn sprite material's tint
    /// is pushed above white, so the dark ink flares to ember, and the drawing swells a little.
    /// </summary>
    public sealed class OutingMapSiteView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int SeedId = Shader.PropertyToID("_Seed");
        static readonly int SpriteUvRectId = Shader.PropertyToID("_SpriteUVRect");

        public Image icon;
        public Image ring;
        [Header("New point: burns and blinks")]
        [Tooltip("Tint at the bright end of a blink. Above 1: the near-black ink is multiplied up into a glowing ember.")]
        [ColorUsage(false, true)] public Color emberColor = new Color(4.5f, 1.9f, .45f, 1f);
        [Min(.1f)] public float blinkPeriod = .9f;
        [Range(0, 1)] public float flicker = .35f;
        [Tooltip("Icon scale at the dark and bright ends of a blink.")]
        public Vector2 breath = new Vector2(1f, 1.08f);
        [Header("Appearing")]
        [Min(.01f)] public float popSeconds = .35f;

        public string Id { get; private set; }
        public bool IsNew { get; private set; }
        Action<OutingMapSiteView> clicked;
        bool hovered, selected;
        float appearAt = -1, noiseSeed;
        Material ownMaterial;

        public void Bind(string id, Sprite sprite, bool isNew, float appearDelay, Action<OutingMapSiteView> onClick)
        {
            Id = id;
            clicked = onClick;
            if (icon) icon.sprite = sprite;
            noiseSeed = UnityEngine.Random.value * 100;
            // Each icon gets its own copy of the material: its own blink, its own motion phase, and its cell of the
            // icon atlas (the sprite material breathes around the drawing's own feet, as on the creatures).
            if (icon && icon.material && icon.material != Graphic.defaultGraphicMaterial)
            {
                ownMaterial = new Material(icon.material) { name = icon.material.name + " (" + id + ")" };
                ownMaterial.SetFloat(SeedId, noiseSeed);
                if (sprite && sprite.texture)
                {
                    var r = sprite.textureRect;
                    float w = sprite.texture.width, h = sprite.texture.height;
                    ownMaterial.SetVector(SpriteUvRectId, new Vector4(r.x / w, r.y / h, r.width / w, r.height / h));
                }
                icon.material = ownMaterial;
            }
            SetNew(isNew);
            appearAt = Time.unscaledTime + appearDelay;
            transform.localScale = appearDelay > 0 ? Vector3.zero : Vector3.one;
            Refresh();
        }

        public void SetNew(bool isNew)
        {
            IsNew = isNew;
            if (isNew) return;
            if (ownMaterial) ownMaterial.SetColor(ColorId, Color.white);
            if (icon) icon.rectTransform.localScale = Vector3.one;
        }

        public void SetSelected(bool value) { selected = value; Refresh(); }

        public void OnPointerEnter(PointerEventData eventData) { hovered = true; Refresh(); }
        public void OnPointerExit(PointerEventData eventData) { hovered = false; Refresh(); }
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left && Time.unscaledTime >= appearAt) clicked?.Invoke(this);
        }

        void OnDisable() { hovered = false; }
        void OnDestroy() { if (ownMaterial) Destroy(ownMaterial); }

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
            if (ownMaterial) ownMaterial.SetColor(ColorId, Color.LerpUnclamped(Color.white, emberColor, heat));
            if (icon) icon.rectTransform.localScale = Vector3.one * Mathf.Lerp(breath.x, breath.y, heat);
        }
    }
}
