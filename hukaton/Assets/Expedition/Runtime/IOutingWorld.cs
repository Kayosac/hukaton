using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// A world built anew for every outing (the endless world of chunks). It fills <see cref="ExpeditionWorld"/>'s
    /// content lists as parts load and empties them as they unload.
    /// </summary>
    public interface IOutingWorld
    {
        /// <summary>Builds a new world for this outing and returns where the player starts.</summary>
        Pose BeginOuting(ExpeditionWorld world);

        /// <summary>The outing is over and the player is home: unload everything.</summary>
        void EndOuting();
    }
}
