using UnityEngine;

namespace Hakaton.WorldAssembly.Unity
{
    /// <summary>
    /// Mountains and the start beacon beyond the fog (Kayospace «Открытый мир v2», 2: силуэты до 200 м): a coarse
    /// height field of the world's raised corners over every chunk that is not loaded, at the real heights so it meets
    /// the loaded chunks' edges, and a tall beacon over the start once the player is away from it. The shader fades
    /// both in from the fog colour by distance. Rebuilt when the player enters another chunk or chunks load or unload.
    /// </summary>
    [RequireComponent(typeof(WorldStreamer))]
    public sealed class FarSilhouettes : MonoBehaviour
    {
        public Material material;
        [Tooltip("Silhouettes fade in from the fog from this distance on: about where loaded chunks end (they stay up to the streamer's unload radius), so the two meet where both are fog.")]
        [Min(16)] public float nearRadius = 96;
        [Tooltip("Over this many metres past Near Radius.")]
        [Min(1)] public float fadeIn = 40;
        [Min(32)] public float farRadius = 250;
        [Tooltip("How dark the silhouettes get against the fog.")]
        [Range(0, 1)] public float strength = .5f;
        [Tooltip("The beacon over the start shows once the player is this far from it (the start chunk's own beacon is culled with its detail at about this distance) and comes out of the fog over Fade In.")]
        [Min(0)] public float beaconFrom = 66;
        public float beaconHeight = 34;

        WorldStreamer streamer;
        Mesh mesh, beaconMesh;
        Material fieldMaterial, beaconMaterial;
        Camera stretched;
        float savedFar;
        GameObject field, beacon;
        ChunkCoord last = new ChunkCoord(int.MinValue, int.MinValue);
        int lastLoaded = -1;

        void Awake() => streamer = GetComponent<WorldStreamer>();

        void LateUpdate()
        {
            bool on = streamer.Active && streamer.player && material;
            if (field) field.SetActive(on);
            if (beacon) beacon.SetActive(false);
            Reach(on ? streamer.World?.viewCamera : null);
            if (!on) { last = new ChunkCoord(int.MinValue, int.MinValue); lastLoaded = -1; return; }
            var here = streamer.CoordAt(streamer.player.position);
            if (!here.Equals(last) || streamer.Version != lastLoaded)
            {
                last = here;
                lastLoaded = streamer.Version;
                Rebuild(here);
            }
            var origin = streamer.root ? streamer.root.position : transform.position;
            var flat = streamer.player.position - origin;
            flat.y = 0;
            if (beacon) beacon.SetActive(flat.magnitude > beaconFrom);
        }

        /// <summary>
        /// The player's camera stops at the fog (60 m) everywhere else; out in the world it has to reach the
        /// silhouettes. Chunks cull their own detail at about 70 m, so only their ground is drawn further out.
        /// Restored when the outing ends.
        /// </summary>
        void Reach(Camera cam)
        {
            if (stretched && stretched != cam)
            {
                stretched.farClipPlane = savedFar;
                stretched = null;
            }
            if (!cam || stretched == cam) return;
            savedFar = cam.farClipPlane;
            cam.farClipPlane = Mathf.Max(savedFar, farRadius + 20);
            stretched = cam;
        }

        void OnDisable() => Reach(null);

        void Ensure()
        {
            if (field) return;
            field = new GameObject("Far silhouettes");
            field.transform.SetParent(transform, false);
            mesh = new Mesh { name = "Far silhouettes", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            field.AddComponent<MeshFilter>().sharedMesh = mesh;
            fieldMaterial = new Material(material) { name = "Far silhouettes" };
            fieldMaterial.SetFloat("_Near", nearRadius);
            fieldMaterial.SetFloat("_FadeIn", fadeIn);
            fieldMaterial.SetFloat("_Far", farRadius);
            var r = field.AddComponent<MeshRenderer>();
            r.sharedMaterial = fieldMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            beacon = new GameObject("Beacon silhouette");
            beacon.transform.SetParent(transform, false);
            beaconMesh = Beacon();
            beacon.AddComponent<MeshFilter>().sharedMesh = beaconMesh;
            // The beacon comes out of the fog where the start chunk's own beacon is culled, and stays much further.
            beaconMaterial = new Material(material) { name = "Beacon silhouette" };
            beaconMaterial.SetFloat("_Near", beaconFrom);
            beaconMaterial.SetFloat("_FadeIn", 30);
            beaconMaterial.SetFloat("_Far", farRadius * 1.6f);
            var br = beacon.AddComponent<MeshRenderer>();
            br.sharedMaterial = beaconMaterial;
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
            return Color.Lerp(fog, dark, Mathf.Clamp01(strength * (.7f + .3f * level))).linear;
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
            for (int i = 0; i <= n; i++)
                for (int j = 0; j <= n; j++)
                {
                    int ci = here.X - reach + i, cj = here.Y - reach + j;
                    int level = map.Corners.Level(ci, cj);
                    // Corner (ci, cj) is the south-west corner of chunk (ci, cj). Real heights, a little lower, so the
                    // field meets the edges of the loaded chunks and never shows above them.
                    float x = (ci - .5f) * size, z = (cj - .5f) * size;
                    float y = level > 0 ? level * map.Rules.PlateauHeight - .3f : -.6f;
                    vertices[i * (n + 1) + j] = origin + new Vector3(x, y, z);
                    colours[i * (n + 1) + j] = Colour(level);
                }
            var triangles = new System.Collections.Generic.List<int>();
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    int ci = here.X - reach + i, cj = here.Y - reach + j;
                    float dx = (ci - here.X) * size, dz = (cj - here.Y) * size;
                    if (dx * dx + dz * dz > farRadius * farRadius) continue;
                    // Cell (ci, cj) is chunk (ci, cj): where the real chunk stands, it shows itself.
                    if (streamer.IsLoaded(new ChunkCoord(ci, cj))) continue;
                    // Only cells that rise: flat country stays to the fog.
                    if (map.Corners.Level(ci, cj) == 0 && map.Corners.Level(ci + 1, cj) == 0 && map.Corners.Level(ci, cj + 1) == 0 && map.Corners.Level(ci + 1, cj + 1) == 0) continue;
                    int a = i * (n + 1) + j, b = (i + 1) * (n + 1) + j, c = (i + 1) * (n + 1) + j + 1, e = i * (n + 1) + j + 1;
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
            if (fieldMaterial) Destroy(fieldMaterial);
            if (beaconMaterial) Destroy(beaconMaterial);
        }
    }
}
