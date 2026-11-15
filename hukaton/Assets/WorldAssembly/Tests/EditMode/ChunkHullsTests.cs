using System.Collections.Generic;
using System.Linq;
using Hakaton.Expeditions;
using Hakaton.WorldAssembly.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hakaton.WorldAssembly.Tests
{
    public sealed class ChunkHullsTests
    {
        [Test]
        public void HollowThingsMoveOnPiecesThatLeaveTheHollowOpen()
        {
            var catalogue = AssetDatabase.LoadAssetAtPath<ChunkCatalog>("Assets/WorldAssembly/Chunk catalogue.asset");
            var seen = new HashSet<ChunkView>();
            int boats = 0, logs = 0;
            var problems = new List<string>();
            foreach (var e in catalogue.entries)
            {
                if (!e.prefab || !seen.Add(e.prefab)) continue;
                if (!e.prefab.props.Any(p => p && p.transform.Find("Hull"))) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(e.prefab.gameObject);
                go.transform.position = new Vector3(40000, 0, 40000);
                Physics.SyncTransforms();
                foreach (var prop in go.GetComponent<ChunkView>().props.Where(p => p && p.transform.Find("Hull")))
                {
                    var hull = prop.transform.Find("Hull").GetComponents<MeshCollider>();
                    if (hull.Length < 4 || hull.Any(c => !c.convex)) problems.Add($"{e.prefab.name}: {prop.name} has {hull.Length} pieces, all convex: {hull.All(c => c.convex)}");
                    if (prop.GetComponentsInChildren<Collider>().Any(c => c.gameObject.name != "Hull")) problems.Add($"{e.prefab.name}: {prop.name} keeps colliders besides its pieces");
                    // Only its own pieces: everything else out of the way.
                    var others = go.GetComponentsInChildren<Collider>().Where(c => !c.transform.IsChildOf(prop.transform) && c.enabled).ToList();
                    foreach (var c in others) c.enabled = false;
                    Physics.SyncTransforms();
                    var b = hull[0].bounds;
                    foreach (var c in hull) b.Encapsulate(c.bounds);
                    if (prop.name.Contains("Boat"))
                    {
                        boats++;
                        // Down into the middle of the boat: its bottom, not a lid over the top.
                        bool hit = Physics.Raycast(new Vector3(b.center.x, b.max.y + 1, b.center.z), Vector3.down, out var down, b.size.y + 2);
                        if (!hit || down.point.y > b.min.y + b.size.y * .45f) problems.Add($"{e.prefab.name}: {prop.name} is shut at {(hit ? down.point.y - b.min.y : -1):F2} of {b.size.y:F2}");
                    }
                    else
                    {
                        logs++;
                        // Along the hollow from end to end: a ball goes through.
                        var axis = prop.transform.forward;
                        float length = Vector3.Scale(b.size, new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z))).magnitude;
                        var centre = hull.Select(c => c.bounds.center).Aggregate(Vector3.zero, (a, v) => a + v) / hull.Length;
                        if (Physics.SphereCast(centre - axis * (length + 1), .15f * prop.transform.lossyScale.x, axis, out var stop, length * 2 + 2))
                            problems.Add($"{e.prefab.name}: {prop.name} is shut inside at {stop.point}");
                    }
                    foreach (var c in others) c.enabled = true;
                }
                Object.DestroyImmediate(go);
            }
            Debug.Log($"[World] hollow things: {boats} boats, {logs} logs, {problems.Count} problems\n" + string.Join("\n", problems));
            Assert.That(boats, Is.GreaterThan(0));
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }
    }
}
