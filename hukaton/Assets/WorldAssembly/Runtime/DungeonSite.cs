using System.Collections;
using System.Collections.Generic;
using Hakaton.Dungeons.Unity;
using Hakaton.Expeditions;
using UnityEngine;

namespace Hakaton.WorldAssembly.Unity
{
    /// <summary>
    /// The endless world's dungeons: each entrance's dungeon is built deep under it the first time the player goes in
    /// (same scene, no loading — a short darkening hides the step down), kept as the player left it for the rest of the
    /// outing (<see cref="Leave"/> only switches it off), and thrown away when the outing ends. While the player is
    /// underground the air holds the <see cref="WorldAtmosphere.Burrow"/> mood: no sun, dark warm fog.
    /// </summary>
    [RequireComponent(typeof(WorldStreamer))]
    public sealed class DungeonSite : MonoBehaviour
    {
        [Tooltip("How far below the entrance the dungeon is laid, m (deeper than any ground the world has).")]
        [Min(20)] public float depth = 90f;
        [Min(0)] public float fadeOut = .3f, fadeIn = .45f;

        WorldStreamer streamer;
        WorldAtmosphere atmosphere;
        readonly Dictionary<ChunkCoord, DungeonBuilder> built = new Dictionary<ChunkCoord, DungeonBuilder>();
        DungeonBuilder current;
        DungeonEntrance currentEntrance;
        /// <summary>Where the player comes out (kept: the mound's chunk may unload while the player is below).</summary>
        Pose outsidePose;
        bool moving;

        public bool Underground => current;
        public DungeonBuilder Current => current;
        public bool CanEnter => !moving && !current && World && World.Running;
        ExpeditionWorld World => streamer ? streamer.World : null;
        public Vector3 PlayerPosition => streamer && streamer.player ? streamer.player.position : Vector3.one * 1e6f;

        void Awake()
        {
            streamer = GetComponent<WorldStreamer>();
            atmosphere = GetComponent<WorldAtmosphere>();
        }

        void Update()
        {
            if (!current || moving || !World || !World.Running) return;
            var d = PlayerPosition - current.WayOut;
            if (Mathf.Abs(d.y) < 2.5f && new Vector2(d.x, d.z).sqrMagnitude < 1.3f * 1.3f) Leave();
        }

        public void Enter(DungeonEntrance entrance)
        {
            if (!CanEnter) return;
            StartCoroutine(Move(entrance, true));
        }

        public void Leave()
        {
            if (!current || moving) return;
            StartCoroutine(Move(currentEntrance, false));
        }

        IEnumerator Move(DungeonEntrance entrance, bool down)
        {
            moving = true;
            var world = World;
            world.thrower.Cancel();
            world.control.SetBlocked(this, true);
            if (world.fade) yield return world.fade.FadeTo(1, fadeOut);
            if (down)
            {
                current = Prepare(entrance);
                currentEntrance = entrance;
                outsidePose = entrance.outside ? new Pose(entrance.outside.position, entrance.outside.rotation) : new Pose(entrance.transform.position + entrance.transform.forward * 6, entrance.transform.rotation);
                current.gameObject.SetActive(true);
                Physics.SyncTransforms();
                var pose = current.Arrival;
                world.motor.Teleport(pose.position + Vector3.up * .1f, pose.rotation);
                Air(current.kit, true);
            }
            else
            {
                var dungeon = current;
                current = null;
                currentEntrance = null;
                Air(dungeon.kit, false);
                world.motor.Teleport(outsidePose.position + Vector3.up * .1f, outsidePose.rotation);
                dungeon.gameObject.SetActive(false);
            }
            yield return new WaitForSecondsRealtime(.15f);
            world.control.SetBlocked(this, false);
            if (world.fade) yield return world.fade.FadeTo(0, fadeIn);
            moving = false;
        }

        DungeonBuilder Prepare(DungeonEntrance entrance)
        {
            if (built.TryGetValue(entrance.coord, out var b) && b) return b;
            var kit = streamer.content ? streamer.content.anthillKit : null;
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
            if (atmosphere) atmosphere.Hold(WorldAtmosphere.Burrow, underground ? 1 : 0);
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
            current = null;
            currentEntrance = null;
            foreach (var b in built.Values) if (b) Destroy(b.gameObject);
            built.Clear();
        }

        /// <summary>Debug (F1): fly to the entrance on chunk <paramref name="coord"/> and stand at its mouth.</summary>
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
                foreach (var e in FindObjectsByType<DungeonEntrance>(FindObjectsSortMode.None))
                    if (e.coord.Equals(coord)) found = e;
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
                else sb.Append(info.Kind == Hakaton.Dungeons.CellKind.Ramp ? "пандус" : "коридор");
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
            foreach (var e in FindObjectsByType<DungeonEntrance>(FindObjectsSortMode.None))
            {
                float d = (e.transform.position - from).sqrMagnitude;
                if (d < bestD) { bestD = d; best = e; }
            }
            return best;
        }
    }
}
