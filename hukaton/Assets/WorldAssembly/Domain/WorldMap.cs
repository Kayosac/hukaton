using System;
using System.Collections.Generic;

namespace Hakaton.WorldAssembly
{
    /// <summary>Everything the game needs to build one chunk.</summary>
    public sealed class ChunkPlan
    {
        public ChunkCoord Coord;
        public int Ring;
        public TileShape Shape;
        public Variant Variant;
        /// <summary>Quarter turns clockwise from the shape's canonical orientation.</summary>
        public int Rotation;
        /// <summary>0 low, 1 plateau; regions add their own level on top.</summary>
        public int BaseLevel;
        /// <summary>World corner levels clockwise NW, NE, SE, SW.</summary>
        public int[] Corners = new int[4];
        /// <summary>World edge middles by side.</summary>
        public Mid[] Mids = new Mid[4];
        public bool MazeHeart;
        /// <summary>Creature groups living here (see <see cref="CreatureThemes"/>); empty for none.</summary>
        public readonly List<Spawn> Spawns = new List<Spawn>();
        /// <summary>Loot to place: item ids ("find", "find-hard", "crystal").</summary>
        public readonly List<string> Loot = new List<string>();
        /// <summary>Guaranteed reward on the island / maze heart.</summary>
        public string PocketReward;
        /// <summary>A small event on this chunk (<see cref="WorldEvents"/>), or null.</summary>
        public string Event;

        public int RegionCount => Shape.Regions.Length + Variant.ExtraRegions.Length;
        public Region RegionAt(int index) => index < Shape.Regions.Length ? Shape.Regions[index] : Variant.ExtraRegions[index - Shape.Regions.Length];
        public int AbsoluteLevel(int region) => BaseLevel + RegionAt(region).Level;

        /// <summary>Region opening onto world side and segment (segments clockwise), or −1.</summary>
        public int Port(Side worldSide, int segment) => Shape.Ports[((int)worldSide - Rotation) & 3, segment];
        public Need Gate(Side worldSide, int segment) => Shape.Gates[((int)worldSide - Rotation) & 3, segment];

        /// <summary>A cell of a giant tangled forest.</summary>
        public bool Forest => Shape.Kind == ShapeKind.Maze && TileShapes.IsForest(Variant.Id);

        public override string ToString() => $"{Coord} {Shape.Id}/{Variant.Id} r{Rotation} b{BaseLevel}";
    }

    /// <summary>
    /// The endless world of one outing. Chunk plans are pure functions of the seed and the coordinate (districts are
    /// planned on demand and cached), except for the ring 0–1 guarantee: at creation the walk graph of rings 0–1 is
    /// checked and river tiles are switched to bridge / ford variants until ring 0 is walkable and has a way out.
    /// </summary>
    public sealed class WorldMap
    {
        public readonly int Seed;
        public readonly WorldRules Rules;
        public readonly CornerField Corners;
        readonly DistrictPlanner districts;
        readonly Dictionary<(int, int), Dictionary<ChunkCoord, ChunkFeature>> districtCache = new Dictionary<(int, int), Dictionary<ChunkCoord, ChunkFeature>>();
        readonly Dictionary<ChunkCoord, ChunkPlan> plans = new Dictionary<ChunkCoord, ChunkPlan>();
        readonly Dictionary<ChunkCoord, string> overrides = new Dictionary<ChunkCoord, string>();
        public readonly List<string> Diagnostics = new List<string>();

        public WorldMap(int seed, WorldRules rules = null)
        {
            Seed = seed;
            Rules = rules ?? new WorldRules();
            Corners = new CornerField(seed, Rules);
            districts = new DistrictPlanner(seed, Rules, Corners);
            Guarantee();
        }

        public ChunkPlan Chunk(ChunkCoord c)
        {
            if (plans.TryGetValue(c, out var plan)) return plan;
            plan = Build(c);
            plans[c] = plan;
            return plan;
        }

        public ChunkPlan Chunk(int x, int y) => Chunk(new ChunkCoord(x, y));

        public ChunkFeature FeatureAt(ChunkCoord c)
        {
            var key = DistrictPlanner.DistrictOf(c, Rules.District);
            if (!districtCache.TryGetValue(key, out var plan)) districtCache[key] = plan = districts.Plan(key.dx, key.dy);
            return plan.TryGetValue(c, out var f) ? f : null;
        }

