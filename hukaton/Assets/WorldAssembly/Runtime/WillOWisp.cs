using Hakaton.Expeditions;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.WorldAssembly.Unity
{
    /// <summary>
    /// A will-o'-the-wisp of the rotten swamp: a pale light over the water that drifts off as the player comes near,
    /// a stretch at a time, always towards where it leads — treasure (a find, a chest, a sunken chest) or a bog
    /// window. There it waits, and goes out when the player reaches it. Nothing tells which it leads to.
    /// </summary>
    public sealed class WillOWisp : MonoBehaviour
    {
        public Light glow;
        [Tooltip("Height over the ground or water it floats at, m.")]
        public float hover = 1.2f;
        [Tooltip("How fast it drifts, m/s; how near the player wakes it, m; how far it goes at a time, m.")]
        public float speed = 3, wake = 9, stretch = 6;
        [Tooltip("At its goal it goes out when the player is this near, m.")]
        public float near = 2.4f;
        public SfxCue drift, vanish;

        ExpeditionWorld world;
        Transform player;
        Vector3 from, goal, legEnd;
        bool moving, going;
        float brightness, age, travelled, total;

        /// <summary>Where it leads (for tests).</summary>
        public Vector3 Goal => goal;

        /// <summary>Starts at <paramref name="start"/> and will lead towards <paramref name="target"/>.</summary>
        public void Lead(ExpeditionWorld world, Transform player, Vector3 start, Vector3 target)
        {
            this.world = world;
            this.player = player;
            from = start;
            goal = target + Vector3.up * hover;
            transform.position = start;
            total = Mathf.Max(.1f, Flat(start, goal));
            brightness = glow ? glow.intensity : 1;
        }

        static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        void Update()
        {
            if (!player) return;
            age += Time.deltaTime;
            var p = transform.position;
            if (going)
            {
                transform.localScale = Vector3.MoveTowards(transform.localScale, Vector3.zero, Time.deltaTime * 2);
                if (glow) glow.intensity = Mathf.MoveTowards(glow.intensity, 0, brightness * Time.deltaTime * 2);
                if (transform.localScale.x <= .01f) Destroy(gameObject);
                return;
            }
            float toPlayer = Flat(p, player.position);
            float toGoal = Flat(p, goal);
            if (toGoal < .4f)
            {
                // At its goal: it waits for the player, then goes out.
                if (toPlayer < near) Vanish();
            }
            else if (!moving && toPlayer < wake)
            {
                var dir = goal - p;
                dir.y = 0;
                legEnd = p + dir.normalized * Mathf.Min(stretch, toGoal);
                moving = true;
                if (world && drift) world.Play(drift, p, .5f, Random.Range(.9f, 1.2f));
            }
            if (moving)
            {
                var next = Vector3.MoveTowards(new Vector3(p.x, 0, p.z), new Vector3(legEnd.x, 0, legEnd.z), speed * Time.deltaTime);
                travelled = Mathf.Clamp01(1 - Flat(next, goal) / total);
                p = new Vector3(next.x, 0, next.z);
                if (Flat(p, legEnd) < .05f) moving = false;
            }
            // Height from start to goal along the way, bobbing; the light breathes.
            float y = Mathf.Lerp(from.y, goal.y, travelled) + Mathf.Sin(age * 2.1f) * .15f;
            transform.position = new Vector3(p.x, y, p.z);
            if (glow) glow.intensity = brightness * (.75f + .25f * Mathf.PerlinNoise(age * 3, 1.7f));
        }

        void Vanish()
        {
            going = true;
            if (world && vanish) world.Play(vanish, transform.position, .6f);
        }
    }
}
