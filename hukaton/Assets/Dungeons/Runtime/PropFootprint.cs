using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;

namespace Hakaton.Dungeons.Unity
{
    /// <summary>
    /// Where the hand-placed props of a chamber stand in a body's way, read from the real shape of their colliders, as
    /// not-walkable boxes for the chamber's navigation (<see cref="DungeonBuilder"/>). The props' model meshes are not
    /// readable at runtime, so neither they nor their mesh colliders can go to the navigation builder as they are
    /// (<c>NavMeshBuilder.CollectSources</c> with physics colliders hands it the same meshes); the physics can still be
    /// asked about any collider. On a grid over the props, rays down find the highest top in each cell, and a box over
    /// the cell finds anything of a prop in the room above it (a board on edge, a patch on a wall). A body (the
    /// navigation agent's radius) rests on the highest top under it; it stands on the floor and goes on from there while
    /// the height it rests at changes by no more than <see cref="Settings.step"/> from one cell to the next and stays no
    /// higher than <see cref="Settings.stand"/>, and it walks under a prop whose underside is higher than
    /// <see cref="Settings.headroom"/>, keeping <see cref="Settings.margin"/> more off anything it may not stand on. The
    /// ground those bodies cover is open; the rest is in the way, and the navigation keeps the agent's radius off it. So a hollow prop stays open where
    /// its floor rises gently from the room (the Queen's bed), and a leaning timber prop is in the way where it comes
    /// down, not under its high end.
    /// The boxes are the same for every copy of a chamber: they are worked out once per chamber kind and kept (and worked
    /// out again if the kind's props or the settings change). The physics is asked in batches on the job threads.
    /// </summary>
    public static class PropFootprint
    {
        [System.Serializable]
        public struct Settings
        {
            /// <summary>Side of the grid the shape is read on, m.</summary>
            public float cell;
            /// <summary>Highest step between neighbouring cells a body takes, m.</summary>
            public float step;
            /// <summary>Highest a body stands over the floor (on a prop's top it got to), m.</summary>
            public float stand;
            /// <summary>Room a body needs over the floor: higher undersides are walked under, m.</summary>
            public float headroom;
            /// <summary>Kept clear of what a body may not stand on, on top of the agent's radius, m.</summary>
            public float margin;
            /// <summary>Colliders lower than this in all are flat litter: no one is stopped by them, m.</summary>
            public float litter;
            /// <summary>A body's radius: the navigation agent's, which the navigation keeps off what is in the way, m.</summary>
            public float radius;
        }

        sealed class Known
        {
            public (int, long, long, long) signature;
            public Settings settings;
            public List<Bounds> boxes;
        }

        static readonly Dictionary<ChamberTemplate, Known> known = new Dictionary<ChamberTemplate, Known>();
        static readonly List<Collider> found = new List<Collider>();

        /// <summary>How many times a chamber's shape has been read (not taken from what was kept): for tests.</summary>
        public static int Reads { get; private set; }

        /// <summary>Forgets what was worked out (tests that time the first build).</summary>
        public static void Forget() => known.Clear();

        /// <summary>
        /// The not-walkable boxes of <paramref name="placed"/>, a copy of the chamber <paramref name="kind"/>, in the
        /// chamber's own frame (origin on its floor). Its colliders must be where the physics sees them
        /// (<see cref="Physics.SyncTransforms"/>).
        /// </summary>
        public static List<Bounds> Boxes(ChamberTemplate kind, ChamberTemplate placed, Settings settings) =>
            Boxes(new[] { (kind, placed) }, settings)[0];

