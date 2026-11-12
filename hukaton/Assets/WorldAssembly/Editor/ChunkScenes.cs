using System;
using System.Collections.Generic;
using System.Linq;
using Hakaton.Expeditions;
using UnityEngine;

namespace Hakaton.WorldAssembly.EditorTools
{
    /// <summary>
    /// Scenes of flat chunks (plain ground and plateau tops, Kayospace «Открытый мир v2»: на каждом чанке что-то
    /// интересное): relief — rock ridges with gaps, gullies, knolls, hollows, pillars — worn trails, ForestArt set
    /// pieces, and the spots where finds, creatures and pockets go. Canonical chunk coordinates 0…16 (see
    /// <see cref="TileTerrain"/>). Every scene keeps all twelve edge segments joined on foot with two or three ways
    /// through; the builder checks that on the baked navigation.
    /// </summary>
    public sealed class ChunkScene
    {
        public readonly string Id;
        /// <summary>Side of the scene, m: 16 for one chunk, more for a landmark cut into parts.</summary>
        public float Size = 16;

        public struct Block { public Rect Area; public float Height, Run; }
        public struct Ridge { public Vector2[] Line; public float Height, Half, Run, Taper; }
        public struct Bump { public Vector2 Centre; public float Radius, Height, Run; }
        public struct Dome { public Vector2 Centre; public float Radius, Height; }
        public struct Pass { public Vector2 Centre; public float Radius, Run; }
        public struct Prop { public string Asset; public Vector2 At; public float Yaw; public Vector3 Scale; public float Lift; public bool NoCollider; }
        /// <summary>A sloped strip from A at HeightA to B at HeightB that fills the ground up to it (never cuts it).</summary>
        public struct Ramp { public Vector2 A, B; public float HeightA, HeightB, Half, Run; }
        /// <summary>Open water: a bed sunk below the water level, Deep at the middle, its banks at Bank.</summary>
        public struct Pool { public Vector2 Centre; public float Radius, Bank, Deep, Island; }

        /// <summary>Raised (Height &gt; 0) or sunk (Height &lt; 0) lines with steep sides.</summary>
        public readonly List<Ridge> Ridges = new List<Ridge>();
        /// <summary>Steep round rocks: boulders that are part of the ground, the pillar of a tower.</summary>
        public readonly List<Bump> Bumps = new List<Bump>();
        /// <summary>Gentle rises and hollows, walkable everywhere.</summary>
        public readonly List<Dome> Domes = new List<Dome>();
        /// <summary>Raised squares with steep sides (the mound of a fort); passes cut ramps into them.</summary>
        public readonly List<Block> Blocks = new List<Block>();
        /// <summary>Where ridges, gullies and blocks give way: passes, land bridges, ramps.</summary>
        public readonly List<Pass> Gaps = new List<Pass>();
        public readonly List<Vector2[]> Trails = new List<Vector2[]>();
        /// <summary>Paved ground (the floor of a ruin).</summary>
        public readonly List<Rect> Floors = new List<Rect>();
        public readonly List<Prop> Props = new List<Prop>();
        /// <summary>Rock dressing along ridges: (ridge index, spacing).</summary>
        public readonly List<(int ridge, float spacing)> RidgeRocks = new List<(int, float)>();
        /// <summary>Logs to walk on: a whole trunk across a gully with a flat walkway on top (centre, yaw, length).</summary>
        public readonly List<(Vector2 at, float yaw, float length)> Walkways = new List<(Vector2, float, float)>();
        /// <summary>Ramps winding up steps of rock (a table mountain) or down into a pit.</summary>
        public readonly List<Ramp> Ramps = new List<Ramp>();
        /// <summary>Pools of water; each stays inside one part of a landmark.</summary>
        public readonly List<Pool> Pools = new List<Pool>();
        /// <summary>Plank decks on posts over water and mud: (line, deck height over the base).</summary>
        public readonly List<(Vector2[] line, float deck)> Boardwalks = new List<(Vector2[], float)>();
        /// <summary>Places that sound: spot, cue ("campfire", "fire-small", "bubbles", "hum"), height over the ground.</summary>
        public readonly List<(Vector2 at, string cue, float lift)> Sounds = new List<(Vector2, string, float)>();
        /// <summary>Lookouts: standing here shows far landmarks out to this many metres.</summary>
        public readonly List<(Vector2 at, float radius)> Lookouts = new List<(Vector2, float)>();
        public Vector2[] Finds = Array.Empty<Vector2>();
        public Vector2[] Creatures = Array.Empty<Vector2>();
        /// <summary>Card-only pocket: the reward's spot and the thicket around it or the pillar under it.</summary>
        public Vector2? Pocket;
        public Rect? Thicket;
        /// <summary>Plants scattered over the rest: grass, ferns, trees.</summary>
        public int Grass = 10, Ferns, Trees = 3, Bushes = 6;

        ChunkScene(string id) { Id = id; }

        static Vector2 V(float x, float y) => new Vector2(x, y);

        Prop Put(string asset, float x, float y, float yaw = 0, float scale = 1, float lift = 0)
        {
            var p = new Prop { Asset = asset, At = V(x, y), Yaw = yaw, Scale = Vector3.one * scale, Lift = lift };
            Props.Add(p);
            return p;
        }

        void PutScaled(string asset, float x, float y, float yaw, Vector3 scale) =>
            Props.Add(new Prop { Asset = asset, At = V(x, y), Yaw = yaw, Scale = scale });

        void Deco(string asset, float x, float y, float yaw = 0, float scale = 1, float lift = 0) =>
            Props.Add(new Prop { Asset = asset, At = V(x, y), Yaw = yaw, Scale = Vector3.one * scale, NoCollider = true, Lift = lift });

        /// <summary>A ramp 2 m wide (full height over the middle 2 m, gone 2.5 m out): set its middle 0.6 m off the face it climbs.</summary>
        void Slope(Vector2 a, float heightA, Vector2 b, float heightB, float half = 1.5f) =>
            Ramps.Add(new Ramp { A = a, B = b, HeightA = heightA, HeightB = heightB, Half = half, Run = .5f });

        /// <summary>A campfire: flame, a wisp of smoke and its crackle.</summary>
        void Fire(float x, float y, float scale = 1)
        {
            Deco("VFX/Fire/Torch/FireTorch", x, y, 0, .7f * scale, .05f);
            Deco("VFX/Smoke/Grey/SmokeTinyGrey", x, y, 0, scale, 1.1f);
            Sounds.Add((V(x, y), "campfire", .4f));
        }

        // ---------- the scenes ----------

        static readonly Dictionary<string, ChunkScene> all = new Dictionary<string, ChunkScene>();

        public static ChunkScene Get(string id)
        {
            if (all.TryGetValue(id, out var s)) return s;
            s = new ChunkScene(id);
            switch (id)
            {
                case "gap": s.Gap(); break;
                case "boulders": s.Boulders(); break;
                case "knoll": s.Knoll(); break;
                case "gully": s.Gully(); break;
                case "ruins": s.Ruins(); break;
                case "camp": s.Camp(); break;
                case "farm": s.Farm(); break;
                case "hollow": s.Hollow(); break;
                case "grove": s.Grove(); break;
                case "pond": s.Pond(); break;
                case "thorns": s.Thorns(); break;
                case "tower": s.Tower(); break;
                case "start-hill": s.StartHill(); break;
                case "watchhill": s.Watchhill(); break;
                case "crater": s.Crater(); break;
                case "fort": s.Fort(); break;
                case "apiary": s.Apiary(); break;
                case "pillar": s.Pillar(); break;
                case "yard": s.Yard(); break;
                case "beeyard": s.Beeyard(); break;
                case "village": s.Village(); break;
                case "stone-forest": s.StoneForest(); break;
                case "giant-log": s.GiantLog(); break;
                case "mesa": s.Mesa(); break;
                case "sinkhole": s.Sinkhole(); break;
                case "swamp": s.Swamp(); break;
                case "lake": s.Lake(); break;
            }
            all[id] = s;
            return s;
        }

        /// <summary>A rock ridge across the chunk with a pass in the middle and a spur: through the pass or round either end.</summary>
        void Gap()
        {
            Ridges.Add(new Ridge { Line = new[] { V(3, 4.6f), V(6.2f, 7), V(9.2f, 8.6f), V(13, 12.2f) }, Height = 3.1f, Half = .95f, Run = .55f });
            Ridges.Add(new Ridge { Line = new[] { V(10.3f, 3.2f), V(12.6f, 5.4f) }, Height = 2f, Half = .7f, Run = .5f });
            Gaps.Add(new Pass { Centre = V(7.7f, 7.8f), Radius = 1.15f, Run = .6f });
            RidgeRocks.Add((0, 1.7f));
            RidgeRocks.Add((1, 1.6f));
            Trails.Add(new[] { V(1, 13), V(4.6f, 10.4f), V(7.7f, 7.8f), V(9.4f, 5.6f), V(8.6f, 1) });
            Put("Trees/FA_TR04_Leaning", 3.2f, 3.4f, 30);
            Put("Markers/FA_MK05_StoneMarkTall", 8.6f, 7.1f, 20, .9f);
            Finds = new[] { V(11.2f, 7.6f), V(4.4f, 11.8f) };
            Creatures = new[] { V(5, 12.4f), V(12.4f, 8.2f) };
            Grass = 12;
        }

