using System;
using System.Collections.Generic;

namespace Hakaton.WorldAssembly
{
    /// <summary>What it takes to move from one walkable region to another: walking, a one-way drop, or a card (Card: any of several — a rope, an updraft, a teleport, a branch bridge, a stone cube…).</summary>
    public enum Need { Walk = 0, Drop = 1, Ice = 2, Impulse = 3, Fire = 4, Card = 5 }

    public enum ShapeKind { Plain, PlateauTop, Cliff, River, Canyon, Maze, Landmark }

    /// <summary>A walkable part of a chunk at one level. Pockets are the places only cards reach; they always hold a reward.</summary>
    public sealed class Region
    {
        public readonly string Name;
        /// <summary>Height level relative to the chunk's base level (canyon floor: -1).</summary>
        public readonly int Level;
        public readonly bool Pocket;
        public Region(string name, int level, bool pocket = false) { Name = name; Level = level; Pocket = pocket; }
    }

    public readonly struct Link
    {
        public readonly int From, To;
        public readonly Need Need;
        /// <summary>Walk both ways (Drop is always one way: From is the top).</summary>
        public readonly bool Both;
        public Link(int from, int to, Need need, bool both = true) { From = from; To = to; Need = need; Both = both && need != Need.Drop; }
    }

    /// <summary>One way a shape can be built: same edges and regions, different links (a bridge, a ramp, an island).</summary>
    public sealed class Variant
    {
        public readonly string Id;
        public readonly Link[] Extra;
        /// <summary>Extra regions only this variant has (the island in a lake).</summary>
        public readonly Region[] ExtraRegions;
        public Variant(string id, Link[] extra = null, Region[] extraRegions = null)
        {
            Id = id; Extra = extra ?? Array.Empty<Link>(); ExtraRegions = extraRegions ?? Array.Empty<Region>();
        }
    }

    /// <summary>
    /// A canonical chunk shape: its corner pattern and edge middles before rotation, its walkable regions, which
    /// region every edge segment opens onto, and the links between regions. Edges are read clockwise around the
    /// chunk; each edge has three segments: 0 next to the edge's first corner, 1 the middle, 2 next to the second.
    /// Corners clockwise: 0 NW, 1 NE, 2 SE, 3 SW; side s runs from corner s to corner s + 1.
    /// </summary>
    public sealed class TileShape
    {
        public readonly string Id;
        public readonly ShapeKind Kind;
        /// <summary>Corner levels relative to the base, canonical rotation.</summary>
        public readonly int[] Corners;
        public readonly Mid[] Mids;
        public readonly Region[] Regions;
        /// <summary>[side, segment] → region index, or -1 where nothing walkable meets the edge.</summary>
        public readonly int[,] Ports;
        /// <summary>[side, segment] → what crossing this segment takes (Fire for a hedge wall), Walk otherwise.</summary>
        public readonly Need[,] Gates;
        public readonly Link[] Links;
        public readonly Variant[] Variants;

        public TileShape(string id, ShapeKind kind, int[] corners, Mid[] mids, Region[] regions, int[,] ports, Link[] links,
            Variant[] variants, Need[,] gates = null)
        {
            Id = id; Kind = kind; Corners = corners; Mids = mids; Regions = regions; Ports = ports; Links = links;
            Variants = variants; Gates = gates ?? new Need[4, 3];
        }

        public Variant FindVariant(string id)
        {
            foreach (var v in Variants) if (v.Id == id) return v;
            return Variants[0];
        }

        /// <summary>Corner pattern after turning the shape clockwise by quarter turns.</summary>
        public bool MatchesCorners(int[] worldRelative, int rotation)
        {
            for (int c = 0; c < 4; c++) if (worldRelative[(c + rotation) & 3] != Corners[c]) return false;
            return true;
        }

        public bool MatchesMids(Mid[] world, int rotation)
        {
            for (int s = 0; s < 4; s++) if (world[(s + rotation) & 3] != Mids[s]) return false;
            return true;
        }
    }

