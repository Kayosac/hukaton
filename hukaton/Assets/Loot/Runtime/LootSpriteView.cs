using UnityEngine;

namespace Hakaton.Loot
{
    /// <summary>
    /// A loot item shown as its drawing (<see cref="LootItem.icon"/>): sized to the item's height, standing on the
    /// pivot of this object and turned to the camera around the vertical, like the creatures. Uses the same
    /// hand-drawn sprite material as the creatures, so it gets the same pixel cell and wobble.
    /// </summary>
    public sealed class LootSpriteView : MonoBehaviour
    {
        public SpriteRenderer spriteRenderer;
        [Tooltip("Turn to the camera every frame (around the vertical only).")]
        public bool faceCamera = true;

        static readonly int SpriteUvRect = Shader.PropertyToID("_SpriteUVRect");
        static readonly int Anchor = Shader.PropertyToID("_Anchor");
        static readonly int Seed = Shader.PropertyToID("_Seed");
        MaterialPropertyBlock properties;
        Camera viewer;

        /// <summary>Scales the sprite canvas to <paramref name="height"/> metres and places its authored ground pivot at the origin.</summary>
        public void Show(Sprite sprite, float height)
        {
            if (!spriteRenderer || !sprite) return;
            spriteRenderer.sprite = sprite;
            float drawn = sprite.bounds.size.y;
            float scale = drawn > 0 ? height / drawn : 1;
            var drawing = spriteRenderer.transform;
            drawing.localScale = Vector3.one * scale;
            // Loot pivots sit on the visible bottom of the artwork, above any transparent PNG margin.
            // Aligning bounds.min here would cancel that pivot and lift the item off its shelf.
            drawing.localPosition = Vector3.zero;
            if (properties == null) properties = new MaterialPropertyBlock();
            Rect rect = sprite.textureRect;
            Texture2D texture = sprite.texture;
            spriteRenderer.GetPropertyBlock(properties);
            properties.SetVector(SpriteUvRect, new Vector4(rect.x / texture.width, rect.y / texture.height,
                rect.width / texture.width, rect.height / texture.height));
            properties.SetFloat(Anchor, sprite.pivot.y / Mathf.Max(1, sprite.rect.height));
            properties.SetFloat(Seed, Application.isPlaying ? Random.Range(0f, 100f) : 0);
            spriteRenderer.SetPropertyBlock(properties);
            Face();
        }

        void LateUpdate()
        {
            if (faceCamera) Face();
        }

        void Face()
        {
            if (!faceCamera) return;
            if (!viewer) viewer = Camera.main;
            if (!viewer) return;
            Vector3 away = transform.position - viewer.transform.position;
            away.y = 0;
            if (away.sqrMagnitude < .0001f) return;
            transform.rotation = Quaternion.LookRotation(away);
        }
    }
}
