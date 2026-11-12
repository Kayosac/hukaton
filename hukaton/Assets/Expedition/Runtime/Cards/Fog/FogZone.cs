using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>Pure geometry of the fog (EditMode-tested).</summary>
    public static class FogRules
    {
        /// <summary>
        /// Does the segment from → to pass through the vertical cylinder standing on <paramref name="baseCentre"/>
        /// (radius, height up from the base)? Ends inside count.
        /// </summary>
        public static bool SegmentCrossesCylinder(Vector3 from, Vector3 to, Vector3 baseCentre, float radius, float height)
        {
            float t0 = 0, t1 = 1;
            float bottom = baseCentre.y, top = baseCentre.y + height, dy = to.y - from.y;
            if (Mathf.Abs(dy) < 1e-6f)
            {
                if (from.y < bottom || from.y > top) return false;
            }
            else
            {
                float a = (bottom - from.y) / dy, b = (top - from.y) / dy;
                if (a > b) (a, b) = (b, a);
                t0 = Mathf.Max(t0, a);
                t1 = Mathf.Min(t1, b);
                if (t0 > t1) return false;
            }
            var p = new Vector2(from.x - baseCentre.x, from.z - baseCentre.z);
            var d = new Vector2(to.x - from.x, to.z - from.z);
            float qa = Vector2.Dot(d, d), qb = 2 * Vector2.Dot(p, d), qc = Vector2.Dot(p, p) - radius * radius;
            if (qa < 1e-8f) return qc <= 0;
            float disc = qb * qb - 4 * qa * qc;
            if (disc < 0) return false;
            float root = Mathf.Sqrt(disc);
            float e0 = (-qb - root) / (2 * qa), e1 = (-qb + root) / (2 * qa);
            return e0 <= t1 && e1 >= t0;
        }
    }

    /// <summary>
    /// A fog bank left by «Туман» (prefab <c>Assets/Game/Prefabs/Cards/Fog/Fog.prefab</c>): a cylinder of radius 3 m for
    /// 8 s (upgraded 12 s). Any line of sight through it is hidden (<see cref="IVisionBlocker"/>): creatures ask
    /// <see cref="ExpeditionWorld.IsVisionBlocked"/> and lose sight of the player, then search the last known place by
    /// their usual rules (they do not forget aggression at once). The player sees a dense crooked fog dome from outside
    /// and fog all around from inside. It does not make anyone wet.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FogZone : CardZoneBehaviour, IVisionBlocker, ICardAreaOutline
    {
        [Header("Fog")]
        [Tooltip("Sight lines shorter than this still see through (face to face inside the fog), m.")]
        [Min(0)] public float clearSightDistance = .9f;

        [Header("Look")]
        [Tooltip("Crooked fog dome, unit radius and height, scaled to the zone; seen from both sides.")]
        public Transform dome;
        public Renderer domeRenderer;
        public Color colour = new Color(.8f, .82f, .84f, 1);
        [Range(0, 1)] public float density = .9f;
        [Tooltip("Retro Arsenal smoke inside the fog; the shape follows the radius.")]
        public ParticleSystem[] wisps;
        [Min(0)] public float gatherSeconds = .6f, fadeSeconds = 1.2f;
        [Min(0)] public float swirlDegreesPerSecond = 7, breathe = .05f;

        static readonly int TintId = Shader.PropertyToID("_Tint");

        float until, born, endedAt = -1, phase;
        MaterialPropertyBlock block;

        public override CardZoneKind Kind => CardZoneKind.Fog;
        public bool Active => World && endedAt < 0;
        public float Remaining => Active ? Mathf.Max(0, until - Time.time) : 0;

        void Awake()
        {
            block = new MaterialPropertyBlock();
            phase = Random.value * 10;
        }

        public override void OnSpawned(ExpeditionWorld world)
        {
            base.OnSpawned(world);
            born = Time.time;
        }

        /// <summary>Numbers from the card asset, right after spawning.</summary>
        public void Setup(float seconds, float fogRadius)
        {
            radius = Mathf.Max(.1f, fogRadius);
            until = Time.time + Mathf.Max(0, seconds);
            if (wisps != null)
                foreach (var wisp in wisps)
                {
                    if (!wisp) continue;
                    var shape = wisp.shape;
                    shape.radius = radius * .8f;
                }
            Animate();
        }

        public bool BlocksVision(Vector3 from, Vector3 to)
        {
            if (!Active) return false;
            if ((to - from).sqrMagnitude <= clearSightDistance * clearSightDistance) return false;
            Vector3 o = transform.position;
            return FogRules.SegmentCrossesCylinder(from, to, new Vector3(o.x, o.y - depth, o.z), radius, height + depth);
        }

        void Update()
        {
            if (!World) return;
            if (Active && Time.time >= until)
            {
                endedAt = Time.time;
                World.Zones.Remove(this);
                if (wisps != null) foreach (var wisp in wisps) if (wisp) wisp.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
            Animate();
            if (!Active && Time.time - endedAt >= fadeSeconds) Destroy(gameObject);
        }

        void Animate()
        {
            float grow = gatherSeconds > 0 ? Ease.OutCubic((Time.time - born) / gatherSeconds) : 1;
            float fade = Active ? 1 : 1 - (fadeSeconds > 0 ? Mathf.Clamp01((Time.time - endedAt) / fadeSeconds) : 1);
            if (dome)
            {
                float t = (Time.time + phase) * Mathf.PI * 2;
                float b = 1 + Mathf.Sin(t * .2f) * breathe;
                float r = radius * Mathf.Lerp(.35f, 1, grow) * b;
                dome.localScale = new Vector3(r, height * Mathf.Lerp(.3f, 1, grow) * (2 - b), r * (1 + Mathf.Sin(t * .27f + 1) * breathe));
                dome.localRotation = Quaternion.Euler(0, (Time.time + phase) * swirlDegreesPerSecond, 0);
            }
            if (domeRenderer)
            {
                if (block == null) block = new MaterialPropertyBlock();
                domeRenderer.GetPropertyBlock(block);
                block.SetColor(TintId, new Color(colour.r, colour.g, colour.b, colour.a * density * Mathf.Clamp01(grow * 1.5f) * fade));
                domeRenderer.SetPropertyBlock(block);
            }
        }

        /// <summary>Outlined on the ground while it works (<see cref="CardAreaDisplay"/>).</summary>
        public bool TryGetOutline(out Vector3 centre, out float radius)
        {
            centre = transform.position;
            radius = this.radius;
            return Active;
        }
    }
}
