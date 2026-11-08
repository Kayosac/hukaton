using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>One physical card quad drawn with the two-sided Hakaton/Card shader.</summary>
    public sealed class CardView : MonoBehaviour
    {
        public MeshRenderer meshRenderer;
        static readonly int FrontId = Shader.PropertyToID("_MainTex"), BackId = Shader.PropertyToID("_BackTex"),
            FlashId = Shader.PropertyToID("_Flash"), FlashColourId = Shader.PropertyToID("_FlashColor"),
            GlowId = Shader.PropertyToID("_Glow"), GlowColourId = Shader.PropertyToID("_GlowColor"),
            AlphaId = Shader.PropertyToID("_Alpha"), TintId = Shader.PropertyToID("_Tint");
        MaterialPropertyBlock block;
        Texture front, back;
        float flash, glow, alpha = 1;
        Color flashColour = Color.white, glowColour = new Color(1, .85f, .4f), tint = Color.white;

        MaterialPropertyBlock Block => block ??= new MaterialPropertyBlock();

        public void Show(Texture frontTexture, Texture backTexture)
        {
            front = frontTexture;
            back = backTexture;
            flash = glow = 0;
            alpha = 1;
            tint = Color.white;
            Apply();
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
            b.SetFloat(FlashId, flash);
            b.SetColor(FlashColourId, flashColour);
            b.SetFloat(GlowId, glow);
            b.SetColor(GlowColourId, glowColour);
            b.SetFloat(AlphaId, alpha);
            b.SetColor(TintId, tint);
            meshRenderer.SetPropertyBlock(b);
        }

        /// <summary>Copies another card's faces (used when a hand card becomes a projectile or debris).</summary>
        public void CopyFrom(CardView other) => Show(other.front, other.back);
    }
}