        /// <summary>Boulders as big as a shed with lanes between them: a little maze of stone.</summary>
        void Boulders()
        {
            Bumps.Add(new Bump { Centre = V(5, 5.2f), Radius = 1.3f, Height = 1.9f, Run = .45f });
            Bumps.Add(new Bump { Centre = V(10.6f, 4.4f), Radius = 1.1f, Height = 1.6f, Run = .45f });
            Bumps.Add(new Bump { Centre = V(8.2f, 8.9f), Radius = 1.5f, Height = 2.3f, Run = .5f });
            Bumps.Add(new Bump { Centre = V(4.6f, 11.3f), Radius = 1.2f, Height = 1.7f, Run = .45f });
            Bumps.Add(new Bump { Centre = V(11.6f, 11.4f), Radius = 1.4f, Height = 2.1f, Run = .45f });
            Bumps.Add(new Bump { Centre = V(13, 7.6f), Radius = .8f, Height = 1.2f, Run = .4f });
            Bumps.Add(new Bump { Centre = V(7, 13.2f), Radius = .8f, Height = 1.2f, Run = .4f });
            foreach (var b in Bumps)
                Put(b.Radius > 1.2f ? "Rocks/FA_RK08_RocksTallGroup" : "Rocks/FA_RK06_BoulderSplit", b.Centre.x, b.Centre.y, b.Centre.x * 47, b.Radius * .9f, -.4f);
            Deco("Decals/FA_D06_MossLong", 8.2f, 8.9f, 0, 1.4f);
            Finds = new[] { V(7, 6.6f), V(9.6f, 12.8f) };
            Creatures = new[] { V(9.6f, 12.6f), V(3, 8) };
            Grass = 10; Ferns = 4;
        }

        /// <summary>A grassy knoll with a rock lip on one side and a lookout tree on top.</summary>
        void Knoll()
        {
            Domes.Add(new Dome { Centre = V(8.4f, 8), Radius = 5.6f, Height = 2.4f });
            Ridges.Add(new Ridge { Line = new[] { V(4.4f, 10.2f), V(6.4f, 12.2f), V(9.6f, 12.9f) }, Height = 1.5f, Half = .6f, Run = .45f });
            RidgeRocks.Add((0, 1.5f));
            Put("Trees/FA_TR14_KnottyGiant", 9.8f, 6.8f, 120);
            Put("Structures/FA_ST12_Bench", 7.6f, 9, 200);
            Put("Markers/FA_MK06_StoneMarkGroup", 6.4f, 6.2f, 40);
            Lookouts.Add((V(8.4f, 8), 400));
            Trails.Add(new[] { V(15, 3), V(11.6f, 4.6f), V(8.4f, 8), V(4, 6.4f), V(1, 5.6f) });
            Finds = new[] { V(8.2f, 7.2f), V(13, 12.4f) };
            Creatures = new[] { V(12.6f, 3.6f), V(3.2f, 3.4f) };
            Grass = 14; Ferns = 2;
        }

        /// <summary>A dry gully across the chunk: walk its floor, cross it by a log or over its land bridge.</summary>
        void Gully()
        {
            Ridges.Add(new Ridge { Line = new[] { V(2.2f, 5.5f), V(5.5f, 7.8f), V(9, 8.4f), V(12, 7), V(13.8f, 8.6f) }, Height = -1.8f, Half = 1.7f, Run = .6f, Taper = 3f });
            Gaps.Add(new Pass { Centre = V(9.2f, 8.3f), Radius = .4f, Run = 2.6f });
            // The log across: yaw follows the gully's normal at (5.5, 7.8).
            Props.Add(new Prop { Asset = "Deadwood/FA_DW01_LogWhole", At = V(5.4f, 7.7f), Yaw = -55, Scale = new Vector3(1.2f, 1.1f, 1.7f), Lift = 1.65f });
            Put("Deadwood/FA_DW05_StumpLow", 3.4f, 9.4f, 10);
            Put("Deadwood/FA_DW08_StumpUprooted", 12.6f, 4.6f, 70);
            Deco("Decals/FA_D18_PawTracks", 11.4f, 7.3f, 60);
            Trails.Add(new[] { V(4, 15), V(4.6f, 10), V(5.4f, 7.7f), V(6.2f, 4.4f), V(6, 1) });
            Finds = new[] { V(6.6f, 8.1f), V(12.4f, 11.6f) };
            Creatures = new[] { V(12, 7.1f), V(10, 12.4f) };
            Grass = 10; Ferns = 5;
        }

        /// <summary>A roofless ruin: in through the doorway or the fallen wall, or round it past the graves.</summary>
        void Ruins()
        {
            var wall = new Vector3(1, 1.7f, 1.3f);
            // South: two pieces and a doorway.
            PutScaled("Structures/FA_ST14_StoneWallRuin", 5.85f, 4.8f, 0, wall);
            PutScaled("Structures/FA_ST14_StoneWallRuin", 10.15f, 4.8f, 180, wall);
            // West and east: closed.
            foreach (float y in new[] { 5.9f, 8, 10.1f })
            {
                PutScaled("Structures/FA_ST14_StoneWallRuin", 4.8f, y, 90, wall);
                PutScaled("Structures/FA_ST14_StoneWallRuin", 11.2f, y, 270, wall);
            }
            // North: half fallen.
            PutScaled("Structures/FA_ST14_StoneWallRuin", 6.1f, 11.2f, 180, wall);
            Put("Rocks/FA_RK07_RocksLowGroup", 9.6f, 11.6f, 30, .9f);
            Floors.Add(Rect.MinMaxRect(5.1f, 5.1f, 10.9f, 10.9f));
            Put("Props/FA_PR05_CandlesDish", 9.4f, 9.6f, 0);
            Put("Props/FA_PR13_CrateClosed", 6, 9.6f, 15);
            Put("Props/FA_PR02_LanternGround", 7.2f, 6.2f, 0);
            Deco("Plants/FA_VG14_IvyLong", 4.8f, 8, 90);
            Put("Special/FA_SP06_GravestoneMedium", 13.4f, 5.4f, 260);
            Put("Special/FA_SP05_GravestoneSmall", 13.6f, 8.2f, 275);
            Put("Special/FA_SP07_GravestoneLarge", 13.3f, 11, 265);
            PutScaled("Structures/FA_ST14_StoneWallRuin", 3, 13.2f, 70, new Vector3(.8f, 1.2f, 1.1f));
            Trails.Add(new[] { V(8, 1), V(8, 4.8f), V(8.4f, 8), V(9.2f, 11.4f), V(10, 15) });
            Finds = new[] { V(8.2f, 9.2f), V(13.6f, 13.6f) };
            Creatures = new[] { V(7.4f, 7.6f), V(2.4f, 3.4f) };
            Grass = 9; Ferns = 3;
        }

        /// <summary>Someone's camp: a shelter, seats round a fire ring, crates, a fence with a wide gate and a sign at the fork.</summary>
        void Camp()
        {
            Put("Structures/FA_ST13_Shelter", 8.6f, 10.4f, 180);
            Put("Markers/FA_MK16_StoneCircle", 8, 7, 0, .6f);
            Deco("Deadwood/FA_DW10_SticksLarge", 8, 7, 30, .8f);
            Fire(8, 7);
            PutScaled("Deadwood/FA_DW01_LogWhole", 5.9f, 7, 0, new Vector3(.8f, .8f, .7f));
            PutScaled("Deadwood/FA_DW01_LogWhole", 10.1f, 6.8f, 10, new Vector3(.8f, .8f, .7f));
            Put("Props/FA_PR13_CrateClosed", 11.4f, 10.8f, 20);
            Put("Props/FA_PR14_CrateOpen", 12, 9.6f, 70);
            Put("Props/FA_PR12_SackLarge", 6.2f, 10.6f, 0);
            Put("Props/FA_PR02_LanternGround", 6.6f, 8.8f, 0);
            Put("Props/FA_PR16_HatchetRusty", 9.2f, 6.2f, 40);
            Put("Structures/FA_ST01_FenceSection", 3.6f, 4.3f, 90);
            Put("Structures/FA_ST01_FenceSection", 3.6f, 10.6f, 90);
            Put("Structures/FA_ST01_FenceSection", 3.6f, 12.7f, 90);
            Put("Markers/FA_MK02_SignFork", 2.4f, 7.6f, 90);
            Trails.Add(new[] { V(1, 8), V(5.2f, 8), V(8, 8.2f), V(11.2f, 7.6f), V(15, 6) });
            Trails.Add(new[] { V(8, 8.2f), V(8.4f, 4), V(9, 1) });
            Finds = new[] { V(12.4f, 8.4f), V(2.2f, 13.4f) };
            Creatures = new[] { V(8, 8.6f), V(10.4f, 8.8f) };
            Grass = 10;
        }

