using System.Collections.Generic;
using Hakaton.Character;
using Hakaton.Loot;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace Hakaton.Trading.Unity
{
    /// <summary>
    /// A close look at a 3D item on the display. In the closed shop R on an aimed-at model lifts a copy of it from the
    /// shelf to the middle of the view (the original only hides meanwhile: its pose stays the shop's), over a dimmed
    /// backdrop and in its own soft light. The mouse turns it (it follows smoothly and coasts after a flick), the wheel
    /// brings it closer or takes it away; R, the right button or Escape fly it back. Movement and look are held like
    /// under any window (<see cref="PlayerControlGate"/>), the held stock is lowered. Ends at once when the item leaves
    /// the display, the shop opens, a menu comes up or the scene goes; flies back when someone else takes control (the
    /// tutorial). TradingPrototype creates it at runtime: nothing to wire in the scene.
    /// </summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(610)]
    public sealed class DisplayItemInspector : MonoBehaviour
    {
        /// <summary>Added to the aim's prompt on a model that can be looked at.</summary>
        public const string PromptSuffix = " · R Рассмотреть";
        /// <summary>The hint line while looking.</summary>
        public const string Hint = "Мышь — крутить · Колесо — ближе · R / ПКМ — вернуть";

        [Header("Framing")]
        [Tooltip("Share of the view's height the item's bounding sphere spans at rest.")]
        [Range(.2f, .9f)] public float heightShare = .52f;
        [Tooltip("How far in front of the eye the item is held, metres, when nothing in view is closer.")]
        [Min(.05f)] public float holdDistance = .5f;
        [Tooltip("Never nearer than this, metres, however close the shelf or a wall is (the picture is the same at any distance).")]
        [Min(.02f)] public float minHoldDistance = .1f;
        [Tooltip("Wheel: the nearest and the farthest the item comes, as multiples of the hold distance.")]
        public Vector2 zoomRange = new Vector2(.62f, 1.3f);
        [Tooltip("Each wheel notch multiplies the distance by this (closer) or divides by it (farther).")]
        [Range(.5f, .99f)] public float zoomStep = .9f;
        [Tooltip("How quickly the item follows the wheel, per second.")]
        [Min(1)] public float zoomResponse = 11;

        [Header("Turning")]
        [Tooltip("Degrees per mouse count.")]
        [Min(.01f)] public float degreesPerCount = .3f;
        [Tooltip("How quickly the spin catches up with the mouse, per second.")]
        [Min(1)] public float grip = 24;
        [Tooltip("How quickly a flicked item stops by itself, per second.")]
        [Min(.1f)] public float drag = 4.5f;
        [Min(10)] public float maxSpin = 900;

        [Header("Motion")]
        [Min(.05f)] public float flyInSeconds = .5f;
        [Min(.05f)] public float flyBackSeconds = .36f;
        [Tooltip("How high the flight arcs, as a share of its length.")]
        [Range(0, .5f)] public float arc = .1f;
        [Tooltip("The size passes its mark by a little on arrival and settles (0: none).")]
        [Range(0, 3)] public float arrivalOvershoot = 1.1f;
        [Tooltip("The turn it arrives in after facing the eye the way it faces the customers: yaw, pitch (degrees).")]
        public Vector2 presentTurn = new Vector2(-24, 14);
        [Tooltip("A slow hover while held: amplitude as a share of the item's size, cycles per second.")]
        [Range(0, .1f)] public float hoverAmount = .018f;
        [Min(0)] public float hoverRate = .3f;

        [Header("Backdrop and light")]
        public Color dimColour = new Color(.035f, .028f, .045f, 1);
        [Tooltip("Opacity of the backdrop in the middle of the view and at its corners.")]
        [Range(0, 1)] public float dimCentre = .66f;
        [Range(0, 1)] public float dimEdge = .9f;
        public Color keyColour = new Color(1, .92f, .8f);
        public Color rimColour = new Color(.62f, .74f, 1);
        [Tooltip("Brightness the lights give the item, whatever its size and distance (intensity = this × distance²).")]
        [Min(0)] public float keyBrightness = 2.4f;
        [Min(0)] public float rimBrightness = 1.6f;

        enum Phase { Idle, FlyingIn, Holding, FlyingBack }

        TradingPrototype trade;
        Phase phase;
        int slot = -1, beganFrame = -1;
        long itemId;
        GameObject original;
        Vector3 centre;
        float radius, depth, restScale, nearestZoom, zoom = 1, zoomTarget = 1, flight, tanV;
        Vector2 spin;
        Quaternion heldTurn;
        Vector3 fromPosition;
        Quaternion fromTurn;
        float fromScale;
        Transform rig, holder;
        MeshRenderer backdrop;
        Material backdropMaterial;
        Texture2D vignette;
        Light key, rim;
        GameObject checkedObject;
        bool checkedIsModel;
        readonly List<Vector3> vertices = new List<Vector3>();

        /// <summary>Lifting, held or turning in front of the eye (not on the way back).</summary>
        public bool Presenting => phase == Phase.FlyingIn || phase == Phase.Holding;
        /// <summary>From the press until the item is back on the shelf: control is held all this time.</summary>
        public bool Active => phase != Phase.Idle;
        /// <summary>The item being looked at.</summary>
        public ItemInstance Item { get; private set; }

        public void Bind(TradingPrototype value) => trade = value;

        TradingSceneBindings World => trade ? trade.World : null;
        Camera Eye => World ? World.ViewCamera : null;

        /// <summary>The aim rests on this display slot: is there a model on it that can be looked at now?</summary>
        public bool CanInspect(int displaySlot) => phase == Phase.Idle && ShownModel(displaySlot) != null;

        GameObject ShownModel(int displaySlot)
        {
            if (!trade || trade.Session == null || displaySlot < 0 || displaySlot >= trade.Session.Slots.Count) return null;
            var shown = trade.DisplayedObject(displaySlot);
            if (!shown || !shown.activeInHierarchy || trade.Session.Slots[displaySlot] == null) return null;
            if (shown != checkedObject) { checkedObject = shown; checkedIsModel = IsModel(shown); }
            return checkedIsModel ? shown : null;
        }

        /// <summary>A 3D model (the collectible curios), not a drawing standing on the shelf.</summary>
        public static bool IsModel(GameObject shown)
        {
            if (!shown || shown.GetComponentInChildren<LootSpriteView>(true)) return false;
            foreach (var renderer in shown.GetComponentsInChildren<Renderer>())
                if (renderer is MeshRenderer && renderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh
                    || renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh) return true;
            return false;
        }

        /// <summary>Starts the look at the model on this slot; false when there is none or the player is busy.</summary>
        public bool TryBegin(int displaySlot)
        {
            var eye = Eye;
            var session = trade ? trade.Session : null;
            // Free walk in the closed shop with nothing else holding the player (a window, the tutorial's lines).
            if (phase != Phase.Idle || session == null || session.IsOpen || session.ExpeditionId != 0 || !World.Control
                || World.Control.HasModalOwner || trade.ViewTransitioning || GamePause.Suspended || !eye || eye.orthographic) return false;
            var shown = ShownModel(displaySlot);
            if (!shown || !Measure(shown, out centre, out float localRadius)) return false;
            EnsureRig();

            slot = displaySlot;
            Item = trade.Session.Slots[slot];
            itemId = Item.Id;
            original = shown;
            var source = shown.transform;
            float sourceScale = Uniform(source.lossyScale);
            holder.SetPositionAndRotation(source.TransformPoint(centre), source.rotation);
            holder.localScale = Vector3.one * sourceScale;
            var copy = Instantiate(shown, holder, false);
            copy.name = shown.name + " (рассматривается)";
            Strip(copy);
            var relative = source.lossyScale / sourceScale;
            copy.transform.localRotation = Quaternion.identity;
            copy.transform.localScale = relative;
            copy.transform.localPosition = -Vector3.Scale(centre, relative);
            radius = localRadius * Mathf.Max(Mathf.Abs(relative.x), Mathf.Abs(relative.y), Mathf.Abs(relative.z));
            original.SetActive(false);

            // Held as close as the view allows, so nothing stands between the eye and the backdrop.
            tanV = Mathf.Tan(eye.fieldOfView * .5f * Mathf.Deg2Rad);
            float ratio = InspectionFraming.RadiusRatio(tanV, heightShare);
            float farthest = Mathf.Max(zoomRange.x, zoomRange.y);
            float reach = holdDistance * (farthest + ratio) * InspectionFraming.BackdropGap + .05f;
            depth = InspectionFraming.HoldDepth(holdDistance, Clearance(eye, reach), farthest, ratio, minHoldDistance);
            nearestZoom = Mathf.Min(farthest, InspectionFraming.NearestZoom(zoomRange.x, ratio, (eye.nearClipPlane + .01f) / depth));
            zoom = zoomTarget = Mathf.Clamp(1, nearestZoom, farthest);
            restScale = InspectionFraming.FitScale(radius, depth, tanV, heightShare);
            heldTurn = RestTurn(source, eye.transform);
            spin = Vector2.zero;
            fromPosition = holder.position;
            fromTurn = holder.rotation;
            fromScale = sourceScale;
            flight = 0;
            phase = Phase.FlyingIn;
            beganFrame = Time.frameCount;

            World.Control.SetBlocked(this, true);
            KeepCursor();
            if (World.HeldStock) World.HeldStock.Suppressed = true;
            rig.gameObject.SetActive(true);
            Present(0);
            trade.ReportInspection(slot, true);
            return true;
        }

        void Update()
        {
            if (phase == Phase.Idle) return;
            if (!StillOnDisplay()) { Finish(); return; }
            KeepCursor();
            float dt = Time.unscaledDeltaTime;
            if (phase != Phase.FlyingBack)
            {
                if (World.Control.HeldByOthers(this) || EndPressed()) { BeginReturn(); return; }
                Turn(dt);
            }
        }

        /// <summary>The item is still the one on its slot and the shop is still closed and in front of the player.</summary>
        bool StillOnDisplay()
        {
            var session = trade ? trade.Session : null;
            return session != null && trade.isActiveAndEnabled && Eye && !session.IsOpen && session.ExpeditionId == 0
                && !GamePause.Suspended && original && trade.DisplayedObject(slot) == original
                && session.Slots[slot] != null && session.Slots[slot].Id == itemId;
        }

        bool EndPressed()
        {
            if (Time.frameCount == beganFrame) return false;
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !GamePause.EscapeConsumed)
            {
                // Escape is spent on putting the item back; the pause menu does not open with it.
                GamePause.ConsumeEscape();
                return true;
            }
            return keyboard != null && keyboard.rKey.wasPressedThisFrame || mouse != null && mouse.rightButton.wasPressedThisFrame;
        }

        void Turn(float dt)
        {
            var mouse = Mouse.current;
            var eye = Eye.transform;
            Vector2 turned = Vector2.zero;
            if (mouse != null && Application.isFocused)
            {
                var delta = mouse.delta.ReadValue();
                // Drag right: the face turns right; drag up: it turns up.
                turned = new Vector2(-delta.x, delta.y) * degreesPerCount;
                float scroll = mouse.scroll.ReadValue().y;
                zoomTarget = InspectionFraming.StepZoom(zoomTarget, scroll, zoomStep, nearestZoom, Mathf.Max(zoomRange.x, zoomRange.y));
            }
            spin = InspectionFraming.Spin(spin, turned, dt, grip, drag, maxSpin);
            if (spin != Vector2.zero)
                heldTurn = Quaternion.AngleAxis(spin.x * dt, eye.up) * Quaternion.AngleAxis(spin.y * dt, eye.right) * heldTurn;
            zoom = Mathf.Lerp(zoom, zoomTarget, 1 - Mathf.Exp(-zoomResponse * dt));
        }

        /// <summary>Puts the item back on the shelf, as R does.</summary>
        public void Return() => BeginReturn();

        void BeginReturn()
        {
            if (phase == Phase.FlyingBack || phase == Phase.Idle) return;
            fromPosition = holder.position;
            fromTurn = holder.rotation;
            fromScale = holder.localScale.x;
            flight = 0;
            phase = Phase.FlyingBack;
        }

        void LateUpdate()
        {
            if (phase == Phase.Idle || !holder || !Eye) return;
            float dt = Time.unscaledDeltaTime;
            var eye = Eye.transform;
            var source = original.transform;
            if (phase == Phase.FlyingIn)
            {
                flight = Mathf.Min(1, flight + dt / flyInSeconds);
                float along = InspectionFraming.Smoother(flight);
                Vector3 to = HeldPosition(eye);
                Vector3 path = Vector3.Lerp(fromPosition, to, along) + eye.up * (Mathf.Sin(along * Mathf.PI) * arc * Vector3.Distance(fromPosition, to));
                float size = Mathf.LerpUnclamped(fromScale, restScale, InspectionFraming.BackOut(flight, arrivalOvershoot));
                holder.SetPositionAndRotation(path, Quaternion.Slerp(fromTurn, heldTurn, InspectionFraming.Smoother(Mathf.Min(1, flight * 1.15f))));
                holder.localScale = Vector3.one * size;
                Present(along);
                if (flight >= 1) phase = Phase.Holding;
            }
            else if (phase == Phase.Holding)
            {
                holder.SetPositionAndRotation(HeldPosition(eye), heldTurn);
                holder.localScale = Vector3.one * restScale;
                Present(1);
            }
            else
            {
                flight = Mathf.Min(1, flight + dt / flyBackSeconds);
                float along = InspectionFraming.Smoother(flight);
                Vector3 to = source.TransformPoint(centre);
                Vector3 path = Vector3.Lerp(fromPosition, to, along) + eye.up * (Mathf.Sin(along * Mathf.PI) * arc * .6f * Vector3.Distance(fromPosition, to));
                holder.SetPositionAndRotation(path, Quaternion.Slerp(fromTurn, source.rotation, along));
                holder.localScale = Vector3.one * Mathf.Lerp(fromScale, Uniform(source.lossyScale), along);
                Present(1 - along);
                if (flight >= 1) Finish();
            }
        }

        Vector3 HeldPosition(Transform eye)
        {
            float hover = Mathf.Sin(Time.unscaledTime * hoverRate * 2 * Mathf.PI) * hoverAmount * radius * restScale;
            return eye.position + eye.forward * (depth * zoom) + eye.up * hover;
        }

        /// <summary>The face the customers see turned to the eye, upright in the view, then turned three-quarter.</summary>
        Quaternion RestTurn(Transform source, Transform eye)
        {
            // The display anchors face the customers' camera with their -X (CurioSetup.FaceShelfToCamera).
            Vector3 front = Vector3.ProjectOnPlane(-source.right, Vector3.up);
            if (front.sqrMagnitude < 1e-4f) front = Vector3.ProjectOnPlane(eye.position - source.position, Vector3.up);
            if (front.sqrMagnitude < 1e-4f) front = Vector3.forward;
            var shelfFrame = Quaternion.LookRotation(front.normalized, Vector3.up);
            var viewFrame = Quaternion.LookRotation(-eye.forward, eye.up);
            var turn = Quaternion.AngleAxis(presentTurn.x, eye.up) * Quaternion.AngleAxis(presentTurn.y, eye.right);
            return turn * viewFrame * Quaternion.Inverse(shelfFrame) * source.rotation;
        }

        /// <summary>Backdrop and lights at <paramref name="amount"/> (0: none, 1: full), placed for the item where it is now.</summary>
        void Present(float amount)
        {
            var camera = Eye;
            var eye = camera.transform;
            float size = radius * holder.localScale.x;
            Vector3 middle = holder.position;
            // Just behind the item, never in front of where it stands: on the shelf at first, at the hold spot later.
            float itemDepth = Vector3.Dot(middle - eye.position, eye.forward);
            float back = Mathf.Max(InspectionFraming.BackdropDepth(depth, Mathf.Max(zoomRange.x, zoomRange.y), InspectionFraming.RadiusRatio(tanV, heightShare)),
                itemDepth + size * 1.15f);
            back = Mathf.Min(back, camera.farClipPlane * .9f);
            float height = 2 * back * Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad) * 1.15f;
            var plate = backdrop.transform;
            plate.SetPositionAndRotation(eye.position + eye.forward * back, eye.rotation);
            plate.localScale = new Vector3(height * camera.aspect * 1.1f, height, 1);
            var colour = dimColour;
            colour.a = amount;
            backdropMaterial.color = colour;
            backdrop.enabled = amount > .001f;

            Place(key, middle + (eye.right * -1.3f + eye.up * 1.15f - eye.forward * 1.7f) * size, middle, keyBrightness * amount);
            Place(rim, middle + (eye.right * 1.5f + eye.up * .55f + eye.forward * 1.6f) * size, middle, rimBrightness * amount);
        }

        /// <summary>A light at <paramref name="at"/> giving the item at <paramref name="target"/> the same brightness at any size.</summary>
        static void Place(Light light, Vector3 at, Vector3 target, float brightness)
        {
            float distance = Mathf.Max(.005f, Vector3.Distance(at, target));
            light.transform.position = at;
            light.intensity = brightness * distance * distance;
            light.range = distance * 3;
            light.enabled = brightness > .001f;
        }

        void Finish()
        {
            bool wasActive = phase != Phase.Idle;
            phase = Phase.Idle;
            if (original) original.SetActive(true);
            original = null;
            if (holder) for (int i = holder.childCount - 1; i >= 0; i--) Destroy(holder.GetChild(i).gameObject);
            if (rig) rig.gameObject.SetActive(false);
            if (World)
            {
                if (World.HeldStock) World.HeldStock.Suppressed = false;
                if (World.Control)
                {
                    // A window that took over meanwhile gets the free cursor the gate gave it.
                    if (wasActive && World.Control.HeldByOthers(this)) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
                    World.Control.SetBlocked(this, false);
                }
            }
            int ended = slot;
            slot = -1;
            Item = null;
            if (wasActive && trade) trade.ReportInspection(ended, false);
        }

        void KeepCursor()
        {
            // The gate frees the cursor for windows; here the mouse turns the item, so it stays captured and hidden.
            if (!Application.isFocused || World.Control.HeldByOthers(this)) return;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        /// <summary>The nearest surface in view (depth along the view), up to <paramref name="reach"/>.</summary>
        static float Clearance(Camera camera, float reach)
        {
            var eye = camera.transform;
            float nearest = reach;
            const int columns = 6, rows = 4;
            for (int y = 0; y <= rows; y++)
                for (int x = 0; x <= columns; x++)
                {
                    var ray = camera.ViewportPointToRay(new Vector3(x / (float)columns, y / (float)rows));
                    float cos = Mathf.Max(.2f, Vector3.Dot(ray.direction, eye.forward));
                    if (Physics.SphereCast(ray, .02f, out var hit, reach / cos, ~(1 << 2), QueryTriggerInteraction.Ignore))
                        nearest = Mathf.Min(nearest, Vector3.Dot(hit.point - eye.position, eye.forward));
                }
            return Mathf.Max(0, nearest);
        }

        /// <summary>
        /// The model's centre (of its bounds) and the radius of the sphere around it that holds every vertex, in its root's
        /// space; for a mesh that cannot be read, its bounds' corners.
        /// </summary>
        bool Measure(GameObject shown, out Vector3 middle, out float reach)
        {
            middle = Vector3.zero;
            reach = 0;
            var root = shown.transform.worldToLocalMatrix;
            var points = new List<Vector3>();
            foreach (var renderer in shown.GetComponentsInChildren<Renderer>())
            {
                Mesh mesh = null;
                if (renderer is MeshRenderer && renderer.TryGetComponent(out MeshFilter filter)) mesh = filter.sharedMesh;
                else if (renderer is SkinnedMeshRenderer skinned) mesh = skinned.sharedMesh;
                if (!mesh || !renderer.enabled) continue;
                var toRoot = root * renderer.transform.localToWorldMatrix;
                vertices.Clear();
                if (mesh.isReadable) mesh.GetVertices(vertices);
                else
                {
                    var box = mesh.bounds;
                    for (int corner = 0; corner < 8; corner++)
                        vertices.Add(box.center + Vector3.Scale(box.extents, new Vector3(
                            (corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                }
                foreach (var v in vertices) points.Add(toRoot.MultiplyPoint3x4(v));
            }
            if (points.Count == 0) return false;
            var bounds = new Bounds(points[0], Vector3.zero);
            foreach (var p in points) bounds.Encapsulate(p);
            middle = bounds.center;
            foreach (var p in points) reach = Mathf.Max(reach, (p - middle).sqrMagnitude);
            reach = Mathf.Sqrt(reach);
            return reach > 1e-5f;
        }

        /// <summary>The copy only shows: no colliders, no behaviour of its own, no shadows across the shop.</summary>
        static void Strip(GameObject copy)
        {
            foreach (var collider in copy.GetComponentsInChildren<Collider>(true)) { collider.enabled = false; Destroy(collider); }
            foreach (var behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
            foreach (var renderer in copy.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            copy.SetActive(true);
        }

        static float Uniform(Vector3 scale) => (Mathf.Abs(scale.x) + Mathf.Abs(scale.y) + Mathf.Abs(scale.z)) / 3;

        /// <summary>The backdrop, the item's holder and its two lights, made once and kept hidden between looks.</summary>
        void EnsureRig()
        {
            if (rig) return;
            rig = new GameObject("Display item inspection").transform;
            holder = new GameObject("Item").transform;
            holder.SetParent(rig, false);

            var plate = new GameObject("Backdrop");
            plate.transform.SetParent(rig, false);
            plate.AddComponent<MeshFilter>().sharedMesh = QuadMesh();
            backdrop = plate.AddComponent<MeshRenderer>();
            backdrop.shadowCastingMode = ShadowCastingMode.Off;
            backdrop.receiveShadows = false;
            backdrop.lightProbeUsage = LightProbeUsage.Off;
            backdrop.reflectionProbeUsage = ReflectionProbeUsage.Off;
            // Drawn last among the scene's sorted transparencies (sprites, effects): over everything behind it.
            backdrop.sortingOrder = short.MaxValue;
            var shader = Shader.Find("Sprites/Default");
            if (!shader) shader = Shader.Find("UI/Default");
            backdropMaterial = new Material(shader) { name = "Inspection backdrop", renderQueue = 3000 };
            vignette = Vignette(64, dimCentre, dimEdge);
            backdropMaterial.mainTexture = vignette;
            backdrop.sharedMaterial = backdropMaterial;

            key = MakeLight("Key light", keyColour);
            rim = MakeLight("Rim light", rimColour);
            rig.gameObject.SetActive(false);
        }

        Light MakeLight(string name, Color colour)
        {
            var light = new GameObject(name).AddComponent<Light>();
            light.transform.SetParent(rig, false);
            light.type = LightType.Point;
            light.color = colour;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            light.enabled = false;
            return light;
        }

        static Mesh QuadMesh()
        {
            var mesh = new Mesh { name = "Inspection backdrop" };
            mesh.SetVertices(new[] { new Vector3(-.5f, -.5f), new Vector3(.5f, -.5f), new Vector3(-.5f, .5f), new Vector3(.5f, .5f) });
            mesh.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) });
            mesh.SetTriangles(new[] { 0, 2, 1, 2, 3, 1 }, 0);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(1, 1, .01f));
            return mesh;
        }

        /// <summary>White with the backdrop's opacity: lighter in the middle, darker toward the corners.</summary>
        static Texture2D Vignette(int size, float middle, float corner)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                { name = "Inspection vignette", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + .5f) / size * 2 - 1, v = (y + .5f) / size * 2 - 1;
                    float t = Mathf.Clamp01(Mathf.Sqrt(u * u + v * v) / 1.41f);
                    pixels[y * size + x] = new Color(1, 1, 1, Mathf.Lerp(middle, corner, t * t * (3 - 2 * t)));
                }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        void OnDisable() => Finish();

        void OnDestroy()
        {
            if (rig) Destroy(rig.gameObject);
            if (backdropMaterial) Destroy(backdropMaterial);
            if (vignette) Destroy(vignette);
        }
    }
}
