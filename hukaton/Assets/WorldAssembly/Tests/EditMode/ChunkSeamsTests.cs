using Hakaton.WorldAssembly.EditorTools;
using NUnit.Framework;

namespace Hakaton.WorldAssembly.Tests
{
    public sealed class ChunkSeamsTests
    {
        [Test]
        public void NeighbouringChunksGroundsMeetWithoutAStep()
        {
            // Every pair of chunks the plans of three worlds put side by side, with their built ground meshes turned
            // and raised as in play: along the shared edge the two grounds have the same vertices at the same height.
            string report = ChunkSeams.Measure(3, out int steps, out int unmatched);
            UnityEngine.Debug.Log("[World] " + report);
            Assert.That(steps, Is.Zero, report);
            Assert.That(unmatched, Is.Zero, report);
        }
    }
}
