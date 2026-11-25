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
        /// <summary>Where the player appears coming down (entrance chamber's "Arrival" marker), facing in.</summary>
        public Pose Arrival { get; private set; }
        /// <summary>The middle of the way-out passage (fallback for code that only wants a spot near the way up).</summary>
        public Vector3 WayOut { get; private set; }
        /// <summary>
        /// The way-out passage's frame: origin on the floor in the middle of its mouth (the entrance chamber's doorway),
        /// +Z running into the passage towards its dead end. The surface entrance has the same passage.
        /// </summary>
        public Transform Passage { get; private set; }
        /// <summary>
        /// The way up after the boss (the queen's chamber "Exit" marker, under its light slot): a column of daylight down
        /// the shaft, switched off until the way opens (<see cref="OpenExit"/>). Null where the dungeon has none.
        /// </summary>
        public Transform Exit { get; private set; }
        public bool ExitOpen { get; private set; }
        GameObject exitLight;
        /// <summary>Hand-placed markers of every chamber by name ("Spawn", "Reward", "Spawn Boss", "Exit"...).</summary>
        public readonly List<(int room, string name, Transform at)> Markers = new List<(int, string, Transform)>();
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
            Passage = null;
            Exit = null;
            ExitOpen = false;
            exitLight = null;
            Chambers.Clear();
            CellGroups.Clear();
            Markers.Clear();
        }

        static void Kill(Object o)
        {
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        /// <param name="largeAllowed">False: the small layout whatever the kit's chance of a large one (see
        /// <see cref="DungeonKit.Recipe"/>).</param>
        public DungeonPlan Build(int dungeonSeed, bool largeAllowed = true)
        {
            Clear();
            Plan = DungeonGenerator.Generate(dungeonSeed, kit.Recipe(largeAllowed));
            Built = new GameObject("Dungeon " + dungeonSeed).transform;
            Built.SetParent(transform, false);
            var dice = new Dice(dungeonSeed ^ 0x5bd1e995);

            foreach (var kv in Plan.Cells)
            {
                if (kv.Value.Kind == CellKind.Passage) continue;
                Shell(kv.Key, kv.Value, dice);
                Backstop(kv.Key, kv.Value);
            }
            LayPassage(dungeonSeed);
            foreach (var room in Plan.Rooms) PlaceChamber(room);
            FindMarkers();
            DressCorridors(dice);
            // The world's light probes (baked outdoors, green and blue) must not light the dungeon: it takes the ambient
            // light the dungeon sets underground (DungeonKit.ambient) and its own lamps only.
            foreach (var r in Built.GetComponentsInChildren<Renderer>(true))
            {
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            }
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
                for (int r = 0; r < rows; r++) Put(Pick(kit.corners, dice) ?? kit.corner, c, Vector3.zero, rot * 90f, h + r * RowHeight);
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
                Put(Pick(kit.vaults, dice) ?? kit.vault, c, Vector3.zero, alongX ? 90f : 0f, top);
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

        void FindMarkers()
        {
            foreach (var kv in Chambers)
            {
                var marks = kv.Value.transform.Find("Markers");
                if (!marks) continue;
                foreach (Transform m in marks) Markers.Add((kv.Key, m.name, m));
            }
            var entrance = Plan.Entrance;
            var arrival = Markers.Find(m => m.room == entrance.Index && m.name == "Arrival").at;
            var inward = Built.rotation * Quaternion.Euler(0, DungeonGeometry.Yaw(Dir.North), 0);
            Arrival = arrival ? new Pose(arrival.position, inward) : new Pose(Built.TransformPoint(DungeonGeometry.Center(entrance.Cells[0], entrance.Floor)), inward);
            WayOut = Passage ? Passage.TransformPoint(new Vector3(0, 0, PassageDepth / 2)) : Arrival.position;
            var exit = Markers.Find(m => m.name == "Exit");
            if (exit.at) LayExit(exit.at, Mathf.Max(1, Plan.Rooms[exit.room].Shape.Rows));
        }

        // ---------- the way up after the boss ----------

        /// <summary>How tall the shaft above the chamber's vault is lit, m.</summary>
        const float ExitShaftTop = 8f;

        void LayExit(Transform marker, int rows)
        {
            // The light lives in the shaft above the chamber's vault: the chamber itself is lit by the lamp only.
            float vault = rows * RowHeight - .3f;
            Exit = marker;
            exitLight = new GameObject("Exit Daylight");
            exitLight.transform.SetParent(marker, false);
            // The glow's frame stands upright (+Z up), its box centred on the column.
            var frame = new GameObject("Shaft").transform;
            frame.SetParent(exitLight.transform, false);
            frame.SetPositionAndRotation(marker.position, Quaternion.LookRotation(Vector3.up, marker.forward));
            frame.position -= frame.up * 1.3f;
            if (kit.passageGlow) Murk(frame, kit.passageGlow, 1.3f, 2.6f, vault + ExitShaftTop, vault + .6f, vault + ExitShaftTop, kit.surfaceLight * .75f, "Exit Glow");
            if (kit.surfaceLightIntensity > 0)
            {
                var l = new GameObject("Exit Lamp").AddComponent<Light>();
                l.transform.SetParent(exitLight.transform, false);
                // A pool of daylight on the floor under the shaft (kept low: up high it would flood the shaft's backstop).
                l.transform.position = marker.position + Vector3.up * 3f;
                l.type = LightType.Point; l.color = kit.surfaceLight; l.range = kit.surfaceLightRange * .8f;
                l.intensity = kit.surfaceLightIntensity * 1.2f; l.shadows = LightShadows.None;
            }
            exitLight.SetActive(false);
        }

        /// <summary>The way up opens (the boss is dead): daylight comes down the shaft.</summary>
        public void OpenExit()
        {
            if (!Exit || ExitOpen) return;
            ExitOpen = true;
            if (exitLight) exitLight.SetActive(true);
        }

        // ---------- the way-out passage ----------

        /// <summary>How deep the passage runs from its mouth to the face of its end wall, m.</summary>
        public const float PassageDepth = DungeonPlan.PassageLength * S - .45f;

        void LayPassage(int dungeonSeed)
        {
            if (!Plan.PassageMouth.HasValue || Plan.Passage.Count == 0) return;
            var (cell, side) = Plan.PassageMouth.Value;
            var group = Group(Plan.Passage[0]);
            foreach (var c in Plan.Passage) Group(c);
            var frame = new GameObject("Passage").transform;
            frame.SetParent(group, false);
            frame.localPosition = DungeonGeometry.Center(cell, Plan.Cells[cell].Floor) + DungeonGeometry.Offset(side) * (S / 2) - group.localPosition;
            frame.localRotation = Quaternion.Euler(0, DungeonGeometry.Yaw(side), 0);
            BuildPassage(kit, frame, dungeonSeed);
            // Daylight at the dead end: a glow that thickens towards it and a lamp that lights the last metres.
            if (kit.passageGlow)
                Murk(frame, kit.passageGlow, 2.3f, 6.6f, PassageDepth, 2f, PassageDepth - .1f, kit.surfaceLight * .85f, "Daylight");
            if (kit.surfaceLightIntensity > 0)
            {
                var l = new GameObject("Daylight Lamp").AddComponent<Light>();
                l.transform.SetParent(frame, false);
                l.transform.localPosition = new Vector3(0, 2.4f, PassageDepth - 1.4f);
                l.type = LightType.Point; l.color = kit.surfaceLight; l.range = kit.surfaceLightRange;
                l.intensity = kit.surfaceLightIntensity; l.shadows = LightShadows.None;
            }
            Passage = frame;
        }

        static MaterialPropertyBlock murkBlock;
        static readonly int MurkColour = Shader.PropertyToID("_Colour"), MurkFrom = Shader.PropertyToID("_From"),
            MurkTo = Shader.PropertyToID("_To"), MurkBox = Shader.PropertyToID("_Box");

        /// <summary>
        /// Depth for a passage (frame +Z inwards; Hakaton/Passage Murk): a box round its <paramref name="halfWidth"/> ×
        /// <paramref name="height"/> × <paramref name="depth"/> inside, drawn from within, that darkens (or lights, with an
        /// additive material) every surface in it by how deep it lies, from <paramref name="from"/> to full at
        /// <paramref name="to"/> m in. A gradient through the space, not panes.
        /// </summary>
        public static void Murk(Transform frame, Material material, float halfWidth, float height, float depth, float from, float to, Color colour, string name)
        {
            if (!material) return;
            murkBlock ??= new MaterialPropertyBlock();
            float x = halfWidth + .5f, y0 = -.6f, y1 = height + .5f, z0 = -.3f, z1 = depth + .8f;
            var v = new List<Vector3>
            {
                new Vector3(-x, y0, z0), new Vector3(x, y0, z0), new Vector3(x, y1, z0), new Vector3(-x, y1, z0),
                new Vector3(-x, y0, z1), new Vector3(x, y0, z1), new Vector3(x, y1, z1), new Vector3(-x, y1, z1),
            };
            // Outward faces (clockwise seen from outside); the shader draws the inner sides.
            var t = new[]
            {
                0, 3, 2, 0, 2, 1,   // -Z
                4, 5, 6, 4, 6, 7,   // +Z
                0, 1, 5, 0, 5, 4,   // -Y
                3, 7, 6, 3, 6, 2,   // +Y
                0, 4, 7, 0, 7, 3,   // -X
                1, 2, 6, 1, 6, 5,   // +X
            };
            var mesh = new Mesh { name = name + " murk" };
            mesh.SetVertices(v);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(frame, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            murkBlock.SetColor(MurkColour, colour);
            murkBlock.SetFloat(MurkFrom, from);
            murkBlock.SetFloat(MurkTo, to);
            murkBlock.SetVector(MurkBox, new Vector4(halfWidth, height, depth, 0));
            r.SetPropertyBlock(murkBlock);
        }

        /// <summary>
        /// Lays the way-out passage in <paramref name="frame"/> (origin = middle of the mouth on the floor, +Z inwards):
        /// <see cref="DungeonPlan.PassageLength"/> corridor cells ending in a wall, with its own backstop. The same
        /// <paramref name="seed"/> lays the same passage, so the one under the surface mound and the one in the dungeon
        /// look alike. No doorway frame: the dungeon's entrance chamber or the surface mound puts its own.
        /// </summary>
        public static void BuildPassage(DungeonKit kit, Transform frame, int seed)
        {
            var dice = new Dice(seed ^ 0x2c1b3c6d);
            int n = DungeonPlan.PassageLength;
            GameObject At(GameObject prefab, Vector3 pos, float yaw)
            {
                if (!prefab) return null;
                var go = Instantiate(prefab, frame);
                go.transform.localPosition = pos;
                go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
                return go;
            }
            for (int i = 0; i < n; i++)
            {
                float z = S / 2 + i * S;
                if (kit.floors.Length > 0) At(kit.floors[dice.Range(0, kit.floors.Length)], new Vector3(0, -.15f, z), dice.Range(0, 4) * 90f);
                bool last = i == n - 1;
                // The dead end's far wall and east side make one corner piece (as the dungeon shell lays them).
                if (last) At(Pick(kit.corners, dice) ?? kit.corner, new Vector3(0, 0, z), 180f);
                else At(Pick(kit.walls, dice), new Vector3(S / 2, 0, z), 270f);
                At(Pick(kit.walls, dice), new Vector3(-S / 2, 0, z), 90f);
                At(Pick(kit.vaults, dice) ?? kit.vault, new Vector3(0, RowHeight, z), 0f);
            }
            // Backstop: a dark earth box round the passage, closing the arch above the mouth too.
            var mesh = new BoxMesh();
            float depth = n * S, top = RowHeight + Ceiling, bottom = -Under;
            mesh.Quad(new Vector3(-Reach, top, 0), new Vector3(Reach, top, 0), new Vector3(Reach, top, depth + .45f), new Vector3(-Reach, top, depth + .45f), Vector3.down);
            mesh.Quad(new Vector3(-Reach, bottom, 0), new Vector3(Reach, bottom, 0), new Vector3(Reach, bottom, depth + .45f), new Vector3(-Reach, bottom, depth + .45f), Vector3.up);
            mesh.Quad(new Vector3(Reach, bottom, 0), new Vector3(Reach, top, 0), new Vector3(Reach, top, depth + .45f), new Vector3(Reach, bottom, depth + .45f), Vector3.left);
            mesh.Quad(new Vector3(-Reach, bottom, 0), new Vector3(-Reach, top, 0), new Vector3(-Reach, top, depth + .45f), new Vector3(-Reach, bottom, depth + .45f), Vector3.right);
            mesh.Quad(new Vector3(-Reach, bottom, depth + .45f), new Vector3(Reach, bottom, depth + .45f), new Vector3(Reach, top, depth + .45f), new Vector3(-Reach, top, depth + .45f), Vector3.back);
            mesh.Quad(new Vector3(-Reach, RowHeight + .45f, 0), new Vector3(Reach, RowHeight + .45f, 0), new Vector3(Reach, top, 0), new Vector3(-Reach, top, 0), Vector3.forward);
            mesh.Make(frame, kit.backstop, "Backstop");
        }

        static GameObject Pick(GameObject[] options, Dice dice)
        {
            if (options == null || options.Length == 0) return null;
            return options[dice.Range(0, options.Length)];
        }

        // ---------- backstop ----------

        /// <summary>The backstop's walls stand this far from the cell's middle (just behind the wall modules).</summary>
        const float Reach = S / 2 + .45f;
        /// <summary>Above the wall tops (the vault is 2.5 m tall) and below the floor slabs.</summary>
        const float Ceiling = 2.75f, Under = .6f, VentShaft = 6f;

        /// <summary>
        /// A dark earth box behind the modules of one cell: wherever two modules do not quite meet (vault ends, steps
        /// down a ramp, the crooked joins), the eye finds earth instead of the void. Six quads at most, no colliders.
        /// </summary>
        void Backstop(Cell c, CellInfo info)
        {
            if (!kit.backstop) return;
            var (bottom, top) = Span(c, info);
            var mesh = new BoxMesh();
            mesh.Quad(new Vector3(-Reach, top, -Reach), new Vector3(Reach, top, -Reach), new Vector3(Reach, top, Reach), new Vector3(-Reach, top, Reach), Vector3.down);
            mesh.Quad(new Vector3(-Reach, bottom, -Reach), new Vector3(Reach, bottom, -Reach), new Vector3(Reach, bottom, Reach), new Vector3(-Reach, bottom, Reach), Vector3.up);
            foreach (var d in Dirs.All)
            {
                var out_ = DungeonGeometry.Offset(d);
                var along = DungeonGeometry.Offset(d.Right());
                if (!Plan.IsOpen(c, d))
                {
                    Side(mesh, out_ * Reach, along, bottom, top, -out_);
                    continue;
                }
                var n = c.Step(d);
                if (!Plan.Cells.TryGetValue(n, out var ni)) continue;
                var (nb, nt) = Span(n, ni);
                // Steps between neighbours of different height: close the part of the edge the other cell does not see.
                if (nb > bottom) Side(mesh, out_ * (S / 2), along, bottom, nb, -out_);
                if (nt < top) Side(mesh, out_ * (S / 2), along, nt, top, -out_);
            }
            mesh.Make(Group(c), kit.backstop, "Backstop");
        }

        static void Side(BoxMesh mesh, Vector3 at, Vector3 along, float y0, float y1, Vector3 facing)
        {
            var a = at - along * Reach; var b = at + along * Reach;
            mesh.Quad(new Vector3(a.x, y0, a.z), new Vector3(b.x, y0, b.z), new Vector3(b.x, y1, b.z), new Vector3(a.x, y1, a.z), facing);
        }

        (float bottom, float top) Span(Cell c, CellInfo info)
        {
            float h = info.Floor;
            int rows = Mathf.Max(1, info.Rows);
            float top = h + rows * RowHeight + Ceiling;
            if (info.Kind == CellKind.Room && IsVent(c)) top += VentShaft;
            return (h - Under, top);
        }

        /// <summary>A few quads in one mesh, each turned to face a given way; earth UVs in metres (2 m tile).</summary>
        sealed class BoxMesh
        {
            readonly List<Vector3> v = new List<Vector3>();
            readonly List<Vector3> nrm = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly List<int> t = new List<int>();

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing)
            {
                int i = v.Count;
                foreach (var p in new[] { a, b, c, d })
                {
                    v.Add(p); nrm.Add(facing);
                    // Project onto the quad's own plane: horizontal ones by x/z, upright ones by their run and height.
                    uv.Add(Mathf.Abs(facing.y) > .5f ? new Vector2(p.x, p.z) / 2f : new Vector2(Mathf.Abs(facing.x) > .5f ? p.z : p.x, p.y) / 2f);
                }
                // Unity draws a triangle's clockwise side: (a, b, c) faces along Cross(b - a, c - a).
                bool along = Vector3.Dot(Vector3.Cross(b - a, c - a), facing) > 0;
                if (along) { t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3); }
                else { t.Add(i); t.Add(i + 2); t.Add(i + 1); t.Add(i); t.Add(i + 3); t.Add(i + 2); }
            }

            public void Make(Transform parent, Material material, string name)
            {
                if (v.Count == 0 || !material) return;
                var m = new Mesh { name = name };
                m.SetVertices(v); m.SetNormals(nrm); m.SetUVs(0, uv); m.SetTriangles(t, 0);
                m.RecalculateBounds();
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
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
            // (as boxes round their colliders: model meshes are not readable at runtime, and boxes are cheap).
            // The chambers were just placed and turned: the physics must see where their colliders are now (transforms
            // are not synced on their own), or a turned chamber's boxes stand where its props were before the turn.
            Physics.SyncTransforms();
            foreach (var chamber in Chambers.Values)
                foreach (var col in chamber.GetComponentsInChildren<Collider>())
                {
                    if (col.isTrigger || !col.enabled) continue;
                    var bb = col.bounds;
                    if (bb.size.y < .25f) continue;   // flat litter does not block anyone
                    sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.ModifierBox, transform = Matrix4x4.TRS(bb.center, Quaternion.identity, Vector3.one), size = bb.size, area = 1 });
                }
            navData = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources, bounds, Vector3.zero, Quaternion.identity);
            navigation = NavMesh.AddNavMeshData(navData);
        }
    }
}
