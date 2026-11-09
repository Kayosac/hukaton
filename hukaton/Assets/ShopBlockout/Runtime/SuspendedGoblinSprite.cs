using UnityEngine;

namespace Hakaton.ShopBlockout
{
    /// <summary>
    /// The card dealer on the ceiling rig, drawn as one hand-drawn sprite like the visitors and the valley creatures.
    /// The lift turns the entire suspension toward the player. The goblin looks startled while the
    /// hatch opens and the rope lowers it, and pleased for a moment after each card bought from it.
    /// </summary>
    [ExecuteAlways]
    public sealed class SuspendedGoblinSprite : MonoBehaviour
    {
        public SuspendedGoblin rig;
        public SpriteRenderer spriteRenderer;
        [Header("Drawings")]
        public Sprite idle;
        public Sprite startled, pleased;
        [Header("Timing")]
        [Tooltip("The startled drawing stays this long after the goblin arrives.")]
        [Min(0)] public float startleAfterArrival = .7f;
        [Tooltip("How long the pleased drawing stays after a purchase.")]
        [Min(0)] public float pleasedSeconds = 2.5f;
        [Header("Presentation")]
        [Tooltip("Shader pixel cell in source texels.")]
        [Range(1, 32)] public float pixelSize = 8;

        static readonly int SpriteUvRect = Shader.PropertyToID("_SpriteUVRect");
        static readonly int Anchor = Shader.PropertyToID("_Anchor");
        static readonly int PixelSize = Shader.PropertyToID("_PixelSize");
        static readonly int Seed = Shader.PropertyToID("_Seed");
        MaterialPropertyBlock properties;
        Sprite shown;
        float startledUntil, pleasedUntil;

        public Sprite Shown => shown;

        void Awake()
        {
            shown = null;
            Apply(idle);
        }

        void OnValidate()
        {
            shown = null;
            Apply(idle);
        }

        /// <summary>A purchase was made: the goblin is pleased with the deal.</summary>
        public void Please() => pleasedUntil = Time.time + pleasedSeconds;

        void LateUpdate()
        {
            if (!Application.IsPlaying(gameObject)) return;
            if (rig && rig.IsMoving) startledUntil = Time.time + startleAfterArrival;
            Sprite sprite = Time.time < startledUntil && startled ? startled
                : Time.time < pleasedUntil && pleased                 ? pleased
                                                                      : idle;
            if (sprite != shown) Apply(sprite);
        }

        void Apply(Sprite sprite)
        {
            if (!spriteRenderer || !sprite) return;
            spriteRenderer.sprite = sprite;
            shown = sprite;
            if (properties == null) properties = new MaterialPropertyBlock();
            // FullRect atlas cell: the hand-drawn shader animates inside this cell around its pivot line.
            Rect rect = sprite.textureRect;
            Texture2D texture = sprite.texture;
            spriteRenderer.GetPropertyBlock(properties);
            properties.SetVector(SpriteUvRect, new Vector4(rect.x / texture.width, rect.y / texture.height,
                rect.width / texture.width, rect.height / texture.height));
            properties.SetFloat(Anchor, sprite.pivot.y / Mathf.Max(1, sprite.rect.height));
            properties.SetFloat(PixelSize, pixelSize);
            properties.SetFloat(Seed, 0);
            spriteRenderer.SetPropertyBlock(properties);
        }
    }
}
