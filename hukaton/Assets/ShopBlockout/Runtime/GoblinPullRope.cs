using UnityEngine;

namespace Hakaton.ShopBlockout
{
    /// <summary>
    /// The goblin's pull rope and its «Потяни меня» sign: hand-drawn sprites on the rig, each turned to the player around
    /// the vertical about its own origin (like the rig's suspension). The sign hangs only until the goblin has been called
    /// down for the first time in this game — <see cref="Hakaton.Decks.GoblinVisits.Visited"/>, kept in the save, so a new
    /// game shows it again. After that first pull it pops up into the ceiling and is gone for good. While it hangs, the
    /// sign and the rope pulse with the hint outline of their shared material (faded per renderer, no material copies).
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(610)]
    public sealed class GoblinPullRope : MonoBehaviour
    {
        [Tooltip("The rig: its card table says whether the goblin was ever called, its viewer is the player camera.")]
        public SuspendedGoblin rig;
        [Tooltip("Turned toward the rig's viewer around the vertical, each about its own origin.")]
        public Transform[] billboards = new Transform[0];
        [Tooltip("Hand-drawn renderers: their atlas cell and anchor go to the shader.")]
        public SpriteRenderer[] drawings = new SpriteRenderer[0];

        [Header("First-use hint")]
        [Tooltip("The «Потяни меня» sign (its origin is where it hangs from).")]
        public Transform sign;
        [Tooltip("Renderers whose hint outline pulses while the sign hangs.")]
        public SpriteRenderer[] outlined = new SpriteRenderer[0];
        [Min(.05f)] public float vanishSeconds = .5f;
        [Tooltip("Sign scale over the vanish (1 = as authored): a quick pop, then it shrinks into its hanging point.")]
        public AnimationCurve vanishScale = new AnimationCurve(
            new Keyframe(0, 1, 0, 0), new Keyframe(.22f, 1.18f, 0, 0), new Keyframe(1, 0, -2.5f, 0));
        [Tooltip("Sign opacity over the vanish.")]
        public AnimationCurve vanishAlpha = new AnimationCurve(
            new Keyframe(0, 1, 0, 0), new Keyframe(.45f, 1, 0, 0), new Keyframe(1, 0, -2, 0));

        static readonly int SpriteUvRect = Shader.PropertyToID("_SpriteUVRect");
        static readonly int Anchor = Shader.PropertyToID("_Anchor");
        static readonly int Seed = Shader.PropertyToID("_Seed");
        static readonly int OutlineStrength = Shader.PropertyToID("_OutlineStrength");

        enum Hint { Hanging, Vanishing, Gone }

        MaterialPropertyBlock properties;
        Hint hint;
        float vanishTime;
        Vector3 signScale = Vector3.one;
        bool signScaleKnown;

        /// <summary>The sign is still up (or on its way out).</summary>
        public bool HintShown => hint != Hint.Gone;

        void OnEnable()
        {
            if (sign && !signScaleKnown) { signScale = sign.localScale; signScaleKnown = true; }
            hint = Hint.Hanging;
            ApplyDrawings();
            ShowHint(1, 1, 1);
        }

        void OnValidate() => ApplyDrawings();

        void Update()
        {
            if (!Application.IsPlaying(gameObject)) return;
            if (hint == Hint.Hanging && Visited())
            {
                // Called right now: the sign leaves with a pop. Already called (a loaded save): it is simply not there.
                if (rig && !rig.IsStowed) { hint = Hint.Vanishing; vanishTime = 0; }
                else Remove();
            }
            if (hint != Hint.Vanishing) return;
            vanishTime += Time.deltaTime;
            float t = Mathf.Clamp01(vanishTime / vanishSeconds);
            if (t >= 1) { Remove(); return; }
            ShowHint(vanishScale.Evaluate(t), vanishAlpha.Evaluate(t), 1 - Mathf.Clamp01(t * 2));
        }

        void LateUpdate()
        {
            if (!Application.IsPlaying(gameObject) || !rig || !rig.viewer) return;
            foreach (var billboard in billboards)
            {
                if (!billboard || !billboard.gameObject.activeInHierarchy) continue;
                Vector3 away = billboard.position - rig.viewer.position;
                away.y = 0;
                if (away.sqrMagnitude > .0001f) billboard.rotation = Quaternion.LookRotation(away, Vector3.up);
            }
        }

        bool Visited() => rig && rig.table && rig.table.Visits.Visited;

        void Remove()
        {
            hint = Hint.Gone;
            ShowHint(1, 1, 0);
            if (sign) sign.gameObject.SetActive(false);
        }

        void ShowHint(float scale, float alpha, float outline)
        {
            if (sign)
            {
                sign.localScale = signScale * Mathf.Max(0, scale);
                foreach (var drawing in sign.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    var color = drawing.color;
                    color.a = Mathf.Clamp01(alpha);
                    drawing.color = color;
                }
            }
            if (properties == null) properties = new MaterialPropertyBlock();
            foreach (var drawing in outlined)
            {
                if (!drawing) continue;
                drawing.GetPropertyBlock(properties);
                properties.SetFloat(OutlineStrength, Mathf.Clamp01(outline));
                drawing.SetPropertyBlock(properties);
            }
        }

        void ApplyDrawings()
        {
            if (properties == null) properties = new MaterialPropertyBlock();
            for (int i = 0; i < drawings.Length; i++)
            {
                var drawing = drawings[i];
                if (!drawing || !drawing.sprite) continue;
                var sprite = drawing.sprite;
                Rect rect = sprite.textureRect;
                Texture2D texture = sprite.texture;
                drawing.GetPropertyBlock(properties);
                properties.SetVector(SpriteUvRect, new Vector4(rect.x / texture.width, rect.y / texture.height,
                    rect.width / texture.width, rect.height / texture.height));
                properties.SetFloat(Anchor, sprite.pivot.y / Mathf.Max(1, sprite.rect.height));
                properties.SetFloat(Seed, 1.3f + i * 2.1f);
                drawing.SetPropertyBlock(properties);
            }
        }
    }
}
