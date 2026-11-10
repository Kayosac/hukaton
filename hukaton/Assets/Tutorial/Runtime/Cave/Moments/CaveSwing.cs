using UnityEngine;

namespace Hakaton.Tutorial
{
    /// <summary>
    /// A hanging thing that can swing (a lantern on its bracket): a damped pendulum around this pivot.
    /// Push it with an angular kick in degrees per second (its direction is the push in world space).
    /// </summary>
    public sealed class CaveSwing : MonoBehaviour
    {
        [Min(.1f)] public float frequency = 1.4f;
        [Range(0, 1)] public float damping = .18f;
        Vector2 angle, speed;
        Quaternion rest;
        bool moving;

        void Awake() => rest = transform.localRotation;

        /// <summary>
        /// Hangs the lantern part of a bracket prefab (lantern body, glass, flame light) from a new pivot at the top of the
        /// lantern, so it can swing while the bracket stays on the wall. The meshes keep their place.
        /// </summary>
        public static CaveSwing Rig(Transform bracket)
        {
            var parts = new System.Collections.Generic.List<Transform>();
            Bounds bounds = default; bool any = false;
            foreach (var renderer in bracket.GetComponentsInChildren<Renderer>(true))
            {
                string n = renderer.name;
                if (!n.Contains("Lantern") && !n.EndsWith("_Light")) continue;
                parts.Add(renderer.transform);
                if (!any) { bounds = renderer.bounds; any = true; } else bounds.Encapsulate(renderer.bounds);
            }
            if (!any) return null;
            foreach (var light in bracket.GetComponentsInChildren<Light>(true)) parts.Add(light.transform);
            var pivot = new GameObject("Swing").transform;
            pivot.SetParent(bracket, false);
            pivot.position = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
            // the flicker remembers the light's local position: let it forget and relearn under the pivot
            var flickers = bracket.GetComponentsInChildren<Hakaton.ShopBlockout.Flicker>(true);
            var was = new bool[flickers.Length];
            for (int i = 0; i < flickers.Length; i++) { was[i] = flickers[i].enabled; flickers[i].enabled = false; }
            foreach (var part in parts) part.SetParent(pivot, true);
            for (int i = 0; i < flickers.Length; i++) flickers[i].enabled = was[i];
            return pivot.gameObject.AddComponent<CaveSwing>();
        }

        public void Push(Vector3 worldKick)
        {
            // a push along +x swings the bottom towards +x: rotation about -z; along +z: about +x
            Vector3 local = transform.parent ? transform.parent.InverseTransformDirection(worldKick) : worldKick;
            speed += new Vector2(local.z, -local.x);
            moving = true;
        }

        void Update()
        {
            if (!moving) return;
            float w = 2 * Mathf.PI * frequency;
            Vector2 accel = -w * w * angle - 2 * damping * w * speed;
            speed += accel * Time.deltaTime;
            angle += speed * Time.deltaTime;
            transform.localRotation = rest * Quaternion.Euler(angle.x, 0, angle.y);
            if (angle.sqrMagnitude < .0004f && speed.sqrMagnitude < .0004f) { moving = false; transform.localRotation = rest; }
        }
    }
}
