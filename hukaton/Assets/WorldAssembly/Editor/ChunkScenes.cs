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

        public struct Ridge { public Vector2[] Line; public float Height, Half, Run, Taper; }
        public struct Bump { public Vector2 Centre; public float Radius, Height, Run; }
        public struct Dome { public Vector2 Centre; public float Radius, Height; }
        public struct Pass { public Vector2 Centre; public float Radius, Run; }
        public struct Prop { public string Asset; public Vector2 At; public float Yaw; public Vector3 Scale; public float Lift; public bool NoCollider; }

        /// <summary>Raised (Height &gt; 0) or sunk (Height &lt; 0) lines with steep sides.</summary>
        public readonly List<Ridge> Ridges = new List<Ridge>();
        /// <summary>Steep round rocks: boulders that are part of the ground, the pillar of a tower.</summary>
        public readonly List<Bump> Bumps = new List<Bump>();
        /// <summary>Gentle rises and hollows, walkable everywhere.</summary>
        public readonly List<Dome> Domes = new List<Dome>();
        /// <summary>Where ridges and gullies give way: passes and land bridges.</summary>
        public readonly List<Pass> Gaps = new List<Pass>();
        public readonly List<Vector2[]> Trails = new List<Vector2[]>();
        /// <summary>Paved ground (the floor of a ruin).</summary>
        public readonly List<Rect> Floors = new List<Rect>();
        public readonly List<Prop> Props = new List<Prop>();
        /// <summary>Rock dressing along ridges: (ridge index, spacing).</summary>
        public readonly List<(int ridge, float spacing)> RidgeRocks = new List<(int, float)>();
        public Vector2[] Finds = Array.Empty<Vector2>();
        public Vector2[] Creatures = Array.Empty<Vector2>();
        /// <summary>Card-only pocket: the reward's spot and the thicket around it or the pillar under it.</summary>
        public Vector2? Pocket;
        public Rect? Thicket;
        /// <summary>Plants scattered over the rest: grass, ferns, trees.</summary>
        public int Grass = 10, Ferns, Trees, Bushes = 6;

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

        void Deco(string asset, float x, float y, float yaw = 0, float scale = 1) =>
            Props.Add(new Prop { Asset = asset, At = V(x, y), Yaw = yaw, Scale = Vector3.one * scale, NoCollider = true });

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
            Grass = 8; Ferns = 10; Trees = 3;
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
            Trails.Add(new[] { V(1, 10), V(5.6f, 9.6f), V(9, 10), V(15, 12.4f) });
            Finds = new[] { V(6.4f, 6.4f), V(3.6f, 13.4f) };
            Creatures = new[] { V(9.6f, 11.6f), V(14, 13.6f) };
            Grass = 12; Trees = 1;
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

        // ---------- relief ----------

        public float Relief(TileTerrain t, Vector2 p)
        {
            if (Ridges.Count == 0 && Bumps.Count == 0 && Domes.Count == 0) return 0;
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
                up = Mathf.Max(up, b.Height * f);
            }
            return (dome + up + down) * TileTerrain.EdgeFade(p, 1.6f);
        }

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
