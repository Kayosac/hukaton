using System;
using Hakaton.Expeditions;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Backroom
{
    /// <summary>
    /// A card just bought from the goblin, brought up to the eyes to be looked at. It takes over from the table's
    /// card seamlessly (same place on screen, same size, same tilt, same side up), flies forward close to the camera
    /// (a card bought blind turns over on the way), and hangs there, breathing a little. Held with the left button it
    /// turns under the mouse and keeps spinning a moment when let go. Sent away "into the bag" it makes a show of it:
    /// winds up, spins faster and faster and shoots out of the frame still spinning and tumbling over. Skipped
    /// (<see cref="Finish"/>) it is put away at once from any of these moments. Lives in the camera's space, so it stays
    /// in front of the eyes. Purely presentation.
    /// </summary>
    public sealed class CardInspect : MonoBehaviour
    {
        /// <summary>Share of the view's height the card fills when it is held up.</summary>
        const float HeldHeight = .62f;
        const float Arrive = .55f;
        /// <summary>Into the bag: the wind-up, the spin, the shot out of the frame (seconds).</summary>
        const float WindUp = .1f, SpinUp = .3f, Shoot = .28f;
        /// <summary>Whole turns while spinning up (unhurried) (it faces the player again on the last one, at the launch).</summary>
        const int Turns = 1;
        /// <summary>How the spin gathers speed: higher starts slower and ends faster.</summary>
        const float SpinCurve = 1.8f;
        /// <summary>Degrees the card turns per screen height of mouse travel.</summary>
        const float TurnPerScreen = 320;

        CardView view;
        Camera viewCamera;
        Vector3 startLocal, heldLocal, startSize, heldSize, leaveLocal, leaveSize;
        Quaternion startTilt, leaveTurn, held = Quaternion.identity;
        float roll, age, hold, leaveAge = -1, idle;
        bool faceDown, turnedOver, dragging;
        Vector2 spin;
        Action turned, gone, launched;
        bool hasLaunched, finished;

        /// <summary>At the eyes and ready to be turned.</summary>
        public bool Arrived => age >= hold + Arrive;
        public bool Leaving => leaveAge >= 0;

        /// <summary>
        /// Lifts a world card from a point of the screen (fractions of its width and height, <paramref name="widthFraction"/>
        /// its width there, <paramref name="rollDegrees"/> its tilt) up to the eyes. <paramref name="hold"/>: seconds it
        /// waits in place while the table fades off it. <paramref name="turned"/> fires when a blind card shows its face;
        /// <paramref name="gone"/> when it has left the frame.
        /// </summary>
        public static CardInspect Launch(GameObject cardPrefab, Texture front, Texture back, int level, Camera camera,
            Vector2 screenFraction, float widthFraction, float rollDegrees, bool faceDown, float hold, Action turned, Action gone)
        {
            if (!cardPrefab || !camera)
            {
                Debug.LogWarning($"[Goblin] cannot lift the card: card prefab {(cardPrefab ? "ok" : "missing")}, camera {(camera ? "ok" : "missing")}");
                return null;
            }
            var go = Instantiate(cardPrefab);
            go.name = "Bought card";
            // The debris prefab's own flight must not run: this drives it.
            foreach (var behaviour in go.GetComponents<MonoBehaviour>()) behaviour.enabled = false;
            var view = go.GetComponentInChildren<CardView>(true);
            if (!view) { Debug.LogWarning("[Goblin] cannot lift the card: no CardView in the card prefab"); Destroy(go); return null; }
            view.Show(front, back, level);

            // Where the card lies on screen, as big as it looks there (the world may be shown in a rect of the screen).
            const float depth = .62f;
            Rect output = BrazierThrow.OutputRect(camera);
            var pixel = new Vector2(screenFraction.x * Screen.width, screenFraction.y * Screen.height);
            var viewport = new Vector2((pixel.x - output.x) / output.width, (pixel.y - output.y) / output.height);
            float tan = Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad);
            float width = Mathf.Max(.01f, widthFraction * Screen.width * 2 * depth * tan / output.height);

            var c = go.AddComponent<CardInspect>();
            c.view = view;
            c.viewCamera = camera;
            c.startLocal = Quaternion.Inverse(camera.transform.rotation)
                * (camera.ViewportToWorldPoint(new Vector3(viewport.x, viewport.y, depth)) - camera.transform.position);
            c.startSize = new Vector3(width, width * 1.5f, 1);
            // Up close: as near as the camera allows, the card filling most of the view's height.
            float near = Mathf.Max(camera.nearClipPlane * 4, .32f);
            float height = 2 * near * tan * HeldHeight;
            c.heldLocal = new Vector3(0, -.03f * height, near);
            c.heldSize = new Vector3(height / 1.5f, height, 1);
            c.roll = rollDegrees;
            c.faceDown = faceDown;
            c.hold = Mathf.Max(0, hold);
            c.turned = turned;
            c.gone = gone;
            c.Pose(c.startLocal, c.startSize, c.Tilt(0));
            return c;
        }

        /// <summary>Turns the held card under the mouse (its movement this frame, screen pixels).</summary>
        public void Turn(Vector2 mouseDelta)
        {
            if (Leaving) return;
            dragging = true;
            float k = TurnPerScreen / Mathf.Max(1, Screen.height);
            var degrees = mouseDelta * k;
            // The near face follows the mouse, like a card turned between the fingers.
            held = Quaternion.AngleAxis(-degrees.x, Vector3.up) * Quaternion.AngleAxis(degrees.y, Vector3.right) * held;
            float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
            spin = Vector2.Lerp(spin, degrees / dt, .5f);
        }

        /// <summary>Let go: it keeps the spin it had and slows down.</summary>
        public void Release() => dragging = false;

        /// <summary>Away into the bag from wherever it is now; <paramref name="launch"/> fires as it shoots off.</summary>
        public void Dismiss(Action launch = null)
        {
            if (Leaving || finished) return;
            launched = launch;
            if (faceDown && !turnedOver) { turnedOver = true; turned?.Invoke(); }
            leaveAge = 0;
            leaveLocal = Local;
            leaveSize = view.transform.localScale;
            leaveTurn = Quaternion.Inverse(viewCamera.transform.rotation) * transform.rotation;
        }

        /// <summary>
        /// Skipped on its way up: it is at the eyes at once, held and breathing (a blind card has turned and fired its
        /// turn). Nothing happens once it has arrived or is already leaving.
        /// </summary>
        public void ArriveNow()
        {
            if (Leaving || finished || Arrived) return;
            age = hold + Arrive;
            spin = Vector2.zero;
            if (faceDown && !turnedOver) { turnedOver = true; turned?.Invoke(); }
            Pose(heldLocal, heldSize, held);
        }

        /// <summary>
        /// Skipped: put away at once, from wherever it is (on its way up, held, or on its way out). A blind card that has
        /// not turned yet still fires its turn; the stow fires as if it had shot off (<paramref name="launch"/>, unless
        /// it was already sent away with one); then it is gone, and the gone callback has run when this returns.
        /// </summary>
        public void Finish(Action launch = null)
        {
            if (finished) return;
            finished = true;
            if (!Leaving) launched = launch;
            if (faceDown && !turnedOver) { turnedOver = true; turned?.Invoke(); }
            if (!hasLaunched) { hasLaunched = true; launched?.Invoke(); }
            var done = gone;
            turned = gone = launched = null;
            done?.Invoke();
            Destroy(gameObject);
        }

        /// <summary>Gone at once, without a word to whoever launched it (the table closed under it).</summary>
        public void Drop()
        {
            turned = gone = null;
            Destroy(gameObject);
        }

        Vector3 Local => Quaternion.Inverse(viewCamera.transform.rotation) * (transform.position - viewCamera.transform.position);

        void Update()
        {
            if (finished) return;
            if (!viewCamera) { Destroy(gameObject); return; }
            float dt = Time.unscaledDeltaTime;
            age += dt;
            if (Leaving) { Away(dt); return; }
            // Still in the table's place while the table fades off it.
            if (age < hold) { Pose(startLocal, startSize, Tilt(0)); return; }
            float t = Mathf.Clamp01((age - hold) / Arrive);
            if (t < 1)
            {
                float e = Ease.OutBack(t, 1.1f);
                var position = Vector3.LerpUnclamped(startLocal, heldLocal, e);
                // A little lift on the way, as if tossed up to the eyes.
                position.y += Mathf.Sin(t * Mathf.PI) * heldSize.y * .12f;
                Pose(position, Vector3.LerpUnclamped(startSize, heldSize, Ease.OutCubic(t)), Tilt(t));
                return;
            }
            // Held: the mouse turns it; let go, the spin runs down.
            if (!dragging && spin.sqrMagnitude > .01f)
            {
                var step = spin * dt;
                held = Quaternion.AngleAxis(-step.x, Vector3.up) * Quaternion.AngleAxis(step.y, Vector3.right) * held;
                spin *= Mathf.Exp(-3.5f * dt);
            }
            if (dragging) spin *= Mathf.Exp(-12 * dt);
            // Breathing: a slow sway and bob, so it never looks pinned to the glass.
            idle = Mathf.MoveTowards(idle, dragging ? .3f : 1, dt * 2);
            float time = age - hold - Arrive;
            var sway = Quaternion.Euler(Mathf.Sin(time * 1.3f) * 2.5f * idle, Mathf.Sin(time * .9f) * 3.5f * idle, Mathf.Sin(time * .7f) * 1.2f * idle);
            var bob = new Vector3(0, Mathf.Sin(time * 1.7f) * heldSize.y * .012f * idle, 0);
            Pose(heldLocal + bob, heldSize, sway * held);
        }

        /// <summary>The card's turn on the way up: its tilt straightens, a blind card turns over, a slight lean back.</summary>
        Quaternion Tilt(float t)
        {
            float flip = faceDown ? 180 * (1 - Ease.OutBack(Mathf.Clamp01((t - .1f) / .75f), 1.3f)) : 0;
            if (faceDown && !turnedOver && flip < 90)
            {
                turnedOver = true;
                turned?.Invoke();
            }
            float lean = -Mathf.Sin(t * Mathf.PI) * 16;
            return Quaternion.Euler(0, 0, roll * (1 - Ease.OutCubic(t))) * Quaternion.Euler(lean, 0, 0) * Quaternion.Euler(0, flip, 0);
        }

        void Away(float dt)
        {
            leaveAge += dt;
            float z = leaveLocal.z;
            float half = z * Mathf.Tan(viewCamera.fieldOfView * .5f * Mathf.Deg2Rad);
            float height = leaveSize.y;
            // The spot it spins up in: the middle of the view, raised a little, set back so the whole card stays in frame.
            var stage = new Vector3(0, height * .06f, z * 1.18f);
            var wound = Quaternion.Euler(8, -35, 6);
            Vector3 position;
            Quaternion turn;
            float scale;

            if (leaveAge < WindUp)
            {
                // Wind-up: straightens to face the player and pulls back the other way, like drawing a bow.
                float a = Ease.OutCubic(leaveAge / WindUp);
                position = Vector3.Lerp(leaveLocal, stage + new Vector3(0, -height * .04f, z * .04f), a);
                turn = Quaternion.Slerp(leaveTurn, wound, a);
                scale = Mathf.Lerp(1, .96f, a);
            }
            else if (leaveAge < WindUp + SpinUp)
            {
                // The spin: faster and faster around its upright, lifting, growing a little.
                float b = (leaveAge - WindUp) / SpinUp;
                float yaw = -35 + (Turns * 360 + 35) * Mathf.Pow(b, SpinCurve);
                float rise = Ease.InOutSine(b);
                position = Vector3.Lerp(stage + new Vector3(0, -height * .04f, z * .04f), stage + new Vector3(0, height * .05f, 0), rise);
                // A slight shudder as it gathers speed.
                position += new Vector3(Mathf.Sin(leaveAge * 71), Mathf.Sin(leaveAge * 53), 0) * (height * .008f * b);
                turn = Quaternion.Euler(8 * (1 - b), 0, 6 * (1 - b) + Mathf.Sin(b * Mathf.PI * 2) * 4) * Quaternion.Euler(0, yaw, 0);
                scale = Mathf.Lerp(.96f, 1.06f, rise);
            }
            else
            {
                float c = Mathf.Clamp01((leaveAge - WindUp - SpinUp) / Shoot);
                if (!hasLaunched)
                {
                    hasLaunched = true;
                    launched?.Invoke();
                }
                // The shot: out of the bottom corner of the frame at full spin, tumbling over, shrinking away.
                var from = stage + new Vector3(0, height * .05f, 0);
                var target = new Vector3(half * 1.1f, -half - height * 1.4f, z * 1.4f);
                float go = Ease.InCubic(c);
                position = Vector3.Lerp(from, target, go);
                position.y += Mathf.Sin(c * Mathf.PI) * height * .25f;
                // The spin keeps the speed it had at the launch (and a bit more), and it tips over as it goes.
                float speed = (Turns * 360 + 35) * SpinCurve / SpinUp;
                float yaw = speed * (leaveAge - WindUp - SpinUp) * (1 + .5f * c);
                turn = Quaternion.Euler(-75 * go, 0, -40 * go) * Quaternion.Euler(0, yaw, 0);
                scale = Mathf.Lerp(1.06f, .3f, go);
            }

            Pose(position, leaveSize * scale, turn);
            if (leaveAge < WindUp + SpinUp + Shoot) return;
            gone?.Invoke();
            gone = null;
            Destroy(gameObject);
        }

        void Pose(Vector3 local, Vector3 size, Quaternion turn)
        {
            var camera = viewCamera.transform;
            transform.SetPositionAndRotation(camera.position + camera.rotation * local, camera.rotation * turn);
            view.transform.localScale = size;
        }

        void OnDestroy()
        {
            // Taken away before it left (the table closed): still counts as gone.
            gone?.Invoke();
            gone = null;
        }
    }
}
