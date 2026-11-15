using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// A loose thing that floats (a sunken boat): the water holds up points along its bottom at the depth it was set
    /// to, and it rises and sinks a little with a slow swell. A card or a blow sends it drifting and rocking; the water
    /// slows it down. Away from the player it lies still like any dormant prop and floats again when he comes near.
    /// </summary>
    [RequireComponent(typeof(PhysicsProp))]
    public sealed class Floater : MonoBehaviour
    {
        public PhysicsProp prop;
        [Tooltip("Points along its bottom (its own space) the water holds up.")]
        public Vector3[] points = new Vector3[0];
        [Tooltip("How deep the points sit when it floats at rest, m (the water line it was set to).")]
        [Min(.01f)] public float draft = .3f;
        [Tooltip("How far it rises and sinks with the water, m.")]
        public float swell = .035f;
        [Tooltip("How long one swell takes, s.")]
        [Min(.5f)] public float swellSeconds = 3.6f;
        [Tooltip("It floats (a real body) within this distance of the player; further away it lies still.")]
        public float awakeWithin = 35;

        static readonly RaycastHit[] Hits = new RaycastHit[8];
        float surface = float.NaN, phase, nextLook;

        /// <summary>The water's surface under it, once found (NaN before).</summary>
        public float Surface => surface;

        void OnEnable()
        {
            phase = Random.value * swellSeconds;
            surface = float.NaN;
            nextLook = 0;
        }

        /// <summary>
        /// The water under it, and its surface made no floor for it: things land on the water's surface (Default and
        /// Water collide), a hull goes into the water.
        /// </summary>
        bool FindWater()
        {
            int n = Physics.RaycastNonAlloc(transform.position + Vector3.up * 6, Vector3.down, Hits, 14, 1 << 4, QueryTriggerInteraction.Ignore);
            float level = float.NaN;
            var own = GetComponentsInChildren<Collider>();
            for (int i = 0; i < n; i++)
            {
                var water = Hits[i].collider;
                if (!water) continue;
                level = float.IsNaN(level) ? Hits[i].point.y : Mathf.Max(level, Hits[i].point.y);
                foreach (var c in own) if (c) Physics.IgnoreCollision(c, water);
            }
            surface = level;
            return !float.IsNaN(level);
        }

        void FixedUpdate()
        {
            var body = prop ? prop.body : null;
            if (!body || points.Length == 0) return;
            if (float.IsNaN(surface))
            {
                // The chunk's water may come a moment after the boat.
                if (Time.time < nextLook) return;
                nextLook = Time.time + 1;
                if (!FindWater()) return;
            }
            if (body.isKinematic)
            {
                var world = ExpeditionWorld.Active;
                if (Time.time < nextLook || !world || !world.motor) return;
                nextLook = Time.time + .5f;
                if ((world.motor.transform.position - transform.position).sqrMagnitude < awakeWithin * awakeWithin) prop.Wake();
                return;
            }
            float level = surface + Mathf.Sin((Time.time + phase) * (2 * Mathf.PI / swellSeconds)) * swell;
            // At rest every point sits at the draft and together they carry the weight.
            float hold = body.mass * -Physics.gravity.y / (points.Length * draft);
            float share = body.mass / points.Length;
            foreach (var p in points)
            {
                var at = transform.TransformPoint(p);
                float depth = level - at.y;
                if (depth <= 0) continue;
                var v = body.GetPointVelocity(at);
                var force = Vector3.up * (hold * Mathf.Min(depth, draft * 2)) - new Vector3(v.x * .8f, v.y * 3, v.z * .8f) * share;
                body.AddForceAtPosition(force, at);
            }
        }
    }
}
