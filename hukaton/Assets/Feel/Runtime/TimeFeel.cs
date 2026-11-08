using UnityEngine;

namespace Hakaton.Feel
{
    /// <summary>
    /// The single owner of Time.timeScale during gameplay: hit-stop freezes and short slow-motion.
    /// Requests overlap safely: the strongest active request wins.
    /// </summary>
    public sealed class TimeFeel : MonoBehaviour
    {
        [Tooltip("Global multiplier for hit-stop durations; zero disables hit-stop.")]
        [Range(0, 2)] public float hitStopScale = 1;
        [Min(0)] public float maximumHitStop = .14f;

        float freezeUntil, freezeScale = 1;
        float slowUntil, slowScale = 1, slowRecover;
        bool paused;

        /// <summary>Freeze the world for a few frames to sell an impact.</summary>
        public void HitStop(float seconds, float scale = .02f)
        {
            if (hitStopScale <= 0) return;
            seconds = Mathf.Min(maximumHitStop, seconds * hitStopScale);
            float until = Time.unscaledTime + seconds;
            if (until > freezeUntil) { freezeUntil = until; freezeScale = scale; }
            else freezeScale = Mathf.Min(freezeScale, scale);
            Apply();
        }

        /// <summary>Slow motion that eases back to normal over <paramref name="recover"/> seconds.</summary>
        public void SlowMotion(float seconds, float scale, float recover = .25f)
        {
            slowUntil = Time.unscaledTime + seconds;
            slowScale = scale;
            slowRecover = Mathf.Max(.01f, recover);
            Apply();
        }

        public void SetPaused(bool value) { paused = value; Apply(); }

        public void ResetTime()
        {
            freezeUntil = slowUntil = 0;
            paused = false;
            Time.timeScale = 1;
        }

        void Update() => Apply();

        void Apply()
        {
            float now = Time.unscaledTime, scale = 1;
            if (now < slowUntil) scale = slowScale;
            else if (now < slowUntil + slowRecover) scale = Mathf.Lerp(slowScale, 1, Ease.InOutSine((now - slowUntil) / slowRecover));
            if (now < freezeUntil) scale = Mathf.Min(scale, freezeScale);
            if (paused) scale = 0;
            Time.timeScale = scale;
        }

        void OnDisable() { Time.timeScale = 1; }
    }
}
