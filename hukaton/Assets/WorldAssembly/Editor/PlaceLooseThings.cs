using System.Collections.Generic;
using System.Linq;
using System.Text;
using Hakaton.Expeditions;
using Hakaton.OldApiary;
using Hakaton.WorldAssembly.Unity;
using UnityEditor;
using UnityEngine;

namespace Hakaton.WorldAssembly.EditorTools
{
    /// <summary>
    /// The endless world's physics for a hand-made place (the Old Apiary, the tutorial cave): the same loose things,
    /// breakables and fences as the chunks get (<see cref="ChunkPrefabBuilder"/>'s tables), plus the cave's and the
    /// shop's own art. Loose things become dormant bodies (still until touched), breakables get their kind, fences come
    /// apart. A thing something fixed depends on (a flame, a light, a web, a fixed piece on or in it) stays put; moss and
    /// fungus growing on a loose thing go with it. Things already set up are left as they are (their kind is updated).
    /// </summary>
    public static class PlaceLooseThings
    {
        /// <summary>The cave's and the shop's art: mass at scale 1, what it is made of, its breakable kind (or null).</summary>
        static readonly Dictionary<string, (float mass, PropSurface surface, string kind)> Extra = new Dictionary<string, (float, PropSurface, string)>
        {
            ["FA_PR08_BottleFlat"] = (.2f, PropSurface.Clay, "Bottle"),
            ["SA_Mug"] = (.3f, PropSurface.Clay, "Clay pot"), ["SA_ClayJar"] = (.5f, PropSurface.Clay, "Clay pot"), ["SA_Pouch"] = (.4f, PropSurface.Soft, null),
            ["CV_Skull"] = (.5f, PropSurface.Stone, null), ["CV_Stool"] = (2, PropSurface.Wood, null), ["CV_WashTub"] = (3, PropSurface.Wood, null),
            ["CV_RopeCoil"] = (.8f, PropSurface.Soft, null), ["CV_RopeCoilFloor"] = (.8f, PropSurface.Soft, null),
        };

        /// <summary>Growth that goes with the loose thing it grows on.</summary>
        static bool Growth(string id) => id.Contains("MossCap") || id.Contains("ShelfFungus") || id.Contains("_VG13_") || id.Contains("_VG14_");

        /// <summary>What hangs from things (webs): a loose thing it touches stays put.</summary>
        static bool Hangs(string id) => id.Contains("_SP01_") || id.Contains("_SP02_") || id.Contains("_SP03_") || id.Contains("Roots");

        static string Id(GameObject go)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(go);
            if (!source) return null;
            var path = AssetDatabase.GetAssetPath(source);
            return string.IsNullOrEmpty(path) ? null : System.IO.Path.GetFileNameWithoutExtension(path);
        }

        static BreakableKind Kind(string name) => name == null ? null : AssetDatabase.LoadAssetAtPath<BreakableKind>($"Assets/Game/Data/Breakables/{name}.asset");