    /// <summary>The first catalogue: every shape the planner can ask for. The Unity side maps shape + variant ids to prefabs.</summary>
    public static class TileShapes
    {
        public static readonly TileShape[] All;
        static readonly Dictionary<string, TileShape> byId = new Dictionary<string, TileShape>();

        public static TileShape Get(string id) => byId[id];

        static TileShapes()
        {
            var list = new List<TileShape>
            {
                Flat("plain", ShapeKind.Plain, 0, Scenes(PlainScenes)),
                Flat("plateau-top", ShapeKind.PlateauTop, 0, Scenes(TopScenes)),
                Cliff("cliff-corner", new[] { 1, 0, 0, 0 }, true),
                Cliff("cliff-edge", new[] { 1, 1, 0, 0 }, true, ledge: true),
                Cliff("cliff-saddle", new[] { 1, 0, 1, 0 }, false),
                Cliff("cliff-inner", new[] { 1, 1, 1, 0 }, true),
                RiverEnd(), RiverStraight(), RiverBend(),
                CanyonEnd(), CanyonStraight(), CanyonBend(),
                Maze("maze-dead", true, false, false, false),
                Maze("maze-straight", true, false, true, false),
                Maze("maze-bend", true, true, false, false),
                Maze("maze-tee", true, true, true, false),
                Maze("maze-cross", true, true, true, true),
            };
            // Parts of landmarks: flat low ground on every edge, one region open all round.
            foreach (var kind in Landmarks.All)
                for (int i = 0; i < kind.Size; i++)
                    for (int j = 0; j < kind.Size; j++)
                        list.Add(Flat(kind.PartId(i, j), ShapeKind.Landmark, 0, new Variant("part")));
            All = list.ToArray();
            foreach (var shape in All) byId[shape.Id] = shape;
        }

        /// <summary>
        /// Scenes of a flat chunk: every one has something to see and two or three ways through, and all edges stay
        /// connected on foot. "thorns" (a thicket to burn, Fire) and "tower" (a rock pillar to jump onto, Impulse) also
        /// hold a pocket with a reward.
        /// </summary>
        public static readonly string[] PlainScenes = { "gap", "boulders", "knoll", "gully", "ruins", "camp", "farm", "hollow", "grove", "pond", "thorns", "tower", "apiary", "pillar", "yard" };
        public static readonly string[] TopScenes = { "gap", "boulders", "knoll", "ruins", "hollow", "thorns", "tower", "pillar", "yard" };

        /// <summary>Scenes with a pocket only a card reaches.</summary>
        public static bool IsPocketScene(string id) => id == "thorns" || id == "tower" || id == "pillar" || id == "yard";

