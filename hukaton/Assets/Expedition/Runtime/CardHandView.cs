using System.Collections.Generic;
using Hakaton.Decks;
using Hakaton.Feel;
using UnityEngine;
using UnityEngine.Serialization;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// First-person viewmodel of the hand: a wide, close fan of large cards centred under the crosshair
    /// (bottoms cut by the screen edge, like Shroom &amp; Gloom), spring-driven; it sways with the
    /// look, bobs with the walk, lifts the selected card and winds it back while a throw is charged.
    /// The draw pile sits as a small stack of backs bottom-right and thins out as it runs low. Polls the model, so selection
    /// changes never re-deal the hand.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class CardHandView : MonoBehaviour
    {
        public ExpeditionWorld world;
        [Tooltip("Five authored card slots (children).")]
        public CardView[] slots;
        [Tooltip("Up to three authored backs showing what is left in the draw pile.")]
        public CardView[] stack;
        [FormerlySerializedAs("explorationBack")]
        public Texture cardBack;

        [Header("Fan layout (camera space, metres)")]
        public Vector3 pivot = new Vector3(0, -1.42f, .55f);
        [Min(.05f)] public float radius = 1.1f;
        [Min(0)] public float degreesPerCard = 6.25f;
        [Min(.01f)] public float cardWidth = .18f;
        [Tooltip("How far unselected cards sink below the fan arc.")]
        public float restDrop = .04f;
        public float selectedLift = .08f, selectedToward = .04f;
        [Min(1)] public float selectedScale = 1.15f;
        [Header("Draw pile stack")]
        public Vector3 stackPosition = new Vector3(.6f, -.36f, .55f);
        public float stackRoll = -16;
        [Header("Charge wind-up")]
        public Vector3 chargePull = new Vector3(.06f, -.025f, -.09f);
        public float chargeTilt = 22, chargeTremble = .0035f;
        [Header("Motion")]
        [Min(.1f)] public float cardFrequency = 7.5f;
        [Range(0, 2)] public float cardDamping = .62f;
        [Tooltip("Metres of lag per degree/second of look rotation.")]
        public float swayPerDegreePerSecond = .00018f;
        public float maxSway = .05f;
        [Min(.1f)] public float swayFrequency = 4.5f;
        [Range(0, 2)] public float swayDamping = .5f;
        public float bobAmplitude = .008f;
        [Header("Colours")]
        public Color returnGlow = new Color(1f, .82f, .35f);

        sealed class Slot
        {
            public CardView view;
            public int copyId = int.MinValue;
            public SpringVector3 position;
            public SpringFloat scale;
            public Quaternion rotation = Quaternion.identity;
            public float drawDelay;
        }

        readonly List<Slot> active = new List<Slot>();
        readonly List<int> lastIds = new List<int>();
        int lastStackCount = -1;
        SpringVector3 sway;
        Quaternion lastCamera;
        float bobPhase;
        bool hidden = true;

        void Awake()
        {
            foreach (var view in slots) active.Add(new Slot { view = view });
            Hide();
        }

        public void Hide()
        {
            hidden = true;
            foreach (var slot in active) { slot.view.Hide(); slot.copyId = int.MinValue; }
            foreach (var card in stack) card.Hide();
            lastIds.Clear();
            lastStackCount = -1;
        }

        /// <summary>Starts a fresh outing: everything is dealt from below.</summary>
        public void Deal()
        {
            Hide();
            hidden = false;
            lastCamera = transform.parent ? transform.parent.rotation : Quaternion.identity;
            sway.Snap(Vector3.zero);
            Sync(true);
        }

        /// <summary>The card is leaving the hand as a projectile: returns its pose and frees the slot.</summary>
        public bool Take(int copyId, out Pose pose, out CardView view)
        {
            foreach (var slot in active)
                if (slot.copyId == copyId && slot.view.Visible)
                {
                    pose = new Pose(slot.view.transform.position, slot.view.transform.rotation);
                    view = slot.view;
                    slot.view.Hide();
                    slot.copyId = int.MinValue;
                    return true;
                }
            pose = default;
            view = null;
            return false;
        }

        /// <summary>Mass discard: every shown card of these ids flutters off as cosmetic debris.</summary>
        public void Scatter(IReadOnlyList<CardCopy> cards)
        {
            var camera = world.viewCamera.transform;
            int index = 0;
            foreach (var card in cards)
                foreach (var slot in active)
                {
                    if (slot.copyId != card.Id || !slot.view.Visible) continue;
                    float side = index - (cards.Count - 1) * .5f;
                    Vector3 velocity = camera.forward * 2.2f + camera.right * side * 1.6f + camera.up * (2.4f + index * .3f);
                    world.SpawnDebris(slot.view, slot.view.transform.position, slot.view.transform.rotation, velocity,
                        new Vector3(Random.Range(-500, 500), Random.Range(-200, 200), Random.Range(-700, 700)), 1.1f);
                    slot.view.Hide();
                    slot.copyId = int.MinValue;
                    index++;
                }
        }

        /// <summary>
        /// The player loses grip (drowning): every shown card and the deck pile burst out of the hand in all
        /// directions as cosmetic debris. Purely visual — the deck itself is untouched.
        /// </summary>
        public void ScatterAll(float speed, float seconds, Transform follow = null)
        {
            var camera = world.viewCamera.transform;
            void Fling(CardView view)
            {
                Vector2 around = Random.insideUnitCircle.normalized;
                Vector3 velocity = (camera.right * around.x * 1.4f + camera.up * (Mathf.Abs(around.y) + .6f) + camera.forward * Random.Range(.4f, 1.2f))
                    * speed * Random.Range(.7f, 1.2f);
                var debris = world.SpawnDebris(view, view.transform.position, view.transform.rotation, velocity,
                    new Vector3(Random.Range(-500, 500), Random.Range(-300, 300), Random.Range(-600, 600)), seconds);
                // Carried along with the view, so the cards stay on screen while the head turns.
                if (debris && follow) debris.transform.SetParent(follow, true);
                view.Hide();
            }
            foreach (var slot in active)
            {
                if (slot.copyId == int.MinValue || !slot.view.Visible) continue;
                Fling(slot.view);
                slot.copyId = int.MinValue;
            }
            foreach (var card in stack) if (card && card.Visible) Fling(card);
        }

        void LateUpdate()
        {
            if (hidden || !world || !world.Running) return;
            Sync(false);
            Animate(Time.deltaTime);
        }

        void Sync(bool fresh)
        {
            var trip = world.workshop.Model.Current;
            if (trip == null) return;
            var hand = trip.Deck.Hand;
            bool changed = fresh || hand.Count != lastIds.Count;
            if (!changed) for (int i = 0; i < hand.Count; i++) if (hand[i].Id != lastIds[i]) { changed = true; break; }
            int stackCount = Mathf.Min(stack.Length, trip.Deck.StockCount);
            if (stackCount != lastStackCount || fresh) UpdateStack(stackCount);
            if (!changed) return;

            lastIds.Clear();
            foreach (var card in hand) lastIds.Add(card.Id);

            // Release slots whose card left the hand (thrown or discarded).
            foreach (var slot in active)
                if (fresh || !Contains(hand, slot.copyId)) { slot.view.Hide(); slot.copyId = int.MinValue; }

            Texture back = cardBack;
            int dealt = 0;
            foreach (var card in hand)
            {
                if (Find(card.Id) != null) continue;
                var slot = FreeSlot();
                if (slot == null) break;
                slot.copyId = card.Id;
                slot.view.Show(world.Artwork(card), back, card.Level);
                slot.view.SetGlow(0, returnGlow);
                // New cards rise from below the screen, staggered like a quick deal.
                Vector3 target = Target(IndexOf(hand, card.Id), hand.Count, false, 0, card.IsReturn);
                slot.position.Snap(target + Vector3.down * .32f);
                slot.scale.Snap(.85f);
                slot.rotation = Quaternion.Euler(0, 0, -30);
                slot.drawDelay = dealt * .055f;
                slot.view.transform.localScale = Vector3.zero;
                if (!fresh) world.Play(world.cues ? world.cues.cardDraw : null, null, .7f);
                dealt++;
            }
        }

        void UpdateStack(int count)
        {
            lastStackCount = count;
            Texture back = cardBack;
            for (int i = 0; i < stack.Length; i++)
            {
                if (i >= count) { stack[i].Hide(); continue; }
                // Show the back towards the camera: the quad is turned around, so the shader draws its reverse face.
                stack[i].Show(back, back);
                stack[i].SetOrder(i);
            }
        }

        void Animate(float dt)
        {
            var trip = world.workshop.Model.Current;
            if (trip == null) return;
            var hand = trip.Deck.Hand;
            var thrower = world.thrower;
            var selected = thrower.Selected;
            var preparation = thrower.Preparation;

            // Look sway: the hand lags behind camera rotation and springs back.
            var cameraRotation = transform.parent ? transform.parent.rotation : transform.rotation;
            var delta = Quaternion.Inverse(lastCamera) * cameraRotation;
            lastCamera = cameraRotation;
            Vector3 euler = delta.eulerAngles;
            float yaw = Mathf.DeltaAngle(0, euler.y), pitch = Mathf.DeltaAngle(0, euler.x);
            Vector3 lookVelocity = new Vector3(-yaw, pitch, 0) / Mathf.Max(dt, .001f);
            Vector3 swayTarget = Vector3.ClampMagnitude(lookVelocity * swayPerDegreePerSecond, maxSway);
            sway.Step(swayTarget, swayFrequency, swayDamping, dt);

            float speed = world.motor.IsGrounded ? world.motor.PlanarVelocity.magnitude : 0;
            bobPhase += dt * Mathf.Lerp(0, 9, Mathf.Clamp01(speed / 6f));
            Vector3 bob = new Vector3(Mathf.Sin(bobPhase * .5f) * bobAmplitude, -Mathf.Abs(Mathf.Sin(bobPhase)) * bobAmplitude, 0)
                * Mathf.Clamp01(speed / 3f);
            float airborne = world.motor.IsGrounded ? 0 : Mathf.Clamp(world.motor.VerticalSpeed * -.004f, -.03f, .03f);
            Vector3 common = sway.Value + bob + Vector3.up * airborne;

            for (int i = 0; i < active.Count; i++)
            {
                var slot = active[i];
                if (slot.copyId == int.MinValue || !slot.view.Visible) continue;
                int index = IndexOf(hand, slot.copyId);
                if (index < 0) continue;
                if (slot.drawDelay > 0) { slot.drawDelay -= dt; continue; }
                var card = hand[index];
                bool isSelected = selected != null && selected.Id == card.Id;
                float charge = isSelected && preparation.Active ? preparation.Charge : 0;
                Vector3 target = Target(index, hand.Count, isSelected, charge, card.IsReturn) + common;
                if (charge > .97f) target += Random.insideUnitSphere * chargeTremble;
                slot.position.Step(target, cardFrequency, cardDamping, dt);
                float scale = slot.scale.Step(isSelected ? selectedScale : 1, cardFrequency, cardDamping, dt);

                float angle = Angle(index, hand.Count);
                float spinRoll = isSelected && preparation.Active ? preparation.SpinX * -28 : 0;
                Quaternion targetRotation = Quaternion.Euler(-charge * chargeTilt, charge * 10, -angle + spinRoll);
                slot.rotation = Quaternion.Slerp(slot.rotation, targetRotation, Ease.Damp(16, dt));

                var t = slot.view.transform;
                t.localPosition = slot.position.Value;
                t.localRotation = slot.rotation;
                t.localScale = new Vector3(cardWidth, cardWidth * 1.5f, 1) * scale;
                slot.view.SetOrder(isSelected ? 50 : 10 + index);

                float glow = card.IsReturn ? .55f + Mathf.Sin(Time.time * 6) * .3f : isSelected ? .18f + charge * .5f : 0;
                slot.view.SetGlow(glow, card.IsReturn ? returnGlow : world.Accent(card));
            }

            for (int i = 0; i < stack.Length; i++)
            {
                if (!stack[i].Visible) continue;
                var t = stack[i].transform;
                t.localPosition = stackPosition + new Vector3(i * .006f, i * .004f, -i * .002f) + common * .6f;
                t.localRotation = Quaternion.Euler(0, 180, stackRoll - i * 3);
                t.localScale = new Vector3(cardWidth * .8f, cardWidth * 1.2f, 1);
            }
        }

        float Angle(int index, int count) => (index - (count - 1) * .5f) * degreesPerCard;

        Vector3 Target(int index, int count, bool isSelected, float charge, bool isReturn)
        {
            float angle = Angle(index, count) * Mathf.Deg2Rad;
            Vector3 position = pivot + new Vector3(Mathf.Sin(angle) * radius, Mathf.Cos(angle) * radius, -index * .001f);
            if (isSelected || isReturn)
            {
                position += new Vector3(0, selectedLift, -selectedToward);
                position += chargePull * charge;
            }
            else position += Vector3.down * restDrop;
            return position;
        }

        Slot Find(int id) { foreach (var slot in active) if (slot.copyId == id) return slot; return null; }
        Slot FreeSlot() { foreach (var slot in active) if (slot.copyId == int.MinValue) return slot; return null; }
        static bool Contains(IReadOnlyList<CardCopy> hand, int id) => IndexOf(hand, id) >= 0;
        static int IndexOf(IReadOnlyList<CardCopy> hand, int id)
        {
            for (int i = 0; i < hand.Count; i++) if (hand[i].Id == id) return i;
            return -1;
        }
    }
}
