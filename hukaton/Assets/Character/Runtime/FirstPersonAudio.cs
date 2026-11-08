using UnityEngine;

namespace Hakaton.Character
{
    /// <summary>Distance-driven footsteps; audio is optional and authored on the player prefab.</summary>
    public sealed class FirstPersonAudio : MonoBehaviour
    {
        public FirstPersonMotor motor;
        public AudioSource source;
        public AudioClip[] footstepClips;
        public AudioClip jumpClip, landClip;
        [Min(.1f)] public float walkStepDistance = 1.7f;
        [Min(.1f)] public float sprintStepDistance = 2.1f;
        [Range(0, 1)] public float footstepVolume = .18f;
        [Range(0, 1)] public float jumpVolume = .13f;
        [Range(0, 1)] public float landingVolume = .22f;
        float distance;
        int nextFootstep;

        void OnEnable()
        {
            if (!motor) return;
            motor.Jumped += OnJumped;
            motor.Landed += OnLanded;
        }

        void LateUpdate()
        {
            if (!motor || !source || !motor.IsGrounded || motor.PlanarVelocity.sqrMagnitude < .09f)
            { distance = 0; return; }
            distance += motor.PlanarVelocity.magnitude * Time.deltaTime;
            float stride = motor.IsSprinting ? sprintStepDistance : walkStepDistance;
            if (distance < stride) return;
            distance %= stride;
            if (footstepClips == null || footstepClips.Length == 0) return;
            var clip = footstepClips[nextFootstep++ % footstepClips.Length];
            Play(clip, footstepVolume * (motor.IsSprinting ? 1.15f : 1));
        }

        void OnJumped() { distance = 0; Play(jumpClip, jumpVolume); }
        void OnLanded(float speed)
        {
            distance = 0;
            Play(landClip, landingVolume * Mathf.Clamp01(speed / 8));
        }
        void Play(AudioClip clip, float volume)
        {
            if (!clip || !source) return;
            source.pitch = Random.Range(.96f, 1.04f);
            source.PlayOneShot(clip, volume);
        }
        void OnDisable()
        {
            if (!motor) return;
            motor.Jumped -= OnJumped;
            motor.Landed -= OnLanded;
            distance = 0;
        }
    }
}
