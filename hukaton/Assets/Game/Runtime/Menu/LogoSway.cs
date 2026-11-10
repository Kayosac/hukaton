using UnityEngine;

namespace Hakaton.Game
{
    /// <summary>The menu logo turns a little back and forth about its vertical axis, on a sine. Real time: pauses never stop it.</summary>
    public sealed class LogoSway : MonoBehaviour
    {
        [Tooltip("Largest turn either way, degrees.")]
        [Range(0, 60)] public float degrees = 14;
        [Tooltip("Seconds for one full swing there and back.")]
        [Min(.1f)] public float period = 5;

        void Update()
        {
            float angle = degrees * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2 / period);
            transform.localRotation = Quaternion.Euler(0, angle, 0);
        }

        void OnDisable() => transform.localRotation = Quaternion.identity;
    }
}
