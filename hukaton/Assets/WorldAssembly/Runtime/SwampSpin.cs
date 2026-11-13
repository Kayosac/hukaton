using UnityEngine;

namespace Hakaton.WorldAssembly.Unity
{
    /// <summary>
    /// Turns slowly round its own axis forever: the drowned mill's wheel (Kayospace «Биом: Гнилая топь», мельница-
    /// утопленница), which turns with no current to turn it.
    /// </summary>
    public sealed class SwampSpin : MonoBehaviour
    {
        [Tooltip("Local axis it turns round (the wheel's axle).")]
        public Vector3 axis = Vector3.right;
        [Tooltip("Degrees a second; negative turns the other way.")]
        public float degreesPerSecond = -14;

        void Update() => transform.Rotate(axis, degreesPerSecond * Time.deltaTime, Space.Self);
    }
}