        /// <summary>An abandoned farmstead: a fence across with three ways through, a shed, a wagon, a scarecrow, hives.</summary>
        void Farm()
        {
            foreach (float x in new[] { 3.6f, 5.7f, 10.3f })
                Put("Structures/FA_ST01_FenceSection", x, 7.6f, 0);
            Put("Structures/FA_ST02_FenceBroken", 12.4f, 7.6f, 0);
            Put("Structures/FA_ST05_PostCrooked", 6.9f, 7.6f, 0, 1.2f);
            Put("Structures/FA_ST05_PostCrooked", 9.1f, 7.6f, 0, 1.2f);
            Put("Special/FA_SP10_BoardedShed", 11.2f, 11.8f, 180);
            Put("Special/FA_SP11_BrokenWagon", 5, 10.8f, 30);
            Put("Special/FA_SP09_Scarecrow", 6.4f, 4.6f, 160);
            Put("Special/FA_SP04_Beehive", 12.8f, 4.6f, 200);
            Put("Props/FA_PR09_PotLow", 9.4f, 10.6f, 0);
            Deco("Decals/FA_D17_HoofTracks", 8, 5.6f, 0);
            Trails.Add(new[] { V(8, 1), V(8, 7.6f), V(8.6f, 10), V(8.4f, 15) });
            Finds = new[] { V(4.2f, 12.6f), V(13.8f, 2.6f) };
            Creatures = new[] { V(8.6f, 12.8f), V(4, 3.4f) };
            Grass = 14;
        }

        /// <summary>A bowl in the ground with a rock lip, a hollow log and webs: something lies at the bottom.</summary>
        void Hollow()
        {
            Domes.Add(new Dome { Centre = V(8, 8), Radius = 5.2f, Height = -2.4f });
            Ridges.Add(new Ridge { Line = new[] { V(9.4f, 3.4f), V(12, 4.4f), V(13, 7) }, Height = 1.4f, Half = .55f, Run = .4f });
            RidgeRocks.Add((0, 1.4f));
            Put("Deadwood/FA_DW03_LogHollow", 8.8f, 8.6f, 40);
            Deco("Special/FA_SP02_WebOrb", 9.6f, 9.2f, 40);
            Deco("Plants/FA_VG16_ShelfFungusGroup", 8.2f, 8.2f, 220);
            Put("Trees/FA_TR10_DeadBare", 4, 11.6f, 80);
            Put("Trees/FA_TR11_Snapped", 12.2f, 12, 10);
            Finds = new[] { V(7.2f, 7.2f), V(3, 3) };
            Creatures = new[] { V(9, 6.6f), V(12.6f, 9.6f) };
            Grass = 10; Ferns = 6;
        }

