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

        /// <summary>
        /// The game's say on whether a tour may start by itself now (balance stage 10: not before the first outing). Null:
        /// always. A replay asked for (the PC's help) is not held back by it.
        /// </summary>
        public System.Func<bool> AutoStart { get; set; }
    }
}