        RoadPlanner roads;

        /// <summary>Roads running through chunk <paramref name="c"/> (see <see cref="RoadPlanner"/>).</summary>
        public List<Road> RoadsThrough(ChunkCoord c) => (roads ??= new RoadPlanner(this, Seed, Rules.District)).Through(c);

        public string RoadReport(ChunkCoord f) => (roads ??= new RoadPlanner(this, Seed, Rules.District)).Report(f);

        /// <summary>The reward chunk of every landmark planned in district (dx, dy), with its ground level.</summary>
        public List<(ChunkCoord focus, LandmarkKind kind, int level)> LandmarkFociOf(int dx, int dy)
        {
            var found = new List<(ChunkCoord, LandmarkKind, int)>();
            if (!districtCache.TryGetValue((dx, dy), out var plan)) districtCache[(dx, dy)] = plan = districts.Plan(dx, dy);
            foreach (var pair in plan)
            {
                var f = pair.Value;
                if (f.Feature != Feature.Landmark || f.Landmark == null || !f.Landmark.HasFocus) continue;
                if (f.PartI != f.Landmark.FocusI || f.PartJ != f.Landmark.FocusJ) continue;
                found.Add((pair.Key, f.Landmark, Corners.Level(pair.Key.X, pair.Key.Y)));
            }
            return found;
        }

        /// <summary>
        /// The reward chunk of every landmark within <paramref name="radius"/> m of <paramref name="here"/>, with its
        /// ground level: read from the district plans alone, so far ones cost no chunk plans (markers, lookouts).
        /// </summary>
        public List<(ChunkCoord focus, LandmarkKind kind, int level)> LandmarkFoci(ChunkCoord here, float radius)
        {
            var found = new List<(ChunkCoord, LandmarkKind, int)>();
            int reach = (int)Math.Ceiling(radius / Rules.ChunkSize);
            var from = DistrictPlanner.DistrictOf(new ChunkCoord(here.X - reach, here.Y - reach), Rules.District);
            var to = DistrictPlanner.DistrictOf(new ChunkCoord(here.X + reach, here.Y + reach), Rules.District);
            double r2 = (double)radius * radius;
            for (int dx = from.dx; dx <= to.dx; dx++)
                for (int dy = from.dy; dy <= to.dy; dy++)
                {
                    if (!districtCache.TryGetValue((dx, dy), out var plan)) districtCache[(dx, dy)] = plan = districts.Plan(dx, dy);
                    foreach (var pair in plan)
                    {
                        var f = pair.Value;
                        if (f.Feature != Feature.Landmark || f.Landmark == null || !f.Landmark.HasFocus) continue;
                        if (f.PartI != f.Landmark.FocusI || f.PartJ != f.Landmark.FocusJ) continue;
                        double ex = (pair.Key.X - here.X) * (double)Rules.ChunkSize, ey = (pair.Key.Y - here.Y) * (double)Rules.ChunkSize;
                        if (ex * ex + ey * ey > r2) continue;
                        found.Add((pair.Key, f.Landmark, Corners.Level(pair.Key.X, pair.Key.Y)));
                    }
                }
            return found;
        }

        // ---------- building one chunk ----------

        ChunkPlan Build(ChunkCoord c)
        {
            var plan = new ChunkPlan { Coord = c, Ring = Rules.RingOf(c) };
            Corners.Corners(c, plan.Corners);
            var feature = FeatureAt(c);
            if (feature != null) Array.Copy(feature.Mids, plan.Mids, 4);
            plan.MazeHeart = feature?.MazeHeart ?? false;

            int min = Math.Min(Math.Min(plan.Corners[0], plan.Corners[1]), Math.Min(plan.Corners[2], plan.Corners[3]));
            int max = Math.Max(Math.Max(plan.Corners[0], plan.Corners[1]), Math.Max(plan.Corners[2], plan.Corners[3]));
            plan.BaseLevel = min;
            var relative = new int[4];
            for (int i = 0; i < 4; i++) relative[i] = plan.Corners[i] - min;

            if (WorldRules.IsStartArea(c))
            {
                var (i, j) = Landmarks.StartHill.PartAt(0, c.X + 1, c.Y + 1);
                Set(plan, TileShapes.Get(Landmarks.StartHill.PartId(i, j)), 0);
            }
            else if (feature != null && feature.Feature == Feature.Landmark)
                Set(plan, TileShapes.Get(feature.Landmark.PartId(feature.PartI, feature.PartJ)), feature.LandmarkRotation);
            else if (max > min) MatchCliff(plan, relative);
            else if (feature == null || feature.Feature == Feature.None) Set(plan, TileShapes.Get(min == 0 ? "plain" : "plateau-top"), Hash.Range(Seed, 4, Salt.Rotation, c.X, c.Y));
            else MatchFeature(plan);

            ChooseVariant(plan, feature);
            if (overrides.TryGetValue(c, out var forced)) plan.Variant = plan.Shape.FindVariant(forced);
            Populate(plan);
            return plan;
        }