        /// <summary>A ring of old trees round a clearing with three ways in, one under an arch.</summary>
        void Grove()
        {
            var openings = new[] { 30f, 150f, 270f };
            string[] trees = { "FA_TR01_ThickCrooked", "FA_TR12_Split", "FA_TR02_TallNarrow", "FA_TR06_Hollow", "FA_TR03_DoubleFork", "FA_TR05_Zigzag" };
            int k = 0;
            for (float a = 0; a < 360; a += 30)
            {
                if (openings.Any(o => Mathf.Abs(Mathf.DeltaAngle(a, o)) < 16)) continue;
                var p = V(8, 8) + new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad)) * 4.4f;
                Put("Trees/" + trees[k++ % trees.Length], p.x, p.y, a * 3, .9f);
            }
            Put("Trees/FA_TR13_Arch", 8, 3.6f, 90);
            Put("Markers/FA_MK06_StoneMarkGroup", 8.6f, 8.8f, 0, .8f);
            Deco("Markers/FA_MK10_CharmTwigDiamond", 7.2f, 7.4f, 0);
            Trails.Add(new[] { V(8, 1), V(8, 8), V(11.8f, 10.2f), V(15, 12) });
            Trails.Add(new[] { V(8, 8), V(4.2f, 10.2f), V(1, 12) });
            Finds = new[] { V(7.6f, 8.4f), V(13.4f, 2.8f) };
            Creatures = new[] { V(8.4f, 7.2f), V(2.6f, 3) };
            Grass = 8; Ferns = 10; Trees = 2;
        }

        /// <summary>A pond with a pier, reeds and a bench.</summary>
        void Pond()
        {
            Put("Structures/FA_ST12_Bench", 5.4f, 8.6f, 60);
            Put("Deadwood/FA_DW04_LogRotten", 12.6f, 10, 20);
            Put("Props/FA_PR02_LanternGround", 6.2f, 3.6f, 0);
            Deco("Plants/FA_VG05_GrassTall", 7.6f, 4, 0, 1.3f);
            Deco("Plants/FA_VG05_GrassTall", 12.6f, 3.2f, 90, 1.2f);
            Deco("Plants/FA_VG05_GrassTall", 12.4f, 7.6f, 30, 1.4f);
            // A spring stirs the middle of the pond (see TileTerrain.PondCentre).
            Deco("VFX/Bubbles/Stream/WaterBubbleStream", 10.4f, 5.2f, 0, .8f, 0);
            Sounds.Add((V(10.4f, 5.2f), "bubbles", 0));
            Trails.Add(new[] { V(1, 10), V(5.6f, 9.6f), V(9, 10), V(15, 12.4f) });
            Finds = new[] { V(6.4f, 6.4f), V(3.6f, 13.4f) };
            Creatures = new[] { V(9.6f, 11.6f), V(14, 13.6f) };
            Grass = 12; Trees = 3;
        }

        /// <summary>A knot of thorns round something worth burning them for; a flag on a pole marks it from afar.</summary>
        void Thorns()
        {
            Thicket = Rect.MinMaxRect(5.9f, 5.9f, 10.1f, 10.1f);
            Pocket = V(8, 8);
            PutScaled("Structures/FA_ST04_Post", 8.6f, 8.6f, 0, new Vector3(1, 3.2f, 1));
            Put("Markers/FA_MK08_ClothLong", 8.6f, 8.6f, 0, 1.2f, 3.6f);
            Put("Rocks/FA_RK03_BoulderLarge", 3.2f, 12.6f, 40);
            Put("Trees/FA_TR09_YoungFork", 12.8f, 3.4f, 0);
            Put("Deadwood/FA_DW06_StumpTall", 3.4f, 3.6f, 0);
            Trails.Add(new[] { V(1, 8), V(4.4f, 8), V(8, 4.4f), V(11.6f, 8), V(15, 8) });
            Trails.Add(new[] { V(8, 1), V(8, 4.4f) });
            Finds = new[] { V(12.8f, 12.6f) };
            Creatures = new[] { V(11.8f, 11.2f), V(3.4f, 9.4f) };
            Grass = 10; Ferns = 3;
        }

        /// <summary>A rock pillar too steep to climb with something on top: Impulse takes you up.</summary>
        void Tower()
        {
            Bumps.Add(new Bump { Centre = V(8, 8), Radius = 1.7f, Height = 2.7f, Run = .35f });
            Bumps.Add(new Bump { Centre = V(4.2f, 11.6f), Radius = .9f, Height = 1.3f, Run = .4f });
            Bumps.Add(new Bump { Centre = V(12, 4.4f), Radius = .8f, Height = 1.1f, Run = .4f });
            Pocket = V(8, 8);
            Lookouts.Add((V(8, 8), 500));
            Put("Markers/FA_MK09_ClothPennants", 8.6f, 7.4f, 30, 1, 0);
            Put("Trees/FA_TR10_DeadBare", 11.6f, 11.8f, 200);
            Put("Rocks/FA_RK06_BoulderSplit", 4.2f, 11.6f, 10, .8f, -.3f);
            Put("Rocks/FA_RK02_BoulderMedium", 12, 4.4f, 70, .9f, -.3f);
            Deco("Decals/FA_D16_HookMark", 6.4f, 8, 90);
            Trails.Add(new[] { V(1, 4), V(5, 5.4f), V(8, 5.4f), V(11, 8), V(15, 10) });
            Finds = new[] { V(3.4f, 3.4f) };
            Creatures = new[] { V(10.8f, 10.2f), V(4.8f, 8.4f) };
            Grass = 10; Ferns = 2;
        }

        // ---------- landmarks (several chunks; parts are cut along every 16 m) ----------

        /// <summary>Within <paramref name="margin"/> of a cut between parts: big things with colliders stay off it.</summary>
        static bool NearSeam(Vector2 p, float margin)
        {
            float x = Mathf.Repeat(p.x, 16), y = Mathf.Repeat(p.y, 16);
            return x < margin || x > 16 - margin || y < margin || y > 16 - margin;
        }

        /// <summary>Near the middle of an edge segment of any part: where links to the neighbours land.</summary>
        static bool NearLink(Vector2 p)
        {
            float x = Mathf.Repeat(p.x, 16), y = Mathf.Repeat(p.y, 16);
            foreach (float s in new[] { 16 / 6f, 8, 16 * 5 / 6f })
            {
                if (Mathf.Abs(x - s) < 3 && (y < 3.2f || y > 12.8f)) return true;
                if (Mathf.Abs(y - s) < 3 && (x < 3.2f || x > 12.8f)) return true;
            }
            return false;
        }

        void TreeRing(Vector2 centre, float radius, int count, float phase, float clear = 2.2f)
        {
            string[] trees = { "FA_TR01_ThickCrooked", "FA_TR12_Split", "FA_TR02_TallNarrow", "FA_TR06_Hollow", "FA_TR03_DoubleFork", "FA_TR05_Zigzag", "FA_TR14_KnottyGiant" };
            for (int k = 0; k < count; k++)
            {
                float a = phase + k * Mathf.PI * 2 / count;
                var p = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (radius + (k % 3 - 1) * .8f);
                if (NearSeam(p, 2.2f) || NearLink(p) || Trails.Any(line => PolyDistance(p, line, out _, out _) < clear) || OnRamp(p, 1.5f)) continue;
                // Clear of the set pieces and of fires.
                if (Props.Any(pr => !pr.NoCollider && !pr.Asset.StartsWith("Trees/") && Vector2.Distance(p, pr.At) < 2.4f * pr.Scale.x) || Sounds.Any(s => Vector2.Distance(p, s.at) < 1.8f)) continue;
                // Scale wanders a lot: saplings next to giants.
                Put("Trees/" + trees[(k * 5 + (int)(radius * 3)) % trees.Length], p.x, p.y, k * 67, .65f + ((k * 37 + (int)(phase * 10)) % 9) * .1f);
            }
        }

        static Vector2[] Circle(Vector2 centre, float radius, int points)
        {
            var line = new Vector2[points + 1];
            for (int k = 0; k <= points; k++)
            {
                float a = k * Mathf.PI * 2 / points;
                line[k] = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
            }
            return line;
        }

        static Vector2 OnCircle(Vector2 centre, float radius, float degrees) =>
            centre + new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad)) * radius;

        /// <summary>
        /// The start: a hill 9 m high over 3 × 3 chunks with the beacon on top and a view all round. Trails run down to
        /// the middle of every side; crags and a rock lip on the slopes, old trees round the foot.
        /// </summary>
        void StartHill()
        {
            Size = 48;
            var c = V(24, 24);
            Domes.Add(new Dome { Centre = c, Radius = 21, Height = 9 });
            Bumps.Add(new Bump { Centre = V(12.6f, 28.4f), Radius = 1.6f, Height = 2.2f, Run = .45f });
            Bumps.Add(new Bump { Centre = V(33.5f, 15.5f), Radius = 1.4f, Height = 2, Run = .45f });
            Bumps.Add(new Bump { Centre = V(31, 34), Radius = 1.2f, Height = 1.8f, Run = .4f });
            Bumps.Add(new Bump { Centre = V(17.5f, 13.5f), Radius = 1.3f, Height = 1.9f, Run = .45f });
            Ridges.Add(new Ridge { Line = new[] { V(10.5f, 19), V(9.5f, 24.5f), V(11.5f, 30.5f) }, Height = 1.8f, Half = .7f, Run = .45f });
            Gaps.Add(new Pass { Centre = V(9.6f, 24.2f), Radius = 1.2f, Run = .6f });
            RidgeRocks.Add((0, 1.5f));
            Trails.Add(new[] { c, V(23.4f, 33), V(24.6f, 40), V(24, 47) });
            Trails.Add(new[] { c, V(33, 24.6f), V(40, 23.4f), V(47, 24) });
            Trails.Add(new[] { c, V(24.6f, 15), V(23.4f, 8), V(24, 1) });
            Trails.Add(new[] { c, V(15, 24.4f), V(9.6f, 24.2f), V(1, 24) });
            // The top: beacon behind the start, a ring of stones, a bench to look out from.
            PutScaled("Structures/FA_ST04_Post", 24, 20.4f, 0, new Vector3(1.4f, 9, 1.4f));
            Put("Props/FA_PR01_LanternHanging", 24, 20.4f, 0, 2.2f, 13);
            Deco("Markers/FA_MK16_StoneCircle", 24, 24, 0, 1.4f);
            Put("Structures/FA_ST12_Bench", 27.2f, 26.2f, 225);
            Put("Markers/FA_MK06_StoneMarkGroup", 20.6f, 27, 30);
            // The view from the top: every landmark for a long way round shows its marker.
            Lookouts.Add((V(24, 24.4f), 600));
            Put("Markers/FA_MK09_ClothPennants", 21, 21, 45);
            Put("Markers/FA_MK01_SignArrow", 26.4f, 21.2f, 135);
            foreach (var crag in Bumps) Put("Rocks/FA_RK08_RocksTallGroup", crag.Centre.x, crag.Centre.y, crag.Centre.x * 31, crag.Radius * .85f, -.4f);
            TreeRing(c, 19, 14, .2f);
            // Nothing tall on the top: the view all round is the point.
            Grass = 30; Ferns = 6; Bushes = 8; Trees = 0;
        }

        /// <summary>A tall hill with a rock band on one side and a lookout on top; the reward waits under its roof.</summary>
        void Watchhill()
        {
            Size = 32;
            var c = V(16, 16);
            Domes.Add(new Dome { Centre = c, Radius = 13.5f, Height = 7 });
            Ridges.Add(new Ridge { Line = new[] { V(14.5f, 25), V(20.5f, 23.4f), V(24.6f, 18.5f), V(25.4f, 13) }, Height = 2.4f, Half = .85f, Run = .45f });
            RidgeRocks.Add((0, 1.6f));
            Bumps.Add(new Bump { Centre = V(8, 22), Radius = 1.2f, Height = 1.6f, Run = .4f });
            Bumps.Add(new Bump { Centre = V(23.5f, 7.5f), Radius = 1.1f, Height = 1.5f, Run = .4f });
            foreach (var b in Bumps) Put("Rocks/FA_RK06_BoulderSplit", b.Centre.x, b.Centre.y, b.Centre.y * 41, b.Radius * .9f, -.3f);
            Trails.Add(new[] { V(1, 4), V(6, 5.4f), V(11, 8.6f), V(15, 12.6f), V(19.4f, 18.4f) });
            Trails.Add(new[] { V(29, 1), V(25.4f, 6), V(22.4f, 11), V(20.4f, 16.6f) });
            Put("Structures/FA_ST13_Shelter", 20.4f, 19.8f, 200);
            Put("Props/FA_PR13_CrateClosed", 18.8f, 21.4f, 20);
            Put("Props/FA_PR02_LanternGround", 22.2f, 18.6f, 0);
            Put("Markers/FA_MK08_ClothLong", 22.6f, 21.4f, 0, 1.3f, 2.6f);
            PutScaled("Structures/FA_ST04_Post", 22.6f, 21.4f, 0, new Vector3(1, 3.4f, 1));
            Put("Structures/FA_ST12_Bench", 19, 18, 160);
            Lookouts.Add((V(19.6f, 17.4f), 600));
            TreeRing(c, 14.2f, 12, .5f);
            Finds = new[] { V(17.6f, 18.6f), V(5.2f, 26.6f) };
            Creatures = new[] { V(21.6f, 17.8f), V(8.4f, 9.4f) };
            Grass = 22; Ferns = 4; Bushes = 8;
        }

        /// <summary>A crater ringed by a rock rim with three passes; standing stones and the reward at the bottom.</summary>
        void Crater()
        {
            Size = 32;
            var c = V(16, 16);
            Domes.Add(new Dome { Centre = c, Radius = 12.4f, Height = -5.5f });
            Ridges.Add(new Ridge { Line = Circle(c, 12.6f, 28), Height = 1.7f, Half = .9f, Run = .45f });
            RidgeRocks.Add((0, 1.7f));
            foreach (float a in new[] { 35f, 165f, 265f })
            {
                var pass = OnCircle(c, 12.6f, a);
                Gaps.Add(new Pass { Centre = pass, Radius = 1.5f, Run = .7f });
                Trails.Add(new[] { OnCircle(c, 15.5f, a), pass, OnCircle(c, 7, a), OnCircle(c, 2.6f, a + 20) });
            }
            Deco("Markers/FA_MK16_StoneCircle", 16.4f, 16.4f, 0, 2.4f);
            Sounds.Add((V(16.4f, 16.4f), "hum", 1));
            foreach (float a in new[] { 45f, 135f, 225f, 315f })
            {
                var p = OnCircle(c, 4.2f, a);
                Put("Markers/FA_MK05_StoneMarkTall", p.x, p.y, a, 1.3f);
            }
            Put("Special/FA_SP07_GravestoneLarge", 21.4f, 12.6f, 300);
            Put("Special/FA_SP05_GravestoneSmall", 10.8f, 20.6f, 120);
            Deco("Special/FA_SP03_WebDrape", 18.8f, 18.8f, 45);
            Put("Props/FA_PR05_CandlesDish", 18.2f, 17.2f, 0);
            Put("Trees/FA_TR10_DeadBare", 23, 22.4f, 30);
            TreeRing(c, 15, 10, .9f);
            Finds = new[] { V(18.4f, 17.8f), V(4, 4) };
            Creatures = new[] { V(19.6f, 16.8f), V(12, 12.4f) };
            Grass = 18; Ferns = 6; Bushes = 6;
        }

        /// <summary>A walled fort on a steep mound: in by the gate ramp or the breach; the reward in the yard.</summary>
        void Fort()
        {
            Size = 32;
            Blocks.Add(new Block { Area = Rect.MinMaxRect(6, 6, 26, 26), Height = 3, Run = .6f });
            Gaps.Add(new Pass { Centre = V(19.6f, 6), Radius = 1.2f, Run = 4 });
            Gaps.Add(new Pass { Centre = V(26, 11.4f), Radius = .9f, Run = 3.6f });
            var wall = new Vector3(1, 1.6f, 1.2f);
            // Walls along the top of the mound, clear of the gate, the breach and the cuts between parts.
            for (float x = 8.4f; x < 25; x += 2.5f)
            {
                if (Mathf.Abs(x - 19.6f) > 2.2f && !NearSeam(V(x, 7.4f), 1.4f)) PutScaled("Structures/FA_ST14_StoneWallRuin", x, 7.4f, 0, wall);
                if (!NearSeam(V(x, 24.6f), 1.4f)) PutScaled("Structures/FA_ST14_StoneWallRuin", x, 24.6f, 180, wall);
            }
            for (float y = 8.4f; y < 25; y += 2.5f)
            {
                if (!NearSeam(V(7.4f, y), 1.4f)) PutScaled("Structures/FA_ST14_StoneWallRuin", 7.4f, y, 90, wall);
                if (Mathf.Abs(y - 11.4f) > 2 && !NearSeam(V(24.6f, y), 1.4f)) PutScaled("Structures/FA_ST14_StoneWallRuin", 24.6f, y, 270, wall);
            }
            Floors.Add(Rect.MinMaxRect(8.2f, 8.2f, 23.8f, 23.8f));
            Put("Structures/FA_ST13_Shelter", 11.2f, 20.6f, 90);
            Put("Special/FA_SP11_BrokenWagon", 12, 11.2f, 60);
            Put("Props/FA_PR13_CrateClosed", 21.2f, 12.6f, 10);
            Put("Props/FA_PR14_CrateOpen", 22.4f, 10.4f, 80);
            Put("Props/FA_PR12_SackLarge", 18.4f, 9.6f, 0);
            Put("Props/FA_PR02_LanternGround", 20.2f, 14.2f, 0);
            PutScaled("Structures/FA_ST04_Post", 21.6f, 20.4f, 0, new Vector3(1, 3.6f, 1));
            Put("Markers/FA_MK08_ClothLong", 21.6f, 20.4f, 0, 1.4f, 2.8f);
            Deco("Markers/FA_MK16_StoneCircle", 20.4f, 20.4f, 0, .7f);
            Fire(20.4f, 20.4f, .9f);
            Trails.Add(new[] { V(19.6f, 1), V(19.6f, 6), V(19.6f, 12), V(14, 18), V(10, 24.6f) });
            Trails.Add(new[] { V(31, 11.4f), V(26, 11.4f), V(21, 12) });
            TreeRing(V(16, 16), 14.4f, 12, .3f);
            Finds = new[] { V(21.8f, 11.4f), V(3.4f, 28.4f) };
            Creatures = new[] { V(19.4f, 12.6f), V(11.6f, 17) };
            Grass = 20; Ferns = 4; Bushes = 8;
        }

        // ---------- card places: a reward several cards reach, none on foot ----------

        /// <summary>A 6 m stone pillar with the reward on top: an updraft lifts you, a rope hung on its side, a teleport.</summary>
        void Pillar()
        {
            Bumps.Add(new Bump { Centre = V(8, 8.4f), Radius = 1.9f, Height = 6.2f, Run = .35f });
            Bumps.Add(new Bump { Centre = V(4, 12), Radius = 1, Height = 1.4f, Run = .4f });
            Bumps.Add(new Bump { Centre = V(12.4f, 4.4f), Radius = .9f, Height = 1.2f, Run = .4f });
            Pocket = V(8, 8.4f);
            Lookouts.Add((V(8, 8.4f), 550));
            PutScaled("Structures/FA_ST04_Post", 8.7f, 9, 0, new Vector3(.9f, 1.8f, .9f));
            Put("Markers/FA_MK08_ClothLong", 8.7f, 9, 0, 1.2f, 1.2f);
            Deco("Structures/FA_ST09_BridgeDamaged", 10.6f, 9.6f, 70, .8f);
            Put("Rocks/FA_RK07_RocksLowGroup", 10.2f, 6.4f, 30);
            Put("Trees/FA_TR10_DeadBare", 4.6f, 4.8f, 40, 1.1f);
            Deco("Plants/FA_VG14_IvyLong", 6.4f, 8.4f, 90, 1.6f);
            Trails.Add(new[] { V(1, 6), V(5, 7.2f), V(8, 5.4f), V(11, 7.6f), V(15, 10) });
            Finds = new[] { V(13.4f, 13) };
            Creatures = new[] { V(11.2f, 11.6f), V(3.6f, 8.4f) };
            Grass = 12; Bushes = 5; Trees = 2;
        }

        /// <summary>
        /// An old stone pen with no gate: walls 2.2 m high all round, the reward and a sleeping lair inside. A stone cube
        /// is a step over, an updraft or a teleport take you in; once in, you drop back out over the wall.
        /// </summary>
        void Yard()
        {
            Ridges.Add(new Ridge { Line = new[] { V(5, 5), V(11, 5), V(11, 11), V(5, 11), V(5, 5) }, Height = 2, Half = .55f, Run = .25f });
            RidgeRocks.Add((0, 1.9f));
            Pocket = V(8, 8);
            Floors.Add(Rect.MinMaxRect(5.7f, 5.7f, 10.3f, 10.3f));
            Put("Props/FA_PR14_CrateOpen", 9.4f, 9.2f, 30);
            Put("Deadwood/FA_DW10_SticksLarge", 6.8f, 9.4f, 0);
            Put("Special/FA_SP05_GravestoneSmall", 13.4f, 8, 270);
            Put("Markers/FA_MK05_StoneMarkTall", 3, 3.4f, 20);
            Put("Structures/FA_ST12_Bench", 13.4f, 12.6f, 220);
            Trails.Add(new[] { V(1, 3), V(4, 3.4f), V(12.6f, 3.6f), V(13.4f, 8), V(12.6f, 15) });
            Finds = new[] { V(2.8f, 13.4f) };
            Creatures = new[] { V(7, 7), V(13, 3) };
            Grass = 12; Bushes = 5; Trees = 2;
        }

        /// <summary>A beekeeper's corner: hives in a fenced yard, the keeper's shed, honey pots, bees in the air.</summary>
        void Apiary()
        {
            foreach (float x in new[] { 4.4f, 6.6f, 8.8f })
                foreach (float y in new[] { 10.2f, 12.8f })
                    Put("Special/FA_SP04_Beehive", x, y, x * 13 + y * 7, .95f + (x + y) % 3 * .06f);
            Put("Structures/FA_ST01_FenceSection", 10.8f, 10.2f, 90);
            Put("Structures/FA_ST01_FenceSection", 10.8f, 13.4f, 90);
            Put("Structures/FA_ST01_FenceSection", 3.8f, 8.4f, 0);
            Put("Structures/FA_ST02_FenceBroken", 8.6f, 8.4f, 0);
            Put("Special/FA_SP10_BoardedShed", 12.2f, 4.2f, 0);
            Put("Props/FA_PR13_CrateClosed", 9.8f, 5.8f, 10);
            Put("Props/FA_PR09_PotLow", 13.8f, 7.2f, 0);
            Put("Props/FA_PR10_Pitcher", 14.4f, 7.6f, 30);
            Put("Props/FA_PR02_LanternGround", 11, 7.6f, 0);
            Put("Structures/FA_ST12_Bench", 8.4f, 3.6f, 80);
            Deco("VFX/Insects/Flies", 6.6f, 11.5f, 0, 1.2f, 1);
            Deco("VFX/Insects/Flies", 4.4f, 13.4f, 0, 1, 1);
            Trails.Add(new[] { V(8, 1), V(7.6f, 6), V(6.4f, 8.4f), V(6.6f, 11.5f) });
            Trails.Add(new[] { V(15, 8.4f), V(11.4f, 7.2f), V(7.6f, 6) });
            Finds = new[] { V(12.2f, 9.6f), V(2.6f, 3) };
            Creatures = new[] { V(6.6f, 11.5f), V(11.6f, 9) };
            Grass = 16; Bushes = 5;
        }

        /// <summary>A big apiary on a meadow mound: rows of hives behind fences, the keeper's house and honey store.</summary>
        void Beeyard()
        {
            Size = 32;
            Domes.Add(new Dome { Centre = V(17, 17), Radius = 13, Height = 2.6f });
            for (float x = 19.4f; x < 28.5f; x += 2.6f)
                for (float y = 19.6f; y < 28.5f; y += 2.8f)
                    if (Mathf.Abs(y - 24.2f) > 1) Put("Special/FA_SP04_Beehive", x, y, x * 11 + y * 5, .9f + (x * 7 + y) % 3 * .07f);
            foreach (float y in new[] { 19.4f, 26.6f }) Put("Structures/FA_ST01_FenceSection", 17.8f, y, 90);
            foreach (float x in new[] { 20.4f, 27.4f }) Put("Structures/FA_ST01_FenceSection", x, 17.8f, 0);
            Put("Special/FA_SP10_BoardedShed", 8, 24.6f, 90);
            Put("Structures/FA_ST13_Shelter", 23.4f, 8.6f, 0);
            foreach (var at in new[] { V(21.6f, 7.4f), V(22.4f, 6.6f), V(25, 7), V(25.6f, 9.6f) }) Put("Props/FA_PR09_PotLow", at.x, at.y, at.x * 20);
            Put("Props/FA_PR13_CrateClosed", 21.4f, 10.4f, 15);
            Put("Props/FA_PR12_SackLarge", 25.6f, 10.8f, 0);
            Put("Special/FA_SP11_BrokenWagon", 6, 8.4f, 40);
            Put("Special/FA_SP09_Scarecrow", 11.6f, 12.6f, 200);
            PutScaled("Structures/FA_ST04_Post", 27.6f, 28.2f, 0, new Vector3(1, 4.2f, 1));
            Put("Markers/FA_MK08_ClothLong", 27.6f, 28.2f, 0, 1.5f, 3.6f);
            foreach (var at in new[] { V(21.6f, 21), V(25.4f, 25.6f), V(23, 28) }) Deco("VFX/Insects/Flies", at.x, at.y, 0, 1.4f, 1);
            Deco("VFX/Smoke/White/SmokeSmallWhite", 23.4f, 9.4f, 0, 1, 2.6f);
            Sounds.Add((V(23.4f, 9.4f), "fire-small", 1));
            Trails.Add(new[] { V(10, 1), V(10.6f, 9), V(16.4f, 14.8f), V(24.2f, 17.8f), V(23.6f, 22.4f) });
            Trails.Add(new[] { V(1, 24.4f), V(8, 24.6f), V(14, 21) });
            Trails.Add(new[] { V(31, 9), V(23.4f, 8.6f), V(16.4f, 14.8f) });
            TreeRing(V(16, 16), 14.6f, 12, .4f);
            Finds = new[] { V(24.6f, 25.2f), V(8, 27.2f) };
            Creatures = new[] { V(21.4f, 25.2f), V(10, 12) };
            Grass = 26; Ferns = 4; Bushes = 8; Trees = 3;
        }

        /// <summary>An abandoned hamlet: sheds round a crossroads, yards behind fences, smoke from one chimney.</summary>
        void Village()
        {
            Size = 32;
            Domes.Add(new Dome { Centre = V(16, 16), Radius = 12, Height = 1.2f });
            Put("Special/FA_SP10_BoardedShed", 8.4f, 23, 90);
            Put("Special/FA_SP10_BoardedShed", 23.4f, 24.4f, 180);
            Put("Special/FA_SP10_BoardedShed", 23.6f, 8.4f, 0);
            Put("Structures/FA_ST13_Shelter", 8.6f, 8.6f, 45);
            foreach (float y in new[] { 19.4f, 26.6f }) Put("Structures/FA_ST01_FenceSection", 12.6f, y, 90);
            foreach (float x in new[] { 4.6f, 8.6f }) Put("Structures/FA_ST01_FenceSection", x, 18.6f, 0);
            foreach (float x in new[] { 20.4f, 26.8f }) Put("Structures/FA_ST02_FenceBroken", x, 19.6f, 0);
            Put("Special/FA_SP11_BrokenWagon", 20.2f, 12.6f, 70);
            Put("Special/FA_SP09_Scarecrow", 27.4f, 14, 250);
            Deco("Markers/FA_MK16_StoneCircle", 19.6f, 19.6f, 0, .8f);
            Put("Props/FA_PR09_PotLow", 19.6f, 19.6f, 0, 1.2f);
            Put("Markers/FA_MK02_SignFork", 17.8f, 13.8f, 45);
            Put("Structures/FA_ST12_Bench", 13.4f, 12.6f, 30);
            Put("Props/FA_PR13_CrateClosed", 10.4f, 26.4f, 10);
            Put("Props/FA_PR12_SackLarge", 11, 25, 0);
            Put("Props/FA_PR02_LanternGround", 25.6f, 21.6f, 0);
            Deco("VFX/Smoke/Grey/SmokeMediumGrey", 23.4f, 24.4f, 0, 1, 3.6f);
            Sounds.Add((V(23.4f, 24.4f), "fire-small", 1.2f));
            Trails.Add(new[] { V(1, 15.4f), V(16, 16.6f), V(31, 15.6f) });
            Trails.Add(new[] { V(16.4f, 1), V(15.6f, 16), V(16.4f, 31) });
            TreeRing(V(16, 16), 14.8f, 10, .7f);
            Finds = new[] { V(9f, 26f), V(26, 26.6f) };
            Creatures = new[] { V(11.4f, 21.4f), V(21, 10.4f) };
            Grass = 22; Bushes = 7; Trees = 3;
        }

        /// <summary>A forest of stone: tall pillars with winding lanes between them and a clearing with the reward.</summary>
        void StoneForest()
        {
            Size = 32;
            var pillars = new[]
            {
                V(5, 5), V(9, 4), V(12.5f, 7.5f), V(5, 11), V(9.5f, 12), V(3.6f, 20), V(7.6f, 19), V(12, 22.6f), V(5, 27), V(10, 28),
                V(19.6f, 4.6f), V(26, 5.6f), V(28.4f, 11), V(20, 13), V(25.4f, 13), V(19, 20.6f), V(24, 19.2f), V(28, 23.4f), V(20.6f, 27.6f), V(26, 28),
            };
            for (int k = 0; k < pillars.Length; k++)
            {
                float r = 1 + k % 3 * .22f, h = 2.8f + (k * 7 % 5) * .45f;
                Bumps.Add(new Bump { Centre = pillars[k], Radius = r, Height = h, Run = .3f });
                PutScaled("Rocks/FA_RK05_StoneTall", pillars[k].x, pillars[k].y, k * 53, new Vector3(r * .9f, h * .5f, r * .9f));
            }
            Ridges.Add(new Ridge { Line = new[] { V(5, 11), V(9.5f, 12) }, Height = 2, Half = .7f, Run = .4f });
            Ridges.Add(new Ridge { Line = new[] { V(24, 19.2f), V(28, 23.4f) }, Height = 2, Half = .7f, Run = .4f });
            Put("Trees/FA_TR10_DeadBare", 15, 10, 30, 1.2f);
            Put("Trees/FA_TR11_Snapped", 22.6f, 23.4f, 80, 1.1f);
            Put("Markers/FA_MK06_StoneMarkGroup", 22.6f, 9.2f, 0, 1.1f);
            Sounds.Add((V(22.4f, 8.6f), "hum", 1));
            PutScaled("Structures/FA_ST04_Post", 23.8f, 10.2f, 0, new Vector3(1, 4, 1));
            Put("Markers/FA_MK08_ClothLong", 23.8f, 10.2f, 0, 1.4f, 3.4f);
            Trails.Add(new[] { V(1, 8), V(7, 7.6f), V(11, 9.8f), V(15, 9), V(22.4f, 8.6f) });
            Trails.Add(new[] { V(16, 31), V(15.4f, 24), V(16.4f, 17), V(22.4f, 8.6f) });
            Finds = new[] { V(22.4f, 8.6f), V(8, 24) };
            Creatures = new[] { V(21.4f, 10), V(14.6f, 25.4f) };
            Grass = 16; Ferns = 6; Bushes = 5; Trees = 2;
        }

        /// <summary>A giant's forest: a ravine crossed by a fallen trunk, the hollow stump of another giant with the reward inside.</summary>
        void GiantLog()
        {
            Size = 32;
            Ridges.Add(new Ridge { Line = new[] { V(2, 22.6f), V(9, 22.4f), V(16, 21.2f), V(24, 21.6f), V(30, 23.2f) }, Height = -3.4f, Half = 2.6f, Run = .9f, Taper = 4.5f });
            Walkways.Add((V(8.4f, 22.4f), 0, 11));
            PutScaled("Deadwood/FA_DW07_StumpHollow", 8.4f, 9.4f, 30, new Vector3(3, 2.6f, 3));
            Put("Deadwood/FA_DW08_StumpUprooted", 24, 9, 120, 2);
            Put("Deadwood/FA_DW03_LogHollow", 25.6f, 27.4f, 70, 1.4f);
            Put("Plants/FA_VG16_ShelfFungusGroup", 9.6f, 10.4f, 0, 1.6f);
            Deco("Decals/FA_D11_LeavesPile", 8.4f, 12.6f, 0, 1.6f);
            Trails.Add(new[] { V(1, 12), V(8.4f, 12.6f), V(8.4f, 17), V(8.4f, 28), V(9, 31) });
            Trails.Add(new[] { V(31, 14), V(20, 15.4f), V(8.4f, 12.6f) });
            TreeRing(V(16, 16), 13.6f, 14, .1f);
            TreeRing(V(16, 16), 9.5f, 6, .9f);
            Finds = new[] { V(8.4f, 13.4f), V(24.4f, 25) };
            Creatures = new[] { V(11.2f, 14), V(20, 26) };
            Grass = 18; Ferns = 10; Bushes = 9; Trees = 4;
        }

        /// <summary>
        /// A table mountain: three tiers of rock 2.3 m each with one ramp up every tier, winding round it, and a ruined
        /// watch post on top with a lookout and the reward. Ramps cross the cuts between parts only where the parts are
        /// linked (the middles and thirds of their edges).
        /// </summary>
        void Mesa()
        {
            Size = 32;
            const float step = 2.3f;
            Blocks.Add(new Block { Area = Rect.MinMaxRect(5, 5, 27, 27), Height = step, Run = .6f });
            Blocks.Add(new Block { Area = Rect.MinMaxRect(9, 9, 23, 23), Height = step * 2, Run = .6f });
            // The top reaches well past the links at 13.33 on the cuts, so its four pieces join.
            Blocks.Add(new Block { Area = Rect.MinMaxRect(11.8f, 11.8f, 18.07f, 19.5f), Height = step * 3, Run = .6f });
            // Up the south face onto the first tier, north along the second tier's east face (over the cut at x 24),
            // then south along the top's east face (over the cut by the link at x 18.67).
            Slope(V(17.5f, 4.4f), 0, V(26.2f, 4.4f), step, 1.8f);
            Slope(V(23.6f, 7), step, V(23.6f, 21), step * 2, 1.8f);
            Slope(V(18.7f, 21.6f), step * 2, V(18.7f, 13), step * 3, 1.8f);
            Trails.Add(new[] { V(16, 1), V(17.5f, 3.4f) });
            Trails.Add(new[] { V(31, 2.6f), V(28, 3.4f), V(26.2f, 4.4f) });
            // The watch post on top: low walls on two sides, a flag, a lantern, the reward under the wall.
            // The top is crossed by the cuts and their links: things with colliders keep to its western corners.
            PutScaled("Structures/FA_ST04_Post", 13.5f, 18.6f, 0, new Vector3(1, 4, 1));
            Put("Markers/FA_MK08_ClothLong", 13.5f, 18.6f, 0, 1.4f, 3.4f);
            Put("Markers/FA_MK05_StoneMarkTall", 13.5f, 13.6f, 40);
            Put("Props/FA_PR02_LanternGround", 14.4f, 18.4f, 0);
            Deco("Structures/FA_ST14_StoneWallRuin", 15.6f, 19, 180, .9f, -.4f);
            Deco("Structures/FA_ST14_StoneWallRuin", 17.6f, 15.8f, 270, .9f, -.4f);
            Lookouts.Add((V(15, 14.6f), 650));
            // Rocks and old trees on the tiers.
            // The first tier's ring is the way round: rocks and trees stand on the second tier's corners instead.
            Put("Rocks/FA_RK08_RocksTallGroup", 10.4f, 21.6f, 40, .6f, -.3f);
            Put("Rocks/FA_RK03_BoulderLarge", 2.8f, 12, 120, .9f, -.3f);
            Put("Trees/FA_TR07_YoungStraight", 10.4f, 10.4f, 30);
            Put("Trees/FA_TR10_DeadBare", 10.4f, 15, 80);
            Deco("Plants/FA_VG14_IvyLong", 12.4f, 16, 90, 1.4f);
            TreeRing(V(16, 16), 14.6f, 12, .6f);
            Finds = new[] { V(14.4f, 17.4f), V(6.8f, 13.6f) };
            Creatures = new[] { V(15.6f, 16.4f), V(25.8f, 12) };
            Grass = 20; Ferns = 4; Bushes = 7; Trees = 3;
        }

        /// <summary>
        /// A sinkhole 19 m across and 6.5 m deep: a ledge winds down its wall three quarters of the way round to a mossy
        /// floor with the reward, standing stones and something humming. The ledge crosses the cuts between parts at
        /// their linked middles.
        /// </summary>
        void Sinkhole()
        {
            Size = 32;
            var c = V(16, 16);
            const float depth = 6.5f, wallRadius = 9.6f;
            Bumps.Add(new Bump { Centre = c, Radius = wallRadius, Height = -depth, Run = .8f });
            // Down counter-clockwise from 315° to 255° (300° in all), hugging the wall: it crosses the cuts by the links
            // at their middles (8 m from the centre). Short segments, so the ledge keeps to the wall's curve.
            const float from = -45, to = 255;
            int segments = 20;
            for (int k = 0; k < segments; k++)
            {
                float a0 = from + (to - from) * k / segments, a1 = from + (to - from) * (k + 1) / segments;
                Slope(OnCircle(c, 8.3f, a0), -depth * k / segments, OnCircle(c, 8.3f, a1), -depth * (k + 1) / segments, 1.8f);
            }
            Ridges.Add(new Ridge { Line = Circle(c, 12, 32), Height = .9f, Half = .5f, Run = .4f });
            Gaps.Add(new Pass { Centre = OnCircle(c, 12, from), Radius = 2.2f, Run = .6f });
            Gaps.Add(new Pass { Centre = OnCircle(c, 12, 140), Radius = 1.4f, Run = .5f });
            RidgeRocks.Add((0, 1.8f));
            Trails.Add(new[] { V(31, 3), V(25.6f, 8), OnCircle(c, 9.6f, from) });
            Trails.Add(new[] { V(1, 29), V(7, 24.6f), OnCircle(c, 12, 140) });
            // The floor.
            Deco("Markers/FA_MK16_StoneCircle", 17.6f, 15, 0, 1.2f);
            foreach (float a in new[] { 20f, 140f, 260f })
            {
                var p = OnCircle(V(17.6f, 15), 2.8f, a);
                Put("Markers/FA_MK05_StoneMarkTall", p.x, p.y, a, 1.1f);
            }
            Put("Deadwood/FA_DW03_LogHollow", 12.6f, 18.6f, 70);
            Put("Plants/FA_VG16_ShelfFungusGroup", 13.4f, 19.2f, 0, 1.4f);
            Put("Props/FA_PR05_CandlesDish", 18.8f, 14, 0);
            Deco("Special/FA_SP03_WebDrape", 14.4f, 13.2f, 210, 1.2f);
            Sounds.Add((V(17.6f, 15), "hum", 1));
            Deco("VFX/Insects/Flies", 17.6f, 15, 0, 1, 1.4f);
            // Roots and ivy over the wall.
            foreach (float a in new[] { 100f, 200f, 300f })
            {
                var p = OnCircle(c, 10, a);
                Deco("Plants/FA_VG14_IvyLong", p.x, p.y, a + 90, 1.8f, -1.2f);
            }
            TreeRing(c, 14, 12, .3f);
            Finds = new[] { V(18.8f, 13.4f), V(26.6f, 27) };
            Creatures = new[] { V(15.4f, 15.2f), V(5.4f, 26) };
            Grass = 18; Ferns = 8; Bushes = 7; Trees = 3;
        }

        /// <summary>
        /// A bog: two deep pools crossed by boardwalks, two shallow ones to wade, dead trees, reeds and mist. The reward
        /// lies on the boardwalk over the far pool; ice freezes a way across too.
        /// </summary>
        void Swamp()
        {
            Size = 32;
            Pools.Add(new Pool { Centre = V(8.4f, 8.2f), Radius = 3.4f, Bank = 4.4f, Deep = 2.2f });
            Pools.Add(new Pool { Centre = V(24, 8.6f), Radius = 3.8f, Bank = 4.8f, Deep = .75f });
            Pools.Add(new Pool { Centre = V(8, 24), Radius = 3.2f, Bank = 4.2f, Deep = .7f });
            Pools.Add(new Pool { Centre = V(23.6f, 23.6f), Radius = 3.6f, Bank = 4.6f, Deep = 2.2f });
            Boardwalks.Add((new[] { V(3, 5.6f), V(8.4f, 8.2f), V(13.8f, 10.8f) }, .3f));
            Boardwalks.Add((new[] { V(18, 22.2f), V(23.6f, 23.6f) }, .3f));
            Boardwalks.Add((new[] { V(23.6f, 23.6f), V(23.6f, 29.4f) }, .3f));
            PutScaled("Structures/FA_ST04_Post", 17.4f, 21, 0, new Vector3(1, 1.6f, 1));
            Put("Props/FA_PR01_LanternHanging", 17.4f, 21, 0, 1, 2);
            Put("Special/FA_SP10_BoardedShed", 12.8f, 5.4f, 180, .9f);
            foreach (var at in new[] { V(4.2f, 12.6f), V(3.2f, 2.6f), V(20.4f, 13.8f), V(28.6f, 18.4f), V(13, 27.6f), V(27.6f, 28.6f) })
                Put("Trees/FA_TR10_DeadBare", at.x, at.y, at.x * 37, .8f + at.y % 3 * .15f);
            Put("Trees/FA_TR11_Snapped", 3.6f, 20, 40);
            Put("Trees/FA_TR04_Leaning", 28.4f, 13, 250, .9f);
            foreach (var at in new[] { V(5, 4.4f), V(11.6f, 11.8f), V(20.6f, 6), V(27.6f, 11.4f), V(5.2f, 27.2f), V(10.6f, 21.4f), V(20.2f, 26.4f), V(27, 20.2f) })
                Deco("Plants/FA_VG05_GrassTall", at.x, at.y, at.x * 20, 1.4f);
            Deco("VFX/Smoke/White/SmokeFlatWhite", 8.4f, 8.2f, 0, 1.6f, .1f);
            Deco("VFX/Smoke/White/SmokeFlatWhite", 23.6f, 23.6f, 0, 1.6f, .1f);
            Deco("VFX/Bubbles/Stream/WaterBubbleStream", 24.4f, 9.2f, 0, .8f, 0);
            Sounds.Add((V(24.4f, 9.2f), "bubbles", 0));
            Deco("VFX/Insects/Flies", 8, 24, 0, 1.2f, 1);
            Trails.Add(new[] { V(1, 16), V(9, 15.6f), V(16, 16.6f), V(23, 16), V(31, 16.4f) });
            Trails.Add(new[] { V(16, 1), V(15.4f, 9), V(16, 16.6f), V(16.6f, 31) });
            Finds = new[] { V(23.6f, 25.4f), V(13.6f, 9.6f) };
            Creatures = new[] { V(19.4f, 20), V(5.6f, 15) };
            Grass = 16; Ferns = 6; Bushes = 6; Trees = 2;
        }

        /// <summary>
        /// A lake 20 m across with an island in the middle: the reward lies on the island, out of reach on foot — freeze
        /// a way over, or jump from the end of the pier. A path runs round the shore past a fisher's shelter.
        /// </summary>
        void Lake()
        {
            Size = 32;
            var c = V(16, 16);
            Pools.Add(new Pool { Centre = c, Radius = 9, Bank = 10.5f, Deep = 2.4f, Island = 6 });
            Pocket = V(17.2f, 17);
            Boardwalks.Add((new[] { OnCircle(c, 11.8f, 45), OnCircle(c, 7.4f, 45) }, .3f));
            Put("Trees/FA_TR10_DeadBare", 14.4f, 17.6f, 40, 1.1f);
            Put("Markers/FA_MK05_StoneMarkTall", 17.6f, 14.8f, 0, .9f);
            Deco("Markers/FA_MK16_StoneCircle", 16.4f, 16.4f, 0, .9f);
            Deco("VFX/Smoke/White/SmokeFlatWhite", 10.6f, 11, 0, 1.8f, .1f);
            Deco("VFX/Smoke/White/SmokeFlatWhite", 22, 11.4f, 0, 1.6f, .1f);
            Deco("VFX/Bubbles/Stream/WaterBubbleStream", 11.2f, 21, 0, .8f, 0);
            Sounds.Add((V(11.2f, 21), "bubbles", 0));
            // The fisher's corner on the north-east shore.
            Put("Structures/FA_ST13_Shelter", 26.8f, 27.4f, 225);
            Put("Props/FA_PR13_CrateClosed", 24.4f, 28.4f, 20);
            Put("Props/FA_PR12_SackLarge", 28.4f, 24.6f, 0);
            Put("Props/FA_PR02_LanternGround", 23, 25.8f, 0);
            Fire(28.2f, 22.2f, .8f);
            foreach (float a in new[] { 10f, 70f, 120f, 160f, 200f, 250f, 290f, 330f })
            {
                var p = OnCircle(c, 10.6f, a);
                Deco("Plants/FA_VG05_GrassTall", p.x, p.y, a, 1.4f);
            }
            Trails.Add(Circle(c, 12.8f, 32));
            TreeRing(c, 14.6f, 12, .5f);
            Finds = new[] { V(23, 27.8f), V(4.6f, 6) };
            Creatures = new[] { V(22.4f, 26.6f), V(6, 25.4f) };
            Grass = 18; Ferns = 4; Bushes = 7; Trees = 3;
        }

        // ---------- relief ----------

        public float Relief(TileTerrain t, Vector2 p)
        {
            if (Ridges.Count == 0 && Bumps.Count == 0 && Domes.Count == 0 && Blocks.Count == 0 && Ramps.Count == 0) return 0;
            float wobble = t.Noise(p, 1.6f, 31) * .3f, grain = 1 + t.Noise(p, 2.2f, 32) * .12f;
            float gap = 1;
            foreach (var g in Gaps) gap = Mathf.Min(gap, Mathf.Clamp01((Vector2.Distance(p, g.Centre) + wobble * .5f - g.Radius) / g.Run));
            float dome = 0, up = 0, down = 0;
            foreach (var d in Domes)
            {
                float k = Mathf.Clamp01(1 - (Vector2.Distance(p, d.Centre) + wobble) / d.Radius);
                dome += d.Height * Mathf.SmoothStep(0, 1, k);
            }
            foreach (var r in Ridges)
            {
                float dist = PolyDistance(p, r.Line, out float along, out float length) + wobble;
                float f = Mathf.Clamp01((r.Half - dist) / r.Run);
                if (r.Taper > 0) f *= Mathf.Clamp01(Mathf.Min(along, length - along) / r.Taper);
                f *= gap;
                if (r.Height > 0) up = Mathf.Max(up, r.Height * grain * f);
                else down = Mathf.Min(down, r.Height * f);
            }
            foreach (var b in Bumps)
            {
                float f = Mathf.Clamp01((b.Radius - Vector2.Distance(p, b.Centre) - wobble) / b.Run);
                if (b.Height > 0) up = Mathf.Max(up, b.Height * f);
                else down = Mathf.Min(down, b.Height * f);
            }
            foreach (var b in Blocks)
            {
                var a = b.Area;
                float inside = Mathf.Min(Mathf.Min(p.x - a.xMin, a.xMax - p.x), Mathf.Min(p.y - a.yMin, a.yMax - p.y)) - wobble;
                float f = Mathf.Clamp01(inside / b.Run) * gap;
                if (b.Height > 0) up = Mathf.Max(up, b.Height * f);
                else down = Mathf.Min(down, b.Height * f);
            }
            float height = dome + up + down;
            // Ramps fill the ground up to their slope where it lies lower: up a step of rock, down into a pit.
            foreach (var r in Ramps)
            {
                var ab = r.B - r.A;
                float tt = Mathf.Clamp01(Vector2.Dot(p - r.A, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                float f = Mathf.Clamp01((r.Half - Vector2.Distance(p, r.A + ab * tt) - wobble * .3f) / r.Run);
                if (f > 0) height += f * Mathf.Max(0, Mathf.Lerp(r.HeightA, r.HeightB, tt) - height);
            }
            return height * TileTerrain.EdgeFade(p, 1.6f, Size);
        }

        /// <summary>Bed of the pools here (float.MaxValue away from them: the ground stays as it is); same shape as a pond's.</summary>
        public float PoolBed(TileTerrain t, Vector2 p, System.Func<float, float, float, float, float> bed)
        {
            float h = float.MaxValue;
            foreach (var pool in Pools)
            {
                float d = Vector2.Distance(p, pool.Centre) + t.Noise(p, 2f, 11) * .3f;
                if (d >= pool.Bank) continue;
                float v = bed(d, pool.Radius, pool.Bank, pool.Deep);
                // An island in the middle: dry ground at the level of the banks.
                if (pool.Island > 0) v = Mathf.Max(v, Mathf.Lerp(-pool.Deep, .7f, Mathf.SmoothStep(0, 1, Mathf.Clamp01(1 - d / pool.Island) * 1.6f)));
                h = Mathf.Min(h, v);
            }
            return h;
        }

        /// <summary>How far outside the nearest pool's banks, m (large without pools).</summary>
        public float PoolDistance(Vector2 p)
        {
            float d = 99;
            foreach (var pool in Pools) d = Mathf.Min(d, Vector2.Distance(p, pool.Centre) - pool.Bank);
            return d;
        }

        /// <summary>Within <paramref name="margin"/> of a ramp's sides: kept clear, it is the only way up or down.</summary>
        public bool OnRamp(Vector2 p, float margin) => Ramps.Any(r =>
        {
            var ab = r.B - r.A;
            float tt = Mathf.Clamp01(Vector2.Dot(p - r.A, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return Vector2.Distance(p, r.A + ab * tt) < r.Half + margin;
        });

        public bool OnBoardwalk(Vector2 p, float margin) => Boardwalks.Any(b => PolyDistance(p, b.line, out _, out _) < margin);

        public bool OnTrail(TileTerrain t, Vector2 p)
        {
            float w = .65f + t.Noise(p, 1.4f, 33) * .25f;
            foreach (var line in Trails) if (PolyDistance(p, line, out _, out _) < w) return true;
            return false;
        }

        public bool OnFloor(Vector2 p) => Floors.Any(r => r.Contains(p));

        public bool InThicket(Vector2 p) => Thicket.HasValue && Thicket.Value.Contains(p);

        /// <summary>Kept clear of scattered plants: set pieces, the thicket, the pillar.</summary>
        public bool Busy(Vector2 p)
        {
            if (InThicket(p) || OnFloor(p)) return true;
            foreach (var prop in Props) if (!prop.NoCollider && Vector2.Distance(p, prop.At) < 1.4f * prop.Scale.x) return true;
            foreach (var spot in Finds) if (Vector2.Distance(p, spot) < .9f) return true;
            if (OnBoardwalk(p, 1.4f) || OnRamp(p, 1)) return true;
            foreach (var l in Lookouts) if (Vector2.Distance(p, l.at) < 1.2f) return true;
            return false;
        }

        public static float PolyDistance(Vector2 p, Vector2[] line, out float along, out float length)
        {
            float best = float.MaxValue, run = 0;
            along = 0;
            for (int i = 0; i + 1 < line.Length; i++)
            {
                var a = line[i];
                var ab = line[i + 1] - a;
                float len = ab.magnitude;
                float tt = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, len * len));
                float d = Vector2.Distance(p, a + ab * tt);
                if (d < best) { best = d; along = run + tt * len; }
                run += len;
            }
            length = run;
            return best;
        }
    }
}
