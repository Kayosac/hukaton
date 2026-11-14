using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// Debug: what the outing world can tell about a spot (biome, chunk preset, ring, distance from the centre).
    /// The cheat menu shows it while it is open.
    /// </summary>
    public interface IOutingLocation
    {
        /// <summary>Several short lines in Russian about <paramref name="position"/>.</summary>
        string DescribeLocation(Vector3 position);
    }
}
