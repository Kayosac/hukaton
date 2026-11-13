using Hakaton.Expeditions;
using Hakaton.WorldAssembly.EditorTools;
using Hakaton.WorldAssembly.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace Hakaton.WorldAssembly.Tests
{
    /// <summary>Creatures wade (Kayospace «Биом: Гнилая топь»): shallow water is walkable for them, deep water is not.</summary>
    public sealed class ShallowWaterTests
    {
        static readonly Vector3 Far = new Vector3(5000, 0, 5000);

        [Test]
        public void CreaturesWadeInShallowWaterButNotInDeep()
        {
            var catalogue = AssetDatabase.LoadAssetAtPath<ChunkCatalog>("Assets/WorldAssembly/Chunk catalogue.asset");
            Assert.IsNotNull(catalogue);
            int shallowMask = 1 << ChunkPrefabBuilder.ShallowWaterArea;
            int wet = 0, wading = 0;
            foreach (var entry in catalogue.entries)
            {
                var view = entry.prefab;
                if (!view || !view.navMesh) continue;
                bool water = false;
                foreach (var o in view.obstacles) if (o && o.kind == ObstacleKind.Lake) water = true;
                if (!water) continue;
                wet++;
                var instance = NavMesh.AddNavMeshData(view.navMesh, Far, Quaternion.identity);
                try
                {
                    bool found = false;
                    for (float x = -7.5f; x <= 7.5f && !found; x += .5f)
                        for (float z = -7.5f; z <= 7.5f && !found; z += .5f)
                            found = NavMesh.SamplePosition(Far + new Vector3(x, -.4f, z), out _, .5f, shallowMask);
                    if (found) wading++;
                    else Debug.Log($"[World] no shallow water to wade in {entry.shape}/{entry.variant}");
                    if (entry.shape == "river-straight" && entry.variant == "plain")
                    {
                        // The middle of a plain river is deep: nobody walks its bed.
                        float bed = -TileTerrain.Deep;
                        Assert.IsFalse(NavMesh.SamplePosition(Far + new Vector3(0, bed, 0), out _, .6f, NavMesh.AllAreas), "creatures walk the bed of a deep river");
                    }
                }
                finally { instance.Remove(); }
            }
            Assert.Greater(wet, 0, "chunks with water");
            Assert.GreaterOrEqual(wading, wet * 3 / 4, $"most chunks with water let creatures wade at the edges ({wading} of {wet})");
        }
    }
}
