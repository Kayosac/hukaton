using UnityEngine;

namespace Hakaton.WorldAssembly.Unity
{
    /// <summary>
    /// A lookout on a high place: standing by it shows the markers of landmarks much further away than the fog lets
    /// you see, for a while (Kayospace «Открытый мир v2»: высота вознаграждается обзором). The streamer checks the
    /// player against the lookouts of loaded chunks and tells <see cref="FarSilhouettes"/>.
    /// </summary>
    public sealed class Lookout : MonoBehaviour
    {
        [Min(1)] public float reach = 3.5f;
        [Tooltip("How far landmark markers show once looked out from here, m.")]
        [Min(100)] public float revealRadius = 600;
        [Tooltip("For how long, s.")]
        [Min(1)] public float seconds = 90;
    }
}
