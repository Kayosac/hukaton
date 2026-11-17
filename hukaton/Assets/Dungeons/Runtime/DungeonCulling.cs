using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.Dungeons.Unity
{
    /// <summary>
    /// Draws only the dungeon cells the viewer could see: cells reachable from the viewer's cell through open edges within
    /// <see cref="reach"/> metres of walking (earth walls hide everything else, however close in a straight line), and
    /// never beyond <see cref="radius"/>. Renderers and lights are switched; colliders stay. The camera's own frustum
    /// culling does the rest. Outside the dungeon nothing of it is drawn.
    /// </summary>
    public sealed class DungeonCulling : MonoBehaviour
    {
        [Tooltip("Whose position decides what is drawn (the player's camera). Empty: the main camera.")]
        public Transform viewer;
        [Tooltip("Walking distance through the dungeon within which cells are drawn, m.")]
        [Min(4)] public float reach = 44f;
        [Tooltip("Straight-line cap, m.")]
        [Min(4)] public float radius = 40f;
        [Min(.05f)] public float interval = .15f;

        sealed class Group
        {
            public Renderer[] renderers;
            public Light[] lights;
            public bool shown = true;
        }

        readonly Dictionary<Cell, Group> groups = new Dictionary<Cell, Group>();
        readonly HashSet<Cell> visible = new HashSet<Cell>();
        readonly Dictionary<Cell, float> walked = new Dictionary<Cell, float>();
        readonly Queue<Cell> queue = new Queue<Cell>();
        DungeonBuilder builder;
        float next;

        public int Shown { get; private set; }
        public int Total => groups.Count;

        public void Collect(DungeonBuilder from)
        {
            builder = from;
            groups.Clear();
            foreach (var kv in from.CellGroups)
                groups[kv.Key] = new Group { renderers = kv.Value.GetComponentsInChildren<Renderer>(true), lights = kv.Value.GetComponentsInChildren<Light>(true) };
            next = 0;
            Refresh();
        }

        void Update()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + interval;
            Refresh();
        }

        void Refresh()
        {
            if (!builder || builder.Plan == null || !builder.Built) return;
            var v = viewer ? viewer : (Camera.main ? Camera.main.transform : null);
            if (!v) return;
            var plan = builder.Plan;
            var local = builder.Built.InverseTransformPoint(v.position);
            var here = new Cell(Mathf.RoundToInt(local.x / DungeonPlan.CellSize), Mathf.RoundToInt(local.z / DungeonPlan.CellSize));
            visible.Clear();
            bool inside = plan.Cells.TryGetValue(here, out var info) && Mathf.Abs(local.y - info.Floor) < 10f;
            if (inside)
            {
                walked.Clear();
                queue.Clear();
                walked[here] = 0;
                queue.Enqueue(here);
                while (queue.Count > 0)
                {
                    var c = queue.Dequeue();
                    visible.Add(c);
                    float d = walked[c];
                    if (d + DungeonPlan.CellSize > reach) continue;
                    foreach (var dir in Dirs.All)
                    {
                        if (!plan.IsOpen(c, dir)) continue;
                        var n = c.Step(dir);
                        if (walked.ContainsKey(n)) continue;
                        var dx = (n.X - here.X) * DungeonPlan.CellSize; var dz = (n.Y - here.Y) * DungeonPlan.CellSize;
                        if (dx * dx + dz * dz > radius * radius) continue;
                        walked[n] = d + DungeonPlan.CellSize;
                        queue.Enqueue(n);
                    }
                }
                // A wall shows on both sides of its edge cell only; neighbours of what is seen keep their walls drawn
                // so nothing looks open at the edge of the reach.
                var rim = new List<Cell>();
                foreach (var c in visible) foreach (var dir in Dirs.All) if (plan.IsOpen(c, dir) && !visible.Contains(c.Step(dir))) rim.Add(c.Step(dir));
                foreach (var c in rim) visible.Add(c);
            }
            int shown = 0;
            foreach (var kv in groups)
            {
                bool show = visible.Contains(kv.Key);
                if (show) shown++;
                var g = kv.Value;
                if (show == g.shown) continue;
                g.shown = show;
                foreach (var r in g.renderers) if (r) r.enabled = show;
                foreach (var l in g.lights) if (l) l.enabled = show;
            }
            Shown = shown;
        }
    }
}
