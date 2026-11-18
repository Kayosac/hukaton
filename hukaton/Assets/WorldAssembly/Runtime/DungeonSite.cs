using System.Collections;
using System.Collections.Generic;
using Hakaton.Dungeons.Unity;
using Hakaton.Expeditions;
using UnityEngine;
using UnityEngine.AI;

namespace Hakaton.WorldAssembly.Unity
{
    /// <summary>
    /// The endless world's dungeons: each entrance's dungeon is built deep under it the first time the player goes in
    /// (same scene, no loading), kept as the player left it for the rest of the outing, and thrown away when the outing
    /// ends. While the player is underground the air holds the <see cref="WorldAtmosphere.Burrow"/> mood.
    /// <para>
    /// The way between is walked, not jumped: the surface mound and the dungeon's entrance chamber each have the same
    /// short dead-end passage (<see cref="DungeonBuilder.BuildPassage"/>). Walking into one, the screen darkens with
    /// every step; at the dark end the player is moved into the other passage at the same depth, turned to its mouth,
    /// and the screen lightens as they keep walking out. As if the two passages were one tunnel.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(WorldStreamer))]
    public sealed class DungeonSite : MonoBehaviour
    {
        [Tooltip("How far below the entrance the dungeon is laid, m (deeper than any ground the world has).")]
        [Min(20)] public float depth = 90f;
        [Tooltip("Into a passage: the screen starts darkening this far from the mouth, m.")]
        [Min(0)] public float darkFrom = 1.2f;
        [Tooltip("...and is black this far in: here the player crosses to the other passage.")]
        [Min(1)] public float darkAt = 6.3f;
        [Min(0)] public float fadeOut = .3f, fadeIn = .45f;

        WorldStreamer streamer;
        WorldAtmosphere atmosphere;
        readonly Dictionary<ChunkCoord, DungeonBuilder> built = new Dictionary<ChunkCoord, DungeonBuilder>();
        DungeonBuilder current;
        DungeonEntrance currentEntrance;
        ChunkCoord currentCoord;
        /// <summary>The surface passage's frame, kept: the mound's chunk may unload while the player is below.</summary>
        Pose surfacePassage;
        bool moving;
        /// <summary>After a crossing the player stands near the dark end: no crossing back until they step towards the mouth.</summary>
        bool armed = true;
        float darkness;
        /// <summary>What the screen fades to: black into the surface passage, daylight into the dungeon's (towards the
        /// surface). After a crossing the colour of the way the player came stays until they are out of the passage.</summary>
        Color tint = Color.black;
        bool carried;

        public bool Underground => current;
        public DungeonBuilder Current => current;
        public bool CanEnter => !moving && !current && World && World.Running;
        /// <summary>How dark the passage the player is in has made the screen (0 outside any).</summary>
        public float Darkness => darkness;
        ExpeditionWorld World => streamer ? streamer.World : null;
        public Vector3 PlayerPosition => streamer && streamer.player ? streamer.player.position : Vector3.one * 1e6f;

        void Awake()
        {
            streamer = GetComponent<WorldStreamer>();
            atmosphere = GetComponent<WorldAtmosphere>();
        }

        void Update()
        {
            var world = World;
            if (!world || !world.Running || moving) return;
            var p = PlayerPosition;
            float dark = 0;
            if (current)
            {
                if (current.Passage && Along(current.Passage, p, out float s)) dark = Walk(s, null);
                else if (current.Exit) dark = Climb(p);
            }
            else
            {
                foreach (var e in DungeonEntrance.All)
                    if (e && e.passage && Along(e.passage, p, out float s)) { dark = Walk(s, e); break; }
            }
            Darken(dark);
        }

        /// <summary>How deep into a passage <paramref name="p"/> is, if it is inside one at all.</summary>
        static bool Along(Transform passage, Vector3 p, out float s)
        {
            var local = passage.InverseTransformPoint(p);
            s = local.z;
            return Mathf.Abs(local.x) < 2.3f && local.y > -1f && local.y < 3.5f && s > -1f && s < DungeonBuilder.PassageDepth + .5f;
        }

        float Walk(float s, DungeonEntrance from)
        {
            if (s < darkAt - .5f) armed = true;
            if (s < darkFrom - .2f) carried = false;
            if (!carried) tint = from ? Color.black : Daylight;
            float dark = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(darkFrom, darkAt, s));
            if (armed && s >= darkAt)
            {
                armed = false;
                carried = true;
                if (from) Down(from); else Up();
                return 1;
            }
            return dark;
        }

