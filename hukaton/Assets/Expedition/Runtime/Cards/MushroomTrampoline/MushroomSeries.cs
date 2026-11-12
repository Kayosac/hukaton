using System.Runtime.CompilerServices;
using Hakaton.Character;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// The player's series of mushroom bounces (playtest rule): every bounce on a mushroom or a wall shelf while the
    /// series lasts is at least <see cref="MushroomBounce.SeriesHeight"/> of the series' first bounce — +5 % per bounce,
    /// up to +25 %. The series goes on from mushroom to mushroom (floor ones and shelves) with no time limit (Moon
    /// gravity can keep the player in the air for seconds) and ends when the player stands on anything that is not a
    /// mushroom, hangs on the rope (the body is externally controlled), is moved without travelling (Teleport, Swap),
    /// the outing ends, or no mushroom is left to watch the player (the next mushroom then starts a new series).
    /// One series per expedition world (<see cref="Of"/>), not per mushroom; every live mushroom feeds it
    /// <see cref="Observe"/> once per frame. The rules are pure (<see cref="Check"/>) and covered by EditMode tests.
    /// </summary>
    public sealed class MushroomSeries
    {
        /// <summary>Why a series ended (or <see cref="None"/>).</summary>
        public enum Break { None, OutingOver, Unwatched, ExternallyControlled, Ground, Moved }

        /// <summary>What a mushroom sees of the player in one frame.</summary>
        public readonly struct Sample
        {
            public readonly bool Running, ExternallyControlled, Grounded, OnMushroom;
            public readonly Vector3 Position;
            /// <summary>Speed of the body, m/s (real units): how far it may honestly travel in a frame.</summary>
            public readonly float Speed;

            public Sample(bool running, bool externallyControlled, bool grounded, bool onMushroom, Vector3 position, float speed)
            {
                Running = running; ExternallyControlled = externallyControlled; Grounded = grounded; OnMushroom = onMushroom;
                Position = position; Speed = speed;
            }
        }

        /// <summary>Distance a body may jump between two frames beyond what its speed explains before it counts as moved, m.</summary>
        public const float MoveSlack = 1.5f;

        static readonly ConditionalWeakTable<ExpeditionWorld, MushroomSeries> Worlds = new ConditionalWeakTable<ExpeditionWorld, MushroomSeries>();

        /// <summary>The series of this world's player.</summary>
        public static MushroomSeries Of(ExpeditionWorld world) => Worlds.GetValue(world, _ => new MushroomSeries());

        /// <summary>Bounces so far in the current series (0: none, the next bounce starts one).</summary>
        public int Bounces { get; private set; }
        /// <summary>Height of the series' first bounce, design m.</summary>
        public float FirstHeight { get; private set; }
        public bool Active => Bounces > 0;
        /// <summary>Why the last series ended.</summary>
        public Break LastBreak { get; private set; }

        int lastFrame = int.MinValue;
        Vector3 lastPosition;
        float lastSpeed;

        /// <summary>
        /// Registers a bounce and returns its height, design m. <paramref name="fallBounceHeight"/> is what the fall alone
        /// gives (<see cref="MushroomBounce.BounceHeight"/>); the first bounce of a series is exactly that and sets the line.
        /// </summary>
        public float Next(float fallBounceHeight, float maxHeight, float stepShare, int maxSteps)
        {
            float height = Bounces == 0
                ? fallBounceHeight
                : MushroomBounce.SeriesBounceHeight(fallBounceHeight, FirstHeight, Bounces, maxHeight, stepShare, maxSteps);
            if (Bounces == 0) FirstHeight = fallBounceHeight;
            Bounces++;
            return height;
        }

        public void Reset(Break reason = Break.None)
        {
            if (Bounces > 0) LastBreak = reason;
            Bounces = 0;
            FirstHeight = 0;
        }

        /// <summary>
        /// The reset rules. <paramref name="framesSinceLast"/>: frames since the previous observation (more than one: no
        /// mushroom was watching, so the player may have stood anywhere); <paramref name="moved"/>: distance since then;
        /// <paramref name="honestTravel"/>: what the body's speed explains over that time.
        /// </summary>
        public static Break Check(in Sample sample, int framesSinceLast, float moved, float honestTravel)
        {
            if (!sample.Running) return Break.OutingOver;
            if (framesSinceLast > 1) return Break.Unwatched;
            if (sample.ExternallyControlled) return Break.ExternallyControlled;
            if (sample.Grounded && !sample.OnMushroom) return Break.Ground;
            if (moved > honestTravel + MoveSlack) return Break.Moved;
            return Break.None;
        }

        /// <summary>One look at the player per frame (later calls in the same frame are ignored). Ends the series by <see cref="Check"/>.</summary>
        public void Observe(int frame, float deltaTime, in Sample sample)
        {
            if (frame == lastFrame) return;
            bool first = lastFrame == int.MinValue;
            int frames = first ? int.MaxValue : frame - lastFrame;
            float moved = first ? 0 : Vector3.Distance(sample.Position, lastPosition);
            float honest = Mathf.Max(sample.Speed, lastSpeed) * Mathf.Max(0, deltaTime) * 1.5f;
            lastFrame = frame;
            lastPosition = sample.Position;
            lastSpeed = sample.Speed;
            if (!Active) return;
            var reason = Check(sample, frames, moved, honest);
            if (reason != Break.None) Reset(reason);
        }

        /// <summary>Builds the sample from the motor and observes it.</summary>
        public void Observe(ExpeditionWorld world, FirstPersonMotor motor)
        {
            if (!world || !motor) return;
            var ground = motor.GroundCollider;
            bool onMushroom = ground && ground.GetComponentInParent<MushroomTrampoline>();
            var sample = new Sample(world.Running, motor.ExternallyControlled, motor.IsGrounded, onMushroom,
                motor.transform.position, motor.Velocity.magnitude);
            Observe(Time.frameCount, Time.deltaTime, sample);
        }

        /// <summary>A bounce is also a look at the player: the series stays watched in the frame it grows.</summary>
        public void Touch(int frame, Vector3 position, float speed)
        {
            lastFrame = frame;
            lastPosition = position;
            lastSpeed = speed;
        }
    }
}
