using System.Collections.Generic;
using Hakaton.Character;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// A bog window of the rotten swamp (Kayospace «Биом: Гнилая топь», трясина): mire of its own colour that does not
    /// drown at once like deep water. Stepping in, the player is slowed hard and sinks slowly; a jump is a lunge that
    /// lifts and pulls towards the nearest edge (weaker the deeper they are), so they can try to get out; sunk to the
    /// head, drowned. Cards that move the body (Impulse, Updraft, Rope, Teleport) pull it out as they would anywhere,
    /// and Ice freezes a crust over the mire (the lake's ice patch) that holds. A creature never walks
    /// in (its navigation keeps out of the bog); pushed in, it goes under and drowns, and its loot floats up at the
    /// edge. Sits beside the <see cref="WorldObstacle"/> (lake) of the bog, which knows the surface.
    /// </summary>
    [RequireComponent(typeof(WorldObstacle))]
    public sealed class BogMire : MonoBehaviour
    {
        [Header("Player")]
        [Tooltip("How fast the player sinks, m/s (design units).")]
        [Min(.01f)] public float sinkSpeed = .14f;
        [Tooltip("Sunk this deep (feet under the surface), m: the head is under — drowned.")]
        [Min(.2f)] public float drownDepth = 1.3f;
        [Tooltip("Walking speed in the mire (times the usual).")]
        [Range(.05f, 1)] public float moveScale = .3f;
        [Tooltip("A jump in the mire: upward speed at the surface (x) and when nearly drowned (y), m/s (design units).")]
        public Vector2 lungeUp = new Vector2(5.5f, 3f);
        [Tooltip("…and towards the nearest edge, at the surface (x) and nearly drowned (y), m/s.")]
        public Vector2 lungeOut = new Vector2(3.5f, 2.2f);
        [Min(0)] public float lungeCooldown = .35f;
        [Tooltip("The nearest edge is looked for this far, m.")]
        [Min(1)] public float edgeSearch = 14;

        [Header("Creatures")]
        [Tooltip("A creature pushed in goes under in this time, s.")]
        [Min(.2f)] public float creatureSinkSeconds = 2.5f;
        [Tooltip("A creature's feet this deep in the mire and it is lost, m (a hop over the edge is not).")]
        [Min(0)] public float creatureCatchDepth = .15f;

        [Header("Feedback")]
        [Tooltip("Thick splash where a body falls in, and at each lunge.")]
        public VfxBurst gulp;
        public Color gulpTint = new Color(.35f, .27f, .15f);
        public SfxCue sinkLoop, lungeSound, swallowSound;

        WorldObstacle water;
        bool holding;
        float nextLunge;
        static readonly List<ICombatant> Bodies = new List<ICombatant>();

        /// <summary>Is the player held by this mire right now?</summary>
        public bool HoldingPlayer => holding;

        WorldObstacle Water => water ? water : water = GetComponent<WorldObstacle>();

        /// <summary>Feet in the mire (under its surface, not on ice that holds).</summary>
        public bool Holds(Vector3 feet) => Water && Water.InWater(feet);

        /// <summary>How far under the surface these feet are, m (0 beside the mire).</summary>
        public float SunkDepth(Vector3 feet) => Water && Water.SurfaceAt(feet, out float level) ? Mathf.Max(0, level - feet.y) : 0;

        void Update()
        {
            var world = Water ? Water.world : null;
            if (!world || !world.Running) { Release(world); return; }
            HoldPlayer(world);
            SwallowCreatures(world);
        }

        void OnDisable() => Release(Water ? Water.world : null);

        // ---------- the player ----------

        void HoldPlayer(ExpeditionWorld world)
        {
            var motor = world.motor;
            if (!motor) return;
            Vector3 feet = motor.transform.position;
            // A rope or an updraft holding the body has it out of the mire's grip.
            if (motor.ExternallyControlled || !Holds(feet)) { Release(world); return; }
            float depth = SunkDepth(feet);
            if (!holding)
            {
                holding = true;
                world.Spawn(gulp, new Vector3(feet.x, feet.y + depth, feet.z), Quaternion.identity, gulpTint, 1.2f);
                if (world.sfx && sinkLoop) world.sfx.Loop(this, sinkLoop, feet);
            }
            // Slow, sinking: the fall through the mire is held to a crawl, whatever goes on above.
            motor.ApplyModifier(MotorModifier.MoveSpeed, moveScale, .2f);
            motor.ApplyFallSpeedLimit(sinkSpeed, .2f);
            if (depth >= drownDepth)
            {
                Release(world);
                world.Play(swallowSound, feet);
                world.Drown();
                return;
            }
            var input = motor.input;
            bool active = !motor.control || motor.control.GameplayActive;
            if (active && input && input.JumpPressed && Time.time >= nextLunge) Lunge(world, motor, feet, depth);
        }

        /// <summary>The player's lunge, as a jump in the mire does it (tests, other inputs). False when not held.</summary>
        public bool LungeNow()
        {
            var world = Water ? Water.world : null;
            if (!holding || !world || !world.motor) return false;
            var feet = world.motor.transform.position;
            Lunge(world, world.motor, feet, SunkDepth(feet));
            return true;
        }

        void Lunge(ExpeditionWorld world, FirstPersonMotor motor, Vector3 feet, float depth)
        {
            nextLunge = Time.time + lungeCooldown;
            float k = Mathf.Clamp01(depth / drownDepth);
            Vector3 out_ = EdgeDirection(feet, motor.transform.forward, out _);
            motor.ApplyNormalizedImpulse(out_ * Mathf.Lerp(lungeOut.x, lungeOut.y, k) + Vector3.up * Mathf.Lerp(lungeUp.x, lungeUp.y, k));
            world.Spawn(gulp, new Vector3(feet.x, feet.y + depth, feet.z), Quaternion.identity, gulpTint, .8f);
            world.Play(lungeSound, feet, 1, Random.Range(.9f, 1.1f));
        }

        void Release(ExpeditionWorld world)
        {
            if (!holding) return;
            holding = false;
            if (world && world.sfx) world.sfx.StopLoop(this);
        }

        // ---------- creatures ----------

        void SwallowCreatures(ExpeditionWorld world)
        {
            Bodies.Clear();
            world.Combatants(Bodies);
            foreach (var body in Bodies)
            {
                if (!(body is WorldCreature creature) || !creature.Alive) continue;
                Vector3 feet = creature.Position;
                if (!Holds(feet) || SunkDepth(feet) < creatureCatchDepth) continue;
                EdgeDirection(feet, creature.transform.forward, out var edge);
                world.Spawn(gulp, feet + Vector3.up * SunkDepth(feet), Quaternion.identity, gulpTint, Mathf.Clamp(creature.Size, .8f, 2));
                creature.SinkInMire(edge, creatureSinkSeconds);
            }
        }

        // ---------- the edge ----------

        /// <summary>
        /// Flat direction to the nearest ground beside the mire (rings of 16 rays, nearest first), and that point;
        /// <paramref name="fallback"/> when no edge is found within <see cref="edgeSearch"/>.
        /// </summary>
        public Vector3 EdgeDirection(Vector3 from, Vector3 fallback, out Vector3 edge)
        {
            for (float r = .75f; r <= edgeSearch; r += .75f)
                for (int i = 0; i < 16; i++)
                {
                    float a = (i + (r * 3.1f) % 1) / 16f * Mathf.PI * 2;
                    var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    var p = from + dir * r;
                    if (Water.SurfaceAt(p, out _)) continue;
                    edge = from + dir * (r + .6f);
                    return dir;
                }
            fallback.y = 0;
            fallback = fallback.sqrMagnitude > 1e-4f ? fallback.normalized : Vector3.forward;
            edge = from + fallback * edgeSearch;
            return fallback;
        }
    }
}