        // ---------- the way up after the boss ----------

        [Tooltip("Under the exit shaft: the screen starts going to daylight this far from its middle, m...")]
        [Min(.5f)] public float exitFrom = 2.8f;
        [Tooltip("...and the player goes up this close to it.")]
        [Min(.1f)] public float exitAt = .7f;

        /// <summary>
        /// The queen's chamber has a shaft to the surface under its light slot: dark while she lives, daylight down it
        /// once she is dead. Stepping under it the screen goes to daylight; in its middle the player is up, in the
        /// hill's passage, walking out of it.
        /// </summary>
        float Climb(Vector3 p)
        {
            var exit = current.Exit;
            if (!current.ExitOpen)
            {
                if (!QueenAlive()) current.OpenExit();
                return 0;
            }
            var d = p - exit.position;
            if (Mathf.Abs(d.y) > 3) return 0;
            float r = new Vector2(d.x, d.z).magnitude;
            if (r > exitFrom) { armed = true; carried = false; return 0; }
            if (!carried) tint = Daylight;
            if (armed && r <= exitAt)
            {
                armed = false;
                carried = true;
                Up(true);
                return 1;
            }
            return Mathf.SmoothStep(0, 1, Mathf.InverseLerp(exitFrom, exitAt, r));
        }

        bool QueenAlive()
        {
            foreach (var c in Creatures) if (c && c.name == "AntQueen" && c.Alive) return true;
            return false;
        }

        void Darken(float dark)
        {
            var fade = World ? World.fade : null;
            if (!fade) return;
            if (dark <= 0 && darkness <= 0) return;
            darkness = dark;
            fade.Set(dark, tint);
        }

        Color Daylight
        {
            get
            {
                var kit = streamer && streamer.content ? streamer.content.anthillKit : null;
                return Color.Lerp(kit ? kit.surfaceLight : new Color(1f, .95f, .82f), Color.white, .35f);
            }
        }

        // ---------- crossing ----------

        void Down(DungeonEntrance entrance)
        {
            var dungeon = Prepare(entrance);
            if (!dungeon || !dungeon.Passage) return;
            current = dungeon;
            currentEntrance = entrance;
            currentCoord = entrance.coord;
            surfacePassage = new Pose(entrance.passage.position, entrance.passage.rotation);
            dungeon.gameObject.SetActive(true);
            Physics.SyncTransforms();
            Wake(dungeon, true);
            Air(dungeon.kit, true);
            Cross(entrance.passage, dungeon.Passage.position, dungeon.Passage.rotation);
        }

        void Up(bool byShaft = false)
        {
            var dungeon = current;
            current = null;
            Air(dungeon.kit, false);
            // The mound's chunk may have gone while the player was below: have it (and the ground round it) back first.
            var frame = Surface();
            if (!frame)
            {
                streamer.StreamNow(surfacePassage.position, 9);
                Physics.SyncTransforms();
                frame = Surface();
            }
            var to = frame ? new Pose(frame.position, frame.rotation) : surfacePassage;
            if (byShaft) Arrive(to);
            else Cross(dungeon.Passage, to.position, to.rotation);
            Wake(dungeon, false);
            dungeon.gameObject.SetActive(false);
            currentEntrance = null;
        }

        Transform Surface()
        {
            if (currentEntrance && currentEntrance.passage) return currentEntrance.passage;
            foreach (var e in DungeonEntrance.All)
                if (e && e.coord.Equals(currentCoord) && e.passage) { currentEntrance = e; return e.passage; }
            return null;
        }

        /// <summary>
        /// Moves the player from one passage to the other as if the two were one tunnel joined at their dark ends:
        /// the same depth in (a step short of the crossing), the same offset from the middle seen from the mouth's
        /// side, and turned half round, so walking on leads out of the other passage's mouth.
        /// </summary>
        void Cross(Transform from, Vector3 toPosition, Quaternion toRotation)
        {
            var world = World;
            var body = world.motor.transform;
            var local = from.InverseTransformPoint(body.position);
            // Kept near the middle: the two passages are not equally wide.
            var target = toPosition + toRotation * new Vector3(Mathf.Clamp(-local.x, -.8f, .8f), local.y, darkAt - .25f);
            float yaw = body.eulerAngles.y - from.eulerAngles.y + 180 + toRotation.eulerAngles.y;
            float pitch = Camera.main ? Camera.main.transform.eulerAngles.x : 0;
            world.thrower.Cancel();
            world.motor.Teleport(target + Vector3.up * .05f, Quaternion.Euler(pitch, yaw, 0));
            Physics.SyncTransforms();
        }

