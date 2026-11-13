using System.Collections;
using Hakaton.Feel;
using Hakaton.Loot;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// A find or trophy lying in the world. What it is comes from its <see cref="LootItem"/>; when the Visual child is
    /// empty, the item's world model (or its drawing) is built there when the game runs, so one pickup prefab serves
    /// every item. Physical until picked up; bobs and glints in the item's rarity colour so it reads from afar;
    /// on pickup it flies into the bag (towards the camera) before it is credited.
    /// </summary>
    public sealed class WorldLoot : MonoBehaviour
    {
        [Tooltip("What this is. Name, price, rarity and look come from the item.")]
        public LootItem item;
        [Tooltip("Optional: the find turns out to be one of these items (weighted). On a ready-made location it is picked " +
                 "anew every outing; in the endless world once per spot. Empty — always the item above.")]
        public LootPool pool;
        [Tooltip("Size of the built 3D model on the ground relative to the item's model (the shelf shows it at 1). " +
                 "Drawings keep their own height.")]
        [Min(.1f)] public float modelScale = 1;
        public bool guaranteed = true;
        [Range(0, 1)] public float spawnChance = .7f;
        public Rigidbody body;
        [Tooltip("Bobs and spins. Leave it empty to show the item's model or drawing; authored children are kept as they are.")]
        public Transform visual;
        public ExpeditionWorld world;
        [Tooltip("Optional authored alternatives for this finding. Every selected position is saved in the scene.")]
        public Transform[] spawnPoints;
        [Tooltip("Optional looping sparkle so the item can be spotted from a distance. Tinted by rarity.")]
        public ParticleSystem glint;

        Vector3 origin;
        Quaternion rotation;
        bool recorded, focused, dressed;
        float focusPulse, popTime = -1;
        SpringFloat scale = new SpringFloat(1);
        LootSpriteView drawing;
        GameObject built;
        bool pickFixed;

        /// <summary>A creature's drop: pulled into the bag by itself once the player is close enough.</summary>
        public bool Magnetic { get; set; }
        public bool Taken { get; private set; }
        public bool Available => !Taken && gameObject.activeInHierarchy;

        void Awake() => Record();

        void Start() => Dress();

        void Record()
        {
            if (recorded) return;
            origin = transform.position;
            rotation = transform.rotation;
            recorded = true;
        }

        /// <summary>Makes this pickup the given item (a creature's drop): look, glint and what is credited.</summary>
        public void Setup(LootItem value) => SetItem(value);

        /// <summary>
        /// The endless world picks the pool item from the world seed and the spot, so a reloaded chunk shows the same
        /// thing; outing resets then keep it.
        /// </summary>
        public void PickFromPool(System.Random random)
        {
            if (!pool) return;
            pickFixed = true;
            SetItem(pool.Pick(random));
        }

        /// <summary>Becomes another item: rebuilds the look the previous one had built and retints the glint.</summary>
        void SetItem(LootItem value)
        {
            if (!value) return;
            if (value == item && dressed) return;
            item = value;
            if (built)
            {
                built.transform.SetParent(null, false);
                Destroy(built);
                built = null;
                drawing = null;
            }
            dressed = false;
            Dress();
        }

        /// <summary>Builds the item's look into an empty Visual and tints the glint, once.</summary>
        void Dress()
        {
            var catalog = world ? world.lootCatalog : null;
            if (dressed || !item || !catalog) return;
            dressed = true;
            if (visual && visual.childCount == 0)
            {
                var made = catalog.CreateVisual(item, visual, false);
                built = made;
                if (made)
                {
                    drawing = made.GetComponent<LootSpriteView>();
                    if (!drawing) made.transform.localScale *= modelScale;
                    foreach (var collider in made.GetComponentsInChildren<Collider>()) collider.enabled = false;
                }
            }
            var style = catalog.Style(item.rarity);
            if (glint && style != null && style.tintGlint)
            {
                var main = glint.main;
                main.startColor = style.colour;
            }
        }

        public void ResetForOuting(System.Random random)
        {
            Record();
            if (pool && !pickFixed) SetItem(pool.Pick(random));
            Dress();
            Taken = false;
            var point = spawnPoints != null && spawnPoints.Length > 0 ? spawnPoints[random.Next(spawnPoints.Length)] : null;
            transform.SetPositionAndRotation(point ? point.position : origin, point ? point.rotation : rotation);
            if (body) { body.position = transform.position; body.rotation = transform.rotation; body.linearVelocity = body.angularVelocity = Vector3.zero; }
            if (visual) visual.localScale = Vector3.one;
            gameObject.SetActive(guaranteed || random.NextDouble() < spawnChance);
        }

        /// <summary>Trophy pop: launched out of the defeated creature, then settles on the ground.</summary>
        public void Pop(Vector3 velocity)
        {
            popTime = Time.time;
            Magnetic = true;
            scale.Snap(.2f);
            scale.Kick(12);
            if (body) { body.isKinematic = false; body.linearVelocity = velocity; body.angularVelocity = Random.insideUnitSphere * 6; }
        }

        public void SetFocused(bool value) => focused = value;

        public bool PickUp(Transform bag)
        {
            if (Taken || !item || !world || !world.Running) return false;
            Taken = true;
            StartCoroutine(FlyIntoBag(bag));
            return true;
        }

        IEnumerator FlyIntoBag(Transform bag)
        {
            if (body) body.isKinematic = true;
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            if (glint) glint.Stop();
            Color sparkle = world.lootCatalog && item.rarity != LootRarity.Common ? world.lootCatalog.Colour(item.rarity) : new Color(1, .9f, .5f);
            world.Spawn(world.vfx ? world.vfx.pickupSparkle : null, transform.position, Quaternion.identity, sparkle, .8f);
            Vector3 start = transform.position;
            float time = 0, duration = .24f;
            while (time < duration)
            {
                time += Time.deltaTime;
                float k = Ease.InCubic(time / duration);
                Vector3 target = bag.position + bag.forward * .35f - bag.up * .25f;
                transform.position = Vector3.Lerp(start, target, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * .35f;
                if (visual) visual.localScale = Vector3.one * Mathf.Lerp(1, .15f, k);
                yield return null;
            }
            world.Collect(item);
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = true;
            // The built look keeps no colliders of its own.
            if (visual) foreach (var c in visual.GetComponentsInChildren<Collider>()) c.enabled = false;
            gameObject.SetActive(false);
        }

        void Update()
        {
            if (Taken) return;
            if (Magnetic && InMagnetReach()) PickUp(world.viewCamera.transform);
            if (!visual || Taken) return;
            focusPulse = Mathf.MoveTowards(focusPulse, focused ? 1 : 0, Time.deltaTime * 6);
            float target = 1 + focusPulse * (.18f + Mathf.Sin(Time.time * 9) * .05f);
            float s = scale.Step(target, 5, .45f, Time.deltaTime);
            visual.localScale = Vector3.one * s;
            // A drawing turns to the camera by itself; spinning would show it edge-on.
            visual.localRotation = drawing ? Quaternion.identity : Quaternion.Euler(0, Time.time * (40 + focusPulse * 120), 0);
            bool settled = popTime < 0 || Time.time - popTime > 1.2f;
            visual.localPosition = settled ? Vector3.up * (.08f + Mathf.Sin(Time.time * 2.4f + origin.x) * .06f) : Vector3.zero;
            focused = false;
        }

        /// <summary>The player's body (feet to eye) is within the magnet radius and the drop has finished popping out.</summary>
        bool InMagnetReach()
        {
            if (!world || !world.Running || !world.settings || !world.motor || !world.viewCamera) return false;
            if (popTime >= 0 && Time.time - popTime < world.settings.lootMagnetDelay) return false;
            Vector3 feet = world.motor.transform.position, eye = world.viewCamera.transform.position;
            Vector3 axis = eye - feet;
            float t = axis.sqrMagnitude > .0001f ? Mathf.Clamp01(Vector3.Dot(transform.position - feet, axis) / axis.sqrMagnitude) : 0;
            float radius = world.settings.lootMagnetRadius;
            return (transform.position - (feet + axis * t)).sqrMagnitude <= radius * radius;
        }

#if UNITY_EDITOR
        /// <summary>Edit mode: an empty Visual shows the item's model as a gizmo, a drawing as a box with its name.</summary>
        void OnDrawGizmos()
        {
            if (Application.isPlaying || !visual || visual.childCount > 0) return;
            Gizmos.color = new Color(1, .85f, .4f, .9f);
            if (!item)
            {
                Gizmos.DrawWireSphere(visual.position + Vector3.up * .3f, .3f);
                return;
            }
            var model = item.model;
            if (model)
            {
                var root = model.transform;
                foreach (var filter in model.GetComponentsInChildren<MeshFilter>())
                {
                    if (!filter.sharedMesh) continue;
                    Gizmos.matrix = visual.localToWorldMatrix * Matrix4x4.Scale(Vector3.one * modelScale) * root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                    Gizmos.DrawMesh(filter.sharedMesh);
                }
                Gizmos.matrix = Matrix4x4.identity;
            }
            else
            {
                float h = item.iconHeight;
                Gizmos.DrawWireCube(visual.position + Vector3.up * h * .5f, new Vector3(h, h, .02f));
                UnityEditor.Handles.Label(visual.position + Vector3.up * (h + .1f), item.displayName);
            }
        }
#endif
    }
}