        static void Set(ChunkPlan plan, TileShape shape, int rotation) { plan.Shape = shape; plan.Rotation = rotation; plan.Variant = shape.Variants[0]; }

        void MatchCliff(ChunkPlan plan, int[] relative)
        {
            foreach (var shape in TileShapes.All)
            {
                if (shape.Kind != ShapeKind.Cliff) continue;
                for (int r = 0; r < 4; r++)
                    if (shape.MatchesCorners(relative, r)) { Set(plan, shape, r); return; }
            }
            throw new InvalidOperationException($"No cliff shape for corners {string.Join(",", plan.Corners)} at {plan.Coord}");
        }

        void MatchFeature(ChunkPlan plan)
        {
            foreach (var shape in TileShapes.All)
            {
                if (shape.Kind != ShapeKind.River && shape.Kind != ShapeKind.Canyon && shape.Kind != ShapeKind.Maze) continue;
                for (int r = 0; r < 4; r++)
                    if (shape.MatchesMids(plan.Mids, r)) { Set(plan, shape, r); return; }
            }
            throw new InvalidOperationException($"No shape for edges {string.Join(",", plan.Mids)} at {plan.Coord}");
        }

        void ChooseVariant(ChunkPlan plan, ChunkFeature feature)
        {
            var c = plan.Coord;
            double roll = Hash.Unit(Seed, Salt.Variant, c.X, c.Y);
            var shape = plan.Shape;
            string id = shape.Variants[0].Id;
            switch (shape.Id)
            {
                case "plain":
                case "plateau-top":
                    id = Scene(plan);
                    break;
                case "river-end":
                    id = roll < .45 ? "lake" : roll < .8 || plan.Ring == 0 ? "spring" : "island";
                    break;
                case "river-straight":
                    if (feature != null && feature.Crossing) id = roll < .6 ? "bridge" : "ford";
                    break;
                case "river-bend":
                    if (feature != null && feature.Crossing) id = "ford";
                    break;
                case "canyon-straight":
                    if (feature != null && feature.Crossing) id = roll < .5 ? "log" : "descent";
                    break;
                case "canyon-bend":
                    if (feature != null && feature.Crossing) id = "descent";
                    break;
                case "cliff-edge":
                case "cliff-corner":
                case "cliff-inner":
                {
                    // Higher up, fewer ramps: the second level mostly, the third almost only by card.
                    double ramp = plan.BaseLevel == 0 ? Rules.RampChance[plan.Ring] : plan.BaseLevel == 1 ? Rules.RampChance[plan.Ring] * Rules.UpperRampFactor : Rules.SummitRampChance;
                    if (shape.Id != "cliff-edge") ramp *= .5;
                    double ledgeRoll = Hash.Unit(Seed, Salt.Ramp, c.X, c.Y);
                    double fallsRoll = Hash.Unit(Seed, Salt.Ramp + 1, c.X, c.Y);
                    id = roll < ramp ? "ramp"
                        : shape.Id == "cliff-edge" && ledgeRoll < Rules.LedgeChance ? "ledge"
                        : shape.Id == "cliff-edge" && fallsRoll < Rules.FallsChance ? "falls"
                        : "cliff";
                    break;
                }
            }
            if (shape.Kind == ShapeKind.Maze && feature != null && feature.Forest)
            {
                // Now and then a hollow trunk across the lane, or deadfall closing a blind alcove with something behind it.
                double kind = Hash.Unit(Seed, Salt.Forest + 3, c.X, c.Y);
                id = plan.MazeHeart ? TileShapes.ForestHeart : feature.Glade ? TileShapes.ForestGlade
                    : kind < Rules.ForestDeadfall ? TileShapes.ForestDeadfall : kind < Rules.ForestDeadfall + Rules.ForestHollow ? TileShapes.ForestHollow
                    : TileShapes.Forest;
            }
            plan.Variant = shape.FindVariant(id);
        }