        /// <summary>Sets up the prefab at <paramref name="path"/> (placed far away to measure, then applied). Returns a report.</summary>
        public static string ApplyToPrefab(string path, bool write)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            go.transform.SetPositionAndRotation(new Vector3(40000, 0, 40000), Quaternion.identity);
            string report;
            try
            {
                report = Apply(go.transform, write);
                if (write) PrefabUtility.ApplyPrefabInstance(go, InteractionMode.AutomatedAction);
            }
            finally { Object.DestroyImmediate(go); }
            return report;
        }

        public static string Apply(Transform root, bool write)
        {
            Physics.SyncTransforms();
            var things = new List<(GameObject go, string id)>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root || !PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)) continue;
                string id = Id(t.gameObject);
                if (id != null) things.Add((t.gameObject, id));
            }
            var candidates = things.Where(x => (ChunkPrefabBuilder.LooseMass.ContainsKey(x.id) || Extra.ContainsKey(x.id)) && x.go.activeInHierarchy).ToList();
            var fences = things.Where(x => ChunkPrefabBuilder.BreakApart.ContainsKey(x.id) && x.go.activeInHierarchy && !x.go.GetComponent<Breakable>()).ToList();
            // What fixed things depend on: flames, lights and sounds (not a thing's own), webs and roots hanging on it,
            // fixed pieces resting on it or stuck in it.
            var spots = new List<(Vector3 at, Transform owner)>();
            // (A creature's own dust and sparks go with it: they hold nothing.)
            foreach (var p in root.GetComponentsInChildren<ParticleSystem>(true))
                if ((!p.transform.parent || !p.transform.parent.GetComponentInParent<ParticleSystem>()) && !p.GetComponentInParent<WorldCreature>(true)) spots.Add((p.transform.position, p.transform));
            foreach (var l in root.GetComponentsInChildren<Light>(true)) spots.Add((l.transform.position, l.transform));
            foreach (var a in root.GetComponentsInChildren<AmbientSpot>(true)) spots.Add((a.transform.position, a.transform));
            var looseIds = new HashSet<GameObject>(candidates.Select(c => c.go));
            var pinned = new Dictionary<GameObject, string>();
            foreach (var (go, id) in candidates.Concat(fences))
            {
                var b = Bounds(go);
                if (b.size == Vector3.zero) { pinned[go] = "nothing drawn"; continue; }
                var near = b;
                near.Expand(new Vector3(.5f, 1.4f, .5f));
                foreach (var (at, owner) in spots)
                    if (!owner.IsChildOf(go.transform) && near.Contains(at)) { pinned[go] = "a flame or light on it: " + owner.name; break; }
                if (pinned.ContainsKey(go)) continue;
                foreach (var (other, otherId) in things)
                {
                    if (other == go || other.transform.IsChildOf(go.transform) || go.transform.IsChildOf(other.transform)) continue;
                    if (Hangs(otherId) && Bounds(other).Intersects(b)) { pinned[go] = "hung with " + other.name; break; }
                }
                if (pinned.ContainsKey(go)) continue;
                foreach (var c in root.GetComponentsInChildren<Collider>())
                {
                    if (c.isTrigger || c.transform.IsChildOf(go.transform) || looseIds.Any(l => c.transform.IsChildOf(l.transform))) continue;
                    var cb = c.bounds;
                    if (cb.size.y > 2.5f || !cb.Intersects(near)) continue;
                    // A fixed piece resting on it: straight down from under it, the first thing hit is this one.
                    var from = new Vector3(cb.center.x, cb.min.y + .08f, cb.center.z);
                    foreach (var hit in Physics.RaycastAll(from, Vector3.down, .5f, ~(1 << 4), QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
                    {
                        if (hit.collider == c || hit.collider.transform.IsChildOf(c.transform)) continue;
                        if (hit.collider.transform.IsChildOf(go.transform)) pinned[go] = "holds " + c.name;
                        break;
                    }
                    if (pinned.ContainsKey(go)) break;
                }
            }
            var sb = new StringBuilder();
            var fresh = new HashSet<PhysicsProp>();
            int made = 0, kinds = 0, fixedBreak = 0, growth = 0, kept = 0;
            foreach (var (go, id) in candidates)
            {
                var prop = go.GetComponent<PhysicsProp>();
                string kindName = ChunkPrefabBuilder.BreakKind.TryGetValue(id, out var k) ? k : Extra.TryGetValue(id, out var e) ? e.kind : null;
                if (prop)
                {
                    // Already loose: only its kind of breaking brought up to date.
                    if (kindName != null && !go.GetComponent<Breakable>()) { if (write) go.AddComponent<Breakable>().kind = Kind(kindName); kinds++; }
                    kept++;
                    continue;
                }
                if (pinned.TryGetValue(go, out var why)) { sb.Append($"  stays: {go.name} ({why})\n"); continue; }
                var scale = go.transform.lossyScale;
                if (Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z)) > 1.6f) { sb.Append($"  stays: {go.name} (too big)\n"); continue; }
                if (!write) { made++; if (kindName != null) kinds++; continue; }
                var colliders = go.GetComponentsInChildren<Collider>().Where(c => !c.isTrigger).ToArray();
                if (colliders.Length == 0)
                {
                    foreach (var f in go.GetComponentsInChildren<MeshFilter>())
                        if (f.sharedMesh && !ChunkLooks.SoftPart(f.name)) f.gameObject.AddComponent<MeshCollider>().sharedMesh = f.sharedMesh;
                    colliders = go.GetComponentsInChildren<Collider>().Where(c => !c.isTrigger).ToArray();
                }
                if (colliders.Length == 0) { sb.Append($"  stays: {go.name} (nothing to hit)\n"); continue; }
                foreach (var c in colliders) if (c is MeshCollider mesh) mesh.convex = true;
                if (!ChunkPrefabBuilder.EaseOut(go, colliders, false)) { sb.Append($"  stays: {go.name} (set into something)\n"); continue; }
                if (!Grounded(go)) { sb.Append($"  stays: {go.name} (no floor under it, or under a floor)\n"); continue; }
                // Still as it was made (exact) until touched; convex once it moves (PhysicsProp.Stir).
                foreach (var c in colliders) if (c is MeshCollider mesh) mesh.convex = false;
                float mass = ChunkPrefabBuilder.LooseMass.TryGetValue(id, out var m) ? m : Extra[id].mass;
                var body = go.TryGetComponent(out Rigidbody had) ? had : go.AddComponent<Rigidbody>();
                body.mass = mass * Mathf.Abs(scale.x * scale.y * scale.z);
                body.angularDamping = .05f;
                body.isKinematic = true;
                body.interpolation = RigidbodyInterpolation.None;
                body.collisionDetectionMode = CollisionDetectionMode.Discrete;
                prop = go.AddComponent<PhysicsProp>();
                fresh.Add(prop);
                prop.body = body;
                prop.dormant = true;
                prop.surface = Extra.TryGetValue(id, out var ex) ? ex.surface : ChunkPrefabBuilder.SurfaceOf(id);
                if (kindName != null) { go.AddComponent<Breakable>().kind = Kind(kindName); kinds++; }
                made++;
                // Moss and fungus on it go with it.
                var b = Bounds(go);
                foreach (var (other, otherId) in things)
                    if (Growth(otherId) && other.transform.parent != go.transform && !go.transform.IsChildOf(other.transform) && Bounds(other).Intersects(b) && Bounds(other).center.y > b.center.y)
                    {
                        other.transform.SetParent(go.transform, true);
                        growth++;
                    }
            }
            // What turned out to hold something fixed after all (a rock group lying on it, a stalagmite in it) stays put;
            // that may free or hold the next, so round again.
            if (write)
                for (int round = 0; round < 5; round++)
                {
                    var all = root.GetComponentsInChildren<PhysicsProp>(true).ToList();
                    var offenders = ChunkLoose.Offenders(root.gameObject, all).Where(p => fresh.Contains(p)).ToList();
                    if (offenders.Count == 0) break;
                    foreach (var p in offenders)
                    {
                        sb.Append($"  stays after all: {p.name} (something fixed lies on it or in it)\n");
                        fresh.Remove(p);
                        made--;
                        var go = p.gameObject;
                        if (go.TryGetComponent(out Breakable breakable)) { Object.DestroyImmediate(breakable); kinds--; }
                        var body = p.body;
                        Object.DestroyImmediate(p);
                        if (body) Object.DestroyImmediate(body);
                    }
                }
            foreach (var (go, id) in fences)
            {
                if (pinned.TryGetValue(go, out var why)) { sb.Append($"  fence stays whole: {go.name} ({why})\n"); continue; }
                if (write) go.AddComponent<Breakable>().kind = Kind(ChunkPrefabBuilder.BreakApart[id]);
                fixedBreak++;
            }
            return $"{root.name}: {made} made loose, {kept} were loose already, {kinds} breakable kinds, {fixedBreak} fences come apart, {growth} growths go along\n" + sb;
        }

        /// <summary>
        /// Something solid right under it, and nothing solid right over it: a thing set under a cave's floor shell (or
        /// floating) would fall through the world once it moved.
        /// </summary>
        static bool Grounded(GameObject go)
        {
            Physics.SyncTransforms();
            var b = Bounds(go);
            bool Own(RaycastHit h) => h.collider.transform.IsChildOf(go.transform);
            var below = Physics.RaycastAll(b.center, Vector3.down, b.extents.y + .6f, ~(1 << 4), QueryTriggerInteraction.Ignore)
                .Where(h => !Own(h)).OrderBy(h => h.distance).ToList();
            if (below.Count == 0) return false;
            var above = Physics.RaycastAll(b.center + Vector3.up * (b.extents.y + 3), Vector3.down, 3 + b.extents.y, ~(1 << 4), QueryTriggerInteraction.Ignore)
                .Where(h => !Own(h)).OrderBy(h => h.distance).ToList();
            // A ceiling far above is fine; a floor shell just over it is not.
            return above.Count == 0 || above[0].point.y > b.max.y + 1.2f;
        }

        static Bounds Bounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b;
        }

        [MenuItem("Hakaton/Physics/Loose things in the Old Apiary and the tutorial cave")]
        public static string ApplyToPlaces()
        {
            var report = ApplyToPrefab("Assets/OldApiary/Prefabs/Old Apiary.prefab", true) + ApplyToPrefab("Assets/TutorialCave/Prefabs/TutorialCave_Set.prefab", true);
            Debug.Log("[Physics] " + report);
            return report;
        }

        public static string Preview() =>
            ApplyToPrefab("Assets/OldApiary/Prefabs/Old Apiary.prefab", false) + ApplyToPrefab("Assets/TutorialCave/Prefabs/TutorialCave_Set.prefab", false);
    }
}