        /// <summary>Up the shaft: in the middle of the hill's passage, as deep as a crossing leaves one, facing its mouth.</summary>
        void Arrive(Pose passage)
        {
            var world = World;
            var target = passage.position + passage.rotation * new Vector3(0, 0, darkAt - .25f);
            float yaw = passage.rotation.eulerAngles.y + 180;
            world.thrower.Cancel();
            world.motor.Teleport(target + Vector3.up * .05f, Quaternion.Euler(0, yaw, 0));
            Physics.SyncTransforms();
        }

        DungeonBuilder Prepare(DungeonEntrance entrance)
        {
            if (built.TryGetValue(entrance.coord, out var b) && b) return b;
            var kit = streamer.content ? streamer.content.anthillKit : null;
            if (!kit) return null;
            var go = new GameObject($"Dungeon {entrance.coord}");
            go.transform.SetParent(transform, false);
            // Under the mound, turned with it so the way in lies beneath the way down.
            go.transform.SetPositionAndRotation(entrance.transform.position + Vector3.down * depth, Quaternion.Euler(0, entrance.transform.eulerAngles.y, 0));
            b = go.AddComponent<DungeonBuilder>();
            b.kit = kit;
            go.AddComponent<DungeonCulling>().viewer = Camera.main ? Camera.main.transform : null;
            b.Build(entrance.seed);
            Populate(b, entrance.seed);
            DungeonHazards.Lay(b, World, entrance.seed);
            built[entrance.coord] = b;
            return b;
        }

        // ---------- inhabitants ----------

        /// <summary>
        /// The dungeon's garrison (<see cref="DungeonKit.garrison"/>) at its chambers' markers, once, when it is first
        /// laid: squads walk a route round their marker. They live under the dungeon's "Creatures" and are kept as they
        /// are left (the dead stay dead) for the rest of the outing.
        /// </summary>
        void Populate(DungeonBuilder dungeon, int seed)
        {
            var kit = dungeon.kit;
            var world = World;
            if (!kit || kit.garrison == null || !world) return;
            var root = new GameObject(CreaturesName).transform;
            root.SetParent(dungeon.transform, false);
            var random = new System.Random(seed ^ 0x51ed27);
            foreach (var room in dungeon.Plan.Rooms)
                foreach (var g in kit.garrison)
                {
                    if (g.role != room.Shape.Role || !g.creature || !g.creature.TryGetComponent(out WorldCreature prefab)) continue;
                    var marks = new List<Transform>();
                    foreach (var (r, name, at) in dungeon.Markers) if (r == room.Index && name == g.marker && at) marks.Add(at);
                    if (marks.Count == 0) continue;
                    for (int i = 0; i < marks.Count; i++)
                    {
                        if (g.slot >= 0 && i != g.slot % marks.Count) continue;
                        if (random.NextDouble() > g.chance) continue;
                        int low = Mathf.Max(0, Mathf.Min(g.count.x, g.count.y)), high = Mathf.Max(g.count.x, g.count.y);
                        int n = low + random.Next(high - low + 1);
                        var group = new List<WorldCreature>();
                        var centre = marks[i].position;
                        for (int m = 0; m < n; m++)
                        {
                            float a = (float)random.NextDouble() * 6.28f + m * 2.4f;
                            var at = m == 0 ? centre : centre + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (1.3f + .45f * m);
                            if (NavMesh.SamplePosition(at, out var hit, 2.5f, NavMesh.AllAreas)) at = hit.position;
                            var creature = Instantiate(prefab, at, marks[i].rotation, root);
                            creature.name = prefab.name;
                            creature.world = world;
                            creature.spawnPoints = null;
                            creature.ResetForOuting(null);
                            world.creatures.Add(creature);
                            group.Add(creature);
                        }
                        if (prefab.patrol.enabled && group.Count > 0) PatrolSquad.Form(group, centre, random.Next());
                    }
                }
            Reward(dungeon, root, random);
        }

