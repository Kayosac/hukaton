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
        Mesh mesh, beaconMesh, markerMesh;
        Material fieldMaterial, beaconMaterial, markerMaterial;
        Camera stretched;
        float savedFar;
        GameObject field, beacon, markers;
        ChunkCoord last = new ChunkCoord(int.MinValue, int.MinValue);
        int lastLoaded = -1;
        float revealUntil = -1, revealRadius;
        bool shownRevealed;

        /// <summary>A lookout is showing far landmarks (see <see cref="Reveal"/>).</summary>
        public bool Revealed => Time.time < revealUntil;
        /// <summary>How far landmark markers show now, m.</summary>
        public float MarkerRange => Revealed ? Mathf.Max(farRadius, revealRadius) : farRadius;

        /// <summary>Landmark markers show out to <paramref name="radius"/> m for <paramref name="seconds"/> (a lookout).</summary>
        public void Reveal(float radius, float seconds)
        {
            revealRadius = Revealed ? Mathf.Max(revealRadius, radius) : radius;
            revealUntil = Mathf.Max(revealUntil, Time.time + seconds);
        }

        void Awake() => streamer = GetComponent<WorldStreamer>();

        void LateUpdate()
        {
            bool on = streamer.Active && streamer.player && material;
            if (field) field.SetActive(on);
            if (markers) markers.SetActive(on);
            if (beacon) beacon.SetActive(false);
            Reach(on ? streamer.World?.viewCamera : null, MarkerRange);
            if (!on) { last = new ChunkCoord(int.MinValue, int.MinValue); lastLoaded = -1; revealUntil = -1; return; }
            var here = streamer.CoordAt(streamer.player.position);
            if (!here.Equals(last) || streamer.Version != lastLoaded || Revealed != shownRevealed)
            {
                last = here;
                lastLoaded = streamer.Version;
                shownRevealed = Revealed;
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
        void Reach(Camera cam, float range)
        {
            if (stretched && stretched != cam)
            {
                stretched.farClipPlane = savedFar;
                stretched = null;
            }
            if (!cam) return;
            if (stretched != cam) { savedFar = cam.farClipPlane; stretched = cam; }
            cam.farClipPlane = Mathf.Max(savedFar, range + 20);
        }

        void OnDisable() => Reach(null, 0);

        void Ensure()
        {
            if (field) return;
            field = new GameObject("Far silhouettes");
            field.transform.SetParent(transform, false);
            mesh = new Mesh { name = "Far silhouettes", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            field.AddComponent<MeshFilter>().sharedMesh = mesh;
            fieldMaterial = new Material(material) { name = "Far silhouettes" };
            // Never before the loaded chunks end: they stay loaded up to the streamer's unload radius.
            fieldMaterial.SetFloat("_Near", Mathf.Max(nearRadius, streamer.unloadRadius + 6));
            fieldMaterial.SetFloat("_FadeIn", fadeIn);
            fieldMaterial.SetFloat("_Far", farRadius);
            fieldMaterial.SetFloat("_FogDensity", RenderSettings.fog ? RenderSettings.fogDensity : 0);
            var r = field.AddComponent<MeshRenderer>();
            r.sharedMaterial = fieldMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            markers = new GameObject("Landmark markers");
            markers.transform.SetParent(transform, false);
            markerMesh = new Mesh { name = "Landmark markers" };
            markers.AddComponent<MeshFilter>().sharedMesh = markerMesh;

            beacon = new GameObject("Beacon silhouette");
            beacon.transform.SetParent(transform, false);
            beaconMesh = Beacon();
            beacon.AddComponent<MeshFilter>().sharedMesh = beaconMesh;
            // The beacon comes out of the fog where the start chunk's own beacon is culled, and stays much further.
            beaconMaterial = new Material(material) { name = "Beacon silhouette" };
            beaconMaterial.SetFloat("_Near", beaconFrom);
            beaconMaterial.SetFloat("_FadeIn", 30);
            beaconMaterial.SetFloat("_Far", farRadius * 1.6f);
            beaconMaterial.SetFloat("_FogDensity", RenderSettings.fog ? RenderSettings.fogDensity : 0);
            var br = beacon.AddComponent<MeshRenderer>();
            br.sharedMaterial = beaconMaterial;
            // Markers fade out a little past how far they show: further while a lookout reveals them.
            markerMaterial = new Material(beaconMaterial) { name = "Landmark markers" };
            var mr = markers.AddComponent<MeshRenderer>();
            mr.sharedMaterial = markerMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
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

        /// <summary>Low ground in clear air: about the moss of the chunks' ground (alpha 0: only the scene's fog applies).</summary>
        static readonly Color Ground = new Color(.43f, .45f, .27f, 0).linear;

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
                    // Low ground is the fog itself; raised ground and its foot darken out of it, so a plateau stands
                    // on the ground instead of floating over a fog-coloured slope.
                    bool foot = level == 0 && (map.Corners.Level(ci - 1, cj) > 0 || map.Corners.Level(ci + 1, cj) > 0 || map.Corners.Level(ci, cj - 1) > 0 || map.Corners.Level(ci, cj + 1) > 0);
                    colours[i * (n + 1) + j] = level > 0 ? Colour(level) : foot ? Colour(.4f) : Ground;
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
                    // Flat country too, in the fog's colour: without it the sky shows under the far mountains.
                    int a = i * (n + 1) + j, b = (i + 1) * (n + 1) + j, c = (i + 1) * (n + 1) + j + 1, e = i * (n + 1) + j + 1;
                    triangles.AddRange(new[] { a, e, c, a, c, b });
                }
            Markers(here, origin);
            mesh.Clear();
            mesh.vertices = vertices;
            mesh.colors = colours;
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        static readonly System.Collections.Generic.Dictionary<string, Color> markerTips = new System.Collections.Generic.Dictionary<string, Color>
        {
            ["watchhill"] = new Color(1f, .55f, .2f), ["crater"] = new Color(.75f, .4f, 1f), ["fort"] = new Color(1f, .3f, .25f),
            ["beeyard"] = new Color(1f, .85f, .2f), ["village"] = new Color(.95f, .95f, .9f), ["stone-forest"] = new Color(.55f, .75f, 1f),
            ["giant-log"] = new Color(.45f, 1f, .45f), ["mesa"] = new Color(.95f, .7f, .4f), ["sinkhole"] = new Color(.35f, .95f, .9f),
            ["swamp"] = new Color(.65f, .9f, .3f),
        };

        /// <summary>
        /// A tall pole with a coloured tip over every landmark's reward beyond the fog, so there is something to head
        /// for from the start hill and from every high place; a lookout shows them much further, the far ones bigger so
        /// they still read. Near ones are left to the landmark itself.
        /// </summary>
        void Markers(ChunkCoord here, Vector3 origin)
        {
            var map = streamer.Map;
            float size = map.Rules.ChunkSize, range = MarkerRange;
            markerMaterial.SetFloat("_Far", range * 1.15f);
            var vertices = new System.Collections.Generic.List<Vector3>();
            var colours = new System.Collections.Generic.List<Color>();
            var triangles = new System.Collections.Generic.List<int>();
            var dark = Colour(2.5f);
            foreach (var (focus, kind, level) in map.LandmarkFoci(here, range))
            {
                float dx = (focus.X - here.X) * size, dz = (focus.Y - here.Y) * size;
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d < beaconFrom) continue;
                var tip = markerTips.TryGetValue(kind.Id, out var t) ? t.linear : Color.white.linear;
                var at = origin + new Vector3(focus.X * size, level * map.Rules.PlateauHeight, focus.Y * size);
                float grow = 1 + Mathf.Max(0, d - 150) / 200;
                float w = .35f * grow, h = 17 * Mathf.Sqrt(grow), cap = grow;
                int b = vertices.Count;
                vertices.AddRange(new[]
                {
                    at + new Vector3(-w, 0, 0), at + new Vector3(w, 0, 0), at + new Vector3(w, h, 0), at + new Vector3(-w, h, 0),
                    at + new Vector3(0, 0, -w), at + new Vector3(0, 0, w), at + new Vector3(0, h, w), at + new Vector3(0, h, -w),
                    at + new Vector3(-1.3f * cap, h, 0), at + new Vector3(1.3f * cap, h, 0), at + new Vector3(0, h + 2.2f * cap, 0), at + new Vector3(0, h - 1.4f * cap, 0),
                    at + new Vector3(0, h, -1.3f * cap), at + new Vector3(0, h, 1.3f * cap), at + new Vector3(0, h + 2.2f * cap, 0), at + new Vector3(0, h - 1.4f * cap, 0),
                });
                for (int k = 0; k < 8; k++) colours.Add(dark);
                for (int k = 0; k < 8; k++) colours.Add(tip);
                triangles.AddRange(new[]
                {
                    b, b + 2, b + 1, b, b + 3, b + 2, b + 4, b + 6, b + 5, b + 4, b + 7, b + 6,
                    b + 8, b + 10, b + 9, b + 8, b + 9, b + 11, b + 12, b + 14, b + 13, b + 12, b + 13, b + 15,
                });
            }
            markerMesh.Clear();
            markerMesh.SetVertices(vertices);
            markerMesh.SetColors(colours);
            markerMesh.SetTriangles(triangles, 0);
            markerMesh.RecalculateBounds();
        }

        void OnDestroy()
        {
            if (markerMesh) Destroy(markerMesh);
            if (mesh) Destroy(mesh);
            if (beaconMesh) Destroy(beaconMesh);
            if (fieldMaterial) Destroy(fieldMaterial);
            if (beaconMaterial) Destroy(beaconMaterial);
            if (markerMaterial) Destroy(markerMaterial);
        }
    }
}
