using System.Collections;
using Hakaton.Expeditions;
using Hakaton.Feel;
using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.SpecialItems
{
    /// <summary>
    /// «Компас-муравей» (pg_anthill-loot-cards): a compass in the left hand, bottom left, alongside the cards; instead
    /// of a needle a live ant on the pin (its own drawing, turned round its centre over the still dial). Above ground the
    /// ant pulls toward the nearest anthill's mouth; in a dungeon toward the nearest stash whose chest is still shut, and
    /// once none is left, toward the way up (<see cref="CompassBearings"/>). It is alive: it swings round with a little
    /// overshoot, twitches now and then — more and faster when the thing it smells is close — and with nothing to smell
    /// it wanders round the dial. Opened by the first slain Queen, sold by the goblin.
    /// </summary>
    public sealed class CompassTool : SpecialItemTool
    {
        enum State { Hidden, Drawing, Ready, Stowing }

        [Header("Compass in the hand")]
        [Tooltip("The hand's canvas; off while the compass is put away.")]
        public Canvas canvas;
        [Tooltip("The hand with the compass; its pivot is at the wrist, which it turns around.")]
        public RectTransform hand;
        [Tooltip("The hand and the dial (the special items' hand-drawn material).")]
        public Image body;
        [Tooltip("The ant on the pin, pivot at its centre; head up is straight ahead.")]
        public RectTransform ant;
        [Tooltip("Where the hand rests and how it comes in and moves (shared with the other items in the hand).")]
        public HandHold hold = new HandHold();
        [Min(.01f)] public float enterSeconds = .24f, leaveSeconds = .18f;

        [Header("The ant")]
        [Tooltip("How often the ant sniffs where its target is, s.")]
        [Min(.05f)] public float sniffSeconds = .25f;
        [Tooltip("How quick and how springy its turn is: frequency (Hz) and damping (1: no overshoot).")]
        [Min(.1f)] public float turnFrequency = 2.6f;
        [Range(.05f, 1.5f)] public float turnDamping = .55f;
        [Tooltip("A twitch now and then: how far (°) and how often (s, from–to).")]
        public float twitch = 9;
        public Vector2 twitchEvery = new Vector2(.5f, 1.4f);
        [Tooltip("Closer than this to its target (m) the ant gets excited: twitches twice as often and wider.")]
        [Min(0)] public float excitedWithin = 8;
        [Tooltip("Nothing to smell: it wanders round the dial at about this speed, °/s.")]
        public float wanderSpeed = 70;

        [Header("Sound")]
        public SfxCue drawSound;

        State state = State.Hidden;
        float slide, angle, velocity, desired, nextSniff, nextTwitch, wander;
        Coroutine motion;

        public override bool IsOut => state != State.Hidden && state != State.Stowing;

        /// <summary>What the ant is drawn to now.</summary>
        public CompassTarget Target { get; private set; }
        /// <summary>The ant's turn on the dial, degrees clockwise from straight ahead.</summary>
        public float Heading => -angle;

        public override string ControlHint
        {
            get
            {
                switch (Target.Kind)
                {
                    case CompassTargetKind.Entrance: return "Муравей тянется к муравейнику";
                    case CompassTargetKind.Stash: return "Муравей тянется к тайнику";
                    case CompassTargetKind.WayOut: return "Тайников нет — муравей тянется к выходу";
                    default: return "Муравей ничего не чует";
                }
            }
        }

        void Awake() => HideAll();

        void HideAll()
        {
            state = State.Hidden;
            if (canvas) canvas.enabled = false;
            slide = 0;
            hold.End();
            hold.Apply(hand, 0, 0);
        }

        public override void OutingBegan()
        {
            if (motion != null) StopCoroutine(motion);
            motion = null;
            HideAll();
        }

        public override void Draw()
        {
            if (IsOut || !World || !World.Running) return;
            if (motion != null) StopCoroutine(motion);
            motion = StartCoroutine(DrawRoutine());
        }

        public override void Stow(bool instant = false)
        {
            if (state == State.Hidden) return;
            if (motion != null) StopCoroutine(motion);
            motion = null;
            if (instant || !isActiveAndEnabled) { HideAll(); return; }
            motion = StartCoroutine(StowRoutine());
        }

        IEnumerator DrawRoutine()
        {
            state = State.Drawing;
            if (canvas) canvas.enabled = true;
            hold.Begin(World);
            World.Play(drawSound);
            // It wakes up pointing wherever it was; it finds its target at once.
            nextSniff = 0;
            Sniff();
            for (float t = 0; t < enterSeconds; t += Time.deltaTime)
            {
                slide = t / enterSeconds;
                yield return null;
            }
            slide = 1;
            state = State.Ready;
            motion = null;
        }

        IEnumerator StowRoutine()
        {
            state = State.Stowing;
            World.Play(drawSound, null, .8f, .85f);
            for (float t = 0; t < leaveSeconds; t += Time.deltaTime)
            {
                slide = 1 - t / leaveSeconds;
                yield return null;
            }
            HideAll();
            motion = null;
        }

        void Update()
        {
            if (state == State.Hidden || !World || !World.motor) return;
            float dt = Time.deltaTime;
            if (Time.time >= nextSniff) Sniff();
            float goal = desired;
            if (!Target.Found)
            {
                // Lost: wanders round the dial, now one way, now the other.
                wander += dt * wanderSpeed * (Mathf.PerlinNoise(Time.time * .35f, 3.1f) * 2 - 1) * 2;
                goal = wander;
            }
            else wander = angle;
            // Twitches: alive, and eager when close.
            if (Time.time >= nextTwitch)
            {
                bool excited = Target.Found && Distance() < excitedWithin;
                float every = Random.Range(twitchEvery.x, twitchEvery.y) * (excited ? .5f : 1);
                nextTwitch = Time.time + every;
                velocity += Random.Range(-1f, 1f) * twitch * (excited ? 2 : 1) * turnFrequency * 2;
            }
            // A spring toward the goal along the short way round.
            float error = Mathf.DeltaAngle(angle, goal);
            float w = turnFrequency * 2 * Mathf.PI;
            velocity += (w * w * error - 2 * turnDamping * w * velocity) * dt;
            angle += velocity * dt;
            if (ant) ant.localEulerAngles = new Vector3(0, 0, angle);
        }

        /// <summary>Where the target is, as a turn of the ant from straight ahead (UI: counter-clockwise positive).</summary>
        void Sniff()
        {
            nextSniff = Time.time + sniffSeconds;
            var player = World.motor.transform;
            Target = CompassBearings.Find(player.position);
            if (!Target.Found) return;
            var view = World.viewCamera ? World.viewCamera.transform : player;
            Vector3 forward = view.forward, to = Target.Point - player.position;
            forward.y = to.y = 0;
            if (forward.sqrMagnitude < 1e-4f || to.sqrMagnitude < .01f) return;
            desired = -Vector3.SignedAngle(forward, to, Vector3.up);
        }

        float Distance()
        {
            Vector3 to = Target.Point - World.motor.transform.position;
            to.y = 0;
            return to.magnitude;
        }

        void LateUpdate()
        {
            if (state == State.Hidden) return;
            // The target's turn follows the view between sniffs: turning round, the ant stays on it.
            if (Target.Found && World && World.motor) RefreshTurn();
            hold.Apply(hand, slide, Time.deltaTime);
        }

        void RefreshTurn()
        {
            var player = World.motor.transform;
            var view = World.viewCamera ? World.viewCamera.transform : player;
            Vector3 forward = view.forward, to = Target.Point - player.position;
            forward.y = to.y = 0;
            if (forward.sqrMagnitude < 1e-4f || to.sqrMagnitude < .01f) return;
            desired = -Vector3.SignedAngle(forward, to, Vector3.up);
        }
    }
}
