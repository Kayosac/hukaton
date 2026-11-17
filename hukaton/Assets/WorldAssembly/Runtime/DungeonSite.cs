using System.Collections;
using System.Collections.Generic;
using Hakaton.Dungeons.Unity;
using Hakaton.Expeditions;
using UnityEngine;

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
            Air(dungeon.kit, true);
            Cross(entrance.passage, dungeon.Passage.position, dungeon.Passage.rotation);
        }

        void Up()
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
            if (frame) Cross(dungeon.Passage, frame.position, frame.rotation);
            else Cross(dungeon.Passage, surfacePassage.position, surfacePassage.rotation);
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
            var target = toPosition + toRotation * new Vector3(-local.x, local.y, darkAt - .25f);
            float yaw = body.eulerAngles.y - from.eulerAngles.y + 180 + toRotation.eulerAngles.y;
            float pitch = Camera.main ? Camera.main.transform.eulerAngles.x : 0;
            world.thrower.Cancel();
            world.motor.Teleport(target + Vector3.up * .05f, Quaternion.Euler(pitch, yaw, 0));
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
            built[entrance.coord] = b;
            return b;
        }

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
