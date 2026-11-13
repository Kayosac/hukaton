using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.WorldAssembly.EditorTools
{
    /// <summary>
    /// Ground of one chunk shape + variant in its canonical orientation: height at any point and what the ground is.
    /// Coordinates: u east, v north, both 0…16 (SW corner at 0, 0); heights relative to the chunk's base level.
    /// Along every edge the height depends only on the two corners and the edge middle, so matched neighbours meet
    /// without seams. Crookedness (noise, jitter) fades out towards the edges.
    /// </summary>
    public sealed class TileTerrain
    {
        public const float Size = 16f;
        public readonly TileShape Shape;
        public readonly string Variant;
        public readonly float Plateau, CanyonDepth;
        public const float WaterLevel = -.3f;

        // River and lake profile.
        public const float ChannelHalf = 2.4f, BankHalf = 3.4f, Deep = 1.9f, FordDeep = .75f;
        public const float LakeRadius = 4.2f, LakeBank = 5.4f, SpringRadius = 2.2f, SpringBank = 3.2f, SpringDeep = .85f;
        public const float IslandRadius = 1.8f, IslandHeight = .5f;
        public const float PondRadius = 2.8f, PondBank = 3.6f, PondDeep = 1.2f;
        public static readonly Vector2 PondCentre = new Vector2(10, 5.5f);
        // Canyon profile.
        public const float FloorHalf = 2.0f, RimHalf = 3.3f;
        // Maze corridor half width.
        public const float Corridor = 2.0f;

        public readonly List<Vector2[]> Centreline = new List<Vector2[]>();
        public readonly List<Ramp> Ramps = new List<Ramp>();
        /// <summary>The scene of a plain or plateau-top chunk, or the whole landmark of a landmark part; null otherwise.</summary>
        public readonly ChunkScene Scene;
        /// <summary>The ground plan of a giant tangled forest's cell (a maze shape's forest variants); null otherwise.</summary>
        public readonly ForestLayout Forest;
        /// <summary>Where this chunk sits in its scene: (0, 0) for one chunk, the part's corner for a landmark part.</summary>
        public readonly Vector2 Offset;
        /// <summary>The land the chunk is built for: the rotten swamp has its own art, water, ground and calmer rolls.</summary>
        public readonly Biome Biome;
        readonly int seed;

        public readonly struct Ramp
        {
            public readonly Vector2 A, B;
            public readonly float HeightA, HeightB, Width;
            public Ramp(Vector2 a, float ha, Vector2 b, float hb, float width) { A = a; HeightA = ha; B = b; HeightB = hb; Width = width; }
        }

        /// <summary>Waterfall of a straight cliff ("falls"): where the water comes over the edge, which way it falls, and the pool at the foot.</summary>
        public readonly Vector2 FallsTop, FallsDown, FallsPool;
        public bool HasFalls => Shape.Kind == ShapeKind.Cliff && Variant == "falls";
        public const float FallsRadius = 1.4f, FallsBank = 2.2f, FallsDeep = .95f;

        public TileTerrain(TileShape shape, string variant, float plateau, float canyonDepth, Biome biome = Biome.Forest)
        {
            Shape = shape; Variant = variant; Plateau = plateau; CanyonDepth = canyonDepth;
            // A swamp scene or landmark is swamp whatever is asked.
            if (TileShapes.IsSwampScene(variant)) biome = Biome.Swamp;
            if (shape.Kind == ShapeKind.Landmark) biome = Landmarks.Parse(shape.Id).kind.Biome;
            Biome = biome;
            seed = (shape.Id + "/" + variant + (biome == Biome.Swamp ? "/swamp" : "")).GetHashCode();
            if (shape.Kind == ShapeKind.Plain || shape.Kind == ShapeKind.PlateauTop) Scene = ChunkScene.Get(variant);
            if (shape.Kind == ShapeKind.Maze && TileShapes.IsForest(variant)) Forest = new ForestLayout(shape.Mids, variant, seed);
            if (shape.Kind == ShapeKind.Landmark)
            {
                // One piece of ground for the whole landmark: same scene and same noise in every part.
                var (kind, i, j) = Landmarks.Parse(shape.Id);
                Scene = ChunkScene.Get(kind.Id);
                Offset = new Vector2(i * Size, j * Size);
                seed = kind.Id.GetHashCode();
                Biome = kind.Biome;
            }
            if (shape.Kind == ShapeKind.River || shape.Kind == ShapeKind.Canyon)
            {
                if (shape.Id.EndsWith("straight")) Centreline.Add(Line(new Vector2(8, 0), new Vector2(8, 16)));
                else if (shape.Id.EndsWith("bend")) Centreline.Add(Arc(new Vector2(16, 16), 8, 180, 270));
                else Centreline.Add(Line(new Vector2(8, 16), new Vector2(8, 8)));
            }
            if (HasFalls)
            {
                // Across the face, 3 m to one side of the middle: find where the crooked edge really is there.
                var c = shape.Corners;
                var up = new Vector2((c[1] - c[0]) + (c[2] - c[3]), (c[0] - c[3]) + (c[1] - c[2])).normalized;
                FallsDown = -up;
                var along = new Vector2(-up.y, up.x);
                var start = new Vector2(8, 8) + along * 3;
                var mid = start;
                for (float s = -6; s <= 6; s += .1f)
                {
                    var q = start + FallsDown * s;
                    if (CliffBase(q) < plateau * .5f) { mid = q; break; }
                }
                FallsTop = mid - FallsDown * 1.1f;
                FallsPool = mid + FallsDown * 2.4f;
            }
            float p = plateau, d = -canyonDepth;
            switch (shape.Id + "/" + variant)
            {
                case "cliff-edge/ramp": Ramps.Add(new Ramp(new Vector2(11.5f, 2.5f), 0, new Vector2(11.5f, 13.5f), p, 3.2f)); break;
                case "cliff-corner/ramp": Ramps.Add(new Ramp(new Vector2(9.5f, 6.5f), 0, new Vector2(2.5f, 13.5f), p, 3.2f)); break;
                case "cliff-inner/ramp": Ramps.Add(new Ramp(new Vector2(2.5f, 2.5f), 0, new Vector2(9.5f, 9.5f), p, 3.2f)); break;
                case "canyon-straight/descent": Ramps.Add(new Ramp(new Vector2(3.4f, 1.6f), 0, new Vector2(6.6f, 13.6f), d, 2.6f)); break;
                case "canyon-bend/descent": Ramps.Add(new Ramp(new Vector2(4.25f, 11.7f), 0, new Vector2(12f, 9.1f), d, 2.6f)); break;
                case "canyon-end/descent": Ramps.Add(new Ramp(new Vector2(8f, .8f), 0, new Vector2(8f, 9.2f), d, 2.6f)); break;
            }
        }

        static Vector2[] Line(Vector2 a, Vector2 b)
        {
            int n = Mathf.CeilToInt(Vector2.Distance(a, b) / .25f);
            var pts = new Vector2[n + 1];
            for (int i = 0; i <= n; i++) pts[i] = Vector2.Lerp(a, b, i / (float)n);
            return pts;
        }

        static Vector2[] Arc(Vector2 centre, float radius, float fromDeg, float toDeg)
        {
            int n = 64;
            var pts = new Vector2[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float a = Mathf.Deg2Rad * Mathf.Lerp(fromDeg, toDeg, i / (float)n);
                pts[i] = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
            }
            return pts;
        }

        public float DistanceToCentre(Vector2 p)
        {
            float best = float.MaxValue;
            foreach (var line in Centreline)
                for (int i = 0; i + 1 < line.Length; i++)
                    best = Mathf.Min(best, SegmentDistance(p, line[i], line[i + 1]));
            return best;
        }

        static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>0 at an edge, 1 from <paramref name="width"/> metres inside.</summary>
        public static float EdgeFade(Vector2 p, float width = 2f) => EdgeFade(p, width, Size);

        public static float EdgeFade(Vector2 p, float width, float size) =>
            Mathf.Clamp01(Mathf.Min(Mathf.Min(p.x, size - p.x), Mathf.Min(p.y, size - p.y)) / width);

        // ---------- the scene, from this chunk's own coordinates ----------

        public Vector2 Big(Vector2 p) => p + Offset;
        public Vector2 Local(Vector2 big) => big - Offset;
        public bool InPart(Vector2 big) => big.x >= Offset.x && big.x < Offset.x + Size && big.y >= Offset.y && big.y < Offset.y + Size;
        public bool SceneTrail(Vector2 p) => Scene != null && Scene.OnTrail(this, p + Offset);
        public bool SceneFloor(Vector2 p) => Scene != null && Scene.OnFloor(p + Offset);
        public bool SceneThicket(Vector2 p) => Scene != null && Scene.InThicket(p + Offset);
        public bool SceneBusy(Vector2 p) => Scene != null && Scene.Busy(p + Offset);

        public float Noise(Vector2 p, float cell, int salt)
        {
            float gx = Mathf.Floor(p.x / cell), gy = Mathf.Floor(p.y / cell);
            float fx = p.x / cell - gx, fy = p.y / cell - gy;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            float V(float x, float y) => (float)Hash.Unit(seed, salt, (int)x, (int)y) * 2 - 1;
            return Mathf.Lerp(Mathf.Lerp(V(gx, gy), V(gx + 1, gy), fx), Mathf.Lerp(V(gx, gy + 1), V(gx + 1, gy + 1), fx), fy);
        }

        /// <summary>Corner level field (0…1) of a cliff, bilinear between the canonical corners NW, NE, SE, SW.</summary>
        float Field(Vector2 p)
        {
            var c = Shape.Corners;
            float x = p.x / Size, y = p.y / Size;
            float south = Mathf.Lerp(c[3], c[2], x), north = Mathf.Lerp(c[0], c[1], x);
            return Mathf.Lerp(south, north, y);
        }

        public bool IsSaddle => Shape.Id == "cliff-saddle";

        /// <summary>The rotten swamp's bed: knee-deep water over it (the water is at <see cref="WaterLevel"/>).</summary>
        public const float SwampBed = -.75f;

        /// <summary>
        /// How much a point of the rotten swamp is under its water (0 dry land, 1 the open swamp): open swamp and its
        /// landmarks are all water; a shore winds between its wet and dry corners, crooked inside, exact at the edges so
        /// neighbours meet. 0 outside the swamp.
        /// </summary>
        public float Wetness(Vector2 p)
        {
            if (!Swampy) return 0;
            if (Shape.Kind != ShapeKind.Shore) return 1;
            float w = Field(p) + Noise(p, 3.4f, 61) * .38f * EdgeFade(p, 2.5f);
            return Mathf.SmoothStep(0, 1, Mathf.Clamp01((w - .3f) / .4f));
        }

        /// <summary>
        /// The swamp's water lies here: a swamp chunk but for a river's channel and the cliffs of its raised terraces,
        /// which are dry ground of the swamp (its moss, its dead trees).
        /// </summary>
        public bool Swampy => Biome == Biome.Swamp && Shape.Kind != ShapeKind.River && Shape.Kind != ShapeKind.Cliff;

        /// <summary>Height without ramps and without crookedness.</summary>
        float Base(Vector2 p)
        {
            float fade = EdgeFade(p, 2.5f);
            if (Swampy)
            {
                // Water everywhere, land in islands: the bed under knee-deep water, islands (the scene's domes) rising out
                // of it, bog windows and deep pools sunk into it.
                float h = SwampBed * Wetness(p);
                // Ragged islands of its own in every piece of swamp, so no two look alike: noise blobs out of the bed,
                // kept off the edges (the neighbour's water meets it there).
                var big = Big(p);
                float blob = Noise(big, 4.6f, 71) * .65f + Noise(big, 2.1f, 72) * .35f;
                h += Mathf.Max(0, blob - .28f) * 2.6f * Wetness(p) * EdgeFade(p, 2.2f) * (Scene != null && Scene.Size > Size ? EdgeFade(big, 2.2f, Scene.Size) : 1);
                if (Scene != null)
                {
                    h += Scene.Relief(this, p + Offset);
                    if (Scene.Pools.Count > 0) h = Mathf.Min(h, Scene.PoolBed(this, p + Offset, Bed));
                }
                return h;
            }
            switch (Shape.Kind)
            {
                case ShapeKind.Cliff:
                {
                    float h = CliffBase(p);
                    if (!HasFalls) return h;
                    float d = Vector2.Distance(p, FallsPool) + Noise(p, 2f, 11) * .25f;
                    return d < FallsBank ? Mathf.Min(h, Bed(d, FallsRadius, FallsBank, FallsDeep)) : h;
                }
                case ShapeKind.River:
                {
                    float d = DistanceToCentre(p) + Noise(p, 2.5f, 8) * .35f * fade;
                    // A ford is shallow in the middle of the chunk; where the river leaves it, it is as deep as any
                    // river, so the beds of neighbours meet.
                    float depth = Variant == "ford" ? Mathf.Lerp(Deep, FordDeep, Mathf.SmoothStep(0, 1, EdgeFade(p, 4f))) : Deep;
                    float h = Bed(d, ChannelHalf, BankHalf, depth);
                    if (Shape.Id == "river-end")
                    {
                        float r = Vector2.Distance(p, new Vector2(8, 8)) + Noise(p, 2f, 9) * .4f * fade;
                        float lake = Variant == "spring" ? Bed(r, SpringRadius, SpringBank, SpringDeep) : Bed(r, LakeRadius, LakeBank, Deep);
                        h = Mathf.Min(h, lake);
                        if (Variant == "island")
                        {
                            float k = Mathf.Clamp01(1 - Vector2.Distance(p, new Vector2(8, 8)) / IslandRadius);
                            h = Mathf.Max(h, Mathf.Lerp(-Deep, IslandHeight, Mathf.SmoothStep(0, 1, k * 1.6f)));
                        }
                    }
                    return h;
                }
                case ShapeKind.Canyon:
                {
                    float d = DistanceToCentre(p) + Noise(p, 2.5f, 10) * .3f * fade;
                    if (d < FloorHalf) return -CanyonDepth;
                    if (d < RimHalf) return -CanyonDepth * (1 - Mathf.SmoothStep(0, 1, (d - FloorHalf) / (RimHalf - FloorHalf)));
                    return 0;
                }
                case ShapeKind.Maze when Forest != null:
                {
                    // Root mounds rise under the tangle; the lanes keep to the ground.
                    float wall = Forest.WallDepth(p);
                    return wall > 0 ? .95f * Mathf.SmoothStep(0, 1, wall / 2.4f) * fade : 0;
                }
                case ShapeKind.Plain when Variant == "pond":
                    return Bed(Vector2.Distance(p, PondCentre) + Noise(p, 2f, 11) * .3f * fade, PondRadius, PondBank, PondDeep);
                default:
                    if (Scene == null) return 0;
                    float relief = Scene.Relief(this, p + Offset);
                    return Scene.Pools.Count > 0 ? Mathf.Min(relief, Scene.PoolBed(this, p + Offset, Bed)) : relief;
            }
        }

        /// <summary>
        /// A crooked edge: big bays and points, small teeth; the face steeper in some places than others. A saddle
        /// stays tamer: its low band across the middle must stay open.
        /// </summary>
        float CliffBase(Vector2 p)
        {
            float fade = EdgeFade(p, 2.5f);
            float field = IsSaddle
                ? Field(p) + (Noise(p, 3f, 7) * .06f - .07f) * fade
                : Field(p) + (Noise(p, 4.2f, 7) * .15f + Noise(p, 1.7f, 17) * .05f) * fade;
            float steep = IsSaddle ? 8f : 8f + 3f * Noise(p, 3f, 19) * fade;
            return Plateau * Mathf.Clamp01((field - .5f) * steep + .5f);
        }

        static float Bed(float d, float channel, float bank, float deep)
        {
            const float lip = .35f;
            if (d < channel) return -(lip + (deep - lip) * (1 - (d / channel) * (d / channel)));
            if (d < bank) return -lip * (1 - Mathf.SmoothStep(0, 1, (d - channel) / (bank - channel)));
            return 0;
        }

        /// <summary>Final height: base, then ramps, then a little crooked noise away from the edges.</summary>
        public float Height(Vector2 p)
        {
            float h = Base(p);
            float onRamp = 0;
            foreach (var ramp in Ramps)
            {
                // A ramp that runs up to an edge (a canyon's descent) gives way to the plain ground there.
                float w = RampWeight(ramp, p, out float t) * EdgeFade(p, 1.2f);
                if (w > 0) h = Mathf.Lerp(h, Mathf.Lerp(ramp.HeightA, ramp.HeightB, t), w);
                onRamp = Mathf.Max(onRamp, w);
            }
            float amplitude = Wet && h < WaterLevel + .4f ? .05f : .18f;
            // Rolling ground everywhere, but water keeps its banks and ramps their run.
            float calm = Mathf.Clamp01((WaterDistance(p) - .4f) / 2.4f) * (1 - onRamp);
            // A canyon's floor and walls stay as cut: the floor is the only way along it.
            if (Shape.Kind == ShapeKind.Canyon) calm *= Mathf.Clamp01((DistanceToCentre(p) - RimHalf - .4f) / 2f);
            // Under a hollow trunk the lane lies level, so the hollow keeps its height all through.
            if (Forest != null && Forest.Hollow)
            {
                // Edges keep their own wave: neighbours meet there.
                float level = Forest.LogLevel(p) * EdgeFade(p, 1.5f);
                calm *= 1 - level;
                amplitude *= 1 - level;
            }
            // Whatever calms the ground inside (a pond, a lake's banks, a ramp), every edge carries the full wave: the
            // neighbour's side has it too, and anything less leaves a step between the two grounds.
            calm = Mathf.Lerp(1, calm, EdgeFade(p, 1.5f));
            return h + Noise(p, 2f, 3) * amplitude * EdgeFade(p) + Undulation(p) * calm;
        }

        /// <summary>Height of the waves every chunk edge carries, m: the same on all edges of all chunks.</summary>
        public const float EdgeWave = .35f;
        /// <summary>Rolling of the ground inside a chunk, m either way.</summary>
        public const float Roll = .85f;

        /// <summary>
        /// The ground's rolls, added to every shape. Along every edge it is the same wave, sin²(π s / 8): zero at the
        /// corners and the middle, a low swell between, symmetric end to end — so any two chunks meet whatever their
        /// turn. Inside, noise rolls take over.
        /// </summary>
        public float Undulation(Vector2 p)
        {
            float fx = Mathf.Clamp01(Mathf.Min(p.x, Size - p.x) / 3), fy = Mathf.Clamp01(Mathf.Min(p.y, Size - p.y) / 3);
            float Wave(float s) { float w = Mathf.Sin(Mathf.PI * s / 8); return w * w; }
            // Under the swamp's water the edges lie almost flat: a wave there would break the water into a grid.
            float edge = EdgeWave * (Wave(p.x) * (1 - fy) + Wave(p.y) * (1 - fx)) * (1 - .85f * Wetness(p));
            // A swamp lies flat: its pools and hummocks are the relief, the ground only sags a little.
            float roll = Swampy ? Roll * .3f : Roll;
            float inner = roll * (Noise(p, 6.5f, 41) * .7f + Noise(p, 3.2f, 42) * .3f) * Mathf.Min(fx, fy);
            return edge + inner;
        }

        /// <summary>How far a point is outside the water's banks, m (large where there is no water).</summary>
        public float WaterDistance(Vector2 p)
        {
            // The swamp's water is everywhere: how high the ground stands over it, as metres of shore.
            if (Swampy) return (Base(p) - WaterLevel) * 4 - .4f;
            if (Shape.Kind == ShapeKind.Plain && Variant == "pond") return Vector2.Distance(p, PondCentre) - PondBank;
            if (Scene != null && Scene.Pools.Count > 0) return Scene.PoolDistance(p + Offset);
            if (HasFalls) return Vector2.Distance(p, FallsPool) - FallsBank;
            if (Shape.Kind != ShapeKind.River) return 99;
            float d = DistanceToCentre(p) - BankHalf;
            if (Shape.Id == "river-end") d = Mathf.Min(d, Vector2.Distance(p, new Vector2(8, 8)) - (Variant == "spring" ? SpringBank : LakeBank));
            return d;
        }

        public float RampWeight(Ramp ramp, Vector2 p, out float t)
        {
            var ab = ramp.B - ramp.A;
            float len = ab.magnitude;
            var dir = ab / len;
            t = Vector2.Dot(p - ramp.A, dir) / len;
            float side = Mathf.Abs(Vector2.Dot(p - ramp.A, new Vector2(-dir.y, dir.x)));
            if (t < -.15f || t > 1.15f) return 0;
            t = Mathf.Clamp01(t);
            return Mathf.Clamp01((ramp.Width / 2 - side) / .9f + .5f);
        }

        public bool OnRamp(Vector2 p)
        {
            foreach (var r in Ramps) if (RampWeight(r, p, out _) > .5f) return true;
            return false;
        }

        /// <summary>Under water (river, lake, spring or pond) here.</summary>
        public bool IsWater(Vector2 p) => Wet && Base(p) < WaterLevel + .02f;

        /// <summary>
        /// The chunk holds water: a river, the pond, or pools of a scene that reach into this part. Water gets its
        /// surface, ice and navigation blocks.
        /// </summary>
        public bool Wet
        {
            get
            {
                if (Swampy || Shape.Kind == ShapeKind.River || (Shape.Kind == ShapeKind.Plain && Variant == "pond") || HasFalls) return true;
                if (Scene == null) return false;
                foreach (var pool in Scene.Pools)
                {
                    var c = pool.Centre - Offset;
                    if (c.x > -pool.Radius && c.x < Size + pool.Radius && c.y > -pool.Radius && c.y < Size + pool.Radius) return true;
                }
                return false;
            }
        }

        /// <summary>Water at least a hand deep here.</summary>
        public bool IsDeep(Vector2 p) => Wet && Base(p) < WaterLevel - .1f;

        /// <summary>In a bog window of the swamp (a pool of mire, <see cref="ChunkScene.Pool.Bog"/>) rather than water.</summary>
        public bool IsBog(Vector2 p) => Scene != null && Scene.InBog(this, p + Offset);

        /// <summary>How far outside the nearest bog window's mire, m (negative inside it; large without one).</summary>
        public float BogDistance(Vector2 p) => Scene == null ? 99 : Scene.BogDistance(this, p + Offset);

        /// <summary>Within <paramref name="margin"/> of the water's own banks.</summary>
        public bool NearWater(Vector2 p, float margin)
        {
            if (Swampy) return WaterDistance(p) < margin;
            if (Shape.Kind == ShapeKind.Plain && Variant == "pond") return Vector2.Distance(p, PondCentre) < PondBank + margin;
            if (Scene != null && Scene.Pools.Count > 0) return Scene.PoolDistance(p + Offset) < margin;
            if (HasFalls) return Vector2.Distance(p, FallsPool) < FallsBank + margin;
            if (Shape.Kind != ShapeKind.River) return false;
            if (DistanceToCentre(p) < BankHalf + margin) return true;
            if (Shape.Id != "river-end") return false;
            float bank = Variant == "spring" ? SpringBank : LakeBank;
            return Vector2.Distance(p, new Vector2(8, 8)) < bank + margin;
        }

        public bool InMazeWall(Vector2 p)
        {
            if (Shape.Kind != ShapeKind.Maze) return false;
            if (Forest != null) return Forest.IsWall(p);
            bool inX = Mathf.Abs(p.x - 8) < Corridor, inY = Mathf.Abs(p.y - 8) < Corridor;
            if (inX && inY) return false;
            if (inX) return !(p.y > 8 ? Shape.Mids[0] == Mid.MazeOpen : Shape.Mids[2] == Mid.MazeOpen);
            if (inY) return !(p.x > 8 ? Shape.Mids[1] == Mid.MazeOpen : Shape.Mids[3] == Mid.MazeOpen);
            return true;
        }

        /// <summary>Which canonical region a dry point belongs to, by the shape's own logic (for sockets).</summary>
        public int RegionAt(Vector2 p)
        {
            float h = Height(p);
            switch (Shape.Kind)
            {
                case ShapeKind.Cliff:
                    if (h < Plateau * .5f) return 0;
                    if (!IsSaddle) return 1;
                    return Vector2.Distance(p, new Vector2(0, 16)) < Vector2.Distance(p, new Vector2(16, 0)) ? 1 : 2;
                case ShapeKind.Canyon:
                    if (h < -CanyonDepth * .7f) return Shape.Regions.Length - 1;
                    if (Shape.Id == "canyon-end") return 0;
                    if (Shape.Id == "canyon-straight") return p.x < 8 ? 0 : 1;
                    return Vector2.Distance(p, new Vector2(16, 16)) < 8 ? 1 : 0;
                case ShapeKind.River:
                    if (Shape.Id == "river-straight") return p.x < 8 ? 0 : 1;
                    if (Shape.Id == "river-bend") return Vector2.Distance(p, new Vector2(16, 16)) < 8 ? 1 : 0;
                    return Variant == "island" && Vector2.Distance(p, new Vector2(8, 8)) < IslandRadius ? 1 : 0;
                case ShapeKind.Plain:
                case ShapeKind.PlateauTop:
                    return Scene?.Pocket != null && Vector2.Distance(p + Offset, Scene.Pocket.Value) < 1.6f ? 1 : 0;
                default:
                    return 0;
            }
        }
    }
}