        static Variant[] Scenes(string[] ids)
        {
            var result = new Variant[ids.Length];
            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i];
                if (id == "thorns") result[i] = new Variant(id, new[] { new Link(0, 1, Need.Fire) }, new[] { new Region("thicket", 0, pocket: true) });
                else if (id == "tower") result[i] = new Variant(id, new[] { new Link(0, 1, Need.Impulse), new Link(1, 0, Need.Drop) }, new[] { new Region("pillar", 0, pocket: true) });
                // Card places: a 6 m pillar (updraft, rope, teleport), a walled yard 2 m high (stone cube, updraft,
                // teleport).
                else if (id == "pillar" || id == "yard")
                    result[i] = new Variant(id, new[] { new Link(0, 1, Need.Card), new Link(1, 0, Need.Drop) }, new[] { new Region(id, 0, pocket: true) });
                else result[i] = new Variant(id);
            }
            return result;
        }

        static readonly Mid[] NoMids = { Mid.None, Mid.None, Mid.None, Mid.None };

        static int[,] AllPorts(int region)
        {
            var p = new int[4, 3];
            for (int s = 0; s < 4; s++) for (int g = 0; g < 3; g++) p[s, g] = region;
            return p;
        }

        static TileShape Flat(string id, ShapeKind kind, int level, params Variant[] variants) =>
            new TileShape(id, kind, new[] { 0, 0, 0, 0 }, NoMids, new[] { new Region("ground", level) }, AllPorts(0), Array.Empty<Link>(), variants);

        /// <summary>Height transition between two levels. Region 0 is low; each separate high part gets its own region.</summary>
        static TileShape Cliff(string id, int[] corners, bool ramp, bool ledge = false)
        {
            var regions = new List<Region> { new Region("low", 0) };
            var cornerRegion = new int[4];
            // Saddle: the two high corners are separate; otherwise all high corners form one region.
            bool saddle = corners[0] == 1 && corners[2] == 1 && corners[1] == 0 && corners[3] == 0;
            int shared = -1;
            for (int c = 0; c < 4; c++)
            {
                if (corners[c] == 0) { cornerRegion[c] = 0; continue; }
                if (saddle || shared < 0)
                {
                    regions.Add(new Region(saddle ? "high " + c : "high", 1));
                    cornerRegion[c] = regions.Count - 1;
                    if (!saddle) shared = cornerRegion[c];
                }
                else cornerRegion[c] = shared;
            }
            var ports = new int[4, 3];
            for (int s = 0; s < 4; s++)
            {
                int a = s, b = (s + 1) & 3;
                ports[s, 0] = cornerRegion[a];
                ports[s, 2] = cornerRegion[b];
                ports[s, 1] = corners[a] == corners[b] ? cornerRegion[a] : -1;
            }
            var links = new List<Link>();
            for (int r = 1; r < regions.Count; r++)
            {
                links.Add(new Link(0, r, Need.Impulse, false));
                links.Add(new Link(r, 0, Need.Drop));
            }
            var variants = new List<Variant> { new Variant("cliff") };
            if (ramp) variants.Add(new Variant("ramp", new[] { new Link(0, 1, Need.Walk) }));
            if (ledge) variants.Add(new Variant("ledge"));
            // Water over the edge into a pool at the foot: same ways as a bare cliff.
            if (ledge) variants.Add(new Variant("falls"));
            return new TileShape(id, ShapeKind.Cliff, corners, NoMids, regions.ToArray(), ports, links.ToArray(), variants.ToArray());
        }

        static TileShape RiverEnd()
        {
            var ports = AllPorts(0);
            ports[0, 1] = -1;
            var island = new Region("island", 0, pocket: true);
            return new TileShape("river-end", ShapeKind.River, new[] { 0, 0, 0, 0 }, new[] { Mid.River, Mid.None, Mid.None, Mid.None },
                new[] { new Region("bank", 0) }, ports, Array.Empty<Link>(), new[]
                {
                    new Variant("lake"), new Variant("spring"),
                    new Variant("island", new[] { new Link(0, 1, Need.Ice), new Link(0, 1, Need.Impulse) }, new[] { island }),
                });
        }

        static TileShape RiverStraight()
        {
            // Water runs north–south: west bank 0, east bank 1.
            var ports = new int[4, 3] { { 0, -1, 1 }, { 1, 1, 1 }, { 1, -1, 0 }, { 0, 0, 0 } };
            return new TileShape("river-straight", ShapeKind.River, new[] { 0, 0, 0, 0 }, new[] { Mid.River, Mid.None, Mid.River, Mid.None },
                new[] { new Region("west bank", 0), new Region("east bank", 0) }, ports,
                new[] { new Link(0, 1, Need.Ice), new Link(0, 1, Need.Impulse) },
                new[] { new Variant("plain"), new Variant("bridge", new[] { new Link(0, 1, Need.Walk) }), new Variant("ford", new[] { new Link(0, 1, Need.Walk) }) });
        }

        static TileShape RiverBend()
        {
            // Water from north to east: inner bank 1 in the north-east corner, outer bank 0.
            var ports = new int[4, 3] { { 0, -1, 1 }, { 1, -1, 0 }, { 0, 0, 0 }, { 0, 0, 0 } };
            return new TileShape("river-bend", ShapeKind.River, new[] { 0, 0, 0, 0 }, new[] { Mid.River, Mid.River, Mid.None, Mid.None },
                new[] { new Region("outer bank", 0), new Region("inner bank", 0) }, ports,
                new[] { new Link(0, 1, Need.Ice), new Link(0, 1, Need.Impulse) },
                new[] { new Variant("plain"), new Variant("ford", new[] { new Link(0, 1, Need.Walk) }) });
        }

        static Link[] CanyonLinks(int floor, params int[] rims)
        {
            var links = new List<Link>();
            foreach (int rim in rims)
            {
                links.Add(new Link(rim, floor, Need.Drop));
                links.Add(new Link(floor, rim, Need.Impulse, false));
            }
            for (int i = 0; i < rims.Length; i++)
                for (int j = i + 1; j < rims.Length; j++)
                    links.Add(new Link(rims[i], rims[j], Need.Impulse));
            return links.ToArray();
        }

        static TileShape CanyonEnd()
        {
            // Rim 0 wraps around the dead end; floor 1 opens north. Always has a way down.
            var ports = AllPorts(0);
            ports[0, 1] = 1;
            return new TileShape("canyon-end", ShapeKind.Canyon, new[] { 0, 0, 0, 0 }, new[] { Mid.Canyon, Mid.None, Mid.None, Mid.None },
                new[] { new Region("rim", 0), new Region("floor", -1) }, ports, CanyonLinks(1, 0),
                new[] { new Variant("descent", new[] { new Link(0, 1, Need.Walk) }) });
        }

        static TileShape CanyonStraight()
        {
            var ports = new int[4, 3] { { 0, 2, 1 }, { 1, 1, 1 }, { 1, 2, 0 }, { 0, 0, 0 } };
            return new TileShape("canyon-straight", ShapeKind.Canyon, new[] { 0, 0, 0, 0 }, new[] { Mid.Canyon, Mid.None, Mid.Canyon, Mid.None },
                new[] { new Region("west rim", 0), new Region("east rim", 0), new Region("floor", -1) }, ports, CanyonLinks(2, 0, 1),
                new[]
                {
                    new Variant("plain"), new Variant("log", new[] { new Link(0, 1, Need.Walk) }),
                    new Variant("descent", new[] { new Link(0, 2, Need.Walk) }),
                });
        }

        static TileShape CanyonBend()
        {
            var ports = new int[4, 3] { { 0, 2, 1 }, { 1, 2, 0 }, { 0, 0, 0 }, { 0, 0, 0 } };
            return new TileShape("canyon-bend", ShapeKind.Canyon, new[] { 0, 0, 0, 0 }, new[] { Mid.Canyon, Mid.Canyon, Mid.None, Mid.None },
                new[] { new Region("outer rim", 0), new Region("inner rim", 0), new Region("floor", -1) }, ports, CanyonLinks(2, 0, 1),
                new[] { new Variant("plain"), new Variant("descent", new[] { new Link(0, 2, Need.Walk) }) });
        }

        /// <summary>A cell of a stone maze: corridor region 0; open sides lead on, walled sides are not crossed.</summary>
        static TileShape Maze(string id, bool n, bool e, bool s, bool w)
        {
            var open = new[] { n, e, s, w };
            var ports = new int[4, 3];
            var gates = new Need[4, 3];
            var mids = new Mid[4];
            for (int side = 0; side < 4; side++)
            {
                ports[side, 0] = -1;
                ports[side, 2] = -1;
                // A walled side is stone: nothing crosses it.
                ports[side, 1] = open[side] ? 0 : -1;
                mids[side] = open[side] ? Mid.MazeOpen : Mid.MazeWall;
            }
            return new TileShape(id, ShapeKind.Maze, new[] { 0, 0, 0, 0 }, mids, new[] { new Region("corridor", 0) }, ports,
                Array.Empty<Link>(), new[] { new Variant("stone") }, gates);
        }
    }
}