        /// <summary>
        /// Chests: the prize (rich, a rare curio guaranteed) in the goal chamber, at its Reward marker or, in the queen's
        /// chamber, under the way up a few steps towards where she sits; ordinary ones at the other Reward markers by
        /// chance. Opened ones stay opened (they live under the dungeon).
        /// </summary>
        void Reward(DungeonBuilder dungeon, Transform root, System.Random random)
        {
            var kit = dungeon.kit;
            var goal = dungeon.Plan.Goal;
            bool prized = false;
            foreach (var (room, name, at) in dungeon.Markers)
            {
                if (name != "Reward" || !at) continue;
                bool prize = !prized && goal != null && room == goal.Index;
                if (prize) prized = true;
                else if (random.NextDouble() > kit.sideChestChance) continue;
                Chest(prize ? kit.prizeChest : kit.sideChest, at.position, at.rotation, root);
            }
            if (!prized && dungeon.Exit)
            {
                var boss = dungeon.Markers.Find(m => m.name == "Spawn Boss").at;
                var toward = boss ? boss.position - dungeon.Exit.position : dungeon.Exit.forward;
                toward.y = 0;
                var at = dungeon.Exit.position + toward.normalized * 2.6f;
                if (NavMesh.SamplePosition(at, out var hit, 2, NavMesh.AllAreas)) at = hit.position;
                Chest(kit.prizeChest, at, Quaternion.LookRotation(-toward.normalized), root);
            }
        }

        void Chest(GameObject prefab, Vector3 at, Quaternion facing, Transform parent)
        {
            if (!prefab || !prefab.TryGetComponent(out TreasureChest _)) return;
            var chest = Instantiate(prefab, at, facing, parent).GetComponent<TreasureChest>();
            chest.name = prefab.name;
            chest.world = World;
            chest.ResetForOuting();
        }

        /// <summary>The chests of the dungeon the player is in (debug, tests).</summary>
        public TreasureChest[] Chests => current && current.transform.Find(CreaturesName) ? current.transform.Find(CreaturesName).GetComponentsInChildren<TreasureChest>(true) : new TreasureChest[0];

        const string CreaturesName = "Creatures";

        /// <summary>
        /// The dungeon's creatures join the outing while the player is below and leave it while the dungeon sleeps
        /// (switched off with it). Those that came into being below on their own (the queen's summoned workers) are
        /// taken in under the dungeon too.
        /// </summary>
        void Wake(DungeonBuilder dungeon, bool on)
        {
            var world = World;
            var root = dungeon.transform.Find(CreaturesName);
            if (!world || !root) return;
            if (on)
            {
                foreach (var c in root.GetComponentsInChildren<WorldCreature>(true))
                    if (c && !world.creatures.Contains(c)) world.creatures.Add(c);
                return;
            }
            float below = dungeon.transform.position.y + depth * .5f;
            for (int i = world.creatures.Count - 1; i >= 0; i--)
            {
                var c = world.creatures[i];
                if (!c) continue;
                bool ours = c.transform.IsChildOf(dungeon.transform);
                if (!ours && c.transform.position.y < below && Vector3.Distance(c.transform.position, dungeon.transform.position) < 400)
                {
                    c.transform.SetParent(root, true);
                    ours = true;
                }
                if (ours) world.creatures.RemoveAt(i);
            }
        }

        /// <summary>The creatures of the dungeon the player is in (debug, tests).</summary>
        public WorldCreature[] Creatures => current && current.transform.Find(CreaturesName) ? current.transform.Find(CreaturesName).GetComponentsInChildren<WorldCreature>() : new WorldCreature[0];

        // The sky's ambient light (cool blue from above) has no business underground: while below, the scene's
        // ambient is the dungeon's own flat warm light, and the open world's is put back on the way up.
        bool ambientSaved;
        UnityEngine.Rendering.AmbientMode savedMode;
        Color savedFlat, savedSky, savedEquator, savedGround;
        float savedIntensity;

        void Air(DungeonKit kit, bool underground)
        {
            // At once: the crossing happens in the dark, the mood must not be seen catching up.
            if (atmosphere) atmosphere.Hold(WorldAtmosphere.Burrow, underground ? 1 : 0, true);
            // The open world's grade (teal split toning, heavy vignette) is made for daylight; underground it would bury
            // the lamplight, so it is mostly let go below.
            if (atmosphere && atmosphere.postProcess) atmosphere.postProcess.weight = underground ? (kit ? kit.postProcessWeight : .15f) : 1;
            if (underground && !ambientSaved)
            {
                savedMode = RenderSettings.ambientMode; savedFlat = RenderSettings.ambientLight;
                savedSky = RenderSettings.ambientSkyColor; savedEquator = RenderSettings.ambientEquatorColor; savedGround = RenderSettings.ambientGroundColor;
                savedIntensity = RenderSettings.ambientIntensity;
                ambientSaved = true;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = kit ? kit.ambient : new Color(.36f, .29f, .25f);
                DynamicGI.UpdateEnvironment();   // the ambient probe is not rebuilt by itself at runtime
            }
            else if (!underground && ambientSaved)
            {
                RenderSettings.ambientMode = savedMode; RenderSettings.ambientLight = savedFlat;
                RenderSettings.ambientSkyColor = savedSky; RenderSettings.ambientEquatorColor = savedEquator; RenderSettings.ambientGroundColor = savedGround;
                RenderSettings.ambientIntensity = savedIntensity;
                ambientSaved = false;
                DynamicGI.UpdateEnvironment();
            }
        }

