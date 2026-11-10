using UnityEngine;

namespace Hakaton.WorldAssembly.Unity
{
    /// <summary>
    /// Mountains and the start beacon beyond the fog (Kayospace «Открытый мир v2», 2: силуэты до 200 м): a coarse
    /// height field of the world's raised corners, drawn without fog in colours that fade into the fog colour, and a
    /// tall beacon over the start once the player is away from it. Rebuilt whenever the player enters another chunk.
    /// </summary>
    [RequireComponent(typeof(WorldStreamer))]
    public sealed class FarSilhouettes : MonoBehaviour
    {
        public Material material;
        [Tooltip("Silhouettes start where the loaded chunks end.")]
        [Min(16)] public float nearRadius = 60;
        [Min(32)] public float farRadius = 200;
        [Tooltip("Heights are stretched a little so far mountains read as mountains.")]
        public float heightScale = 1.4f;
        [Range(0, 1)] public float strength = .5f;
        public float beaconHeight = 34;

        WorldStreamer streamer;
        Mesh mesh, beaconMesh;
        GameObject field, beacon;
        ChunkCoord last = new ChunkCoord(int.MinValue, int.MinValue);

        void Awake() => streamer = GetComponent<WorldStreamer>();

        void LateUpdate()
        {
            bool on = streamer.Active && streamer.player && material;
            if (field) field.SetActive(on);
            if (beacon) beacon.SetActive(false);
            if (!on) { last = new ChunkCoord(int.MinValue, int.MinValue); return; }
            var here = streamer.CoordAt(streamer.player.position);
            if (!here.Equals(last)) { last = here; Rebuild(here); }
            var origin = streamer.root ? streamer.root.position : transform.position;
            var flat = streamer.player.position - origin;
            flat.y = 0;
            if (beacon) beacon.SetActive(flat.magnitude > nearRadius);
        }

        void Ensure()
        {
            if (field) return;
            field = new GameObject("Far silhouettes");
            field.transform.SetParent(transform, false);
            mesh = new Mesh { name = "Far silhouettes", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            field.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = field.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            beacon = new GameObject("Beacon silhouette");
            beacon.transform.SetParent(transform, false);
            beaconMesh = Beacon();
            beacon.AddComponent<MeshFilter>().sharedMesh = beaconMesh;
            var br = beacon.AddComponent<MeshRenderer>();
            br.sharedMaterial = material;
            br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        Mesh Beacon()
        {
            var m = new Mesh { name = "Beacon silhouette" };
            float w = .6f, h = beaconHeight;
            var dark = Colour(2.5f);
            var glow = new Color(1f, .78f, .35f).linear;
            m.vertices = new[]
            {
                new Vector3(-w, 0, 0), new Vector3(w, 0, 0), new Vector3(w, h, 0), new Vector3(-w, h, 0),
                new Vector3(0, 0, -w), new Vector3(0, 0, w), new Vector3(0, h, w), new Vector3(0, h, -w),
                new Vector3(-1.6f, h, 0), new Vector3(1.6f, h, 0), new Vector3(0, h + 2.4f, 0),
                new Vector3(0, h, -1.6f), new Vector3(0, h, 1.6f), new Vector3(0, h + 2.4f, 0),
            };
            m.colors = new[] { dark, dark, dark, dark, dark, dark, dark, dark, glow, glow, glow, glow, glow, glow };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2, 4, 6, 5, 4, 7, 6, 8, 10, 9, 11, 13, 12 };
            m.RecalculateBounds();
            return m;
        }

        Color Colour(float level)
        {
            var fog = RenderSettings.fogColor;
            var dark = new Color(fog.r * .45f, fog.g * .42f, fog.b * .4f);
            // Vertex colours are not converted to linear space: convert here, as the fog colour is.
            return Color.Lerp(fog, dark, Mathf.Clamp01(strength * (.6f + .2f * level))).linear;
        }

        void Rebuild(ChunkCoord here)
        {
            Ensure();
            var map = streamer.Map;
            float size = map.Rules.ChunkSize;
            var origin = streamer.root ? streamer.root.position : transform.position;
            field.transform.position = Vector3.zero;
            beacon.transform.position = origin + Vector3.up * 3;
            int reach = Mathf.CeilToInt(farRadius / size);
            int n = reach * 2 + 1;
            var vertices = new Vector3[(n + 1) * (n + 1)];
            var colours = new Color[vertices.Length];
            var fog = RenderSettings.fogColor;
            for (int i = 0; i <= n; i++)
                for (int j = 0; j <= n; j++)
                {
                    int ci = here.X - reach + i, cj = here.Y - reach + j;
                    int level = map.Corners.Level(ci, cj);
                    // Corner (ci, cj) is the south-west corner of chunk (ci, cj).
                    float x = (ci - .5f) * size, z = (cj - .5f) * size;
                    float y = level > 0 ? level * map.Rules.PlateauHeight * heightScale : -.6f;
                    vertices[i * (n + 1) + j] = origin + new Vector3(x, y, z);
                    colours[i * (n + 1) + j] = level > 0 ? Colour(level) : fog.linear;
                }
            var triangles = new System.Collections.Generic.List<int>();
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    int ci = here.X - reach + i, cj = here.Y - reach + j;
                    float dx = (ci - here.X) * size, dz = (cj - here.Y) * size;
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d < nearRadius || d > farRadius) continue;
                    int a = i * (n + 1) + j, b = (i + 1) * (n + 1) + j, c = (i + 1) * (n + 1) + j + 1, e = i * (n + 1) + j + 1;
                    // Only cells that rise: flat country stays to the fog.
                    if (map.Corners.Level(ci, cj) == 0 && map.Corners.Level(ci + 1, cj) == 0 && map.Corners.Level(ci, cj + 1) == 0 && map.Corners.Level(ci + 1, cj + 1) == 0) continue;
                    triangles.AddRange(new[] { a, e, c, a, c, b });
                }
            mesh.Clear();
            mesh.vertices = vertices;
            mesh.colors = colours;
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        void OnDestroy()
        {
            if (mesh) Destroy(mesh);
            if (beaconMesh) Destroy(beaconMesh);
        }
    }
}
