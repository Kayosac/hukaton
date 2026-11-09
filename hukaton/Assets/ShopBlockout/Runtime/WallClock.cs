using System;
using UnityEngine;

namespace Hakaton.ShopBlockout
{
    /// <summary>
    /// Decorative wall clock showing the player's local time. Hands turn about their local Z axis;
    /// the authored hand pivots point to 12 o'clock with local +X towards 3 o'clock.
    /// </summary>
    public sealed class WallClock : MonoBehaviour
    {
        public Transform hourHand, minuteHand, secondHand;
        [Tooltip("1 = real time. Negative values run the clock backwards.")]
        public float rate = 1;
        [Tooltip("Seconds the second hand needs to settle after each tick.")]
        [Range(.02f, .5f)] public float tickSeconds = .14f;
        [Range(0, .5f)] public float tickOvershoot = .2f;

        double startClock, startRealtime;

        void OnEnable()
        {
            // Real time, not Time.unscaledTime: the latter does not count the scene load before the first frame.
            startClock = DateTime.Now.TimeOfDay.TotalSeconds;
            startRealtime = Time.realtimeSinceStartupAsDouble;
        }

        void Update()
        {
            double clock = startClock + (Time.realtimeSinceStartupAsDouble - startRealtime) * rate;
            clock = ((clock % 43200) + 43200) % 43200; // twelve hours
            double whole = Math.Floor(clock);
            float tick = Mathf.Clamp01((float)(clock - whole) / tickSeconds);
            // The second hand jumps once a second, overshoots a little and settles.
            float settle = 1 - Mathf.Pow(1 - tick, 3) + tickOvershoot * Mathf.Sin(tick * Mathf.PI) * (1 - tick);
            Set(secondHand, (float)((whole % 60) - 1 + settle) * 6);
            Set(minuteHand, (float)(clock / 60 % 60) * 6);
            Set(hourHand, (float)(clock / 3600) * 30);
        }

        static void Set(Transform hand, float clockwiseDegrees)
        {
            if (hand) hand.localRotation = Quaternion.Euler(0, 0, -clockwiseDegrees);
        }
    }
}
