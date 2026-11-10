using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.WorldAssembly.EditorTools
{
    /// <summary>
    /// Ground plan of one cell of a giant tangled forest (Kayospace «Открытый мир v2»: гигантские запутанные леса), in
    /// canonical chunk metres 0…16: winding lanes from every open side to a hub, a nook at a dead end, a glade, or the
    /// heart's ring round the greatest tree. Everything else is wall: giant trunks, root mounds and deadfall. Lanes
    /// meet the edges straight and exactly as wide as a stone maze's corridor, so cells join whatever their turn.
    /// </summary>
    public sealed class ForestLayout
    {
        public const float EdgeHalf = TileTerrain.Corridor;
        public const float GladeRadius = 4.8f, HeartRadius = 5.8f, TrunkRadius = 2.1f, NookRadius = 2.7f;
        /// <summary>Straight run of a lane in from its edge, m.</summary>
        const float Lead = 2.6f;

        public readonly bool[] Open = new bool[4];
        public readonly bool Glade, Heart;
        public readonly Vector2 Hub;
        /// <summary>Lane centrelines, edge middle first, hub last.</summary>
        public readonly List<Vector2[]> Lanes = new List<Vector2[]>();
        /// <summary>Where the heart's reward lies, at the foot of the great tree; and where its guard waits.</summary>
        public readonly Vector2 PocketSpot, GuardSpot;
        readonly int seed;

        static readonly Vector2[] Middle = { new Vector2(8, 16), new Vector2(16, 8), new Vector2(8, 0), new Vector2(0, 8) };
        static readonly Vector2[] Inward = { new Vector2(0, -1), new Vector2(-1, 0), new Vector2(0, 1), new Vector2(1, 0) };

        public ForestLayout(Mid[] canonicalMids, string variant, int seed)
        {
            this.seed = seed;
            for (int s = 0; s < 4; s++) Open[s] = canonicalMids[s] == Mid.MazeOpen;
            Glade = variant == TileShapes.ForestGlade;
            Heart = variant == TileShapes.ForestHeart;
            Hub = new Vector2(8, 8);
            if (!Glade && !Heart) Hub += new Vector2(Unit(1) * 2 - 1, Unit(2) * 2 - 1) * 1.3f;
            int lanes = 0;
            for (int s = 0; s < 4; s++)
            {
                if (!Open[s]) continue;
                lanes++;
                var a = Middle[s];
                var b = a + Inward[s] * Lead;
                // The far end: the hub, or the rim of the open space round it.
                var end = Hub;
                if (Glade || Heart)
                {
                    float r = (Glade ? GladeRadius : HeartRadius) - 1;
                    end = Hub - Inward[s] * r;
                }
                // Lanes wind: a bow to one side, sometimes back the other way, never straight to the middle.
                var across = new Vector2(-Inward[s].y, Inward[s].x);
                float bow = (Unit(10 + s) * 2 - 1) * 3f;
                if (Mathf.Abs(bow) < 1.2f) bow = Mathf.Sign(bow + .01f) * 1.2f;
                float back = Unit(20 + s) < .5f ? -bow * .8f : bow * .4f;
                var c1 = Vector2.Lerp(b, end, .33f) + across * bow;
                var c2 = Vector2.Lerp(b, end, .66f) + across * back;
                var line = new List<Vector2> { a };
                for (float d = .25f; d < Lead; d += .25f) line.Add(a + Inward[s] * d);
                for (int k = 0; k <= 32; k++)
                {
                    float t = k / 32f, u = 1 - t;
                    line.Add(u * u * u * b + 3 * u * u * t * c1 + 3 * u * t * t * c2 + t * t * t * end);
                }
                Lanes.Add(line.ToArray());
            }
            // The heart's reward lies at the tree's foot, on the side away from the first lane; its guard stands across.
            var away = Vector2.zero;
            for (int s = 0; s < 4; s++) if (Open[s]) away += Inward[s];
            if (away.sqrMagnitude < .01f) away = new Vector2(1, 1);
            away = away.normalized;
            PocketSpot = Hub + away * (TrunkRadius + 1.2f);
            GuardSpot = Hub + new Vector2(-away.y, away.x) * (TrunkRadius + 1.6f);
            if (lanes == 0) Lanes.Add(new[] { Hub, Hub });
            // A blind alcove off one lane: somewhere to hide a find or a creature, and one more turn to get lost in.
            if (!Heart && lanes > 0)
            {
                var lane = Lanes[(int)(Unit(30) * Lanes.Count) % Lanes.Count];
                int at = lane.Length / 2 + (int)((Unit(31) - .5f) * lane.Length * .3f);
                var p = lane[Mathf.Clamp(at, 2, lane.Length - 3)];
                var dir = (lane[Mathf.Clamp(at + 2, 0, lane.Length - 1)] - lane[Mathf.Clamp(at - 2, 0, lane.Length - 1)]).normalized;
                var side = new Vector2(-dir.y, dir.x);
                // Towards the roomier side: away from the hub and the edges.
                var left = p + side * 4.2f;
                var right = p - side * 4.2f;
                float Room(Vector2 q) => Mathf.Min(Mathf.Min(q.x, 16 - q.x), Mathf.Min(q.y, 16 - q.y)) + .3f * Vector2.Distance(q, Hub);
                var tip = Room(left) >= Room(right) ? left : right;
                if (Mathf.Min(Mathf.Min(tip.x, 16 - tip.x), Mathf.Min(tip.y, 16 - tip.y)) > 2.6f)
                {
                    var line = new List<Vector2>();
                    for (int k = 0; k <= 12; k++) line.Add(Vector2.Lerp(p, tip, k / 12f) + dir * Mathf.Sin(k / 12f * Mathf.PI) * .8f);
                    Alcove = tip;
                    alcove = line.ToArray();
                }
            }
        }

        /// <summary>The end of the blind alcove, if the cell has one.</summary>
        public readonly Vector2? Alcove;
        readonly Vector2[] alcove;

        float Unit(int salt) => (float)Hash.Unit(seed, 97, salt);

        float Noise(Vector2 p, float cell, int salt)
        {
            float gx = Mathf.Floor(p.x / cell), gy = Mathf.Floor(p.y / cell);
            float fx = p.x / cell - gx, fy = p.y / cell - gy;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            float V(float x, float y) => (float)Hash.Unit(seed, salt, (int)x, (int)y) * 2 - 1;
            return Mathf.Lerp(Mathf.Lerp(V(gx, gy), V(gx + 1, gy), fx), Mathf.Lerp(V(gx, gy + 1), V(gx + 1, gy + 1), fx), fy);
        }

        static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b, out float t)
        {
            var ab = b - a;
            t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>Distance from the nearest lane's centreline, and how far along it that is from the edge, m.</summary>
        public float LaneDistance(Vector2 p, out float along)
        {
            float best = float.MaxValue;
            along = 0;
            foreach (var line in Lanes)
            {
                float run = 0;
                for (int i = 0; i + 1 < line.Length; i++)
                {
                    float seg = Vector2.Distance(line[i], line[i + 1]);
                    float d = SegmentDistance(p, line[i], line[i + 1], out float t);
                    if (d < best) { best = d; along = run + seg * t; }
                    run += seg;
                }
            }
            return best;
        }

        /// <summary>Half width of a lane this far from its edge: the corridor's at the edge, then wider and narrower by turns.</summary>
        float LaneHalf(float along, Vector2 p)
        {
            float k = Mathf.Clamp01((along - Lead + .4f) / 1.6f);
            float inner = 1.95f + .38f * Noise(new Vector2(along, 0), 2.7f, 5) + .15f * Noise(p, 1.3f, 6);
            return Mathf.Lerp(EdgeHalf, inner, k);
        }

        /// <summary>
        /// How far inside the wall a point is, m: negative on the walkable ground (lanes, nook, glade, the heart's ring),
        /// positive in the tangle. Exact at the edges, where lanes meet the corridor of the neighbour.
        /// </summary>
        public float WallDepth(Vector2 p)
        {
            float lane = LaneDistance(p, out float along) - LaneHalf(along, p);
            // Ragged sides away from the edges.
            float fade = TileTerrain.EdgeFade(p, 1.2f);
            lane += .3f * Noise(p, 1.1f, 7) * fade;
            float open = lane;
            if (alcove != null)
            {
                float best = float.MaxValue;
                for (int i = 0; i + 1 < alcove.Length; i++) best = Mathf.Min(best, SegmentDistance(p, alcove[i], alcove[i + 1], out _));
                open = Mathf.Min(open, best - 1.3f + .2f * Noise(p, 1.1f, 11));
                open = Mathf.Min(open, Vector2.Distance(p, Alcove.Value) - 1.8f);
            }
            if (Glade || Heart)
            {
                // Round the rim by angle (no seam where the angle wraps), plus a little grain.
                var toward = (p - Hub).normalized;
                float wobble = .55f * Noise(toward * 1.8f + new Vector2(5, 5), 1, 8) + .2f * Noise(p, 1.2f, 9);
                float rim = Vector2.Distance(p, Hub) - (Glade ? GladeRadius : HeartRadius) - wobble;
                open = Mathf.Min(open, rim);
            }
            else if (CountOpen() <= 1) open = Mathf.Min(open, Vector2.Distance(p, Hub) - NookRadius);
            else open = Mathf.Min(open, Vector2.Distance(p, Hub) - 2.2f);
            return open;
        }

        /// <summary>The worn middle of a lane, and the ring worn round the great tree.</summary>
        public bool OnTrail(Vector2 p)
        {
            if (IsWall(p)) return false;
            float wobble = .15f * Noise(p, 1.4f, 12);
            if (LaneDistance(p, out _) + wobble < .7f) return true;
            return Heart && Mathf.Abs(Vector2.Distance(p, Hub) - TrunkRadius - 1.9f) + wobble < .55f;
        }

        /// <summary>Inside the great tree's trunk (the heart only).</summary>
        public bool InTrunk(Vector2 p, float margin = 0) => Heart && Vector2.Distance(p, Hub) < TrunkRadius + margin;

        public bool IsWall(Vector2 p) => WallDepth(p) > 0 || InTrunk(p);

        int CountOpen()
        {
            int n = 0;
            foreach (bool o in Open) if (o) n++;
            return n;
        }

        /// <summary>
        /// The wall as rectangles on a half-metre grid (runs along rows merged with equal runs above), for the
        /// barriers: what blocks is exactly what the ground plan calls wall.
        /// </summary>
        public List<Rect> WallRects(float cell = .5f)
        {
            int n = Mathf.RoundToInt(TileTerrain.Size / cell);
            var wall = new bool[n, n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    wall[i, j] = IsWall(new Vector2((i + .5f) * cell, (j + .5f) * cell));
            var rects = new List<Rect>();
            var done = new bool[n, n];
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    if (!wall[i, j] || done[i, j]) continue;
                    int i1 = i;
                    while (i1 + 1 < n && wall[i1 + 1, j] && !done[i1 + 1, j]) i1++;
                    int j1 = j;
                    while (j1 + 1 < n)
                    {
                        bool row = true;
                        for (int k = i; k <= i1 && row; k++) if (!wall[k, j1 + 1] || done[k, j1 + 1]) row = false;
                        if (!row) break;
                        j1++;
                    }
                    for (int a = i; a <= i1; a++) for (int b = j; b <= j1; b++) done[a, b] = true;
                    rects.Add(Rect.MinMaxRect(i * cell, j * cell, (i1 + 1) * cell, (j1 + 1) * cell));
                }
            return rects;
        }
    }
}
