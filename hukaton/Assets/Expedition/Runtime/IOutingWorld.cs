using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// A world built anew for every outing (the endless world of chunks). It fills <see cref="ExpeditionWorld"/>'s
    /// content lists as parts load and empties them as they unload.
    /// </summary>
    /// <summary>Where an outing goes: the ready-made location or a new endless world.</summary>
    /// <summary>Where an outing goes: the ready-made location, a new endless world, or the one trip to the god's
    /// mountains (not an outing world: the game runs it like the cave prologue).</summary>
    public enum OutingPlace { Ready, Endless, God }

    public interface IOutingWorld
    {
        /// <summary>Builds a new world for this outing and returns where the player starts.</summary>
        Pose BeginOuting(ExpeditionWorld world);

        /// <summary>The outing is over and the player is home: unload everything.</summary>
        void EndOuting();
    }

    /// <summary>An outing world with a centre (the endless world's start hill): how far the player has gone from it.</summary>
    public interface IOutingCentre
    {
        /// <summary>The centre on the ground, or false where it does not count (underground, in a dungeon).</summary>
        bool TryGetCentre(out Vector3 centre);
    }
}
