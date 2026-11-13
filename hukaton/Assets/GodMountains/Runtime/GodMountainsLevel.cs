using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hakaton.GodMountains
{
    /// <summary>
    /// The root of the god's mountains scene: what the game needs from the place when it loads it next to its own scene
    /// (<see cref="GodMountainsVisit"/>). Opened alone in the editor, the scene switches on its own test player and speech
    /// plate (<see cref="standalone"/>), so Play walks the whole level as before. In the game they stay off and the
    /// game's player and plate are handed to the encounter instead.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GodMountainsLevel : MonoBehaviour
    {
        /// <summary>Set by the game while it loads and runs the level: the scene's own test rig stays off.</summary>
        public static bool Hosted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Hosted = false;

        public GodEncounter encounter;
        public GodMountainsAtmosphere atmosphere;
        [Tooltip("Where the player wakes up (Z0, facing the wall).")]
        public Transform spawn;
        [Tooltip("The camera's far plane while the player is here, m: the god and the far ranges must be seen.")]
        [Min(100)] public float farClip = 12000;
        [Tooltip("Played alone (the scene opened by itself) these are switched on: the test player and the god's own speech " +
                 "plate. Saved switched off; in the game they stay off.")]
        public GameObject[] standalone = new GameObject[0];

        [Header("Performance (web)")]
        [Tooltip("Sew the parts of every placed kit model into one mesh when the level loads (colliders stay on the parts).")]
        public bool sewOnLoad = true;
        [Tooltip("Nothing under these casts shadows (the far panorama: massifs, cloud sea, rocks under the god's hands). " +
                 "Skinned renderers (the god himself) keep theirs.")]
        public Transform[] noShadows = new Transform[0];
        [Tooltip("Pieces smaller than this (bounds diagonal, m) cast no shadows: pebbles, tufts, rosettes.")]
        [Min(0)] public float smallPieceNoShadow = 1.5f;
        [Tooltip("Far copies of the big models (the A09 massifs at 15% of their triangles, same part names). A placed model " +
                 "farther than Far From from every Near Point gets the far copy's meshes when the level loads.")]
        public FarModel[] farModels = new FarModel[0];
        [Tooltip("Where the player can come closest to the panorama: the lip, the god, the talking point before his face.")]
        public Transform[] nearPoints = new Transform[0];
        [Min(0)] public float farFrom = 700;

        [Serializable]
        public struct FarModel
        {
            [Tooltip("The model as placed in the scene (FBX root).")] public GameObject full;
            [Tooltip("Its far copy (FBX root): the same parts by name, fewer triangles.")] public GameObject far;
        }

        void Awake()
        {
            if (!Hosted) foreach (var o in standalone) if (o) o.SetActive(true);
            Optimize();
        }

        void Optimize()
        {
            foreach (var root in noShadows)
                if (root) foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;
            if (smallPieceNoShadow > 0)
                foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
                    if (r.bounds.size.magnitude < smallPieceNoShadow) r.shadowCastingMode = ShadowCastingMode.Off;
            int swapped = UseFarCopies();
            if (swapped > 0) Debug.Log($"God mountains: {swapped} far models use their light copies.", this);
            if (!sewOnLoad) return;
            var report = GodMountainsBatcher.Merge(transform);
            Debug.Log("God mountains: " + report, this);
        }

        int UseFarCopies()
        {
            if (farModels.Length == 0 || nearPoints.Length == 0) return 0;
            var farByMesh = new Dictionary<Mesh, Dictionary<string, Mesh>>();
            foreach (var model in farModels)
            {
                if (!model.full || !model.far) continue;
                var byName = new Dictionary<string, Mesh>();
                foreach (var f in model.far.GetComponentsInChildren<MeshFilter>(true))
                    if (f.sharedMesh) byName[f.gameObject.name] = f.sharedMesh;
                foreach (var f in model.full.GetComponentsInChildren<MeshFilter>(true))
                    if (f.sharedMesh) farByMesh[f.sharedMesh] = byName;
            }
            // one decision per placed model: all its parts together
            var placed = new Dictionary<Transform, List<MeshFilter>>();
            foreach (var f in GetComponentsInChildren<MeshFilter>(true))
            {
                if (!f.sharedMesh || !f.transform.parent || !farByMesh.ContainsKey(f.sharedMesh)) continue;
                if (!placed.TryGetValue(f.transform.parent, out var parts)) placed[f.transform.parent] = parts = new List<MeshFilter>();
                parts.Add(f);
            }
            float limit = farFrom * farFrom;
            int swapped = 0;
            foreach (var pair in placed)
            {
                Bounds bounds = default;
                bool any = false;
                foreach (var f in pair.Value)
                {
                    var r = f.GetComponent<Renderer>();
                    if (!r) continue;
                    if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
                }
                if (!any) continue;
                bool far = true;
                foreach (var p in nearPoints)
                    if (p && bounds.SqrDistance(p.position) < limit) { far = false; break; }
                if (!far) continue;
                foreach (var f in pair.Value)
                    if (farByMesh[f.sharedMesh].TryGetValue(f.gameObject.name, out var light)) f.sharedMesh = light;
                swapped++;
            }
            return swapped;
        }
    }
}
