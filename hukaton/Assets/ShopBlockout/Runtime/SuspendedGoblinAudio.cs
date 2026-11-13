using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.ShopBlockout
{
    /// <summary>
    /// Foley of the goblin's ceiling rig: the pull rope and its handle, the hatch (unlatch, a knock on every overshoot
    /// against its open stop, the slam shut) and the winch (freewheeling down, ratcheting up, a clunk when it stops).
    /// Only observes <see cref="SuspendedGoblin.State"/> and the hatch hinge; never changes the rig.
    /// </summary>
    [DefaultExecutionOrder(650)]
    public sealed class SuspendedGoblinAudio : MonoBehaviour
    {
        public SuspendedGoblin rig;
        [Tooltip("The scene's pooled sound player, assigned by the scene.")]
        public SfxPlayer sfx;

        [Header("Rope")]
        [Tooltip("The player's tug on the handle rope.")]
        public SfxCue ropePull;
        [Tooltip("The wooden handle knocking back up against the frame after the pull.")]
        public SfxCue handleKnock;
        [Tooltip("The two cables creaking under his weight on the way down and up.")]
        public SfxCue cableCreak;
        [Range(0, 1)] public float ropePullVolume = 1, handleKnockVolume = .7f, cableCreakVolume = .65f;

        [Header("Hatch")]
        public SfxCue hatchUnlatch;
        [Tooltip("Played each time the opening hatch swings past fully open and turns back (its overshoot and bounces).")]
        public SfxCue hatchKnock;
        public SfxCue hatchSlam;
        [Range(0, 1)] public float hatchUnlatchVolume = .8f, hatchSlamVolume = 1;
        [Tooltip("Knock volume for the smallest and for a full overshoot.")]
        public Vector2 hatchKnockVolume = new Vector2(.35f, 1);
        [Tooltip("Overshoot past fully open (fraction of the open angle) that gives the full knock volume.")]
        [Min(.01f)] public float fullKnockOvershoot = .2f;
        [Tooltip("Smaller overshoots than this do not knock.")]
        [Min(0)] public float minKnockOvershoot = .015f;

        [Header("Mechanism")]
        [Tooltip("Winch freewheeling while he is lowered (authored for the default descent time).")]
        public SfxCue winchLower;
        [Tooltip("Winch ratcheting while he is hauled back up.")]
        public SfxCue winchRaise;
        [Tooltip("Brake and pawl catching as he arrives at the deployed pose.")]
        public SfxCue winchStopDown;
        [Tooltip("The winch stopping with him back up under the hatch.")]
        public SfxCue winchStopUp;
        [Range(0, 1)] public float winchVolume = .75f, winchStopVolume = .9f;
        [Tooltip("The descent time the winch clips were authored for; other SuspendedGoblin.lowerSeconds values change their pitch to fit.")]
        [Min(.1f)] public float authoredTravelSeconds = 1.15f;
        public Vector2 travelPitchLimits = new Vector2(.8f, 1.25f);

        GoblinLiftState seen;
        bool tracking, knocked, slammed;
        float lastHatch, lastHatchVelocity;
        AudioSource winch;
        AudioClip winchClip;

        void OnEnable()
        {
            tracking = rig;
            if (!tracking) return;
            seen = rig.State;
            lastHatch = HatchOpening();
            lastHatchVelocity = 0;
        }

        void OnDisable()
        {
            StopWinch();
            tracking = false;
        }

        void LateUpdate()
        {
            if (!tracking || !rig || !sfx) return;
            var state = rig.State;
            // A long frame can pass several phases at once: voice every phase entered, in order.
            for (int guard = 0; seen != state && guard < 6; guard++)
            {
                seen = Next(seen);
                Enter(seen);
            }
            seen = state;
            TrackHatch(state);
        }

        static GoblinLiftState Next(GoblinLiftState state) => state == GoblinLiftState.ClosingHatch ? GoblinLiftState.Stowed : state + 1;

        void Enter(GoblinLiftState state)
        {
            switch (state)
            {
                case GoblinLiftState.OpeningHatch:
                    Play(ropePull, Handle, ropePullVolume);
                    Play(hatchUnlatch, Hatch, hatchUnlatchVolume);
                    knocked = false;
                    lastHatch = HatchOpening();
                    lastHatchVelocity = 0;
                    break;
                case GoblinLiftState.Lowering:
                    // The handle springs back as the hatch settles; an opening without overshoot still hits its stop.
                    Play(handleKnock, Handle, handleKnockVolume);
                    if (!knocked) Play(hatchKnock, Hatch, hatchKnockVolume.y * .8f);
                    StartWinch(winchLower);
                    Play(cableCreak, Pulleys, cableCreakVolume);
                    break;
                case GoblinLiftState.Deployed:
                    StopWinch();
                    Play(winchStopDown, Pulleys, winchStopVolume);
                    break;
                case GoblinLiftState.Raising:
                    StartWinch(winchRaise);
                    Play(cableCreak, Pulleys, cableCreakVolume);
                    break;
                case GoblinLiftState.ClosingHatch:
                    StopWinch();
                    Play(winchStopUp, Pulleys, winchStopVolume);
                    slammed = false;
                    lastHatch = HatchOpening();
                    break;
                case GoblinLiftState.Stowed:
                    if (!slammed) Play(hatchSlam, Hatch, hatchSlamVolume);
                    slammed = true;
                    break;
            }
        }

        void TrackHatch(GoblinLiftState state)
        {
            float hatch = HatchOpening();
            float velocity = hatch - lastHatch;
            if (state == GoblinLiftState.OpeningHatch)
            {
                // Swung past fully open and turned back: the hatch hits its stop.
                float overshoot = lastHatch - 1;
                if (lastHatchVelocity > 0 && velocity < 0 && overshoot > minKnockOvershoot)
                {
                    float weight = Mathf.Clamp01(overshoot / fullKnockOvershoot);
                    Play(hatchKnock, Hatch, Mathf.Lerp(hatchKnockVolume.x, hatchKnockVolume.y, weight));
                    knocked = true;
                }
            }
            else if (state == GoblinLiftState.ClosingHatch && !slammed && lastHatch > .02f && hatch <= .02f)
            {
                Play(hatchSlam, Hatch, hatchSlamVolume);
                slammed = true;
            }
            if (Mathf.Abs(velocity) > 1e-5f) lastHatchVelocity = velocity;
            lastHatch = hatch;
        }

        /// <summary>The hatch's opening as a fraction of its open angle: 0 closed, 1 open, above 1 overshooting.</summary>
        float HatchOpening()
        {
            if (!rig || !rig.hatchHinge) return 0;
            Quaternion closed = Quaternion.Euler(rig.closedHatchEuler);
            Quaternion full = Quaternion.Inverse(closed) * Quaternion.Euler(rig.openHatchEuler);
            full.ToAngleAxis(out float fullAngle, out Vector3 fullAxis);
            if (fullAngle < .01f) return 0;
            Quaternion now = Quaternion.Inverse(closed) * rig.hatchHinge.localRotation;
            now.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180) angle -= 360;
            return angle * Mathf.Sign(Vector3.Dot(axis, fullAxis)) / fullAngle;
        }

        void StartWinch(SfxCue cue)
        {
            StopWinch();
            float fit = Mathf.Clamp(authoredTravelSeconds / Mathf.Max(.01f, rig.lowerSeconds), travelPitchLimits.x, travelPitchLimits.y);
            winch = Play(cue, Pulleys, winchVolume, fit);
            winchClip = winch ? winch.clip : null;
        }

        void StopWinch()
        {
            // The pool may have handed the source to another sound meanwhile: stop only our own clip.
            if (winch && winch.isPlaying && winch.clip == winchClip) winch.Stop();
            winch = null;
            winchClip = null;
        }

        AudioSource Play(SfxCue cue, Vector3 at, float volume, float pitch = 1) =>
            cue && sfx ? sfx.Play(cue, at, volume, pitch) : null;

        Vector3 Handle => rig.pullHandle ? rig.pullHandle.position : transform.position;
        Vector3 Hatch => rig.hatchHinge ? rig.hatchHinge.position : transform.position;

        /// <summary>The ceiling pulleys: between the two cable tops.</summary>
        Vector3 Pulleys
        {
            get
            {
                var tops = rig.cableTops;
                if (tops == null || tops.Length < 2 || !tops[0] || !tops[1]) return Hatch;
                return (tops[0].position + tops[1].position) * .5f;
            }
        }
    }
}
