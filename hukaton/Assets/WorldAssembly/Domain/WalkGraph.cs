using System;
using System.Collections.Generic;

namespace Hakaton.WorldAssembly
{
    /// <summary>
    /// Who can get where over a rectangle of chunks: nodes are (chunk, region); edges come from links inside a chunk
    /// and from edge segments that two neighbours open onto. An edge carries what it takes (walk, drop, Ice, Impulse,
    /// Fire); drops are one way.
    /// </summary>
    public sealed class WalkGraph
    {
        public readonly struct Node : IEquatable<Node>
        {
            public readonly ChunkCoord Coord;
            public readonly int Region;
            public Node(ChunkCoord coord, int region) { Coord = coord; Region = region; }
            public bool Equals(Node other) => Coord.Equals(other.Coord) && Region == other.Region;
            public override bool Equals(object obj) => obj is Node other && Equals(other);
            public override int GetHashCode() => Coord.GetHashCode() * 17 + Region;
            public override string ToString() => $"{Coord}#{Region}";
        }

        public readonly struct Edge
        {
            public readonly Node To;
            public readonly Need Need;
            public Edge(Node to, Need need) { To = to; Need = need; }
        }

        readonly Dictionary<Node, List<Edge>> edges = new Dictionary<Node, List<Edge>>();
        public IEnumerable<Node> Nodes => edges.Keys;
        public IReadOnlyList<Edge> From(Node node) => edges.TryGetValue(node, out var list) ? list : (IReadOnlyList<Edge>)Array.Empty<Edge>();

        public WalkGraph(WorldMap map, int minX, int minY, int maxX, int maxY)
        {
            for (int x = minX; x <= maxX; x++)
                for (int y = minY; y <= maxY; y++)
                {
                    var plan = map.Chunk(x, y);
                    for (int r = 0; r < plan.RegionCount; r++) Ensure(new Node(plan.Coord, r));
                    foreach (var link in plan.Shape.Links) AddLink(plan, link);
                    foreach (var link in plan.Variant.Extra) AddLink(plan, link);
                }
            for (int x = minX; x <= maxX; x++)
                for (int y = minY; y <= maxY; y++)
                {
                    var plan = map.Chunk(x, y);
                    foreach (var side in new[] { Side.East, Side.North })
                    {
                        var other = plan.Coord.Step(side);
                        if (other.X > maxX || other.Y > maxY) continue;
                        Join(plan, map.Chunk(other), side);
                    }
                }
        }

        void Ensure(Node node) { if (!edges.ContainsKey(node)) edges[node] = new List<Edge>(); }

        void Add(Node from, Node to, Need need) { Ensure(from); Ensure(to); edges[from].Add(new Edge(to, need)); }

        void AddLink(ChunkPlan plan, Link link)
        {
            var a = new Node(plan.Coord, link.From);
            var b = new Node(plan.Coord, link.To);
            Add(a, b, link.Need);
            if (link.Both) Add(b, a, link.Need);
        }

        /// <summary>Segments of a's side meet b's opposite side in reverse order (both read clockwise).</summary>
        void Join(ChunkPlan a, ChunkPlan b, Side side)
        {
            for (int segment = 0; segment < 3; segment++)
            {
                int ra = a.Port(side, segment), rb = b.Port(side.Opposite(), 2 - segment);
                if (ra < 0 || rb < 0) continue;
                int la = a.AbsoluteLevel(ra), lb = b.AbsoluteLevel(rb);
                if (la != lb) continue; // a cliff foot meeting a plateau top never happens on a matched edge; be safe
                Need gate = Max(a.Gate(side, segment), b.Gate(side.Opposite(), 2 - segment));
                Add(new Node(a.Coord, ra), new Node(b.Coord, rb), gate);
                Add(new Node(b.Coord, rb), new Node(a.Coord, ra), gate);
            }
        }

        static Need Max(Need a, Need b) => a > b ? a : b;

        /// <summary>Nodes reachable from a chunk's region; walkOnly counts walking and dropping only.</summary>
        public HashSet<Node> Reachable(ChunkCoord start, int region, bool walkOnly, Need[] cards = null)
        {
            var seen = new HashSet<Node>();
            var queue = new Queue<Node>();
            var first = new Node(start, region);
            seen.Add(first);
            queue.Enqueue(first);
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                foreach (var edge in From(node))
                {
                    bool ok = edge.Need == Need.Walk || edge.Need == Need.Drop || (!walkOnly && (cards == null || Array.IndexOf(cards, edge.Need) >= 0));
                    if (ok && seen.Add(edge.To)) queue.Enqueue(edge.To);
                }
            }
            return seen;
        }
    }
}
