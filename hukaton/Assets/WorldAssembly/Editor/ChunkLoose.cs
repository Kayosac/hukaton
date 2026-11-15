using System.Collections.Generic;
using System.Linq;
using Hakaton.Expeditions;
using Hakaton.WorldAssembly.Unity;
using UnityEditor;
using UnityEngine;

namespace Hakaton.WorldAssembly.EditorTools
{
    /// <summary>
    /// Checks the loose things of every chunk prefab: nothing that cannot move (a flame, a light, a hatchet) may depend
    /// on one, or it would be left hanging in the air once the loose thing is thrown off.
    /// </summary>
    public static class ChunkLoose
    {
        const string CataloguePath = "Assets/WorldAssembly/Chunk catalogue.asset";

        public static List<string> Problems(out int checkedProps)
        {
            var problems = new List<string>();
            checkedProps = 0;
            var catalogue = AssetDatabase.LoadAssetAtPath<ChunkCatalog>(CataloguePath);
            var seen = new HashSet<ChunkView>();
            foreach (var e in catalogue.entries)
            {
                if (!e.prefab || !seen.Add(e.prefab)) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(e.prefab.gameObject);
                go.transform.position = new Vector3(40000, 0, 40000);
                Physics.SyncTransforms();
                var view = go.GetComponent<ChunkView>();
                var props = view.props.Where(p => p).ToList();
                checkedProps += props.Count;
                Check(go, e.prefab.name, props, problems);
                Object.DestroyImmediate(go);
            }
            return problems;
        }
        /// <summary>Problems of one place: what cannot move and depends on a loose thing (a flame on it, a piece on or in it).</summary>
        static void Check(GameObject go, string label, List<PhysicsProp> props, List<string> problems, HashSet<PhysicsProp> offenders = null)
        {
            // A creature's own dust and a loose thing's own flame go with it.
            var particles = go.GetComponentsInChildren<ParticleSystem>(true).Where(p => (!p.transform.parent || !p.transform.parent.GetComponentInParent<ParticleSystem>())
                && !p.GetComponentInParent<WorldCreature>(true) && !props.Any(q => p.transform.IsChildOf(q.transform))).ToList();
            var spots = particles.Select(p => (p.transform.position, p.name))
                .Concat(go.GetComponentsInChildren<AmbientSpot>(true).Select(s => (s.transform.position, s.name)))
                .Concat(go.GetComponentsInChildren<Light>(true).Where(l => !props.Any(p => l.transform.IsChildOf(p.transform))).Select(l => (l.transform.position, l.name)))
                .ToList();
            foreach (var prop in props)
            {
                var own = prop.GetComponentsInChildren<Collider>().Where(c => !c.isTrigger).ToList();
                if (own.Count == 0) continue;
                var b = own[0].bounds;
                foreach (var c in own) b.Encapsulate(c.bounds);
                var near = b;
                near.Expand(new Vector3(.3f, .8f, .3f));
                foreach (var (at, name) in spots)
                    if (near.Contains(at)) { problems.Add($"{label}: {name} on loose {prop.name}"); offenders?.Add(prop); }
                // Something that cannot move resting on it.
                var top = new Vector3(b.center.x, b.max.y + .02f, b.center.z);
                foreach (var hit in Physics.OverlapBox(top, new Vector3(b.extents.x * .8f, .04f, b.extents.z * .8f), Quaternion.identity, ~(1 << 4), QueryTriggerInteraction.Ignore))
                {
                    if (hit.transform.IsChildOf(prop.transform) || hit.attachedRigidbody && hit.attachedRigidbody.GetComponent<PhysicsProp>()) continue;
                    if (hit.transform.IsChildOf(go.transform.Find("Ground") ?? go.transform) && hit.transform.parent == go.transform) continue;
                    var piece = hit.GetComponentInParent<Transform>();
                    // Only a piece whose lowest point is about there (it lies on the prop), not a wall or tree it touches.
                    if (Mathf.Abs(hit.bounds.min.y - b.max.y) < .25f && hit.bounds.size.y < 1.5f && hit.bounds.max.y > b.max.y)
                    { problems.Add($"{label}: {piece.name} rests on loose {prop.name}"); offenders?.Add(prop); }
                }
            }
            // Straight down from under every piece that cannot move: a loose thing first means it stands on it.
            var looseColliders = new Dictionary<Collider, PhysicsProp>();
            foreach (var prop in props) foreach (var c in prop.GetComponentsInChildren<Collider>()) looseColliders[c] = prop;
            var ground = go.transform.Find("Ground");
            foreach (var c in go.GetComponentsInChildren<Collider>())
            {
                if (c.isTrigger || looseColliders.ContainsKey(c) || ground && c.transform.IsChildOf(ground)) continue;
                if (c.attachedRigidbody && c.attachedRigidbody.GetComponent<PhysicsProp>()) continue;
                var b = c.bounds;
                if (b.size.y > 2.5f) continue;
                if (Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) < 1.5f)
                    foreach (var kv in looseColliders)
                        if (kv.Key.bounds.Intersects(b)
                            && Physics.ComputePenetration(c, c.transform.position, c.transform.rotation, kv.Key, kv.Key.transform.position, kv.Key.transform.rotation, out _, out float depth)
                            && depth > .02f)
                        { problems.Add($"{label}: {c.name} is stuck in loose {kv.Value.name}"); offenders?.Add(kv.Value); }
                var from = new Vector3(b.center.x, b.min.y + .08f, b.center.z);
                foreach (var hit in Physics.RaycastAll(from, Vector3.down, .5f, ~(1 << 4), QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
                {
                    if (hit.collider == c || hit.collider.transform.IsChildOf(c.transform)) continue;
                    if (looseColliders.TryGetValue(hit.collider, out var under)) { problems.Add($"{label}: {c.name} stands on loose {under.name}"); offenders?.Add(under); }
                    break;
                }
            }
        }

        /// <summary>The loose things under <paramref name="go"/> that something fixed depends on.</summary>
        internal static HashSet<PhysicsProp> Offenders(GameObject go, List<PhysicsProp> props)
        {
            Physics.SyncTransforms();
            var offenders = new HashSet<PhysicsProp>();
            Check(go, go.name, props, new List<string>(), offenders);
            return offenders;
        }

        /// <summary>The same for a hand-made place (the Old Apiary, the tutorial cave): every loose thing under it.</summary>
        public static List<string> PlaceProblems(string prefabPath, out int checkedProps)
        {
            var problems = new List<string>();
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
            go.transform.position = new Vector3(40000, 0, 40000);
            Physics.SyncTransforms();
            var props = go.GetComponentsInChildren<PhysicsProp>(true).Where(p => p.isActiveAndEnabled || p.gameObject.activeInHierarchy).ToList();
            checkedProps = props.Count;
            Check(go, go.name, props, problems);
            Object.DestroyImmediate(go);
            return problems;
        }
    }
}
