using Hakaton.WorldAssembly.EditorTools;
using NUnit.Framework;

namespace Hakaton.WorldAssembly.Tests
{
    /// <summary>What you see is what you hit, and fire opens what it burns: over every chunk prefab.</summary>
    public sealed class ChunkLooksTests
    {
        [Test]
        public void EverythingSolidIsHitWhereItIsDrawn()
        {
            // Rocks, trunks with their branches, crowns, logs, houses: their drawn surface is their collider's, so a
            // rope or a card holds where it looks like it should. Grass, leaves and bushes are walked through.
            string report = ChunkLooks.Measure(out float worst);
            UnityEngine.Debug.Log("[World] " + report);
            Assert.That(worst, Is.LessThan(.02f), report);
        }

        [Test]
        public void BurntObstaclesOpenTheWayForTheBody()
        {
            // Walked through the colliders, not the navigation: once a thicket or a deadfall burns, the body gets past.
            string report = ChunkReach.Check("");
            UnityEngine.Debug.Log("[World] " + report);
            Assert.That(report, Does.Contain(", 0 still shut"), report);
        }
    }
}
