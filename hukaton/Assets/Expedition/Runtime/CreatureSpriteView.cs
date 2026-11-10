using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>Full-body poses from the monster atlases (AGENTS.md: default, fear/aggression, taking damage, attack, death).</summary>
    public enum CreaturePose { Default, Alarm, Hurt, Attack, Death }

    /// <summary>
    /// A creature drawn as one billboarded hand-drawn sprite, like the shop visitors. The GameObject sits at the
    /// creature's feet (the atlas pivot is the shared ground anchor) and turns to the camera every frame; the
    /// creature itself only says which pose to show and how to squash. Rear views exist for the default and death
    /// poses only, and the art is mirrored toward the creature's heading.
    /// </summary>
    [ExecuteAlways]
    public sealed class CreatureSpriteView : MonoBehaviour
    {
        public SpriteRenderer spriteRenderer;
        [Header("Poses (front unless noted)")]
        public Sprite idle;
        public Sprite idleBack;
        [Tooltip("Fear for peaceful creatures, aggression for the rest.")]
        public Sprite alarm;
        public Sprite hurt;
        [Tooltip("Peaceful creatures have no attack pose; alarm is shown instead.")]
        public Sprite attack;
        public Sprite death, deathBack;
        [Header("Presentation")]
        [Tooltip("Visible height in metres at scale one (manifest physical height). Marks and effects sit above it.")]
        [Min(.05f)] public float height = 1.5f;
        [Tooltip("The front art looks toward screen right; it is mirrored while the creature heads left.")]
        public bool facesRight = true;

        /// <summary>Size multiplier from the creature (<see cref="WorldCreature.size"/>): scales the drawing and its offsets.</summary>
        public float Size { get; set; } = 1;
        public CreaturePose Pose { get; private set; }
        public bool ShowingBack { get; private set; }
        public Vector3 Top => transform.position + Vector3.up * height * transform.lossyScale.y;

        static readonly int SpriteUvRect = Shader.PropertyToID("_SpriteUVRect");
        static readonly int Anchor = Shader.PropertyToID("_Anchor");
        static readonly int Seed = Shader.PropertyToID("_Seed");
        MaterialPropertyBlock properties;
        Sprite shown;
        float seed;
        bool mirrored;
        Camera viewer;

        void Awake()
        {
            seed = Application.isPlaying ? Random.Range(0f, 100f) : 0;
            shown = null;
            if (spriteRenderer && !spriteRenderer.sprite) spriteRenderer.sprite = idle;
            Apply(spriteRenderer ? spriteRenderer.sprite : null);
        }

        void OnValidate()
        {
            shown = null;
            if (spriteRenderer) Apply(spriteRenderer.sprite ? spriteRenderer.sprite : idle);
        }

        /// <summary>Shows a pose seen from the camera. Scale is (width, height) of the drawing; roll tilts it in the picture plane.</summary>
        public void Present(CreaturePose pose, Vector3 heading, Vector3 offset, Vector2 scale, float roll)
        {
            if (!spriteRenderer) return;
            if (!viewer) viewer = Camera.main;
            Vector3 feet = transform.parent ? transform.parent.position : transform.position;
            Vector3 away = viewer ? feet - viewer.transform.position : Vector3.forward;
            away.y = 0;
            if (away.sqrMagnitude < .0001f) away = Vector3.forward;
            away.Normalize();
            heading.y = 0;
            if (heading.sqrMagnitude > .0001f)
            {
                heading.Normalize();
                // Hysteresis so a creature walking across the view does not flicker between drawings.
                float from = Vector3.Dot(heading, away);
                if (ShowingBack ? from < .1f : from > .35f) ShowingBack = !ShowingBack;
                float side = Vector3.Dot(heading, Vector3.Cross(Vector3.up, away));
                if (Mathf.Abs(side) > .25f) mirrored = facesRight ? side < 0 : side > 0;
            }
            Pose = pose;
            Sprite sprite = Pick(pose);
            if (sprite != shown) Apply(sprite);
            spriteRenderer.flipX = mirrored;
            transform.localPosition = offset * Size;
            transform.rotation = Quaternion.LookRotation(away) * Quaternion.Euler(0, 0, roll);
            transform.localScale = new Vector3(scale.x * Size, scale.y * Size, 1);
        }

        public void Tint(Color colour) { if (spriteRenderer) spriteRenderer.color = colour; }

        Sprite Pick(CreaturePose pose)
        {
            bool back = ShowingBack;
            switch (pose)
            {
                case CreaturePose.Alarm: return alarm ? alarm : idle;
                case CreaturePose.Hurt: return hurt ? hurt : idle;
                case CreaturePose.Attack: return attack ? attack : alarm ? alarm : idle;
                case CreaturePose.Death: return back && deathBack ? deathBack : death ? death : idle;
                default: return back && idleBack ? idleBack : idle;
            }
        }

        void Apply(Sprite sprite)
        {
            if (!spriteRenderer || !sprite) return;
            spriteRenderer.sprite = sprite;
            shown = sprite;
            if (properties == null) properties = new MaterialPropertyBlock();
            // FullRect atlas cells: the shader animates inside this cell and around this cell's ground line.
            Rect rect = sprite.textureRect;
            Texture2D texture = sprite.texture;
            spriteRenderer.GetPropertyBlock(properties);
            properties.SetVector(SpriteUvRect, new Vector4(rect.x / texture.width, rect.y / texture.height,
                rect.width / texture.width, rect.height / texture.height));
            properties.SetFloat(Anchor, sprite.pivot.y / Mathf.Max(1, sprite.rect.height));
            properties.SetFloat(Seed, seed);
            spriteRenderer.SetPropertyBlock(properties);
        }
    }
}
