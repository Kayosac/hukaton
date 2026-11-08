using System.Collections;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// A find or trophy lying in the world. Physical until picked up; bobs and glints so it reads from afar;
    /// on pickup it flies into the bag (towards the camera) before it is credited.
    /// </summary>
    public sealed class WorldLoot : MonoBehaviour
    {
        public string itemId;
        public string displayName = "Лесная находка";
        public int value = 12;
        public bool guaranteed = true;
        [Range(0, 1)] public float spawnChance = .7f;
        public Rigidbody body;
        public Transform visual;
        public ExpeditionWorld world;
        [Tooltip("Optional authored alternatives for this finding. Every selected position is saved in the scene.")]
        public Transform[] spawnPoints;
        [Tooltip("Optional looping sparkle so the item can be spotted from a distance.")]
        public ParticleSystem glint;

        Vector3 origin;
        Quaternion rotation;
        bool recorded, focused;
        float focusPulse, popTime = -1;
        SpringFloat scale = new SpringFloat(1);

        public bool Taken { get; private set; }
        public bool Available => !Taken && gameObject.activeInHierarchy;

        void Awake() => Record();

        void Record()
        {
            if (recorded) return;
            origin = transform.position;
            rotation = transform.rotation;
            recorded = true;
        }

        public void ResetForOuting(System.Random random)
        {
            Record();
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
            scale.Snap(.2f);
            scale.Kick(12);
            if (body) { body.isKinematic = false; body.linearVelocity = velocity; body.angularVelocity = Random.insideUnitSphere * 6; }
        }

        public void SetFocused(bool value) => focused = value;

        public bool PickUp(Transform bag)
        {
            if (Taken || !world || !world.Running) return false;
            Taken = true;
            StartCoroutine(FlyIntoBag(bag));
            return true;
        }

        IEnumerator FlyIntoBag(Transform bag)
        {
            if (body) body.isKinematic = true;
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            if (glint) glint.Stop();
            world.Spawn(world.vfx ? world.vfx.pickupSparkle : null, transform.position, Quaternion.identity, new Color(1, .9f, .5f), .8f);
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
            world.Collect(itemId, displayName, value);
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = true;
            gameObject.SetActive(false);
        }

        void Update()
        {
            if (!visual || Taken) return;
            focusPulse = Mathf.MoveTowards(focusPulse, focused ? 1 : 0, Time.deltaTime * 6);
            float target = 1 + focusPulse * (.18f + Mathf.Sin(Time.time * 9) * .05f);
            float s = scale.Step(target, 5, .45f, Time.deltaTime);
            visual.localScale = Vector3.one * s;
            visual.localRotation = Quaternion.Euler(0, Time.time * (40 + focusPulse * 120), 0);
            bool settled = popTime < 0 || Time.time - popTime > 1.2f;
            visual.localPosition = settled ? Vector3.up * (.08f + Mathf.Sin(Time.time * 2.4f + origin.x) * .06f) : Vector3.zero;
            focused = false;
        }
    }
}
