using UnityEngine;

namespace Hakaton.Tutorial
{
    /// <summary>
    /// A hand-drawn prop of the cave (the goblin's bush): its drawing stands on this object's origin at a set height and
    /// turns to the camera around the vertical, with the same hand-drawn sprite material, pixel cell and wobble as the
    /// creatures. Put it on a child of the prop; the drawing is a SpriteRenderer below it.
    /// </summary>
    [ExecuteAlways]
    public sealed class CaveDrawnProp : MonoBehaviour
    {
        public SpriteRenderer spriteRenderer;
        [Tooltip("Height of the drawing in metres (its transparent margins are not counted).")]
        [Min(.05f)] public float height = 1.6f;
        [Tooltip("Shader pixel cell in source texels, as on the creatures.")]
        [Range(1, 32)] public float pixelSize = 8;
        public bool faceCamera = true;

        static readonly int SpriteUvRect = Shader.PropertyToID("_SpriteUVRect");
        static readonly int Anchor = Shader.PropertyToID("_Anchor");
        static readonly int PixelSize = Shader.PropertyToID("_PixelSize");
        static readonly int Seed = Shader.PropertyToID("_Seed");
        MaterialPropertyBlock properties;
        Camera viewer;

        void OnEnable() => Apply();
        void OnValidate() => Apply();

        void Apply()
        {
            if (!spriteRenderer || !spriteRenderer.sprite) return;
            var sprite = spriteRenderer.sprite;
            float drawn = sprite.bounds.size.y;
            float scale = drawn > 0 ? height / drawn : 1;
            var drawing = spriteRenderer.transform;
            drawing.localScale = Vector3.one * scale;
            drawing.localPosition = new Vector3(0, -sprite.bounds.min.y * scale, 0);
            properties ??= new MaterialPropertyBlock();
            Rect rect = sprite.textureRect;
            Texture2D texture = sprite.texture;
            spriteRenderer.GetPropertyBlock(properties);
            properties.SetVector(SpriteUvRect, new Vector4(rect.x / texture.width, rect.y / texture.height,
                rect.width / texture.width, rect.height / texture.height));
            properties.SetFloat(Anchor, sprite.pivot.y / Mathf.Max(1, sprite.rect.height));
            properties.SetFloat(PixelSize, pixelSize);
            properties.SetFloat(Seed, 3.7f);
            spriteRenderer.SetPropertyBlock(properties);
        }

        void LateUpdate()
        {
            if (!faceCamera || !Application.isPlaying) return;
            if (!viewer) viewer = Camera.main;
            if (!viewer) return;
            Vector3 away = transform.position - viewer.transform.position;
            away.y = 0;
            if (away.sqrMagnitude > .0001f) transform.rotation = Quaternion.LookRotation(away);
        }
    }
}
