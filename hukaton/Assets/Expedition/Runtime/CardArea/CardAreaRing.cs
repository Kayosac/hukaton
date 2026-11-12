using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// One crooked, hand-drawn ring lying on the ground: the reach of a card. The circle is sampled once per centre
    /// and radius (each vertex finds the ground under it, ignoring creatures and loose props), then redrawn a few
    /// times a second with a fresh wobble, like a boiling ink line. A dashed ink stroke (black edge, tinted fill)
    /// tiles a whole number of times around so the loop never shows a seam. Owned and pooled by
    /// <see cref="CardAreaDisplay"/>.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public sealed class CardAreaRing : MonoBehaviour
    {
        public LineRenderer line;

        [Header("Shape")]
        [Tooltip("Vertices per metre of circumference (clamped below).")]
        [Min(.1f)] public float verticesPerMetre = 1.6f;
        [Min(8)] public int minimumVertices = 40;
        [Min(8)] public int maximumVertices = 220;
        [Tooltip("Crookedness: radial wobble as a share of the radius.")]
        [Range(0, .2f)] public float wobble = .035f;
        [Tooltip("Wobble limits, m: small rings still look drawn by hand, big ones do not wander off.")]
        public Vector2 wobbleLimits = new Vector2(.05f, .8f);
        [Tooltip("Bumps around the circle (noise frequency on the unit circle).")]
        [Min(.1f)] public float wobbleFrequency = 1.7f;
        [Tooltip("The wobble is redrawn this many times a second (hand-drawn boil). 0 keeps it still.")]
        [Min(0)] public float boilRate = 7;

        [Header("Stroke")]
        [Tooltip("Stroke width, m, at radius 0; grows with the radius so far rings stay readable.")]
        [Min(.01f)] public float width = .16f;
        [Min(0)] public float widthPerMetre = .012f;
        [Min(.01f)] public float maximumWidth = .6f;
        [Tooltip("Length of one dash (with its gap) in stroke widths: the texture's aspect keeps its pixels square.")]
        [Min(.5f)] public float dashInWidths = 4;
        [Tooltip("Lift above the ground, m. The material (Hakaton/Ground Mark) also pulls the stroke toward the camera, so it is not hidden by the ground's own depth.")]
        [Min(0)] public float lift = .05f;

        [Header("Ground")]
        [Tooltip("How far above the centre a vertex looks for ground: × radius, clamped, m.")]
        [Min(0)] public float probeUpPerMetre = .5f;
        public Vector2 probeUpLimits = new Vector2(2, 25);
        [Tooltip("How far below the centre a vertex looks for ground: × radius, clamped, m.")]
        [Min(0)] public float probeDownPerMetre = .6f;
        public Vector2 probeDownLimits = new Vector2(3, 40);

        static readonly RaycastHit[] Hits = new RaycastHit[16];

        Vector3 centre;
        float radius = -1, widthScale = 1;
        Color colour = Color.white;
        int count, boilFrame = int.MinValue;
        Vector3[] ground = new Vector3[0], directions = new Vector3[0], points = new Vector3[0];
        float seed;

        public bool Visible => line && line.enabled;
        public Vector3 Centre => centre;
        public float Radius => radius;

        void Reset() => line = GetComponent<LineRenderer>();

        void Awake()
        {
            if (!line) line = GetComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = true;
            // Lies flat: the stroke faces up, so it reads as drawn on the ground, not as a ribbon in the air.
            line.alignment = LineAlignment.TransformZ;
            line.enabled = false;
            seed = Random.Range(0f, 1000f);
        }

        /// <summary>Shows the ring (or moves it). Ground is resampled only when the centre or radius really changed.</summary>
        public void Show(Vector3 at, float reach, Color tint, float strokeScale = 1)
        {
            reach = Mathf.Max(.05f, reach);
            bool moved = (at - centre).sqrMagnitude > .0025f || Mathf.Abs(reach - radius) > .01f || count == 0;
            centre = at;
            radius = reach;
            if (moved) Sample();
            SetLook(tint, strokeScale);
            transform.rotation = Flat;
            if (!line.enabled)
            {
                line.enabled = true;
                boilFrame = int.MinValue;
            }
            Redraw(moved);
        }

        /// <summary>Colour (alpha included) and stroke width multiplier, without moving the ring.</summary>
        public void SetLook(Color tint, float strokeScale = 1)
        {
            colour = tint;
            widthScale = Mathf.Max(0, strokeScale);
            line.startColor = line.endColor = colour;
            line.widthMultiplier = StrokeWidth * widthScale;
        }

        public void Hide()
        {
            if (line) line.enabled = false;
        }

        float StrokeWidth => Mathf.Min(maximumWidth, width + widthPerMetre * radius);

        static readonly Quaternion Flat = Quaternion.LookRotation(Vector3.up, Vector3.forward);

        void Update()
        {
            if (!line.enabled) return;
            // The owner rides on the player: keep the stroke facing up whatever the parent does.
            transform.rotation = Flat;
            Redraw(false);
        }

        void Sample()
        {
            float circumference = 2 * Mathf.PI * radius;
            count = Mathf.Clamp(Mathf.CeilToInt(circumference * verticesPerMetre), minimumVertices, Mathf.Max(minimumVertices, maximumVertices));
            if (ground.Length != count)
            {
                ground = new Vector3[count];
                directions = new Vector3[count];
                points = new Vector3[count];
            }
            float up = Mathf.Clamp(radius * probeUpPerMetre, probeUpLimits.x, probeUpLimits.y);
            float down = Mathf.Clamp(radius * probeDownPerMetre, probeDownLimits.x, probeDownLimits.y);
            for (int i = 0; i < count; i++)
            {
                float angle = i * (2 * Mathf.PI / count);
                var direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                directions[i] = direction;
                ground[i] = GroundUnder(centre + direction * radius, up, down, centre.y);
            }
            // A whole number of dashes around the loop: no seam where it closes.
            int dashes = Mathf.Max(3, Mathf.RoundToInt(circumference / (StrokeWidth * dashInWidths)));
            line.textureMode = LineTextureMode.Stretch;
            line.textureScale = new Vector2(dashes, 1);
            line.positionCount = count;
        }

        /// <summary>
        /// The ground under a point on the circle: the nearest solid, non-moving surface below the lowest ceiling
        /// above the centre's height. Creatures, cards and loose props are ignored; water's surface counts.
        /// </summary>
        public static Vector3 GroundUnder(Vector3 point, float up, float down, float fallbackHeight)
        {
            int mask = CardFlight.CollisionMask;
            var start = new Vector3(point.x, fallbackHeight, point.z);
            if (Physics.Raycast(start, Vector3.up, out var ceiling, up, mask, QueryTriggerInteraction.Ignore))
                up = Mathf.Max(0, ceiling.distance - .1f);
            var from = start + Vector3.up * up;
            int hits = Physics.RaycastNonAlloc(from, Vector3.down, Hits, up + down, mask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Vector3 found = start;
            for (int i = 0; i < hits; i++)
            {
                var body = Hits[i].rigidbody;
                if (body && !body.isKinematic) continue;
                if (Hits[i].distance < best)
                {
                    best = Hits[i].distance;
                    found = Hits[i].point;
                }
            }
            return found;
        }

        void Redraw(bool force)
        {
            if (count == 0) return;
            int frame = boilRate > 0 ? Mathf.FloorToInt(Time.time * boilRate) : 0;
            if (!force && frame == boilFrame) return;
            boilFrame = frame;
            float amplitude = Mathf.Clamp(radius * wobble, wobbleLimits.x, wobbleLimits.y);
            float phase = seed + frame * 3.71f;
            for (int i = 0; i < count; i++)
            {
                var direction = directions[i];
                // Smooth bumps around the circle plus a little per-vertex shake: drawn, not plotted.
                float bump = Mathf.PerlinNoise(phase + direction.x * wobbleFrequency, phase * .37f + direction.z * wobbleFrequency) * 2 - 1;
                float shake = (Hash(i, frame) * 2 - 1) * .25f;
                float offset = amplitude * (bump + shake);
                var p = ground[i] + direction * offset;
                p.y += lift;
                points[i] = p;
            }
            line.SetPositions(points);
        }

        static float Hash(int index, int frame)
        {
            unchecked
            {
                uint h = (uint)(index * 73856093) ^ (uint)(frame * 19349663);
                h ^= h >> 13;
                h *= 0x5bd1e995;
                h ^= h >> 15;
                return (h & 0xffff) / 65535f;
            }
        }
    }
}
