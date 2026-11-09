using NUnit.Framework;
using UnityEngine;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>Deep water is measured from the visible surface: sloping rivers, banks beside the mesh, mesh walls.</summary>
    public sealed class WaterDepthTests
    {
        // A 4 × 4 m river segment that drops from 1 m (z = 0) to 0 m (z = 4), plus a vertical wall at x = 0.
        static readonly Vector3[] River =
        {
            new Vector3(0, 1, 0), new Vector3(0, 0, 4), new Vector3(4, 1, 0),
            new Vector3(4, 1, 0), new Vector3(0, 0, 4), new Vector3(4, 0, 4),
            new Vector3(0, -2, 0), new Vector3(0, 1, 0), new Vector3(0, 1, 4),
        };

        [Test]
        public void SurfaceFollowsTheSlope()
        {
            Assert.IsTrue(ExpeditionEffectRules.SurfaceHeight(River, 2, 1, out float upstream));
            Assert.IsTrue(ExpeditionEffectRules.SurfaceHeight(River, 2, 3, out float downstream));
            Assert.AreEqual(.75f, upstream, 1e-4f);
            Assert.AreEqual(.25f, downstream, 1e-4f);
        }

        [Test]
        public void BesideTheMeshIsNotInTheWater()
        {
            Assert.IsFalse(ExpeditionEffectRules.SurfaceHeight(River, 5, 2, out _));
            Assert.IsFalse(ExpeditionEffectRules.SurfaceHeight(River, 2, -1, out _));
        }

        [Test]
        public void VerticalWallsDoNotRaiseTheSurface()
        {
            Assert.IsTrue(ExpeditionEffectRules.SurfaceHeight(River, 0, 2, out float level));
            Assert.AreEqual(.5f, level, 1e-4f);
        }
    }
}
