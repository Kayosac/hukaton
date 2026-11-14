using UnityEngine;

namespace Hakaton.Guide
{
    /// <summary>
    /// The scene's one record of the first-open guides (<see cref="GuideProgress"/>). The game saves and restores it;
    /// the guides of the brazier and the PC read and write it through their serialized reference.
    /// </summary>
    public sealed class GuideBook : MonoBehaviour
    {
        public GuideProgress Progress { get; } = new GuideProgress();
    }
}