        /// <summary>The boxes of several chambers at once (the physics is asked once for all of them).</summary>
        public static List<Bounds>[] Boxes(IReadOnlyList<(ChamberTemplate kind, ChamberTemplate placed)> chambers, Settings settings)
        {
            var result = new List<Bounds>[chambers.Count];
            var readings = new List<Reading>();
            var pending = new Dictionary<ChamberTemplate, Reading>();
            for (int c = 0; c < chambers.Count; c++)
            {
                var (kind, placed) = chambers[c];
                var props = Props(placed, settings.litter);
                var signature = Signature(props);
                if (kind && known.TryGetValue(kind, out var k) && k.signature.Equals(signature) && k.settings.Equals(settings)) { result[c] = k.boxes; continue; }
                if (kind && pending.TryGetValue(kind, out var same) && same.signature.Equals(signature)) { same.copies.Add(c); continue; }
                var reading = new Reading(kind, placed.transform, props, signature, settings);
                reading.copies.Add(c);
                readings.Add(reading);
                if (kind) pending[kind] = reading;
            }
            if (readings.Count == 0) return result;

            Cast(readings, r => r.DownRays(), (r, i, hit) => r.OnTop(i, hit));
            Cast(readings, r => r.UpRays(), (r, i, hit) => r.OnUnderside(i, hit));
            foreach (var r in readings) r.MarkTall();
            Overlap(readings);
            if (readings.Count == 1) readings[0].Work();
            else Parallel.For(0, readings.Count, i => readings[i].Work());

            foreach (var r in readings)
            {
                Reads++;
                if (r.kind) known[r.kind] = new Known { signature = r.signature, settings = settings, boxes = r.boxes };
                foreach (int c in r.copies) result[c] = r.boxes;
            }
            return result;
        }

