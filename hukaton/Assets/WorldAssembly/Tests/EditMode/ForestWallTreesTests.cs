using System.Collections.Generic;
using System.Linq;
using Hakaton.Expeditions.Cards;
using Hakaton.WorldAssembly.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hakaton.WorldAssembly.Tests
{
    public sealed class ForestWallTreesTests
    {
        const string CataloguePath = "Assets/WorldAssembly/Chunk catalogue.asset";

        [Test]
        public void EveryWokenGiantTreeLeavesItsPlaceInTheWallOpen()
        {
            // Waking a giant tree of a forest maze's wall is a way through it: in every forest maze chunk, each giant tree
            // stops a walker where it stands (a hollow one lets the lane through by design), and once it is hidden (as the
            // card hides it) nothing that stays behind stands where its trunk was: what is in its place goes with it.
            var catalogue = AssetDatabase.LoadAssetAtPath<ChunkCatalog>(CataloguePath);
            var problems = new List<string>();
            int chunks = 0, trees = 0;
            foreach (var e in catalogue.entries)
            {
                if (!e.prefab || !e.shape.StartsWith("maze-")) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(e.prefab.gameObject);
                try
                {
                    go.transform.position = new Vector3(40000, 0, 40000);
                    var giants = go.transform.Find("Forest/Giant trees");
                    if (!giants) continue;
                    chunks++;
                    var ground = go.transform.Find("Ground");
                    foreach (Transform child in giants)
                    {
                        var tree = child.GetComponent<AwakenableTree>();
                        if (!tree || !child.gameObject.activeSelf) continue;
                        trees++;
                        Physics.SyncTransforms();
                        var foot = child.position;
                        List<string> Blocking() => Physics.OverlapCapsule(foot + Vector3.up * .78f, foot + Vector3.up * 1.52f, .28f, ~(1 << 4), QueryTriggerInteraction.Ignore)
                            .Where(c => c.transform.IsChildOf(go.transform) && !(ground && c.transform.IsChildOf(ground)))
                            .Select(c => $"{Path(c.transform, go.transform)} at {Vector3.Distance(Flat(c.transform.position), Flat(foot)):0.00} m")
                            .ToList();
                        string where = $"{e.prefab.name}: {child.name} at {Flat(foot - go.transform.position) + new Vector3(8, 0, 8)}";
                        if (!child.name.Contains("Hollow") && Blocking().Count == 0) problems.Add(where + " stops nobody");
                        child.gameObject.SetActive(false);
                        Physics.SyncTransforms();
                        var left = Blocking();
                        child.gameObject.SetActive(true);
                        if (left.Count > 0) problems.Add(where + " leaves its place shut by " + string.Join(", ", left));
                    }
                }
                finally { Object.DestroyImmediate(go); }
            }
            Debug.Log($"[World] {trees} giant trees in {chunks} forest maze chunks, {problems.Count} problems\n" + string.Join("\n", problems));
            Assert.That(chunks, Is.GreaterThan(10));
            Assert.That(trees, Is.GreaterThan(50));
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);

        static string Path(Transform t, Transform root)
        {
            var names = new List<string>();
            for (; t && t != root; t = t.parent) names.Insert(0, t.name);
            return string.Join("/", names);
        }
    }
}
