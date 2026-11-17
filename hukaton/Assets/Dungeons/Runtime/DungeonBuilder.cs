using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace Hakaton.Dungeons.Unity
{
    /// <summary>
    /// Lays a planned dungeon out of a <see cref="DungeonKit"/>: earth shell (floor, walls, corners, doorways, vaults,
    /// ramps) on every cell, the hand-made chamber prefabs, corridor dressing and lights; then builds its navigation.
    /// Everything goes under this object (cell (0,0) of the entrance at its origin) and is grouped per cell so
    /// <see cref="DungeonCulling"/> can switch far renderers off.
    /// </summary>
    public sealed class DungeonBuilder : MonoBehaviour
    {
        public DungeonKit kit;
        [Tooltip("Builds a dungeon on Start (test scenes). 0 = random seed.")]
        public bool buildOnStart;
        public int seed;

        public DungeonPlan Plan { get; private set; }
        public Transform Built { get; private set; }
        public readonly Dictionary<int, ChamberTemplate> Chambers = new Dictionary<int, ChamberTemplate>();
        public readonly Dictionary<Cell, Transform> CellGroups = new Dictionary<Cell, Transform>();
        NavMeshDataInstance navigation;
        NavMeshData navData;

        const float S = DungeonPlan.CellSize;
        const float RowHeight = 4f;

        void Start()
        {
            if (buildOnStart) Build(seed != 0 ? seed : Random.Range(1, int.MaxValue));
        }

        void OnDestroy() => Clear();

        public void Clear()
        {
            if (navigation.valid) navigation.Remove();
            if (navData) Kill(navData);
            navData = null;
            if (Built) Kill(Built.gameObject);
            Built = null;
            Plan = null;
            Chambers.Clear();
            CellGroups.Clear();
        }

        static void Kill(Object o)
        {
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        public DungeonPlan Build(int dungeonSeed)
        {
            Clear();
            Plan = DungeonGenerator.Generate(dungeonSeed, kit.Recipe());
            Built = new GameObject("Dungeon " + dungeonSeed).transform;
            Built.SetParent(transform, false);
            var dice = new Dice(dungeonSeed ^ 0x5bd1e995);

            foreach (var kv in Plan.Cells) Shell(kv.Key, kv.Value, dice);
            foreach (var room in Plan.Rooms) PlaceChamber(room);
            DressCorridors(dice);
            BuildNavigation();
            var culling = GetComponent<DungeonCulling>();
            if (culling) culling.Collect(this);
            return Plan;
        }

        Transform Group(Cell c)
        {
            if (CellGroups.TryGetValue(c, out var t)) return t;
            t = new GameObject($"Cell {c.X},{c.Y}").transform;
            t.SetParent(Built, false);
            t.localPosition = DungeonGeometry.Center(c, 0);
            CellGroups[c] = t;
            return t;
        }

        GameObject Put(GameObject prefab, Cell c, Vector3 localOffset, float yaw, float y, float scale = 1)
        {
            if (!prefab) return null;
            var g = Group(c);
            var go = Instantiate(prefab, g);
            go.transform.localPosition = localOffset + Vector3.up * y;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            if (scale != 1) go.transform.localScale = Vector3.one * scale;
            return go;
        }

        void Shell(Cell c, CellInfo info, Dice dice)
        {
            float h = info.Floor;
            // Floor slab (top 0.15 m above its pivot) under rooms, corridors and ramps alike.
            if (kit.floors.Length > 0) Put(kit.floors[dice.Range(0, kit.floors.Length)], c, Vector3.zero, dice.Range(0, 4) * 90f, h - .15f);
            if (info.Kind == CellKind.Ramp)
                Put(kit.ramp, c, Vector3.zero, ((int)info.RampUp - (int)Dir.South) * 90f, h);

            // Walls: every side that is not open. Two walls meeting at a corner of the cell become one corner piece.
            var closed = Dirs.All.Where(d => !Plan.IsOpen(c, d)).ToList();
            var cornerPairs = new List<(Dir a, Dir b)>();
            for (int i = 0; i < 4; i++)
            {
                var a = (Dir)i; var b = a.Right();      // a then the next side clockwise
                if (closed.Contains(a) && closed.Contains(b) && !cornerPairs.Any(p => p.a == a || p.b == a || p.a == b || p.b == b))
                    cornerPairs.Add((a, b));
            }
            int rows = Mathf.Max(1, info.Rows);
            foreach (var (a, b) in cornerPairs)
            {
                // The corner covers its south and west sides: (S, W) is rotation 0, then (W, N), (N, E), (E, S).
                int rot = ((int)a - (int)Dir.South + 4) % 4;
                for (int r = 0; r < rows; r++) Put(kit.corner, c, Vector3.zero, rot * 90f, h + r * RowHeight);
            }
            foreach (var d in closed)
            {
                if (cornerPairs.Any(p => p.a == d || p.b == d)) continue;
                for (int r = 0; r < rows; r++)
                    Put(kit.walls[dice.Range(0, kit.walls.Length)], c, DungeonGeometry.Offset(d) * (S / 2), DungeonGeometry.Yaw(d.Opposite()), h + r * RowHeight);
            }
            foreach (var d in Dirs.All)
            {
                if (!Plan.IsDoorway(c, d)) continue;
                // A doorway frame on the room side only (the corridor cell beyond does not frame it again).
                if (info.Kind != CellKind.Room) continue;
                Put(kit.doorway, c, DungeonGeometry.Offset(d) * (S / 2), DungeonGeometry.Yaw(d.Opposite()), h);
                for (int r = 1; r < rows; r++)
                    Put(kit.walls[dice.Range(0, kit.walls.Length)], c, DungeonGeometry.Offset(d) * (S / 2), DungeonGeometry.Yaw(d.Opposite()), h + r * RowHeight);
            }

            // Ceiling.
            bool vent = info.Kind == CellKind.Room && IsVent(c);
            float top = h + rows * RowHeight;
            if (vent) Put(kit.ventCollar, c, Vector3.zero, 0, top);
            else
            {
                // Span the vault across a corridor; rooms keep one direction.
                bool alongX = info.Kind == CellKind.Room ? Plan.Cells[c].Room % 2 == 0 : CorridorRunsAlongX(c, info);
                Put(kit.vault, c, Vector3.zero, alongX ? 90f : 0f, top);
            }
        }

        bool CorridorRunsAlongX(Cell c, CellInfo info)
        {
            if (info.Kind == CellKind.Ramp) return info.RampUp == Dir.East || info.RampUp == Dir.West;
            bool ew = Plan.IsOpen(c, Dir.East) || Plan.IsOpen(c, Dir.West);
            bool ns = Plan.IsOpen(c, Dir.North) || Plan.IsOpen(c, Dir.South);
            return ew && !ns;
        }

        bool IsVent(Cell c)
        {
            var info = Plan.Cells[c];
            var room = Plan.Rooms[info.Room];
            var chamber = Template(room);
            if (!chamber) return false;
            foreach (var v in chamber.ventCells)
                if (room.ToWorld(new Cell(v.x, v.y)).Equals(c)) return true;
            return false;
        }

        ChamberTemplate Template(PlacedRoom room) => kit.chambers.Find(t => t && t.id == room.Shape.Id);

        void PlaceChamber(PlacedRoom room)
        {
            var template = Template(room);
            if (!template) return;
            var go = Instantiate(template.gameObject, Group(room.Origin));
            go.transform.localPosition = Vector3.up * room.Floor;
            go.transform.localRotation = Quaternion.Euler(0, room.Rotation * 90f, 0);
            go.name = $"{template.id} #{room.Index}";
            Chambers[room.Index] = go.GetComponent<ChamberTemplate>();
        }

        void DressCorridors(Dice dice)
        {
            int n = 0;
            foreach (var c in Plan.CorridorCells)
            {
                var info = Plan.Cells[c];
                n++;
                var walls = Dirs.All.Where(d => !Plan.IsOpen(c, d)).ToList();
                bool light = n % kit.corridorLightEvery == 0 && walls.Count > 0;
                if (light && kit.corridorLight)
                {
                    var d = dice.Pick(walls);
                    float y = info.Kind == CellKind.Ramp ? Plan.EdgeFloor(c, d) : info.Floor;
                    var lamp = Put(kit.corridorLight, c, DungeonGeometry.Offset(d) * (S / 2 - .55f), DungeonGeometry.Yaw(d.Opposite()), y);
                    if (lamp && kit.corridorLightIntensity > 0)
                    {
                        var l = new GameObject("Corridor Light").AddComponent<Light>();
                        l.transform.SetParent(lamp.transform, false);
                        l.transform.localPosition = new Vector3(0, 2f, .9f);
                        l.type = LightType.Point; l.color = kit.corridorLightColor; l.range = kit.corridorLightRange;
                        l.intensity = kit.corridorLightIntensity; l.shadows = LightShadows.None;
                    }
                    walls.Remove(d);
                }
                if (info.Kind == CellKind.Ramp) continue;
                foreach (var d in walls)
                    foreach (var w in kit.wallDressing)
                        if (w.prefab && dice.Chance(w.chance))
                        {
                            float sc = w.scaleRange == Vector2.zero ? 1 : Mathf.Lerp(w.scaleRange.x, w.scaleRange.y, (float)dice.Unit());
                            Put(w.prefab, c, DungeonGeometry.Offset(d) * (S / 2 - .55f), DungeonGeometry.Yaw(d.Opposite()) + ((float)dice.Unit() - .5f) * w.yawJitter, info.Floor, sc);
                            break;
                        }
                foreach (var f in kit.corridorFloorDressing)
                    if (f.prefab && dice.Chance(f.chance))
                    {
                        // Against a wall corner of the cell, off the walking line.
                        var d = walls.Count > 0 ? dice.Pick(walls) : Dir.North;
                        var side = d.Right();
                        var off = DungeonGeometry.Offset(d) * 1.2f + DungeonGeometry.Offset(side) * ((float)dice.Unit() - .5f) * 2f;
                        float sc = f.scaleRange == Vector2.zero ? 1 : Mathf.Lerp(f.scaleRange.x, f.scaleRange.y, (float)dice.Unit());
                        Put(f.prefab, c, off, (float)dice.Unit() * 360f, info.Floor, sc);
                    }
                foreach (var f in kit.ceilingDressing)
                    if (f.prefab && dice.Chance(f.chance))
                        Put(f.prefab, c, new Vector3(((float)dice.Unit() - .5f) * 1.5f, 0, ((float)dice.Unit() - .5f) * 1.5f), (float)dice.Unit() * 360f, info.Floor + RowHeight - .2f);
            }
        }

        // ---------- navigation ----------

        void BuildNavigation()
        {
            var sources = new List<NavMeshBuildSource>();
            var bounds = new Bounds(Built.position, Vector3.zero);
            foreach (var kv in Plan.Cells)
            {
                var c = kv.Key; var info = kv.Value;
                var center = Built.TransformPoint(DungeonGeometry.Center(c, info.Floor));
                Matrix4x4 m;
                Vector3 size;
                if (info.Kind == CellKind.Ramp)
                {
                    var up = DungeonGeometry.Offset(info.RampUp);
                    float angle = Mathf.Atan2(DungeonPlan.RampRise, S) * Mathf.Rad2Deg;
                    var rot = Built.rotation * Quaternion.LookRotation(up) * Quaternion.Euler(-angle, 0, 0);
                    m = Matrix4x4.TRS(center + Vector3.up * (DungeonPlan.RampRise / 2 - .15f), rot, Vector3.one);
                    size = new Vector3(3.2f, .3f, Mathf.Sqrt(S * S + DungeonPlan.RampRise * DungeonPlan.RampRise) + .05f);
                }
                else
                {
                    m = Matrix4x4.TRS(center + Vector3.down * .15f, Built.rotation, Vector3.one);
                    size = new Vector3(S, .3f, S);
                }
                sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, transform = m, size = size, area = 0 });
                bounds.Encapsulate(center + new Vector3(S, 6, S));
                bounds.Encapsulate(center - new Vector3(S, 2, S));
            }
            // Hand-placed things in the chambers block the way as their colliders do.
            foreach (var chamber in Chambers.Values)
            {
                var found = new List<NavMeshBuildSource>();
                NavMeshBuilder.CollectSources(chamber.transform, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), found);
                foreach (var s in found) { var x = s; x.area = 1; sources.Add(x); }
            }
            navData = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources, bounds, Vector3.zero, Quaternion.identity);
            navigation = NavMesh.AddNavMeshData(navData);
        }
    }
}