        /// <summary>
        /// The boxes as one not-walkable sheet at <paramref name="height"/> over the floor (in the chamber's frame): put
        /// over the navigation agent's climb and under its height, it leaves no room for anyone on the floor under it, so
        /// the navigation leaves that floor out and keeps the agent's radius off it, and has no walkable top of its own.
        /// One mesh for the whole chamber is much less work for the navigation builder than a box per cell run.
        /// </summary>
        public static Mesh Sheet(List<Bounds> boxes, float height)
        {
            var vertices = new List<Vector3>(boxes.Count * 4);
            var triangles = new List<int>(boxes.Count * 6);
            foreach (var b in boxes)
            {
                int v = vertices.Count;
                vertices.Add(new Vector3(b.min.x, height, b.min.z));
                vertices.Add(new Vector3(b.min.x, height, b.max.z));
                vertices.Add(new Vector3(b.max.x, height, b.max.z));
                vertices.Add(new Vector3(b.max.x, height, b.min.z));
                triangles.Add(v); triangles.Add(v + 1); triangles.Add(v + 2);
                triangles.Add(v); triangles.Add(v + 2); triangles.Add(v + 3);
            }
            var mesh = new Mesh { name = "Props in the way" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Whether a collider of a chamber counts as a prop in anyone's way (not a trigger, not flat litter).</summary>
        public static bool InTheWay(Collider col, Transform chamber, float litter) =>
            col && !col.isTrigger && col.enabled && LocalBounds(col, chamber).size.y >= litter;

        static List<(Collider col, Bounds local)> Props(ChamberTemplate placed, float litter)
        {
            var frame = placed.transform;
            var props = new List<(Collider, Bounds)>();
            placed.GetComponentsInChildren(found);
            foreach (var col in found)
            {
                if (col.isTrigger || !col.enabled) continue;
                var b = LocalBounds(col, frame);
                if (b.size.y < litter) continue;
                props.Add((col, b));
            }
            found.Clear();
            return props;
        }

        /// <summary>What the chamber's props are, to the centimetre: copies in any turn agree, an edited kind does not.</summary>
        static (int, long, long, long) Signature(List<(Collider col, Bounds local)> props)
        {
            long x = 0, y = 0, z = 0;
            foreach (var (_, b) in props)
            {
                var v = b.center + b.size * 1.618f;
                x += Mathf.RoundToInt(v.x * 100);
                y += Mathf.RoundToInt(v.y * 100);
                z += Mathf.RoundToInt(v.z * 100);
            }
            return (props.Count, x, y, z);
        }

        /// <summary>The collider's box in the chamber's frame, from its own shape's box (tight for a turned prop).</summary>
        static Bounds LocalBounds(Collider col, Transform frame)
        {
            Bounds own;
            switch (col)
            {
                case BoxCollider box: own = new Bounds(box.center, box.size); break;
                case MeshCollider mesh when mesh.sharedMesh: own = mesh.sharedMesh.bounds; break;
                case SphereCollider sphere: own = new Bounds(sphere.center, Vector3.one * sphere.radius * 2); break;
                case CapsuleCollider capsule:
                    var size = Vector3.one * capsule.radius * 2;
                    size[capsule.direction] = Mathf.Max(capsule.height, capsule.radius * 2);
                    own = new Bounds(capsule.center, size);
                    break;
                default:
                    return Transformed(frame.worldToLocalMatrix, col.bounds);
            }
            return Transformed(frame.worldToLocalMatrix * col.transform.localToWorldMatrix, own);
        }

        static Bounds Transformed(Matrix4x4 m, Bounds b)
        {
            var result = new Bounds(m.MultiplyPoint3x4(b.center), Vector3.zero);
            for (int c = 0; c < 8; c++)
            {
                var corner = b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                result.Encapsulate(m.MultiplyPoint3x4(corner));
            }
            return result;
        }

        // ---------- the physics, in batches ----------

        static void Cast(List<Reading> readings, System.Func<Reading, List<RaycastCommand>> rays, System.Action<Reading, int, RaycastHit> onHit)
        {
            var lists = new List<RaycastCommand>[readings.Count];
            int total = 0;
            for (int r = 0; r < readings.Count; r++) { lists[r] = rays(readings[r]); total += lists[r].Count; }
            if (total == 0) return;
            var commands = new NativeArray<RaycastCommand>(total, Allocator.TempJob);
            var results = new NativeArray<RaycastHit>(total, Allocator.TempJob);
            try
            {
                int at = 0;
                foreach (var list in lists) foreach (var command in list) commands[at++] = command;
                RaycastCommand.ScheduleBatch(commands, results, 32, 1).Complete();
                at = 0;
                for (int r = 0; r < readings.Count; r++)
                    for (int i = 0; i < lists[r].Count; i++, at++)
                    {
                        var hit = results[at];
                        if (hit.normal != Vector3.zero) onHit(readings[r], i, hit);
                    }
            }
            finally
            {
                commands.Dispose();
                results.Dispose();
            }
        }

        const int MaxOverlaps = 4;

        static void Overlap(List<Reading> readings)
        {
            var lists = new List<OverlapBoxCommand>[readings.Count];
            int total = 0;
            for (int r = 0; r < readings.Count; r++) { lists[r] = readings[r].Looks(); total += lists[r].Count; }
            if (total == 0) return;
            var commands = new NativeArray<OverlapBoxCommand>(total, Allocator.TempJob);
            var results = new NativeArray<ColliderHit>(total * MaxOverlaps, Allocator.TempJob);
            try
            {
                int at = 0;
                foreach (var list in lists) foreach (var command in list) commands[at++] = command;
                OverlapBoxCommand.ScheduleBatch(commands, results, 32, MaxOverlaps).Complete();
                at = 0;
                for (int r = 0; r < readings.Count; r++)
                    for (int i = 0; i < lists[r].Count; i++, at++)
                        for (int h = 0; h < MaxOverlaps; h++)
                        {
                            var col = results[at * MaxOverlaps + h].collider;
                            if (!col) break;
                            if (readings[r].Owns(col)) { readings[r].OnInTheWay(i); break; }
                        }
            }
            finally
            {
                commands.Dispose();
                results.Dispose();
            }
        }

        // ---------- one chamber ----------

        /// <summary>One chamber's shape being read: its grid, what the physics found over it, and then where bodies go.</summary>
        sealed class Reading
        {
            public readonly ChamberTemplate kind;
            public readonly (int, long, long, long) signature;
            public readonly List<int> copies = new List<int>();
            public List<Bounds> boxes;

            readonly Transform frame;
            readonly Vector3 up;
            readonly Quaternion turn;
            readonly HashSet<Collider> own = new HashSet<Collider>();
            readonly Settings s;
            readonly float cell;
            readonly int x0, z0, nx, nz, n;
            readonly float[] top;      // the highest top of a prop over the floor in the cell, m (0: bare floor)
            readonly bool[] wall;      // something in the cell no body stands on or walks under
            readonly List<(int k, Vector3 at, float from)> samples = new List<(int, Vector3, float)>();
            readonly List<int> tall = new List<int>();
            bool[] overhead;
            readonly List<int> looked = new List<int>();

            public Reading(ChamberTemplate kind, Transform frame, List<(Collider col, Bounds local)> props, (int, long, long, long) signature, Settings settings)
            {
                this.kind = kind;
                this.frame = frame;
                this.signature = signature;
                s = settings;
                up = frame.up;
                turn = frame.rotation;
                cell = Mathf.Max(.05f, s.cell);
                foreach (var (col, _) in props) own.Add(col);
                if (props.Count == 0) { boxes = new List<Bounds>(); return; }

                var area = props[0].local;
                foreach (var (_, b) in props) area.Encapsulate(b);
                // Room round the props for every reach below: the cells near them and the body's disc from there.
                float pad = (s.radius + s.margin + cell / 2) * 2 + cell * 4;
                x0 = Mathf.FloorToInt((area.min.x - pad) / cell);
                z0 = Mathf.FloorToInt((area.min.z - pad) / cell);
                nx = Mathf.FloorToInt((area.max.x + pad) / cell) - x0 + 1;
                nz = Mathf.FloorToInt((area.max.z + pad) / cell) - z0 + 1;
                n = nx * nz;
                top = new float[n];
                wall = new bool[n];

                // Where to look: every cell a collider's box reaches into (once a cell however many colliders reach it),
                // from just over the highest of them there. A ray down the middle of the cell finds the top; what it
                // misses of a thin thing off the middle, the box over the cell finds (Looks).
                var start = new float[n];
                foreach (var (_, b) in props)
                {
                    float over = b.max.y + .1f;
                    int i0 = Mathf.FloorToInt(b.min.x / cell), i1 = Mathf.FloorToInt(b.max.x / cell);
                    int j0 = Mathf.FloorToInt(b.min.z / cell), j1 = Mathf.FloorToInt(b.max.z / cell);
                    for (int j = j0; j <= j1; j++)
                        for (int i = i0; i <= i1; i++)
                        {
                            int k = (j - z0) * nx + i - x0;
                            if (over > start[k]) start[k] = over;
                        }
                }
                for (int k = 0; k < n; k++)
                    if (start[k] > 0) samples.Add((k, new Vector3((x0 + k % nx + .5f) * cell, 0, (z0 + k / nx + .5f) * cell), start[k]));
            }

            static QueryParameters Query(bool backfaces) => new QueryParameters(Physics.DefaultRaycastLayers, false, QueryTriggerInteraction.Ignore, backfaces);

            /// <summary>Down from over the props: the highest top in each cell.</summary>
            public List<RaycastCommand> DownRays()
            {
                var rays = new List<RaycastCommand>(samples.Count);
                foreach (var (_, at, from) in samples)
                    rays.Add(new RaycastCommand(frame.TransformPoint(at + Vector3.up * from), -up, Query(false), from + .05f));
                return rays;
            }

            public void OnTop(int i, RaycastHit hit)
            {
                int k = samples[i].k;
                float h = frame.InverseTransformPoint(hit.point).y;
                if (h > s.headroom) tall.Add(i);
                else if (h > top[k]) top[k] = h;
            }

            /// <summary>
            /// Higher than a body's room, a top may be only overhead: then up from the floor, back faces too, the first
            /// thing is an underside higher than that (else the spot is inside a pillar, or under something low).
            /// </summary>
            public List<RaycastCommand> UpRays()
            {
                overhead = new bool[tall.Count];
                var rays = new List<RaycastCommand>(tall.Count);
                foreach (int i in tall)
                    rays.Add(new RaycastCommand(frame.TransformPoint(samples[i].at + Vector3.up * .02f), up, Query(true), samples[i].from));
                return rays;
            }

            public void OnUnderside(int t, RaycastHit hit) =>
                overhead[t] = Vector3.Dot(hit.normal, up) < 0 && frame.InverseTransformPoint(hit.point).y > s.headroom;

            public void MarkTall()
            {
                for (int t = 0; t < tall.Count; t++)
                    if (!overhead[t]) wall[samples[tall[t]].k] = true;
            }

            /// <summary>
            /// Anything of a prop in the room over a cell's top (a body's height, less a step), seen by the rays or not:
            /// a box over each cell a body could stand over (none rests higher than it stands).
            /// </summary>
            public List<OverlapBoxCommand> Looks()
            {
                var looks = new List<OverlapBoxCommand>();
                if (n == 0) return looks;
                var done = new bool[n];
                foreach (var (k, _, _) in samples)
                {
                    if (done[k] || wall[k] || top[k] > s.stand) continue;
                    done[k] = true;
                    float lo = top[k] + s.step, hi = s.headroom;
                    if (lo >= hi) continue;
                    var centre = new Vector3((x0 + k % nx + .5f) * cell, (lo + hi) / 2, (z0 + k / nx + .5f) * cell);
                    looked.Add(k);
                    looks.Add(new OverlapBoxCommand(frame.TransformPoint(centre), new Vector3(cell / 2, (hi - lo) / 2, cell / 2), turn, Query(false)));
                }
                return looks;
            }

            public bool Owns(Collider col) => own.Contains(col);

            public void OnInTheWay(int i) => wall[looked[i]] = true;

            /// <summary>Where bodies go, and the boxes round the rest (no Unity calls: may run off the main thread).</summary>
            public void Work()
            {
                if (boxes != null) return;
                // Where a body's middle gets to. Standing with its middle in a cell, a body (radius and margin, plus half
                // a cell) rests on the highest top under it and cannot stand where anything under it is in the way; it walks on
                // from cell to cell while that height changes by no more than a step, starting from the floor. Only the
                // cells within a body of a prop need working out (found as a square: two sliding windows, along x then
                // along z): farther off a body rests on the bare floor. The grid's pad keeps every offset below inside.
                float body = s.radius + s.margin + cell / 2;
                var footing = Offsets(body / cell);
                int reachOut = Mathf.CeilToInt(body / cell);
                var alongX = new bool[n];
                for (int j = 0; j < nz; j++)
                {
                    int row = j * nx, count = 0;
                    for (int i = -reachOut; i < nx; i++)
                    {
                        int enter = i + reachOut, leave = i - reachOut - 1;
                        if (enter < nx && (wall[row + enter] || top[row + enter] > 0)) count++;
                        if (leave >= 0 && (wall[row + leave] || top[row + leave] > 0)) count--;
                        if (i >= 0) alongX[row + i] = count > 0;
                    }
                }
                var near = new bool[n];
                var nearCells = new List<int>();
                for (int i = 0; i < nx; i++)
                {
                    int count = 0;
                    for (int j = -reachOut; j < nz; j++)
                    {
                        int enter = j + reachOut, leave = j - reachOut - 1;
                        if (enter < nz && alongX[enter * nx + i]) count++;
                        if (leave >= 0 && alongX[leave * nx + i]) count--;
                        if (j >= 0 && count > 0) { near[j * nx + i] = true; nearCells.Add(j * nx + i); }
                    }
                }

                var rest = new float[n];
                var reach = new bool[n];
                for (int k = 0; k < n; k++) reach[k] = !near[k];
                var queue = new Queue<int>();
                foreach (int k in nearCells)
                {
                    float h = 0;
                    bool free = true;
                    foreach (int d in footing)
                    {
                        if (wall[k + d]) { free = false; break; }
                        if (top[k + d] > h) h = top[k + d];
                    }
                    rest[k] = free ? h : float.MaxValue;
                    if (free && h <= s.step) { reach[k] = true; queue.Enqueue(k); }
                }
                int[] sides = { 1, -1, nx, -nx };
                while (queue.Count > 0)
                {
                    int k = queue.Dequeue();
                    foreach (int d in sides)
                    {
                        int m = k + d;
                        if (reach[m] || rest[m] > s.stand || Mathf.Abs(rest[m] - rest[k]) > s.step) continue;
                        reach[m] = true;
                        queue.Enqueue(m);
                    }
                }

                // Got to by different ways, two neighbouring cells may still lie more than a step apart (the floor beside
                // a low top reached up a slope from its other side): the navigation lies flat on the floor, so that edge
                // is a wall between them, or a way would lead straight up it. (Only a cell raised over a step can lie more
                // than a step from a neighbour.)
                int[] round = { 1, -1, nx, -nx, nx + 1, nx - 1, -nx + 1, -nx - 1 };
                var cliff = new bool[n];
                var cliffs = new List<int>();
                foreach (int k in nearCells)
                {
                    if (!reach[k] || rest[k] <= s.step) continue;
                    foreach (int d in round)
                        if (reach[k + d] && Mathf.Abs(rest[k + d] - rest[k]) > s.step)
                        {
                            if (!cliff[k]) { cliff[k] = true; cliffs.Add(k); }
                            if (!cliff[k + d]) { cliff[k + d] = true; cliffs.Add(k + d); }
                        }
                }
                foreach (int k in cliffs) reach[k] = false;

                // The ground those bodies cover (without the margin) is open; the rest is in the way (the navigation then
                // keeps the agent's radius off it, which brings the bodies' middles back to where they got to).
                var cover = Offsets(s.radius / cell);
                var stopped = new List<int>(cliffs);
                foreach (int k in nearCells)
                {
                    if (cliff[k]) continue;
                    bool open = false;
                    foreach (int d in cover)
                        if (reach[k + d]) { open = true; break; }
                    if (!open) stopped.Add(k);
                }
                var blocked = new bool[n];
                foreach (int k in stopped) blocked[k] = true;

                // As few boxes as will do: a run along x, grown along z while the next row holds the whole run.
                boxes = new List<Bounds>();
                var used = new bool[n];
                for (int j = 0; j < nz; j++)
                    for (int i = 0; i < nx; i++)
                    {
                        int k = j * nx + i;
                        if (!blocked[k] || used[k]) continue;
                        int i1 = i;
                        while (i1 + 1 < nx && blocked[k + i1 + 1 - i] && !used[k + i1 + 1 - i]) i1++;
                        int j1 = j;
                        while (j1 + 1 < nz)
                        {
                            bool whole = true;
                            for (int t = i; t <= i1 && whole; t++) whole = blocked[(j1 + 1) * nx + t] && !used[(j1 + 1) * nx + t];
                            if (!whole) break;
                            j1++;
                        }
                        for (int jj = j; jj <= j1; jj++)
                            for (int t = i; t <= i1; t++) used[jj * nx + t] = true;
                        var centre = new Vector3((x0 + (i + i1 + 1) * .5f) * cell, s.headroom / 2, (z0 + (j + j1 + 1) * .5f) * cell);
                        boxes.Add(new Bounds(centre, new Vector3((i1 - i + 1) * cell, s.headroom, (j1 - j + 1) * cell)));
                    }
            }

            /// <summary>Steps in the grid to the cells whose middles lie within <paramref name="radius"/> cells of a cell's.</summary>
            int[] Offsets(float radius)
            {
                var steps = new List<int>();
                int r = Mathf.FloorToInt(radius);
                for (int dj = -r; dj <= r; dj++)
                    for (int di = -r; di <= r; di++)
                        if (di * di + dj * dj <= radius * radius + 1e-4f) steps.Add(dj * nx + di);
                return steps.ToArray();
            }
        }
    }
}
