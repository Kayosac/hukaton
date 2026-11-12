using System;
using UnityEngine;

namespace Hakaton.Teaser
{
    /// <summary>
    /// Plays the PENUS teaser as a list of shots on one camera at a fixed frame rate (Time.captureFramerate), so every
    /// run gives the same frames. Each shot sets the camera pose (optionally drifting to an end pose), the field of
    /// view, the black matte and the objects that are visible only during that shot. Tracks animate the rest.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public sealed class TeaserTimeline : MonoBehaviour
    {
        [Serializable]
        public sealed class Shot
        {
            public string name;
            [Tooltip("First frame of the shot.")] [Min(0)] public int startFrame;
            [Tooltip("Frame after the last frame of the shot.")] [Min(1)] public int endFrame = 1;
            [Tooltip("Camera pose at the start of the shot. Empty: the camera stays where it is.")]
            public Transform pose;
            [Tooltip("Optional camera pose at the end of the shot; the camera drifts there evenly.")]
            public Transform poseEnd;
            [Range(5, 120)] public float fieldOfView = 50;
            [Tooltip("Covers the whole picture with black (overlay UI above the matte stays visible).")]
            public bool blackMatte;
            [Tooltip("The camera clears to black instead of the sky (for objects shown alone, like the coin).")]
            public bool clearToBlack;
            [Tooltip("Objects shown only during this shot.")]
            public GameObject[] visible = new GameObject[0];
        }

        public Camera viewCamera;
        [Min(1)] public int framesPerSecond = 30;
        [Min(1)] public int totalFrames = 540;
        [Tooltip("Frames played before frame 0 so the scene settles (shaders, first walk of the customer).")]
        [Min(0)] public int preRollFrames = 45;
        [Tooltip("Fixed seed so random sprite wobble and flicker repeat between runs.")]
        public int randomSeed = 20261112;
        public GameObject blackMatte;
        public Shot[] shots = new Shot[0];
        public TeaserTrack[] tracks = new TeaserTrack[0];

        /// <summary>Teaser frame shown this frame; negative during the pre-roll.</summary>
        public int Frame { get; private set; }
        public float Seconds => Frame / (float)framesPerSecond;
        public bool Finished => Frame >= totalFrames;

        int ticks;
        bool cameraBaseCaptured;
        CameraClearFlags cameraBaseClear;
        Color cameraBaseBackground;

        void Awake()
        {
            UnityEngine.Random.InitState(randomSeed);
            Time.captureFramerate = framesPerSecond;
            Frame = -preRollFrames;
            Apply();
        }

        void Update()
        {
            Frame = ticks - preRollFrames;
            ticks++;
            Apply();
        }

        void OnDestroy() => Time.captureFramerate = 0;

        /// <summary>Edit-mode preview of one frame: shots only (camera, matte, visibility), tracks are not run.</summary>
        public void Preview(int frame)
        {
            Frame = frame;
            ApplyShots();
        }

        /// <summary>Shows the state of <see cref="Frame"/>.</summary>
        public void Apply()
        {
            ApplyShots();
            float seconds = Seconds;
            foreach (var track in tracks)
                if (track) track.Evaluate(seconds);
        }

        void ApplyShots()
        {
            int frame = Mathf.Clamp(Frame, 0, totalFrames - 1);
            Shot current = null;
            foreach (var shot in shots)
                if (shot != null && frame >= shot.startFrame && frame < shot.endFrame) { current = shot; break; }

            foreach (var shot in shots)
                if (shot != null && shot != current)
                    foreach (var go in shot.visible) SetActive(go, false);
            if (current != null)
                foreach (var go in current.visible) SetActive(go, true);
            SetActive(blackMatte, current == null || current.blackMatte);

            if (viewCamera)
            {
                if (!cameraBaseCaptured)
                {
                    cameraBaseClear = viewCamera.clearFlags;
                    cameraBaseBackground = viewCamera.backgroundColor;
                    cameraBaseCaptured = true;
                }
                bool black = current != null && current.clearToBlack;
                viewCamera.clearFlags = black ? CameraClearFlags.SolidColor : cameraBaseClear;
                viewCamera.backgroundColor = black ? Color.black : cameraBaseBackground;
            }

            if (current != null && viewCamera)
            {
                if (current.pose)
                {
                    float k = current.endFrame - current.startFrame > 1
                        ? (frame - current.startFrame) / (float)(current.endFrame - current.startFrame - 1) : 0;
                    var end = current.poseEnd ? current.poseEnd : current.pose;
                    viewCamera.transform.SetPositionAndRotation(
                        Vector3.Lerp(current.pose.position, end.position, k),
                        Quaternion.Slerp(current.pose.rotation, end.rotation, k));
                }
                viewCamera.fieldOfView = current.fieldOfView;
            }
        }

        static void SetActive(GameObject go, bool active)
        {
            if (go && go.activeSelf != active) go.SetActive(active);
        }
    }
}
