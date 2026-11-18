using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// The ring of «Эхолот» (<see cref="SonarEffect"/>, prefab «Sonar wave»): a thin glowing circle that runs out from
    /// the player along the ground at the wave's speed to the card's reach, fading as it goes, then a second, fainter
    /// one behind it — the echo. Drawn with the sonar material (seen through walls, dimmer there). A LineRenderer on
    /// this object, flat at the height it was spawned.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public sealed class SonarWave : MonoBehaviour
    {
        public LineRenderer line;
        public LineRenderer echo;
        [Min(8)] public int segments = 96;
        [Tooltip("Width of the ring near the player and at the far edge, m.")]
        public Vector2 width = new Vector2(.12f, .6f);
        [Tooltip("The echo ring starts this long after the first, s.")]
        [Min(0)] public float echoDelay = .35f;
        [Range(0, 1)] public float echoStrength = .45f;

        float reach = 25, speed = 32, started;
        Color colour = Color.cyan;
        bool running;

        void Reset() => line = GetComponent<LineRenderer>();

        /// <summary>Starts the ring: out to <paramref name="radius"/> at <paramref name="metresPerSecond"/>.</summary>
        public void Run(float radius, float metresPerSecond, Color tint)
        {
            if (!line) line = GetComponent<LineRenderer>();
            reach = Mathf.Max(1, radius);
            speed = Mathf.Max(1, metresPerSecond);
            colour = tint;
            started = Time.time;
            running = true;
            Prepare(line);
            Prepare(echo);
            Draw(line, 0, 1);
            Draw(echo, -1, echoStrength);
        }

        void Prepare(LineRenderer ring)
        {
            if (!ring) return;
            ring.useWorldSpace = false;
            ring.loop = true;
            ring.positionCount = segments;
        }

        void Update()
        {
            if (!running) return;
            float t = Time.time - started;
            bool first = Draw(line, t * speed, 1);
            bool second = Draw(echo, (t - echoDelay) * speed, echoStrength);
            if (!first && !second && t * speed > reach) Destroy(gameObject);
        }

        /// <summary>Draws one ring at <paramref name="radius"/>; false (hidden) before it starts or once it is past the reach.</summary>
        bool Draw(LineRenderer ring, float radius, float strength)
        {
            if (!ring) return false;
            bool on = radius > 0 && radius <= reach;
            ring.enabled = on;
            if (!on) return false;
            float k = radius / reach;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2 / segments;
                ring.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius));
            }
            float w = Mathf.Lerp(width.x, width.y, k);
            ring.startWidth = ring.endWidth = w;
            var c = new Color(colour.r, colour.g, colour.b, colour.a * strength * (1 - k * k) * Mathf.Clamp01(radius / 1.5f));
            ring.startColor = ring.endColor = c;
            return true;
        }
    }
}
