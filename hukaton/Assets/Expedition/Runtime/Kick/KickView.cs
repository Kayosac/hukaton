using Hakaton.Feel;
using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// The kicking leg on screen (<see cref="PlayerKick"/>): the author's three drawings — the foot bent, half way, the
    /// full kick — on the hand-drawn pixel material. The kick is one quick movement, not a flip-book: the straight leg
    /// shoots in from the bottom right (ease-out with a small overshoot, a scale punch), the blow lands, it holds a beat
    /// and is pulled back down, bending as it goes (full → half → bent), so the leg is bent at the end of the kick, not
    /// at its start. It hangs on the view like the special items do in the hand (SpecialItems' HandHold): turning leaves it behind and tilts it, jumps and falls lag it, the step sways it, a landing knocks it.
    /// <para>
    /// The canvas renders through the view camera a few centimetres in front of the eyes (Screen Space – Camera), on its
    /// own copy of the material (depth test off, render queue under the card viewmodel's), so the cards in the hand are
    /// always drawn over the leg and nothing in the world cuts it. The drawing's cut edges (the trouser leg leaves the
    /// sprite at its right and bottom) stay below the screen: the pivot is the sprite's bottom-right corner, its resting
    /// place puts the cut out of view, and nothing may lift it more than <see cref="maxLift"/>. Scaled time: a hit-stop
    /// freezes it with the world.
    /// </para>
    /// </summary>
    public sealed class KickView : MonoBehaviour
    {
        [Tooltip("The leg's canvas; off while the leg is down.")]
        public Canvas canvas;
        [Tooltip("The leg; its pivot is the drawing's bottom-right corner, where the trouser leg is cut.")]
        public RectTransform leg;
        public Image body;
        [Tooltip("The foot bent, half way, the full kick.")]
        public Sprite[] frames = new Sprite[3];

        [Header("Render (in front of the eyes, under the cards)")]
        [Tooltip("The leg's material; a copy of it is made so its pixel grid can follow the view's field of view.")]
        public Material material;
        [Tooltip("Distance of the leg's canvas from the eyes, metres (the camera's near plane is 0.05).")]
        [Min(.06f)] public float planeDistance = .12f;
        [Tooltip("One pixel of the drawing on screen, screen pixels (the special items in the hand: 2.5).")]
        [Min(.5f)] public float cellPixels = 2.5f;

        [Header("Placement (reference pixels at 1920 × 1080, from the bottom centre)")]
        [Tooltip("Where the drawing's bottom-right corner rests at the full kick: low enough that the cut is under the screen.")]
        public Vector2 restPosition = new Vector2(452, -375);
        [Tooltip("The most anything (the overshoot, the step, a fall) may lift the leg above its rest: the cut stays hidden.")]
        [Min(0)] public float maxLift = 40;
        [Tooltip("Where the straight leg shoots in from, relative to its rest, and how far it is turned then (degrees).")]
        public Vector2 enterOffset = new Vector2(170, -560);
        public float enterAngle = 9;
        [Tooltip("Where the bent leg is pulled back to, relative to its rest, and how far it is turned then.")]
        public Vector2 exitOffset = new Vector2(110, -660);
        public float exitAngle = -7;

        [Header("Timing (seconds, scaled)")]
        [Tooltip("The thrust: the straight leg shoots in. The blow lands about here (PlayerKick.hitDelay).")]
        [Min(.01f)] public float thrustSeconds = .06f;
        [Tooltip("Overshoot of the thrust (0 none).")]
        [Range(0, 3)] public float thrustOvershoot = 1.6f;
        [Tooltip("The full kick holds this long.")]
        [Min(0)] public float holdSeconds = .07f;
        [Tooltip("Pulled back down and out, bending: full → half → bent.")]
        [Min(.01f)] public float retractSeconds = .2f;
        [Tooltip("Share of the way back where the half drawing and then the bent one take over.")]
        [Range(0, 1)] public float halfAt = .18f;
        [Range(0, 1)] public float bentAt = .45f;
        [Tooltip("Scale punch of the thrust: the leg comes in this much bigger and settles.")]
        public float thrustPunch = .07f;

        [Header("A landed blow")]
        [Tooltip("The leg is knocked back (reference pixels per second) and turned (degrees per second).")]
        public Vector2 landedJolt = new Vector2(-260, -900);
        public float landedTurn = -120;

        [Header("On the view (like the special items in the hand)")]
        [Tooltip("Turning the view leaves the leg behind: reference pixels per degree per second, and the limit.")]
        public float lookLag = .3f;
        public float maxLookLag = 60;
        [Tooltip("It also tilts against a turn: degrees per degree per second, and the limit.")]
        public float lookTilt = .02f;
        public float maxLookTilt = 5;
        [Tooltip("Rising leaves it low, falling lifts it (reference pixels per m/s, and the limit).")]
        public float fallLag = 6;
        public float maxFallLag = 60;
        [Tooltip("A landing knocks it down: reference pixels per second per m/s of the landing.")]
        public float landKick = 40;
        [Min(.1f)] public float lagFrequency = 4.5f;
        [Range(0, 2)] public float lagDamping = .45f;
        [Tooltip("The step: side sway and dip (reference pixels) walking and running, and steps per second.")]
        public Vector2 walkBob = new Vector2(6, 9);
        public Vector2 runBob = new Vector2(12, 16);
        public float walkStepRate = 3.4f, runStepRate = 5.2f;

        ExpeditionWorld world;
        Material instance;
        float time = -1, warmUntil = -1;
        float stepPhase, lastYaw, lastPitch;
        bool lookKnown;
        SpringVector3 lag, jolt;
        SpringFloat punch, stepAmount, running;
        System.Action<float> landed;

        public bool Showing => time >= 0;
        float Duration => thrustSeconds + holdSeconds + retractSeconds;

        void Awake() => Hide();

        /// <summary>The outing the leg belongs to: its canvas renders through the view camera.</summary>
        public void Bind(ExpeditionWorld outing)
        {
            if (world && world.motor && landed != null) world.motor.Landed -= landed;
            world = outing;
            landed ??= speed => { if (Showing) lag.Kick(new Vector3(0, -speed * landKick, 0)); };
            if (world && world.motor) world.motor.Landed += landed;
            if (canvas && world && world.viewCamera)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = world.viewCamera;
                canvas.planeDistance = planeDistance;
            }
            if (body && material && !instance)
            {
                instance = new Material(material) { name = material.name + " (leg)" };
                body.material = instance;
            }
            Warm();
        }

        /// <summary>
        /// The editor compiles a shader variant the first time it is drawn and shows a cyan stand-in meanwhile: the leg is
        /// drawn once, far below the screen and all but transparent, for a moment after the outing binds it.
        /// </summary>
        void Warm()
        {
            if (!canvas || !leg || !body || Showing) return;
            warmUntil = Time.unscaledTime + 2;
            canvas.enabled = true;
            body.color = new Color(1, 1, 1, .004f);
            leg.anchoredPosition = restPosition + new Vector2(0, -3000);
        }

        void EndWarm()
        {
            warmUntil = -1;
            if (body) body.color = Color.white;
        }

        void OnDestroy()
        {
            if (world && world.motor && landed != null) world.motor.Landed -= landed;
            if (instance) Destroy(instance);
        }

        /// <summary>A kick: the straight leg shoots in, holds, is pulled back bending.</summary>
        public void Play()
        {
            EndWarm();
            time = 0;
            jolt = default;
            lag = default;
            punch = new SpringFloat(thrustPunch);
            lookKnown = false;
            if (canvas) canvas.enabled = true;
            Pose(0);
        }

        /// <summary>The blow met something: the leg is knocked back.</summary>
        public void Landed()
        {
            jolt.Kick(new Vector3(landedJolt.x, landedJolt.y, landedTurn));
            punch.Kick(-.8f);
        }

        void Hide()
        {
            time = -1;
            if (canvas) canvas.enabled = false;
        }

        void OnDisable()
        {
            EndWarm();
            Hide();
        }

        void LateUpdate()
        {
            if (warmUntil >= 0 && Time.unscaledTime >= warmUntil)
            {
                EndWarm();
                Hide();
            }
            if (time < 0) return;
            float dt = Time.deltaTime;
            time += dt;
            if (time >= Duration) { Hide(); return; }
            Pose(dt);
        }

        void Pose(float dt)
        {
            // The way: in (straight), hold, out (bending).
            Vector2 way;
            float angle;
            int frame;
            if (time < thrustSeconds)
            {
                float k = EaseOutBack(time / thrustSeconds, thrustOvershoot);
                way = Vector2.LerpUnclamped(enterOffset, Vector2.zero, k);
                angle = Mathf.LerpUnclamped(enterAngle, 0, k);
                frame = 2;
            }
            else if (time < thrustSeconds + holdSeconds)
            {
                way = Vector2.zero;
                angle = 0;
                frame = 2;
            }
            else
            {
                float t = Mathf.Clamp01((time - thrustSeconds - holdSeconds) / retractSeconds);
                float k = t * t * (3 - 2 * t);
                way = Vector2.Lerp(Vector2.zero, exitOffset, k * k);
                angle = Mathf.Lerp(0, exitAngle, k);
                frame = t < halfAt ? 2 : t < bentAt ? 1 : 0;
            }
            if (body && frames != null && frames.Length > 0)
            {
                var sprite = frames[Mathf.Clamp(frame, 0, frames.Length - 1)];
                if (sprite && body.sprite != sprite) body.sprite = sprite;
            }

            var follow = Follow(dt);
            if (dt > 0)
            {
                jolt.Step(Vector3.zero, 9, .45f, dt);
                punch.Step(0, 11, .5f, dt);
            }
            UpdateGrid();
            if (!leg) return;
            var position = restPosition + way + follow + new Vector2(jolt.Value.x, jolt.Value.y);
            // The cut edges stay under the screen, whatever lifts the leg.
            position.y = Mathf.Min(position.y, restPosition.y + maxLift);
            leg.anchoredPosition = new Vector2(Mathf.Round(position.x), Mathf.Round(position.y));
            leg.localRotation = Quaternion.Euler(0, 0, angle + jolt.Value.z + lag.Value.z);
            leg.localScale = Vector3.one * (1 + punch.Value);
        }

        /// <summary>The view's movement as an offset of the leg (reference pixels); its turn goes into <see cref="lag"/>.z.</summary>
        Vector2 Follow(float dt)
        {
            var target = Vector3.zero;
            if (dt > 0 && world && world.viewCamera && world.motor)
            {
                var euler = world.viewCamera.transform.eulerAngles;
                if (lookKnown)
                {
                    float yawRate = Mathf.DeltaAngle(lastYaw, euler.y) / dt, pitchRate = Mathf.DeltaAngle(lastPitch, euler.x) / dt;
                    target.x = Mathf.Clamp(-yawRate * lookLag, -maxLookLag, maxLookLag);
                    target.y = Mathf.Clamp(pitchRate * lookLag, -maxLookLag, maxLookLag);
                    target.z = Mathf.Clamp(yawRate * lookTilt, -maxLookTilt, maxLookTilt);
                }
                lastYaw = euler.y;
                lastPitch = euler.x;
                lookKnown = true;
                target.y += Mathf.Clamp(-world.motor.Velocity.y * fallLag, -maxFallLag, maxFallLag);
                bool walking = world.motor.IsGrounded && world.motor.Speed01 > .05f;
                stepAmount.Step(walking ? Mathf.Clamp01(world.motor.Speed01 * 1.5f) : 0, 6, .9f, dt);
                running.Step(world.motor.IsSprinting ? 1 : 0, 5, .9f, dt);
                if (walking) stepPhase += dt * Mathf.Lerp(walkStepRate, runStepRate, running.Value) * Mathf.PI;
            }
            if (dt > 0) lag.Step(target, lagFrequency, lagDamping, dt);
            var amplitude = Vector2.Lerp(walkBob, runBob, running.Value) * stepAmount.Value;
            var step = new Vector2(Mathf.Sin(stepPhase) * amplitude.x, -Mathf.Abs(Mathf.Cos(stepPhase)) * amplitude.y + amplitude.y * .5f);
            return new Vector2(lag.Value.x, lag.Value.y) + step;
        }

        /// <summary>
        /// The material's pixel grid is in world units; the canvas stands <see cref="planeDistance"/> in front of the eyes,
        /// so one cell is <see cref="cellPixels"/> screen pixels when ppu = screen height × cot(fov / 2) / (2 × distance × cell).
        /// </summary>
        void UpdateGrid()
        {
            if (!instance || !canvas || canvas.renderMode != RenderMode.ScreenSpaceCamera || !canvas.worldCamera) return;
            float cot = 1 / Mathf.Tan(canvas.worldCamera.fieldOfView * .5f * Mathf.Deg2Rad);
            float ppu = Screen.height * cot / (2 * canvas.planeDistance * cellPixels);
            instance.SetFloat(PixelsPerUnit, ppu);
        }

        static readonly int PixelsPerUnit = Shader.PropertyToID("_PixelsPerUnit");

        static float EaseOutBack(float t, float overshoot)
        {
            t = Mathf.Clamp01(t);
            float c3 = overshoot + 1, u = t - 1;
            return 1 + c3 * u * u * u + overshoot * u * u;
        }
    }
}
