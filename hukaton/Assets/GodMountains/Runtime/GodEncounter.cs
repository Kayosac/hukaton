using System.Collections;
using Hakaton.Character;
using Hakaton.Tutorial;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace Hakaton.GodMountains
{
    public enum GodStage { Path, Reaching, Waiting, Riding, Talking, Done }

    /// <summary>
    /// The meeting with the god at the end of the route (docs/level-design/god-mountains, chapter 9, with the finger of
    /// C01 instead of a palm). He sits deep in his own hands; when the player comes out of the slot he stands up. Crossing
    /// the edge line E takes the controls away and turns the view to him: he lets go of himself, his legs drop, and he lays
    /// his index finger under the lip. Controls come back; the player jumps onto the finger. After half a second on it
    /// the body rides with the finger (external motor control, a fixed offset from the fingertip bone; the finger stays
    /// level and only turns about the vertical): out to his left and down over the cloud sea, back up along his left and
    /// in to his face, the summit's air held all the way. There he talks, one line per E on the linen bubble, the view
    /// held on his face. A fall off the summit while he waits puts the player back on CP3.
    /// The hand-over of «Пора домой» and the way back to the shop come with the game hookup: <see cref="finished"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GodEncounter : MonoBehaviour, IExternalMotorControl
    {
        [Header("God")]
        public Animator god;
        [Tooltip("The head bone: the view rests a little under it while he talks.")]
        public Transform head;
        [Tooltip("The index fingertip bone (C01_f0_02): the player rides at a fixed offset from it.")]
        public Transform finger;
        [Tooltip("The walkable finger: colliders on the finger bones.")]
        public Collider[] fingerColliders = new Collider[0];

        [Header("Level marks")]
        [Tooltip("M, the mouth of the slot: he notices the player here.")]
        public Transform mouth;
        [Tooltip("E, the edge line: crossing it starts the reach.")]
        public Transform edgeLine;
        [Tooltip("The open middle of the lip.")]
        public Transform lip;
        [Tooltip("Where a fall off the summit puts the player back (CP3).")]
        public Transform checkpoint;

        [Header("Player")]
        public FirstPersonMotor motor;
        public FirstPersonView view;
        public PlayerControlGate control;
        public TutorialBubble bubble;

        [Header("Feel")]
        [Min(0)] public float noticeRadius = 7;
        [Tooltip("Half the width of the open lip: crossing E counts only inside it.")]
        [Min(.5f)] public float edgeHalfWidth = 4;
        [Min(0)] public float boardSeconds = .5f;
        [Tooltip("The player must stand at least this far beyond the lip, on the finger.")]
        [Min(0)] public float boardBeyondLip = 2;
        [Tooltip("Lower than the lip by this much the player has fallen.")]
        [Min(1)] public float fallDepth = 8;
        [Tooltip("Where along the head bone his eyes are (bone units).")]
        public float faceHeight = .55f;
        [Tooltip("The finger's top above the fingertip bone, m (the finger is about 40 m thick).")]
        public float fingerTopHeight = 20;
        [Tooltip("How fast the view follows the hand while it comes (1/s).")]
        [Min(.1f)] public float followRate = 1.6f;

        [Header("Lines (draft)")]
        [TextArea]
        public string[] lines =
        {
            "А. Вот ты где. Маленький. Шумный.",
            "Ты шёл через мою гору. Каждый твой шаг щекотал мне ладонь.",
            "Внизу ты торгуешь. Монеты звенят до самых облаков, я слышу.",
            "Тысяча монет. Для тебя гора. Для меня пылинка на ногте.",
            "Но ты дошёл. Значит, твоя дверь теперь откроется и в другие места.",
            "Иди. Торгуй. Бросай свои карты в камни мира.",
        };

        [Tooltip("The talk is over (the game hookup hands over «Пора домой» here).")]
        public UnityEvent finished = new UnityEvent();

        public GodStage Stage { get; private set; } = GodStage.Path;

        static readonly int NoticeId = Animator.StringToHash("Notice");
        static readonly int ReachId = Animator.StringToHash("Reach");
        static readonly int LiftId = Animator.StringToHash("Lift");
        static readonly int AlertIdleState = Animator.StringToHash("AlertIdle");
        static readonly int ReachHoldState = Animator.StringToHash("ReachHold");
        static readonly int TalkState = Animator.StringToHash("Talk");

        bool noticed, riding, continueRequested;
        float onFinger;
        Vector3 rideOffset;
        CharacterController body;

        Vector3 Player => motor ? motor.transform.position : Vector3.zero;

        /// <summary>The top of the fingertip bone's skin: what the player looks at and jumps onto.</summary>
        Vector3 FingerTop => finger.position + Vector3.up * fingerTopHeight;

        void Start()
        {
            if (!god || !finger || !motor || !view || !control || !edgeLine || !lip)
            {
                Debug.LogError("GodEncounter needs the god, the finger, the player and the level marks.", this);
                enabled = false;
                return;
            }
            body = motor.GetComponent<CharacterController>();
            if (bubble) bubble.Hide();
        }

        void Update()
        {
            switch (Stage)
            {
                case GodStage.Path:
                    if (!noticed && mouth && (Player - mouth.position).magnitude < noticeRadius) Notice();
                    if (PastEdge()) StartCoroutine(Reach());
                    break;
                case GodStage.Waiting:
                    if (Player.y < lip.position.y - fallDepth) { FallBack(); break; }
                    onFinger = OnFinger() ? onFinger + Time.deltaTime : 0;
                    if (onFinger >= boardSeconds) Board();
                    break;
            }
        }

        void LateUpdate()
        {
            // After the Animator: the finger is where this frame's pose puts it; the body follows it.
            if (!riding) return;
            Vector3 target = finger.TransformPoint(rideOffset);
            motor.MoveExternally(target - Player);
        }

        void Notice()
        {
            if (noticed) return;
            noticed = true;
            god.SetTrigger(NoticeId);
        }

        bool PastEdge()
        {
            Vector3 toLip = lip.position - edgeLine.position;
            toLip.y = 0;
            Vector3 p = Player - edgeLine.position;
            p.y = 0;
            if (toLip.sqrMagnitude < 1e-4f) return false;
            Vector3 along = toLip.normalized;
            Vector3 side = Vector3.Cross(Vector3.up, along);
            return Vector3.Dot(p, along) >= 0 && Mathf.Abs(Vector3.Dot(p, side)) <= edgeHalfWidth;
        }

        IEnumerator Reach()
        {
            Stage = GodStage.Reaching;
            control.SetBlocked(this, true);
            control.AllowPause(this, true);
            Vector3 look = head ? head.position : finger.position;
            view.Guide(look);
            if (!noticed)
            {
                Notice();
                yield return WaitForState(AlertIdleState);
            }
            god.SetTrigger(ReachId);
            // The view follows the hand with a little lag, from his face down to the top of the finger at the lip.
            while (!InState(ReachHoldState))
            {
                look = Vector3.Lerp(look, FingerTop, 1 - Mathf.Exp(-followRate * Time.deltaTime));
                view.Guide(look);
                yield return null;
            }
            float settle = 1.2f;
            while ((settle -= Time.deltaTime) > 0)
            {
                look = Vector3.Lerp(look, FingerTop, 1 - Mathf.Exp(-followRate * Time.deltaTime));
                view.Guide(look);
                yield return null;
            }
            view.Guide(null);
            control.SetBlocked(this, false);
            Stage = GodStage.Waiting;
        }

        bool OnFinger()
        {
            if (!motor.IsGrounded) return false;
            Vector3 fromLip = Player - lip.position;
            fromLip.y = 0;
            Vector3 out_ = edgeLine ? lip.position - edgeLine.position : Vector3.forward;
            out_.y = 0;
            if (Vector3.Dot(fromLip, out_.normalized) < boardBeyondLip) return false;
            var hits = Physics.RaycastAll(Player + Vector3.up * .3f, Vector3.down, .8f, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
                foreach (var c in fingerColliders)
                    if (h.collider == c) return true;
            return false;
        }

        void FallBack()
        {
            onFinger = 0;
            if (checkpoint) motor.Teleport(checkpoint.position, checkpoint.rotation);
        }

        void Board()
        {
            if (!motor.TryClaimExternalControl(this)) return;
            rideOffset = finger.InverseTransformPoint(Player);
            // the hand carries the player off the walking route, out over the cloud sea: the air stays the summit's
            var air = FindFirstObjectByType<GodMountainsAtmosphere>();
            if (air) air.Hold(air.RouteMetres(Player));
            // the finger moves with the body now: it must not push the capsule around
            if (body) foreach (var c in fingerColliders) if (c) Physics.IgnoreCollision(body, c, true);
            riding = true;
            Stage = GodStage.Riding;
            god.SetTrigger(LiftId);
            StartCoroutine(Ride());
        }

        IEnumerator Ride()
        {
            yield return WaitForState(TalkState);
            yield return Talk();
        }

        IEnumerator Talk()
        {
            Stage = GodStage.Talking;
            control.SetBlocked(this, true);
            control.AllowPause(this, true);
            if (head) view.Guide(head.TransformPoint(Vector3.up * faceHeight));
            for (int i = 0; i < lines.Length; i++)
            {
                continueRequested = false;
                if (bubble) bubble.Say(lines[i], true);
                yield return null;
                while (true)
                {
                    if (head) view.Guide(head.TransformPoint(Vector3.up * faceHeight));
                    var keyboard = Keyboard.current;
                    if (keyboard != null && keyboard.eKey.wasPressedThisFrame) Continue();
                    if (continueRequested) { continueRequested = false; break; }
                    yield return null;
                }
                yield return null;
            }
            if (bubble) bubble.Hide();
            view.Guide(null);
            control.SetBlocked(this, false);
            Stage = GodStage.Done;
            finished.Invoke();
        }

        /// <summary>Closes the line on screen, as E does: the first press finishes the typing, the next one moves on.</summary>
        public void Continue()
        {
            if (Stage == GodStage.Talking && !(bubble && bubble.SkipTyping())) continueRequested = true;
        }

        bool InState(int state) => god.GetCurrentAnimatorStateInfo(0).shortNameHash == state && !god.IsInTransition(0);

        IEnumerator WaitForState(int state)
        {
            while (!InState(state)) yield return null;
        }

        public Vector3 OnControlRevoked()
        {
            riding = false;
            if (body) foreach (var c in fingerColliders) if (c) Physics.IgnoreCollision(body, c, false);
            return Vector3.zero;
        }

        void OnDisable()
        {
            if (riding && motor) motor.ReleaseExternalControl(this, Vector3.zero);
            riding = false;
            if (control) control.SetBlocked(this, false);
        }
    }
}
