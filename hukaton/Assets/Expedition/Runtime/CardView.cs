using UnityEngine;
using TMPro;
using Hakaton.Decks;

namespace Hakaton.Expeditions
{
    /// <summary>One physical card quad drawn with the two-sided Hakaton/Card shader.</summary>
    public sealed class CardView : MonoBehaviour
    {
        public MeshRenderer meshRenderer;
        [Header("Upgrade mark (front only)")]
        public TMP_FontAsset levelFont;
        public Color levelMarkColor = Color.white;
        public Color levelPaperColor = new Color(.16f, .10f, .06f, 1);
        [Tooltip("Right/top inset in card UVs; negative values place the mark outside the card.")]
        public Vector2 levelMarkInset = new Vector2(0, -.005f);
        [Min(16)] public int levelMarkSize = 64;
        public int Level { get; private set; } = 1;
        // The physical face samples the saved TMP bitmap directly, so the mark follows its
        // depth, alpha, front/back and sorting rules without a second floating renderer.
        const int MaxMarkGlyphs = 16;
        readonly Vector4[] glyphUVs = new Vector4[MaxMarkGlyphs];
        readonly Vector4[] glyphRects = new Vector4[MaxMarkGlyphs];
        static readonly int FrontId = Shader.PropertyToID("_MainTex"), BackId = Shader.PropertyToID("_BackTex"),
            FlashId = Shader.PropertyToID("_Flash"), FlashColourId = Shader.PropertyToID("_FlashColor"),
            GlowId = Shader.PropertyToID("_Glow"), GlowColourId = Shader.PropertyToID("_GlowColor"),
            AlphaId = Shader.PropertyToID("_Alpha"), TintId = Shader.PropertyToID("_Tint");
        MaterialPropertyBlock block;
        Texture front, back;
        float flash, glow, alpha = 1;
        Color flashColour = Color.white, glowColour = new Color(1, .85f, .4f), tint = Color.white;

        MaterialPropertyBlock Block => block ??= new MaterialPropertyBlock();

        public void Show(Texture frontTexture, Texture backTexture, int level = 1)
        {
            Level = level;
            front = frontTexture;
            back = backTexture;
            flash = glow = 0;
            alpha = 1;
            tint = Color.white;
            Apply();
            ApplyLevelMark();
            gameObject.SetActive(true);
        }

        public void Hide() => gameObject.SetActive(false);
        public bool Visible => gameObject.activeSelf;
        public Texture Front => front;
        public Texture Back => back;

        public void SetFlash(float amount, Color colour) { flash = amount; flashColour = colour; Apply(); }
        public void SetGlow(float amount, Color colour) { glow = amount; glowColour = colour; Apply(); }
        public void SetAlpha(float amount) { alpha = amount; Apply(); }
        public void SetTint(Color colour) { tint = colour; Apply(); }
        public void SetOrder(int order) { meshRenderer.sortingOrder = order; }

        void Apply()
        {
            if (!meshRenderer) return;
            var b = Block;
            meshRenderer.GetPropertyBlock(b);
            if (front) b.SetTexture(FrontId, front);
            if (back) b.SetTexture(BackId, back);
            b.SetFloat("_FrontIsBack", front && front == back ? 1 : 0);
            b.SetFloat(FlashId, flash);
            b.SetColor(FlashColourId, flashColour);
            b.SetFloat(GlowId, glow);
            b.SetColor(GlowColourId, glowColour);
            b.SetFloat(AlphaId, alpha);
            b.SetColor(TintId, tint);
            meshRenderer.SetPropertyBlock(b);
        }

        void ApplyLevelMark()
        {
            if (!meshRenderer) return;
            var b = Block;
            meshRenderer.GetPropertyBlock(b);
            string mark = CardCopy.UpgradeMark(Level);
            int count = levelFont ? Mathf.Min(mark.Length, MaxMarkGlyphs) : 0;
            b.SetFloat("_LevelGlyphCount", count);
            var bounds = new Bounds(Vector3.zero, new Vector3(1, 1, .01f));
            if (count > 0)
            {
                var atlas = levelFont.atlasTexture;
                float scale = levelMarkSize / levelFont.faceInfo.pointSize;
                float right = 1 - levelMarkInset.x;
                float top = 1 - levelMarkInset.y;
                for (int i = count - 1; i >= 0; i--)
                {
                    var glyph = levelFont.characterLookupTable[mark[i]].glyph;
                    var rect = glyph.glyphRect;
                    var metric = glyph.metrics;
                    float width = metric.width * scale / 256f;
                    float height = metric.height * scale / 384f;
                    glyphRects[i] = new Vector4(right - width, top - height, width, height);
                    glyphUVs[i] = new Vector4((float)rect.x / atlas.width, (float)rect.y / atlas.height,
                        (float)rect.width / atlas.width, (float)rect.height / atlas.height);
                    right -= metric.horizontalAdvance * scale / 256f;
                }
                float left = glyphRects[0].x;
                float bottom = top - levelFont.characterLookupTable[mark[0]].glyph.metrics.height * scale / 384f;
                var markRect = new Vector4(left - 2f / 256f, bottom - 2f / 384f,
                    1 - levelMarkInset.x - left + 4f / 256f, top - bottom + 4f / 384f);
                b.SetVector("_LevelMarkRect", markRect);
                bounds.Encapsulate(new Vector3(markRect.x - .5f, markRect.y - .5f, 0));
                bounds.Encapsulate(new Vector3(markRect.x + markRect.z - .5f, markRect.y + markRect.w - .5f, 0));
                b.SetColor("_LevelPaperColor", levelPaperColor);
                b.SetTexture("_LevelFont", atlas);
                b.SetColor("_LevelMarkColor", levelMarkColor);
                b.SetVectorArray("_LevelGlyphUVs", glyphUVs);
                b.SetVectorArray("_LevelGlyphRects", glyphRects);
            }
            meshRenderer.localBounds = bounds;
            meshRenderer.SetPropertyBlock(b);
        }

        /// <summary>Copies another card's faces (used when a hand card becomes a projectile or debris).</summary>
        public void CopyFrom(CardView other) => Show(other.front, other.back, other.Level);
    }
}
