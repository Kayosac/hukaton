using System.Collections.Generic;
using System.Linq;
using System.Text;
using Hakaton.Dungeons;
using Hakaton.Dungeons.Unity;
using Hakaton.Expeditions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace Hakaton.WorldAssembly.Tests
{
    /// <summary>
    /// The anthill's navigation keeps bodies off the chambers' props (<see cref="PropFootprint"/>): in twenty anthills,
    /// small and large, every way it finds between random spots of a chamber is walked by a worker's body (its capsule,
    /// resting where it stands and stepping no higher than the kit's step) without brushing a prop. Every marker is still
    /// joined with its chamber, and the Queen's bed stays open inside.
    /// </summary>
    public sealed class AnthillPropsNavigationTests
    {
        const string KitPath = "Assets/Dungeons/Anthill/Anthill Kit.asset";
        const string WorkerPath = "Assets/Game/Prefabs/AntWorker.prefab";
        const int Anthills = 20, PairsPerChamber = 30;
        /// <summary>How far apart along a way the body is put down, m.</summary>
        const float Pace = .1f;
        /// <summary>The body's capsule is this much wider than the worker's when it is checked against the props, m.</summary>
        const float Brush = .03f;

        DungeonKit kit;
        float radius, height;
        GameObject root;

        [SetUp]
        public void LayAside()
        {
            kit = AssetDatabase.LoadAssetAtPath<DungeonKit>(KitPath);
            Assert.That(kit, Is.Not.Null);
            var worker = AssetDatabase.LoadAssetAtPath<GameObject>(WorkerPath);
            var creature = worker.GetComponent<WorldCreature>();
            var capsule = worker.GetComponent<CapsuleCollider>();
            Assert.That(creature && capsule, Is.True, "the worker has its body");
            // Its body as the game sizes it (WorldCreature.ApplySize): about 0.5 m round and 1.6 m tall.
            radius = capsule.radius * creature.size;
            height = capsule.height * creature.size;
        }

        [TearDown]
        public void PutBack()
        {
            if (root) Object.DestroyImmediate(root);
        }

        DungeonBuilder Builder()
        {
            root = new GameObject("Props test anthill");
            // Somewhere out of the way, turned off the grid: the chambers' props are read in any turn.
            root.transform.SetPositionAndRotation(new Vector3(0, -2000, 0), Quaternion.Euler(0, 37, 0));
            var builder = root.AddComponent<DungeonBuilder>();
            builder.kit = kit;
            return builder;
        }

        static int Seed(int i) => 1000 + i * 7919;

        [Test]
        public void BodiesWalkTheChambersWaysWithoutBrushingAProp()
        {
            var builder = Builder();
            var failures = new List<string>();
            int large = 0, small = 0, ways = 0, whole = 0;
            var path = new NavMeshPath();
            for (int a = 1; a <= Anthills; a++)
            {
                int seed = Seed(a);
                var plan = builder.Build(seed, a % 2 == 0);
                if (plan.Large) large++; else small++;
                var dice = new System.Random(seed);
                foreach (var kv in builder.Chambers)
                {
                    var room = plan.Rooms[kv.Key];
                    var spots = Spots(builder, room);
                    if (spots.Count < 2) continue;
                    for (int p = 0; p < PairsPerChamber; p++)
                    {
                        var from = spots[dice.Next(spots.Count)];
                        var to = spots[dice.Next(spots.Count)];
                        ways++;
                        if (!NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                        whole++;
                        var brushed = Walk(path.corners, kit.propStep);
                        if (brushed != null) failures.Add($"seed {seed} {room.Shape.Id}: {brushed}");
                    }
                }
            }
            builder.Clear();
            Debug.Log($"[Anthill props] {Anthills} anthills ({small} small, {large} large): {ways} ways tried, {whole} whole, {failures.Count} brush a prop");
            Assert.That(small, Is.GreaterThanOrEqualTo(5), "small anthills among them");
            Assert.That(large, Is.GreaterThanOrEqualTo(3), "large anthills among them");
            Assert.That(whole, Is.GreaterThan(ways * .9f), "nearly every pair of spots in a chamber is joined");
            Assert.That(failures, Is.Empty, string.Join("\n", failures.Take(20)));
        }

        [Test]
        public void EveryMarkerIsReachedInItsChamberAndTheQueensBedStaysOpen()
        {
            var builder = Builder();
            var failures = new StringBuilder();
            var path = new NavMeshPath();
            int queens = 0;
            for (int a = 1; a <= Anthills; a++)
            {
                int seed = Seed(a);
                var plan = builder.Build(seed, a % 2 == 0);
                foreach (var (room, name, at) in builder.Markers)
                {
                    string where = $"seed {seed} {plan.Rooms[room].Shape.Id} {name}";
                    // The garrison and the chests are put on the navigation within 2.5 m of their marker (DungeonSite).
                    if (!NavMesh.SamplePosition(at.position, out var near, 2.5f, NavMesh.AllAreas))
                    {
                        failures.AppendLine($"{where}: no navigation within 2.5 m");
                        continue;
                    }
                    // Joined with most of its chamber: the props do not shut a marker away in a pocket.
                    var spots = Spots(builder, plan.Rooms[room]);
                    int joined = spots.Count(s => NavMesh.CalculatePath(s, near.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete);
                    if (joined * 2 < spots.Count) failures.AppendLine($"{where}: joined with {joined} of {spots.Count} spots of its chamber");
                    if (name == "Spawn Boss")
                    {
                        queens++;
                        // In the bed: its floor (up to 0.34 m over the room's) is open round the Queen's place.
                        var flat = near.position - at.position;
                        flat.y = 0;
                        if (flat.magnitude > .5f) failures.AppendLine($"seed {seed}: the Queen's bed is shut ({flat.magnitude:F2} m from her place to the navigation)");
                    }
                }
            }
            builder.Clear();
            Assert.That(queens, Is.GreaterThan(0), "a large anthill with its Queen among them");
            Assert.That(failures.Length, Is.EqualTo(0), failures.ToString());
        }

        [Test]
        public void EachChamberKindIsReadOnce()
        {
            var builder = Builder();
            PropFootprint.Forget();
            int reads = PropFootprint.Reads;
            var kinds = new HashSet<string>();
            for (int a = 1; a <= 6; a++)
            {
                var plan = builder.Build(Seed(a), true);
                foreach (var room in plan.Rooms) kinds.Add(room.Shape.Id);
            }
            builder.Clear();
            Assert.That(PropFootprint.Reads - reads, Is.EqualTo(kinds.Count), "every chamber kind read once, then kept");
        }

        /// <summary>Spots of the navigation over a chamber, every half metre.</summary>
        static List<Vector3> Spots(DungeonBuilder builder, PlacedRoom room)
        {
            var spots = new List<Vector3>();
            foreach (var cell in room.Cells)
                for (float x = -1.75f; x <= 1.76f; x += .5f)
                    for (float z = -1.75f; z <= 1.76f; z += .5f)
                    {
                        var p = builder.Built.TransformPoint(DungeonGeometry.Center(cell, room.Floor) + new Vector3(x, 0, z));
                        if (!NavMesh.SamplePosition(p, out var hit, .3f, NavMesh.AllAreas)) continue;
                        var off = hit.position - p;
                        off.y = 0;
                        if (off.magnitude < .05f) spots.Add(hit.position);
                    }
            return spots;
        }

        static readonly RaycastHit[] casts = new RaycastHit[16];

        /// <summary>
        /// Walks a body along the way: every <see cref="Pace"/> it rests (a sphere of its radius dropped from above its
        /// height) and is checked, as a capsule raised by a step, for any prop; it may not go up or down more than a step
        /// between two paces on a prop. Null if it gets through, else what it brushed and where.
        /// </summary>
        string Walk(Vector3[] corners, float step)
        {
            const float drop = 1.5f;
            float last = float.NaN;
            Collider lastOn = null;
            for (int c = 1; c < corners.Length; c++)
            {
                Vector3 leg = corners[c] - corners[c - 1];
                float length = leg.magnitude;
                for (float t = 0; t <= length + .001f; t += Pace)
                {
                    var p = corners[c - 1] + leg * (length > 0 ? Mathf.Min(1, t / length) : 0);
                    float rest = p.y, best = float.MaxValue;
                    Collider on = null;
                    int count = Physics.SphereCastNonAlloc(p + Vector3.up * (drop + radius), radius - .02f, Vector3.down, casts, drop + .3f, ~(1 << 2), QueryTriggerInteraction.Ignore);
                    for (int h = 0; h < count; h++)
                        if (casts[h].distance > 0 && casts[h].distance < best) { best = casts[h].distance; on = casts[h].collider; }
                    if (on) rest = p.y + drop + radius - best - (radius - .02f);
                    if (!float.IsNaN(last) && Mathf.Abs(rest - last) > step + .05f && (Prop(on) || Prop(lastOn)))
                        return $"steps {rest - last:+0.00;-0.00} m onto {Name(Prop(on) ? on : lastOn)} at {Local(p, Prop(on) ? on : lastOn)}";
                    last = rest;
                    lastOn = on;
                    var low = new Vector3(p.x, rest + step + radius, p.z);
                    var high = new Vector3(p.x, Mathf.Max(low.y, rest + height - radius), p.z);
                    foreach (var col in Physics.OverlapCapsule(low, high, radius + Brush, ~(1 << 2), QueryTriggerInteraction.Ignore))
                        if (Prop(col)) return $"brushes {Name(col)} at {Local(p, col)}";
                    if (length == 0) break;
                }
            }
            return null;
        }

        bool Prop(Collider col)
        {
            if (!col) return false;
            var chamber = col.GetComponentInParent<ChamberTemplate>();
            return chamber && PropFootprint.InTheWay(col, chamber.transform, kit.propLitter);
        }

        static string Name(Collider col)
        {
            var t = col.transform;
            while (t.parent && !t.parent.GetComponent<ChamberTemplate>() && t.parent.parent && !t.parent.parent.GetComponent<ChamberTemplate>()) t = t.parent;
            return t.name + "/" + col.name;
        }

        static string Local(Vector3 p, Collider col) => col.GetComponentInParent<ChamberTemplate>().transform.InverseTransformPoint(p).ToString("F2");
    }
}