        /// <summary>
        /// The scene of a flat chunk. Never the same as the chunk to the west or south would take first, so like scenes
        /// do not run in rows; pockets stay out of ring 0, where everything is reached on foot.
        /// </summary>
        string Scene(ChunkPlan plan)
        {
            var c = plan.Coord;
            var ids = plan.Shape.Kind == ShapeKind.Plain ? TileShapes.PlainScenes : TileShapes.TopScenes;
            string First(int x, int y, int salt)
            {
                for (int k = 0; k < 8; k++)
                {
                    string id = ids[Hash.Range(Seed, ids.Length, Salt.Scene + salt, x, y, k)];
                    if (plan.Ring == 0 && TileShapes.IsPocketScene(id)) continue;
                    return id;
                }
                return ids[0];
            }
            string west = First(c.X - 1, c.Y, 0), south = First(c.X, c.Y - 1, 0);
            for (int k = 0; k < 12; k++)
            {
                string id = ids[Hash.Range(Seed, ids.Length, Salt.Scene, c.X, c.Y, k)];
                if (plan.Ring == 0 && TileShapes.IsPocketScene(id)) continue;
                if (k < 11 && (id == west || id == south)) continue;
                return id;
            }
            return ids[0];
        }

        // ---------- creatures and loot ----------

        void Populate(ChunkPlan plan)
        {
            var c = plan.Coord;
            plan.Spawns.Clear();
            plan.Loot.Clear();
            plan.PocketReward = null;
            plan.Event = null;
            if (WorldRules.IsStartArea(c)) return;
            int ring = plan.Ring;
            double creatureRoll = Hash.Unit(Seed, Salt.Creature, c.X, c.Y);
            double pick = Hash.Unit(Seed, Salt.Creature + 1, c.X, c.Y);
            string theme = null;
            double chance = Rules.CreatureChance[ring];
            switch (plan.Shape.Kind)
            {
                case ShapeKind.Plain:
                    theme = CreatureThemes.ForScene(plan.Variant.Id);
                    // Someone always lives at a camp, an apiary or a farm; ruins and hollows are lairs.
                    if (theme == "camp" || theme == "apiary" || theme == "farm") chance = Math.Max(chance, .75);
                    else if (theme == "ruins" || theme == "hollow") chance *= 1.5;
                    break;
                case ShapeKind.Maze:
                    theme = plan.Forest ? "forest" : "maze";
                    chance *= 1.5;
                    // The forest's heart is always guarded.
                    if (plan.Forest && plan.MazeHeart) chance = 1;
                    break;
                case ShapeKind.River: theme = "river"; chance *= .6; break;
                case ShapeKind.Canyon: theme = "canyon"; chance *= 1.4; break;
                case ShapeKind.PlateauTop: theme = plan.BaseLevel >= 2 ? "summit" : "plateau"; chance *= 1.2; break;
                case ShapeKind.Landmark:
                {
                    // The landmark's reward is always guarded; its other parts are as busy as plain ground.
                    var (kind, i, j) = Landmarks.Parse(plan.Shape.Id);
                    theme = kind.Id;
                    if (kind.HasFocus && i == kind.FocusI && j == kind.FocusJ) chance = 1;
                    break;
                }
            }
            if (theme != null && creatureRoll < chance) plan.Spawns.AddRange(CreatureThemes.Pick(theme, ring, pick));

            double findRoll = Hash.Unit(Seed, Salt.Loot, c.X, c.Y);
            switch (plan.Shape.Kind)
            {
                case ShapeKind.PlateauTop:
                    if (findRoll < (plan.BaseLevel >= 2 ? .8 : .6)) plan.Loot.Add(plan.BaseLevel >= 2 ? "crystal" : "find-hard");
                    break;
                case ShapeKind.Cliff:
                    if (plan.Variant.Id == "ledge" && findRoll < .5) plan.Loot.Add("find-hard");
                    break;
                case ShapeKind.Landmark:
                {
                    var (kind, i, j) = Landmarks.Parse(plan.Shape.Id);
                    // A landmark with a card-only pocket keeps its best reward there; the focus then holds a plain find.
                    bool pocket = i == kind.PocketI && j == kind.PocketJ;
                    if (pocket) plan.PocketReward = ring == 2 ? "crystal" : "find-hard";
                    if (kind.HasFocus && i == kind.FocusI && j == kind.FocusJ) plan.Loot.Add(pocket ? "find" : ring == 2 ? "crystal" : "find-hard");
                    else if (findRoll < Rules.SceneFindChance[ring] * .6) plan.Loot.Add("find");
                    break;
                }
                case ShapeKind.Plain:
                    // Every scene has a place for a find; most of them hold one.
                    if (findRoll < Rules.SceneFindChance[ring]) plan.Loot.Add("find");
                    break;
                case ShapeKind.Maze when plan.Variant.Id == TileShapes.ForestGlade:
                    // A glade always holds something; far out, something good.
                    plan.Loot.Add(ring == 2 && findRoll < .5 ? "find-hard" : "find");
                    break;
                default:
                    if (findRoll < Rules.FindChance[ring]) plan.Loot.Add("find");
                    break;
            }
            if (plan.MazeHeart) plan.PocketReward = ring == 2 ? "crystal" : "find-hard";
            if (plan.Variant.Id == "island" || plan.Variant.Id == TileShapes.ForestDeadfall || TileShapes.IsPocketScene(plan.Variant.Id))
                plan.PocketReward = ring == 2 || plan.BaseLevel >= 2 ? "crystal" : "find-hard";
            plan.Event = WorldEvents.Choose(plan, Seed, Rules);
            // The brawl's two packs are the chunk's beasts.
            if (plan.Event == WorldEvents.Brawl) plan.Spawns.Clear();
        }

