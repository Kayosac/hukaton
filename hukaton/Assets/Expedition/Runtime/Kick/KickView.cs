using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// The kicking leg on screen (<see cref="PlayerKick"/>): the author's three drawings — the foot bent, half way, the
    /// full kick — on the special items' hand material (hand-drawn, hard pixels, dithered). The leg shoots up from below
    /// the screen, bottom centre, straightening into the full kick as the blow lands, holds a moment and drops back the
    /// same way. A landed blow jolts it; a tired player only half lifts it and lets it fall. Its canvas is off while the
    /// leg is down. Scaled time: a hit-stop freezes it with the world.
    /// </summary>
    public sealed class KickView : MonoBehaviour
    {
        [Tooltip("The leg's canvas; off while the leg is down.")]
        public Canvas canvas;
        [Tooltip("The leg; its pivot is at the bottom (where it leaves the screen).")]
        public RectTransform leg;
        public Image body;
        [Tooltip("The foot bent, half way, the full kick.")]
        public Sprite[] frames = new Sprite[3];

        [Header("Placement (reference pixels at 1920 × 1080)")]
        [Tooltip("Where the leg stands at the full kick (anchored to the bottom centre of the screen).")]
        public Vector2 restPosition = new Vector2(140, -40);
        [Tooltip("Where it comes from, relative to its place: from below the screen…")]
        public Vector2 enterOffset = new Vector2(120, -620);
        [Tooltip("…turned this far, degrees (it straightens on the way up).")]
        public float enterAngle = -14;

        [Header("Timing (seconds)")]
        [Tooltip("Up from below, bending → half → full; the blow lands as it straightens.")]
        [Min(.01f)] public float riseSeconds = .12f;
        [Tooltip("The full kick holds this long.")]
        [Min(0)] public float holdSeconds = .1f;
        [Tooltip("Back down: full → half → bent, out of the screen.")]
        [Min(.01f)] public float dropSeconds = .18f;
        [Tooltip("A landed blow jolts the leg back by this much (reference pixels).")]
        public Vector2 landedJolt = new Vector2(-10, -34);
        [Tooltip("Tired: the leg comes up this share of the way and falls back.")]
        [Range(0, 1)] public float tiredRise = .45f;
        [Min(.05f)] public float tiredSeconds = .32f;

        float time = -1, duration;
        bool tired;
        Vector2 jolt, joltVelocity;

        public bool Showing => time >= 0;

        void Awake() => Hide();

        /// <summary>A kick: the leg goes up, straightens, comes back down.</summary>
        public void Play()
        {
            tired = false;
            duration = riseSeconds + holdSeconds + dropSeconds;
            Begin();
        }

        /// <summary>Too tired to kick: the leg twitches half way up and drops.</summary>
        public void Tired()
        {
            tired = true;
            duration = tiredSeconds;
            Begin();
        }

        /// <summary>The blow met something: the leg jolts back.</summary>
        public void Landed() => joltVelocity += landedJolt * 30;

        void Begin()
        {
            time = 0;
            jolt = joltVelocity = Vector2.zero;
            if (canvas) canvas.enabled = true;
            Pose();
        }

        void Hide()
        {
            time = -1;
            if (canvas) canvas.enabled = false;
        }

        void OnDisable() => Hide();

        void Update()
        {
            if (time < 0) return;
            float dt = Time.deltaTime;
            time += dt;
            // A small spring for the jolt of a landed blow.
            joltVelocity += (-jolt * 260 - joltVelocity * 22) * dt;
            jolt += joltVelocity * dt;
            if (time >= duration) { Hide(); return; }
            Pose();
        }

        /// <summary>Where along the way up the leg is (0 below the screen, 1 at the full kick) and which drawing shows.</summary>
        void Pose()
        {
            float up;
            int frame;
            if (tired)
            {
                float t = Mathf.Clamp01(time / duration);
                up = Mathf.Sin(t * Mathf.PI) * tiredRise;
                frame = 0;
            }
            else if (time < riseSeconds)
            {
                float t = time / riseSeconds;
                up = EaseOut(t);
                frame = t < .4f ? 0 : t < .8f ? 1 : 2;
            }
            else if (time < riseSeconds + holdSeconds)
            {
                up = 1;
                frame = 2;
            }
            else
            {
                float t = Mathf.Clamp01((time - riseSeconds - holdSeconds) / dropSeconds);
                up = 1 - t * t;
                frame = t < .3f ? 2 : t < .6f ? 1 : 0;
            }
            if (body && frames != null && frames.Length > 0)
            {
                var sprite = frames[Mathf.Clamp(frame, 0, frames.Length - 1)];
                if (sprite && body.sprite != sprite) body.sprite = sprite;
            }
            if (!leg) return;
            var position = restPosition + enterOffset * (1 - up) + jolt;
            leg.anchoredPosition = new Vector2(Mathf.Round(position.x), Mathf.Round(position.y));
            leg.localRotation = Quaternion.Euler(0, 0, enterAngle * (1 - up));
        }

        static float EaseOut(float t)
        {
            t = Mathf.Clamp01(t);
            return 1 - (1 - t) * (1 - t) * (1 - t);
        }
    }
}
