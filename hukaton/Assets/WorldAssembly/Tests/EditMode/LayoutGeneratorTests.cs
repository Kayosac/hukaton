using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Hakaton.WorldAssembly.Tests
{
    public sealed class LayoutGeneratorTests
    {
        static readonly ChunkType[] Catalogue =
        {
            new ChunkType("lake", 1, 3, new[] { "explore", "ice" }),
            new ChunkType("thicket", 2, 3, new[] { "explore", "fire" }),
            new ChunkType("ledge", 1, 2, new[] { "explore", "impulse" }),
            new ChunkType("lair", 1, 2, new[] { "hunt" }),
            new ChunkType("meadow", 3, 4, new[] { "hunt" }),
            new ChunkType("woods", 3, 4, new string[0]),
        };

        static LayoutRules Rules(int count = 7) => new LayoutRules
        {
            ChunkCount = count, RequiredTags = new[] { "hunt", "explore", "fire", "ice" }, HalfWidth = 2, Depth = 3,
        };

        static string Describe(WorldLayout layout) => string.Join(";", layout.Chunks.Select(c => c.ToString()));

        [Test]
        public void SameSeedGivesSameValley()
        {
            for (int seed = 0; seed < 50; seed++)
                Assert.AreEqual(Describe(LayoutGenerator.Generate(seed, Catalogue, Rules())),
                    Describe(LayoutGenerator.Generate(seed, Catalogue, Rules())), $"seed {seed}");
        }

        [Test]
        public void SeedsGiveDifferentValleys()
        {
            var distinct = new HashSet<string>();
            for (int seed = 0; seed < 30; seed++) distinct.Add(Describe(LayoutGenerator.Generate(seed, Catalogue, Rules())));
            Assert.Greater(distinct.Count, 25);
        }

        [Test]
        public void EveryChunkIsWalkableFromTheStart()
        {
            for (int seed = 0; seed < 500; seed++)
                Assert.IsTrue(LayoutGenerator.Generate(seed, Catalogue, Rules()).IsConnected(), $"seed {seed}");
        }

        [Test]
        public void ValleyLiesInFrontOfTheOverlookWithinBounds()
        {
            var rules = Rules();
            for (int seed = 0; seed < 500; seed++)
                foreach (var chunk in LayoutGenerator.Generate(seed, Catalogue, rules).Chunks)
                {
                    Assert.GreaterOrEqual(chunk.Cell.Y, 1, $"seed {seed}: {chunk}");
                    Assert.LessOrEqual(chunk.Cell.Y, rules.Depth, $"seed {seed}: {chunk}");
                    Assert.LessOrEqual(System.Math.Abs(chunk.Cell.X), rules.HalfWidth, $"seed {seed}: {chunk}");
                }
        }

        [Test]
        public void NoCellIsUsedTwice()
        {
            for (int seed = 0; seed < 500; seed++)
            {
                var layout = LayoutGenerator.Generate(seed, Catalogue, Rules());
                var cells = layout.Chunks.Select(c => c.Cell).Append(layout.Start).ToList();
                Assert.AreEqual(cells.Count, cells.Distinct().Count(), $"seed {seed}");
            }
        }

        [Test]
        public void ChunkCountAndQuotasAreKept()
        {
            for (int seed = 0; seed < 500; seed++)
            {
                var layout = LayoutGenerator.Generate(seed, Catalogue, Rules());
                Assert.AreEqual(7, layout.Chunks.Count, $"seed {seed}");
                foreach (var type in Catalogue)
                    Assert.LessOrEqual(layout.Count(type.Id), type.MaxCount, $"seed {seed}: {type.Id}");
            }
        }

        [Test]
        public void RequiredTagsAreAlwaysPresent()
        {
            var rules = Rules();
            for (int seed = 0; seed < 500; seed++)
            {
                var layout = LayoutGenerator.Generate(seed, Catalogue, rules);
                foreach (var tag in rules.RequiredTags)
                    Assert.IsTrue(layout.Chunks.Any(c => Catalogue.First(t => t.Id == c.TypeId).Has(tag)), $"seed {seed}: {tag}");
            }
        }

        [Test]
        public void TightQuotasGiveFewerChunksButStayConnected()
        {
            var small = new[] { new ChunkType("a", 1, 1, new string[0]), new ChunkType("b", 2, 1, new string[0]) };
            for (int seed = 0; seed < 100; seed++)
            {
                var layout = LayoutGenerator.Generate(seed, small, Rules());
                Assert.AreEqual(3, layout.Chunks.Count);
                Assert.IsTrue(layout.IsConnected());
            }
        }

        [Test]
        public void RotationOnlyForRotatableChunks()
        {
            var fixedOnly = new[] { new ChunkType("fixed", 10, 1, new string[0], rotatable: false) };
            for (int seed = 0; seed < 50; seed++)
                Assert.IsTrue(LayoutGenerator.Generate(seed, fixedOnly, Rules()).Chunks.All(c => c.Rotation == 0));
        }

        [Test]
        public void OpenEdgesSurroundTheValley()
        {
            for (int seed = 0; seed < 200; seed++)
            {
                var layout = LayoutGenerator.Generate(seed, Catalogue, Rules());
                var edges = layout.OpenEdges().ToList();
                // Every occupied cell has 4 sides; each shared side is counted twice from both cells.
                int shared = 0;
                foreach (var cell in layout.Chunks.Select(c => c.Cell).Append(layout.Start))
                    foreach (var side in Cell.Sides)
                        if (layout.IsOccupied(cell.Step(side))) shared++;
                Assert.AreEqual(4 * (layout.Chunks.Count + 1) - shared, edges.Count, $"seed {seed}");
                Assert.IsTrue(edges.All(e => !layout.IsOccupied(e.cell.Step(e.side))));
            }
        }
    }
}