        /// <summary>The outing is over: every dungeon goes.</summary>
        public void Clear()
        {
            StopAllCoroutines();
            if (moving && World) World.control.SetBlocked(this, false);
            moving = false;
            if (current) Air(current.kit, false);
            if (darkness > 0 && World && World.fade) World.fade.Set(0);
            darkness = 0;
            armed = true;
            carried = false;
            current = null;
            currentEntrance = null;
            foreach (var b in built.Values) if (b) Destroy(b.gameObject);
            built.Clear();
        }

        /// <summary>Debug (F1): fly to the entrance on chunk <paramref name="coord"/> and stand before its mouth.</summary>
        public void GoTo(ChunkCoord coord, Vector3 roughly)
        {
            if (moving || current || !World) return;
            StartCoroutine(Fly(coord, roughly));
        }

        IEnumerator Fly(ChunkCoord coord, Vector3 roughly)
        {
            moving = true;
            var world = World;
            world.thrower.Cancel();
            world.control.SetBlocked(this, true);
            if (world.fade) yield return world.fade.FadeTo(1, fadeOut);
            world.motor.Teleport(new Vector3(roughly.x, PlayerPosition.y + 40, roughly.z), world.motor.transform.rotation);
            DungeonEntrance found = null;
            for (float t = 0; t < 4 && !found; t += Time.unscaledDeltaTime)
            {
                foreach (var e in DungeonEntrance.All)
                    if (e && e.coord.Equals(coord)) found = e;
                if (!found) yield return null;
            }
            if (found && found.outside) world.motor.Teleport(found.outside.position + Vector3.up * .1f, found.outside.rotation);
            yield return new WaitForSecondsRealtime(.15f);
            world.control.SetBlocked(this, false);
            if (world.fade) yield return world.fade.FadeTo(0, fadeIn);
            moving = false;
        }

        /// <summary>Debug: where in the dungeon the position is.</summary>
        public string Describe(Vector3 position)
        {
            if (!current || current.Plan == null) return "Данж";
            var plan = current.Plan;
            var local = current.Built.InverseTransformPoint(position);
            var cell = new Hakaton.Dungeons.Cell(Mathf.RoundToInt(local.x / Hakaton.Dungeons.DungeonPlan.CellSize), Mathf.RoundToInt(local.z / Hakaton.Dungeons.DungeonPlan.CellSize));
            var sb = new System.Text.StringBuilder();
            sb.Append("Муравейник, сид ").Append(plan.Seed).Append(plan.Large ? ", большой" : ", малый").Append(", этажей ").Append(plan.Levels).Append('\n');
            sb.Append("Комнат ").Append(plan.Rooms.Count).Append(", клеток ").Append(plan.Cells.Count).Append('\n');
            if (plan.Cells.TryGetValue(cell, out var info))
            {
                sb.Append("Клетка ").Append(cell.X).Append(", ").Append(cell.Y).Append(": ");
                if (info.Room >= 0) { var room = plan.Rooms[info.Room]; sb.Append(room.Shape.Role).Append(" (").Append(room.Shape.Id).Append("), этаж ").Append(room.Level + 1); }
                else sb.Append(info.Kind == Hakaton.Dungeons.CellKind.Ramp ? "пандус" : info.Kind == Hakaton.Dungeons.CellKind.Passage ? "выход" : "коридор");
                sb.Append(", пол ").Append(info.Floor.ToString("0")).Append(" м");
            }
            var cull = current.GetComponent<DungeonCulling>();
            if (cull) sb.Append("\nРисуется клеток: ").Append(cull.Shown).Append(" из ").Append(cull.Total);
            return sb.ToString();
        }

        /// <summary>Debug: the nearest dungeon entrance among the loaded chunks, or null.</summary>
        public DungeonEntrance Nearest(Vector3 from)
        {
            DungeonEntrance best = null; float bestD = float.MaxValue;
            foreach (var e in DungeonEntrance.All)
            {
                if (!e) continue;
                float d = (e.transform.position - from).sqrMagnitude;
                if (d < bestD) { bestD = d; best = e; }
            }
            return best;
        }
    }
}
