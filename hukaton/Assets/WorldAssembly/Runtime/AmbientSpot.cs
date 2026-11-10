using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.WorldAssembly.Unity
{
    /// <summary>
    /// A place in a chunk that sounds (a campfire, a chimney, a spring): the streamer keeps the nearest few of them
    /// looping through the expedition's sound player while the player is within the cue's reach.
    /// </summary>
    public sealed class AmbientSpot : MonoBehaviour
    {
        public SfxCue cue;
        [Range(0, 1)] public float volume = 1;
    }
}