        // ---------- ring 0–1 guarantee ----------

        /// <summary>Chunks within this many of the start are checked for the guarantee.</summary>
        int GuaranteeRadius => (int)Math.Ceiling(Rules.MiddleRadius / Rules.ChunkSize) + 1;

        void Guarantee()
        {
            int r = GuaranteeRadius;
            for (int pass = 0; pass < 200; pass++)
            {
                var graph = new WalkGraph(this, -r, -r, r, r);
                var reach = graph.Reachable(new ChunkCoord(0, 0), 0, walkOnly: true);
                bool ring0Done = true, exit = false;
                foreach (var node in graph.Nodes)
                {
                    var plan = Chunk(node.Coord);
                    if (plan.RegionAt(node.Region).Pocket) continue;
                    if (plan.Ring == 0 && !reach.Contains(node)) ring0Done = false;
                    if (plan.Ring == 1 && reach.Contains(node)) exit = true;
                }
                if (ring0Done && exit) return;

                // Bridge the first river tile that splits reached ground from unreached ground, nearest to the start.
                ChunkPlan best = null;
                foreach (var node in graph.Nodes)
                {
                    var plan = Chunk(node.Coord);
                    if (plan.Shape.Kind != ShapeKind.River || plan.Variant.Id != "plain") continue;
                    if (plan.Ring > (ring0Done ? 1 : 0)) continue;
                    bool reached = false, unreached = false;
                    for (int region = 0; region < plan.Shape.Regions.Length; region++)
                    {
                        if (reach.Contains(new WalkGraph.Node(plan.Coord, region))) reached = true; else unreached = true;
                    }
                    if (!reached || !unreached) continue;
                    if (best == null || Dist(plan.Coord) < Dist(best.Coord)) best = plan;
                }
                if (best == null) { Diagnostics.Add($"guarantee not met (ring0 {ring0Done}, exit {exit})"); return; }
                string variant = best.Shape.Id == "river-straight" ? "bridge" : "ford";
                overrides[best.Coord] = variant;
                plans.Remove(best.Coord);
                Diagnostics.Add($"bridged {best.Coord} ({variant})");
            }
        }

        static int Dist(ChunkCoord c) => c.X * c.X + c.Y * c.Y;
    }
}
