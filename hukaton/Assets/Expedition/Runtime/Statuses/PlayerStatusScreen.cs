using System;
using Hakaton.Feel;
using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// Full-screen HUD layers of the player's debuffs: Blind darkens from the edges, Poison pulses green, Burn flickers
    /// orange, Fragile cracks the edges like glass, Slow drips honey from the top, Mark glows red. Each layer fades in
    /// and out with its debuff; it never blocks the cursor (no raycasts). Lives under the expedition HUD canvas.
    /// </summary>
    public sealed class PlayerStatusScreen : MonoBehaviour
    {
        [Serializable]
        public struct Layer
        {
            public PlayerStatusKind kind;
            public Image image;
            [Tooltip("Colour and strongest opacity of the layer.")]
            public Color colour;
            [Min(.05f)] public float fadeSeconds;
            [Tooltip("Opacity swing 0–1 (pulse, flicker) and how fast it swings, per second.")]
            [Range(0, 1)] public float pulse;
            [Min(0)] public float pulseSpeed;
            [Tooltip("Random flicker instead of a smooth pulse (fire).")]
            public bool flicker;
            [Tooltip("The layer breathes in scale by this share (Blind's dark ring tightening).")]
            [Range(0, .3f)] public float breathe;
            [Tooltip("Slowly sags down and back up by this many reference pixels (honey running down).")]
            [Min(0)] public float slide;
        }

        [Tooltip("The expedition HUD this screen belongs to (its world is the outing).")]
        public ExpeditionHud hud;
        public Layer[] layers = new Layer[0];

        float[] weights = new float[0];
        Vector2[] basePositions = new Vector2[0];

        void Awake()
        {
            weights = new float[layers.Length];
            basePositions = new Vector2[layers.Length];
            for (int i = 0; i < layers.Length; i++)
            {
                var image = layers[i].image;
                if (!image) continue;
                image.raycastTarget = false;
                basePositions[i] = image.rectTransform.anchoredPosition;
                image.enabled = false;
            }
        }

        void LateUpdate()
        {
            var world = hud ? hud.world : null;
            if (!world) return;
            if (weights.Length != layers.Length) Awake();
            var statuses = world.statuses;
            bool running = world.Running && statuses;
            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < layers.Length; i++)
            {
                var layer = layers[i];
                if (!layer.image) continue;
                bool on = running && statuses.Has(layer.kind);
                weights[i] = running ? Mathf.MoveTowards(weights[i], on ? 1 : 0, dt / Mathf.Max(.05f, layer.fadeSeconds)) : 0;
                float weight = Ease.InOutSine(weights[i]);
                if (weight <= .001f)
                {
                    layer.image.enabled = false;
                    continue;
                }
                float swing = 1;
                if (layer.pulse > 0)
                {
                    float wave = layer.flicker
                        ? Mathf.PerlinNoise(Time.time * layer.pulseSpeed, i * 3.7f)
                        : .5f + .5f * Mathf.Sin(Time.time * layer.pulseSpeed * Mathf.PI * 2);
                    swing = 1 - layer.pulse * wave;
                }
                layer.image.enabled = true;
                var colour = layer.colour;
                colour.a *= weight * swing;
                layer.image.color = colour;
                var rect = layer.image.rectTransform;
                rect.localScale = Vector3.one * (1 + layer.breathe * (.5f + .5f * Mathf.Sin(Time.time * 1.7f)));
                if (layer.slide > 0)
                    rect.anchoredPosition = basePositions[i] + Vector2.down * Mathf.Round(layer.slide * (.5f + .5f * Mathf.Sin(Time.time * .8f)));
            }
        }
    }
}
