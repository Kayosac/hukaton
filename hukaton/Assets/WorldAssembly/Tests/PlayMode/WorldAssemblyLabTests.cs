#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using Hakaton.WorldAssembly.Unity;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hakaton.WorldAssembly.Tests
{
    /// <summary>
    /// Assembles many valleys from the greybox chunks in the lab scene and walks the navmesh from the overlook
    /// to the gate of every edge of every chunk: the edge standard must keep the whole valley walkable.
    /// Also keeps the assembly cheap enough to hide behind the departure fade.
    /// </summary>
    public sealed class WorldAssemblyLabTests
    {
        const string ScenePath = "Assets/WorldAssembly/Scenes/WorldAssemblyLab.unity";
        const int Seeds = 20;
        const double BakeBudgetMs = 1000;

        [UnitySetUp]
        public IEnumerator LoadTheLab()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
        }

        [UnityTest]
        public IEnumerator EveryChunkIsWalkableFromTheOverlook()
        {
            var assembler = Object.FindAnyObjectByType<WorldAssembler>();
            Assert.IsNotNull(assembler, "The lab scene needs a WorldAssembler.");
            var failures = new List<string>();
            var path = new NavMeshPath();
            double slowest = 0;
            for (int seed = 1; seed <= Seeds; seed++)
            {
                var layout = assembler.Assemble(seed);
                yield return null;
                slowest = System.Math.Max(slowest, assembler.LastBakeMs);
                Assert.AreEqual(assembler.chunkCount, layout.Chunks.Count, $"seed {seed}");
                Assert.IsTrue(NavMesh.SamplePosition(assembler.DeparturePose.position, out var from, 3, NavMesh.AllAreas),
                    $"seed {seed}: no navmesh under the departure pose");
                foreach (var chunk in assembler.Chunks)
                {
                    if (chunk.departurePose) continue;
                    foreach (var direction in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
                    {
                        var gate = chunk.transform.position + direction * (assembler.chunkSize / 2 - 2);
                        if (!NavMesh.SamplePosition(gate, out var to, 2, NavMesh.AllAreas)) { failures.Add($"seed {seed}: {chunk.name} gate {direction} has no navmesh"); continue; }
                        NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, path);
                        if (path.status != NavMeshPathStatus.PathComplete) failures.Add($"seed {seed}: {chunk.name} gate {direction} {path.status}");
                    }
                }
            }
            assembler.Clear();
            Debug.Log($"[World] {Seeds} valleys, slowest navmesh bake {slowest:0} ms");
            Assert.IsEmpty(failures, string.Join("\n", failures));
            Assert.Less(slowest, BakeBudgetMs, "Navmesh bake is too slow to hide behind the departure fade.");
        }

        [UnityTest]
        public IEnumerator SameSeedRebuildsTheSameValley()
        {
            var assembler = Object.FindAnyObjectByType<WorldAssembler>();
            string Snapshot()
            {
                var names = new List<string>();
                foreach (var chunk in assembler.Chunks) names.Add($"{chunk.name} {chunk.transform.position} {chunk.transform.eulerAngles.y:0}");
                return string.Join("; ", names);
            }
            assembler.Assemble(42);
            yield return null;
            string first = Snapshot();
            assembler.Assemble(7);
            yield return null;
            assembler.Assemble(42);
            yield return null;
            Assert.AreEqual(first, Snapshot());
            assembler.Clear();
        }
    }
}
#endif
