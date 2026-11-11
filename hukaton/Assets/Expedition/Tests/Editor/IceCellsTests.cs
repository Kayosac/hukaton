using System.Collections.Generic;
using Hakaton.Expeditions;
using NUnit.Framework;
using UnityEngine;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>The ice on water: freezes in a crooked circle over water only, spreads out as a wave, melts back.</summary>
    public sealed class IceCellsTests
    {
        const float Cell = .25f;

        // Water everywhere at height 1.
        static bool Everywhere(Vector3 p, out float level) { level = 1; return true; }
        // Water only where x < 0 (a shore along x = 0).
        static bool WestOnly(Vector3 p, out float level) { level = 1; return p.x < 0; }

        [Test]
        public void FreezesInsideTheRadiusOnly()
        {
            var cells = new IceCells(Cell);
            int added = cells.Freeze(Vector3.zero, 2, 0, 6, 3, 0, Everywhere);
            Assert.That(added, Is.GreaterThan(0));
            foreach (var pair in cells.All)
                Assert.That(Vector3.Distance(cells.Centre(pair.Key), Vector3.zero), Is.LessThanOrEqualTo(2 + 1e-4f));
            // About the area of the circle.
            float area = added * Cell * Cell;
            Assert.That(area, Is.EqualTo(Mathf.PI * 4).Within(1f));
        }

        [Test]
        public void RimWobblesButStaysNearTheRadius()
        {
            var cells = new IceCells(Cell);
            cells.Freeze(Vector3.zero, 3, 0, 6, 7.3f, .2f, Everywhere);
            float min = float.MaxValue, max = 0;
            for (int i = 0; i < 64; i++)
            {
                float a = i / 64f * Mathf.PI * 2;
                float r = IceCells.RimRadius(3, a, 7.3f, .2f);
                min = Mathf.Min(min, r);
                max = Mathf.Max(max, r);
            }
            Assert.That(min, Is.GreaterThanOrEqualTo(3 * .8f - 1e-4f));
            Assert.That(max, Is.LessThanOrEqualTo(3 * 1.2f + 1e-4f));
            Assert.That(max - min, Is.GreaterThan(.05f), "the rim should be crooked, not a clean circle");
        }

        [Test]
        public void FreezesOnlyOverWater()
        {
            var cells = new IceCells(Cell);
            cells.Freeze(Vector3.zero, 2, 0, 6, 1, 0, WestOnly);
            Assert.That(cells.Count, Is.GreaterThan(0));
            foreach (var pair in cells.All) Assert.That(cells.Centre(pair.Key).x, Is.LessThan(0));
        }

        [Test]
        public void KeepsTheWaterLevelOfEachCell()
        {
            var cells = new IceCells(Cell);
            cells.Freeze(Vector3.zero, 1, 0, 6, 1, 0, (Vector3 p, out float level) => { level = p.x < 0 ? .5f : 2; return true; });
            foreach (var pair in cells.All)
                Assert.That(pair.Value.level, Is.EqualTo(cells.Centre(pair.Key).x < 0 ? .5f : 2));
        }

        [Test]
        public void SpreadsOutAsAWave()
        {
            var cells = new IceCells(Cell);
            cells.Freeze(Vector3.zero, 3, 10, 6, 1, 0, Everywhere);
            Assert.That(cells.TryGet(cells.Key(new Vector3(.1f, 0, .1f)), out var near));
            Assert.That(cells.TryGet(cells.Key(new Vector3(2.8f, 0, 0)), out var far));
            Assert.That(far.bornAt, Is.GreaterThan(near.bornAt));
            Assert.That(far.bornAt - 10, Is.EqualTo(2.8f / 6).Within(.1f));
            // Not there yet, then grown in.
            Assert.That(IceCells.Amount(far, 10.1f, .25f, .5f), Is.EqualTo(0));
            Assert.That(IceCells.Amount(far, far.bornAt + .25f, .25f, .5f), Is.EqualTo(1));
        }

        [Test]
        public void FreezingAgainOverIceAddsNothing()
        {
            var cells = new IceCells(Cell);
            cells.Freeze(Vector3.zero, 2, 0, 6, 1, 0, Everywhere);
            Assert.That(cells.Freeze(Vector3.zero, 2, 1, 6, 1, 0, Everywhere), Is.EqualTo(0));
            Assert.That(cells.Freeze(new Vector3(2, 0, 0), 2, 1, 6, 1, 0, Everywhere), Is.GreaterThan(0), "overlapping circles grow the slab");
        }

        [Test]
        public void MeltsInsideTheRadiusAndDropsTheCells()
        {
            var cells = new IceCells(Cell);
            cells.Freeze(Vector3.zero, 3, 0, 6, 1, 0, Everywhere);
            int before = cells.Count;
            var touched = new List<Vector2Int>();
            int melting = cells.Melt(new Vector3(1, 0, 0), 1, 5, 4, touched);
            Assert.That(melting, Is.GreaterThan(0));
            Assert.That(touched.Count, Is.EqualTo(melting));
            foreach (var key in touched) Assert.That(Vector3.Distance(cells.Centre(key), new Vector3(1, 0, 0)), Is.LessThanOrEqualTo(1 + 1e-4f));
            // Still there until it has thinned away.
            Assert.That(cells.RemoveMelted(5.1f, .5f), Is.EqualTo(0));
            Assert.That(cells.RemoveMelted(5 + 1f / 4 + .5f + .01f, .5f), Is.EqualTo(melting));
            Assert.That(cells.Count, Is.EqualTo(before - melting));
            Assert.That(cells.Contains(cells.Key(new Vector3(1, 0, 0))), Is.False);
            Assert.That(cells.Contains(cells.Key(new Vector3(-2, 0, 0))), Is.True);
        }

        [Test]
        public void ThawingIceIsNotSolidAndFreezingAgainSavesIt()
        {
            var cells = new IceCells(Cell);
            cells.Freeze(Vector3.zero, 2, 0, 6, 1, 0, Everywhere);
            var key = cells.Key(Vector3.zero);
            Assert.That(cells.IsSolid(key, 1, .25f, .5f));
            cells.Melt(Vector3.zero, 1, 2, 4);
            Assert.That(cells.IsSolid(key, 2.4f, .25f, .5f), Is.False);
            Assert.That(cells.Freeze(Vector3.zero, 1, 2.4f, 6, 1, 0, Everywhere), Is.GreaterThan(0));
            Assert.That(cells.TryGet(key, out var cell) && !cell.Melting);
        }

        [Test]
        public void AnyHoldingSeesOnlyIceThatHolds()
        {
            var cells = new IceCells(Cell);
            cells.Freeze(Vector3.zero, 1, 0, 6, 1, 0, Everywhere);
            Assert.That(cells.AnyHolding(new Bounds(Vector3.zero, Vector3.one)));
            Assert.That(cells.AnyHolding(new Bounds(new Vector3(10, 0, 10), Vector3.one)), Is.False);
            cells.Melt(Vector3.zero, 5, 0, 4);
            Assert.That(cells.AnyHolding(new Bounds(Vector3.zero, Vector3.one * 4)), Is.False);
        }
    
        [Test]
        public void AbsorbTakesTheOtherSheetsCellsWithoutDuplicates()
        {
            var a = new IceCells(Cell);
            var b = new IceCells(Cell);
            a.Freeze(Vector3.zero, 1, 0, 100, 0, 0, Everywhere);
            b.Freeze(new Vector3(1.5f, 0, 0), 1, 0, 100, 0, 0, Everywhere);
            int before = a.Count;
            int came = a.Absorb(b);
            Assert.That(came, Is.GreaterThan(0));
            Assert.That(came, Is.LessThan(b.Count), "the overlap stays the host's own");
            Assert.That(a.Count, Is.EqualTo(before + came));
            Assert.That(a.Contains(a.Key(new Vector3(2.2f, 0, 0))), Is.True);
        }
}
}
