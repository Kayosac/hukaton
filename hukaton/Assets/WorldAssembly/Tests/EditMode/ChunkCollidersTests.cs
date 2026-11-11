using Hakaton.WorldAssembly.EditorTools;
using NUnit.Framework;

namespace Hakaton.WorldAssembly.Tests
{
    public sealed class ChunkCollidersTests
    {
        [Test]
        public void NothingInvisibleStopsThePlayer()
        {
            // Every solid collider of every chunk prefab stands where something visible is (no invisible boxes): at most
            // a sliver where a piece's own collider is a little rounder than its mesh.
            string report = ChunkColliders.Measure(out float worst);
            UnityEngine.Debug.Log("[World] " + report);
            Assert.That(worst, Is.LessThan(1.5f), report);
        }
    }
}
