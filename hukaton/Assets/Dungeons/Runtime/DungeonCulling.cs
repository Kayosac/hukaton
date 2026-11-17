using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.Dungeons.Unity
{
    /// <summary>
    /// Keeps only the dungeon cells near the viewer drawn (renderers and lights; colliders stay). The anthill is dark and
    /// foggy, so nothing beyond the radius can be seen anyway, and its modules are detailed (heavy for WebGL).
    /// </summary>
    public sealed class DungeonCulling : MonoBehaviour
    {
        [Tooltip("Whose position decides what is drawn (the player's camera). Empty: the main camera.")]
        public Transform viewer;
        [Min(4)] public float radius = 34f;
        [Min(1)] public float verticalRadius = 14f;
        [Min(.05f)] public float interval = .2f;

        sealed class Group
        {
            public Vector3 center;
            public Renderer[] renderers;
            public Light[] lights;
            public bool shown = true;
        }

        readonly List<Group> groups = new List<Group>();
        float next;

        public int Shown { get; private set; }
        public int Total => groups.Count;

        public void Collect(DungeonBuilder builder)
        {
            groups.Clear();
            foreach (var kv in builder.CellGroups)
            {
                float floor = builder.Plan.Cells[kv.Key].Floor;
                groups.Add(new Group
                {
                    center = kv.Value.position + Vector3.up * (floor + 2),
                    renderers = kv.Value.GetComponentsInChildren<Renderer>(true),
                    lights = kv.Value.GetComponentsInChildren<Light>(true),
                });
            }
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
            var v = viewer ? viewer : (Camera.main ? Camera.main.transform : null);
            if (!v) return;
            var p = v.position;
            int shown = 0;
            foreach (var g in groups)
            {
                var d = g.center - p;
                bool show = new Vector2(d.x, d.z).sqrMagnitude <= radius * radius && Mathf.Abs(d.y) <= verticalRadius + 6;
                if (show) shown++;
                if (show == g.shown) continue;
                g.shown = show;
                foreach (var r in g.renderers) if (r) r.enabled = show;
                foreach (var l in g.lights) if (l) l.enabled = show;
            }
            Shown = shown;
        }
    }
}
