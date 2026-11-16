using System;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// A trophy card on its way into the hand (<see cref="TrophyCards"/>), so the player sees what he got and where from:
    /// it pops up out of the creature, chest or crate spinning, turns its face to the player, then flies fast into the
    /// fan at the bottom of the screen, trailing its colour, and hands over to the fan exactly where it arrives.
    /// </summary>
    public sealed class TrophyCardFlight : MonoBehaviour
    {
        public CardView view;
        public TrailRenderer trail;
        [Tooltip("Width of the card in the world while it pops up, metres.")]
        [Min(.01f)] public float worldWidth = .45f;
        [Tooltip("It pops up this high out of the source, metres, in this many seconds, turning this many times.")]
        public float popHeight = 1f;
        [Min(.01f)] public float popSeconds = .38f;
        public float popTurns = 2;
        [Tooltip("Then it flies into the hand in this many seconds.")]
        [Min(.01f)] public float flySeconds = .4f;

        Action<Pose> arrived;
        CardHandView hand;
        Transform eye;
        Vector3 from, popped;
        float time;
        bool flying;

        /// <summary>Sends the card from <paramref name="source"/> into <paramref name="fan"/>; <paramref name="done"/> gets its last pose.</summary>
        public void Launch(CardHandView fan, Transform camera, Texture front, Texture back, int level, Color colour, Vector3 source, Action<Pose> done)
        {
            hand = fan;
            eye = camera;
            arrived = done;
            from = source;
            popped = source + Vector3.up * popHeight;
            time = 0;
            flying = true;
            if (view) view.Show(front, back, level);
            if (view) view.SetGlow(.6f, colour);
            if (trail)
            {
                trail.Clear();
                trail.startColor = colour;
                trail.endColor = new Color(colour.r, colour.g, colour.b, 0);
            }
            Place();
        }

        void Update()
        {
            if (!flying) return;
            time += Time.deltaTime;
            if (time >= popSeconds + flySeconds || !eye || !hand)
            {
                flying = false;
                var pose = new Pose(transform.position, transform.rotation);
                arrived?.Invoke(pose);
                Destroy(gameObject);
                return;
            }
            Place();
        }

        void Place()
        {
            if (!eye) return;
            var toEye = Quaternion.LookRotation(eye.position - transform.position);
            if (time < popSeconds)
            {
                // Up out of the source, slowing, spinning about its own upright axis, face to the player at the end.
                float t = time / popSeconds;
                float k = 1 - (1 - t) * (1 - t);
                transform.position = Vector3.LerpUnclamped(from, popped, k);
                float spin = (1 - k) * popTurns * 360;
                transform.rotation = Quaternion.LookRotation(-(eye.position - transform.position).normalized, Vector3.up) * Quaternion.Euler(0, spin, 0);
                transform.localScale = new Vector3(worldWidth, worldWidth * CardHandView.CardAspect, 1) * Mathf.Lerp(.4f, 1, k);
                return;
            }
            // Into the fan: accelerating, shrinking to a hand card, turning to the hand's pose.
            float u = Mathf.Clamp01((time - popSeconds) / flySeconds);
            float e = u * u * (3 - 2 * u) * .4f + u * u * .6f;
            var target = hand.ArrivalPose();
            transform.position = Vector3.LerpUnclamped(popped, target.position, e);
            var facing = Quaternion.LookRotation(-(eye.position - popped).normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(facing, target.rotation, e);
            transform.localScale = Vector3.Lerp(new Vector3(worldWidth, worldWidth * CardHandView.CardAspect, 1),
                new Vector3(hand.cardWidth, hand.cardWidth * CardHandView.CardAspect, 1) * hand.transform.lossyScale.x, e);
        }
    }
}
