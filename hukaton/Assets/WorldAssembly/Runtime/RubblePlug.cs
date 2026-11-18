using Hakaton.Expeditions;
using Hakaton.Expeditions.Cards;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.WorldAssembly.Unity
{
    /// <summary>
    /// A heap of loose earth that fills a way from wall to wall (a dungeon stash's lock). Only the «Сквозняк» card's gust
    /// shifts it: the heap is blown off along the wind as tumbling chunks in a cloud of dust, and the way is open for the
    /// rest of the outing. The heap is solid as it looks; nothing invisible blocks.
    /// </summary>
    public sealed class RubblePlug : MonoBehaviour
    {
        [Tooltip("The heap as it stands (with its own colliders).")]
        public GameObject intact;
        [Tooltip("Solid across the way, within the heap's shape.")]
        public Collider barrier;
        [Tooltip("A gust reaching this far from the heap's middle takes it, m.")]
        [Min(.2f)] public float reach = 1.8f;
        [Tooltip("Chunks blown off (each child is one chunk).")]
        public GameObject debris;
        [Tooltip("Dust as it goes (a VfxBurst prefab).")]
        public GameObject dust;
        [Min(1)] public int chunks = 9;

        public bool Cleared { get; private set; }
        ExpeditionWorld world;
        float nextLook;

        public void Init(ExpeditionWorld owner, GameObject chunkPrefab, GameObject dustPrefab)
        {
            world = owner;
            if (chunkPrefab) debris = chunkPrefab;
            if (dustPrefab) dust = dustPrefab;
        }

        void Update()
        {
            if (Cleared || Time.time < nextLook) return;
            nextLook = Time.time + .1f;
            var middle = transform.position + Vector3.up * 1.2f;
            foreach (var gust in FindObjectsByType<DraftGust>(FindObjectsSortMode.None))
                if (gust && gust.Blowing && gust.Strip.Overlaps(middle, reach))
                {
                    Blow(gust.Strip.Direction);
                    return;
                }
        }

        /// <summary>The gust takes the heap: chunks fly off along <paramref name="wind"/>, the way opens.</summary>
        public void Blow(Vector3 wind)
        {
            if (Cleared) return;
            Cleared = true;
            wind.y = 0;
            wind = wind.sqrMagnitude > .01f ? wind.normalized : transform.forward;
            if (barrier) barrier.enabled = false;
            if (intact) intact.SetActive(false);
            var burst = dust ? dust.GetComponent<VfxBurst>() : null;
            var colour = new Color(.55f, .42f, .32f);
            VfxBurst.Spawn(burst, transform.position + Vector3.up * .8f, Quaternion.LookRotation(wind), transform, colour, 2.2f);
            VfxBurst.Spawn(burst, transform.position + wind * 2 + Vector3.up * 1.2f, Quaternion.LookRotation(wind), transform, colour, 1.8f);
            if (world && world.feel) world.feel.AddTrauma(.2f);
            if (!debris || debris.transform.childCount == 0) return;
            for (int i = 0; i < chunks; i++)
            {
                var source = debris.transform.GetChild(Random.Range(0, debris.transform.childCount));
                var at = transform.position + transform.right * Random.Range(-1.3f, 1.3f) + Vector3.up * Random.Range(.3f, 2.2f);
                var chunk = Instantiate(source.gameObject, at, Random.rotation, transform);
                chunk.name = "Blown " + source.name;
                foreach (var col in chunk.GetComponentsInChildren<Collider>()) DestroyImmediate(col);
                var mesh = chunk.GetComponentInChildren<MeshFilter>();
                if (mesh)
                {
                    var mc = mesh.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mesh.sharedMesh;
                    mc.convex = true;
                }
                var body = chunk.AddComponent<Rigidbody>();
                body.mass = 2;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.linearVelocity = wind * Random.Range(6f, 11f) + Vector3.up * Random.Range(1f, 3f);
                body.angularVelocity = Random.insideUnitSphere * 8;
            }
        }
    }
}
