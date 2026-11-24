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
    /// The draw pile is not drawn: the HUD counts it in the bottom-right corner. The whole hand is shown at once, up to
    /// <see cref="CardFanLayout.MaxCards"/> cards: bigger hands flatten and squeeze the fan to the screen
    /// (<see cref="CardFanLayout"/>) and, past that, enough to keep even the raised selected card off the HUD's bottom corners
    /// (the standing controls line, the card counter: <see cref="ExpeditionHud.FanKeepOut"/>), and a hand bigger than the
    /// authored slots clones the slot template (pooled).
    /// Oversized cheat hands scroll a window of that size around the selected card; every copy stays selectable.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class CardHandView : MonoBehaviour
    {
        /// <summary>Card height over width.</summary>
        public const float CardAspect = 1.5f;

        public ExpeditionWorld world;
        [Tooltip("Authored visible card slots (children). A bigger hand clones the slot template; past CardFanLayout.MaxCards the fan scrolls with the selection.")]
        public CardView[] slots;
        [Tooltip("Cloned once per missing slot (and kept) when a hand outgrows the authored slots, up to CardFanLayout.MaxCards. Empty: the authored slots are all there is.")]
        public CardView slotTemplate;
        [Tooltip("Up to three authored backs showing what is left in the draw pile.")]
        [FormerlySerializedAs("explorationBack")]
        public Texture cardBack;

        [Header("Fan layout (camera space, metres)")]
        public Vector3 pivot = new Vector3(0, -1.42f, .55f);
        [Min(.05f)] public float radius = 1.1f;
        [Min(0)] public float degreesPerCard = 6.25f;
        [Min(.01f)] public float cardWidth = .18f;
        [Tooltip("Bigger hands flatten the arc: the outermost card sinks at most this far below the top of the arc (metres).")]
        [Min(0)] public float maxSink = .02f;
        [Tooltip("Bigger hands squeeze together so the outermost card's outer edge stays within this share of the half-screen width at the card depth (the bottom corners stay free for the HUD counter).")]
        [Range(.2f, 1)] public float screenShare = .74f;
        [Tooltip("Vertical field of view the fan is fitted to: the camera's resting FOV (the sprint widening is left out, so the fan does not breathe).")]
        [Range(10, 120)] public float fitFieldOfView = 75;
        [Tooltip("How far unselected cards sink below the fan arc.")]
        public float restDrop = .04f;
        public float selectedLift = .08f, selectedToward = .04f;
        [Min(1)] public float selectedScale = 1.15f;
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
        [Tooltip("Glow of a card that is in the hand for this outing only (a trophy card, the phone's prize).")]
        public Color temporaryGlow = new Color(1f, .9f, .55f);

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
        // Cards flying in (trophy cards): their slots start where the flying card arrived, not below the screen.
        readonly Dictionary<int, Pose> arrivals = new Dictionary<int, Pose>();
        readonly List<int> lastIds = new List<int>();
        int firstVisible;
        SpringVector3 sway;
        Quaternion lastCamera;
        float bobPhase;
        bool hidden = true;
        bool lowered, dealPending;

        void Awake()
        {
            foreach (var view in slots) active.Add(new Slot { view = view });
            Hide();
        }

        public void Hide()
        {
            dealPending = false;
            hidden = true;
            foreach (var slot in active) { slot.view.Hide(); slot.copyId = int.MinValue; }
            lastIds.Clear();
            firstVisible = 0;
        }

        /// <summary>Starts a fresh outing: everything is dealt from below.</summary>
        public void Deal()
        {
            if (lowered) { Hide(); dealPending = true; return; }
            Hide();
            hidden = false;
            lastCamera = transform.parent ? transform.parent.rotation : Quaternion.identity;
            sway.Snap(Vector3.zero);
            Sync(true);
        }

        /// <summary>
        /// Out of sight behind the main menu: the hand dealt meanwhile waits, and rises from below (a fresh deal) once
        /// the menu lets the player in.
        /// </summary>
        public void SetLowered(bool value)
        {
            if (lowered == value) return;
            if (value)
            {
                bool shown = !hidden;
                Hide();
                lowered = true;
                dealPending = shown;
            }
            else
            {
                lowered = false;
                if (dealPending) Deal();
            }
        }

        /// <summary>A card flying into the hand (a trophy card) arrives at this world pose: its slot starts there.</summary>
        public void ArriveFrom(int copyId, Pose pose) => arrivals[copyId] = pose;

        /// <summary>Where a card flying into the hand aims: the top of the fan, facing as the hand's cards do.</summary>
        public Pose ArrivalPose() => new Pose(transform.TransformPoint(pivot + Vector3.up * radius), transform.rotation);

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
                    float side = index - (Mathf.Min(cards.Count, lastIds.Count) - 1) * .5f;
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
            int count = Mathf.Min(hand.Count, CardFanLayout.MaxCards);
            Grow(count);
            count = Mathf.Min(count, active.Count);
            var selected = world.thrower.Selected;
            int selectedIndex = selected == null ? 0 : IndexOf(hand, selected.Id);
            int start = Mathf.Clamp(selectedIndex - count / 2, 0, hand.Count - count);
            bool windowMoved = start != firstVisible;
            bool changed = fresh || windowMoved || count != lastIds.Count;
            if (!changed) for (int i = 0; i < count; i++) if (hand[start + i].Id != lastIds[i]) { changed = true; break; }
            if (!changed) return;

            firstVisible = start;
            lastIds.Clear();
            for (int i = 0; i < count; i++) lastIds.Add(hand[start + i].Id);

            // Release cards outside the visible window, including thrown or discarded copies.
            foreach (var slot in active)
                if (fresh || !lastIds.Contains(slot.copyId)) { slot.view.Hide(); slot.copyId = int.MinValue; }

            Texture back = cardBack;
            var fan = Fan(count);
            int dealt = 0;
            for (int i = 0; i < count; i++)
            {
                var card = hand[start + i];
                if (Find(card.Id) != null) continue;
                var slot = FreeSlot();
                if (slot == null) break;
                slot.copyId = card.Id;
                slot.view.Show(world.Artwork(card), back, card.Level);
                slot.view.SetGlow(0, returnGlow);
                // New cards rise from below the screen, staggered like a quick deal.
                Vector3 target = Target(fan, i, false, 0, card.IsReturn);
                slot.position.Snap(target + Vector3.down * .32f);
                slot.scale.Snap(.85f);
                slot.rotation = Quaternion.Euler(0, 0, -30);
                slot.drawDelay = dealt * .055f;
                slot.view.transform.localScale = Vector3.zero;
                if (arrivals.TryGetValue(card.Id, out var arrival))
                {
                    // Flew in (a trophy card): it starts where it arrived and springs into its place.
                    arrivals.Remove(card.Id);
                    var parent = slot.view.transform.parent ? slot.view.transform.parent : transform;
                    slot.position.Snap(parent.InverseTransformPoint(arrival.position));
                    slot.rotation = Quaternion.Inverse(parent.rotation) * arrival.rotation;
                    slot.scale.Snap(1);
                    slot.drawDelay = 0;
                    continue;
                }
                if (!fresh && !windowMoved) world.Play(world.cues ? world.cues.cardDraw : null, null, .7f);
                dealt++;
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

            bool onFoot = world.motor.GroundedForFeedback;
            float speed = onFoot ? world.motor.PlanarVelocity.magnitude : 0;
            bobPhase += dt * Mathf.Lerp(0, 9, Mathf.Clamp01(speed / 6f));
            Vector3 bob = new Vector3(Mathf.Sin(bobPhase * .5f) * bobAmplitude, -Mathf.Abs(Mathf.Sin(bobPhase)) * bobAmplitude, 0)
                * Mathf.Clamp01(speed / 3f);
            float airborne = onFoot ? 0 : Mathf.Clamp(world.motor.VerticalSpeed * -.004f, -.03f, .03f);
            Vector3 common = sway.Value + bob + Vector3.up * airborne;
            // Crooked hands: the whole hand trembles.
            float tremble = world.statuses ? world.statuses.HandTremble : 0;
            if (tremble > 0) common += Random.insideUnitSphere * tremble;
            var fan = Fan(lastIds.Count);

            for (int i = 0; i < active.Count; i++)
            {
                var slot = active[i];
                if (slot.copyId == int.MinValue || !slot.view.Visible) continue;
                int index = IndexOf(hand, slot.copyId);
                if (index < 0) continue;
                if (slot.drawDelay > 0) { slot.drawDelay -= dt; continue; }
                var card = hand[index];
                int visibleIndex = index - firstVisible;
                bool isSelected = selected != null && selected.Id == card.Id;
                float charge = isSelected && preparation.Active ? preparation.Charge : 0;
                Vector3 target = Target(fan, visibleIndex, isSelected, charge, card.IsReturn) + common;
                if (charge > .97f) target += Random.insideUnitSphere * chargeTremble;
                slot.position.Step(target, cardFrequency, cardDamping, dt);
                float scale = slot.scale.Step(isSelected ? selectedScale : 1, cardFrequency, cardDamping, dt);

                float angle = fan.Roll(visibleIndex);
                Quaternion targetRotation = Quaternion.Euler(-charge * chargeTilt, charge * 10, -angle);
                slot.rotation = Quaternion.Slerp(slot.rotation, targetRotation, Ease.Damp(16, dt));

                var t = slot.view.transform;
                t.localPosition = slot.position.Value;
                t.localRotation = slot.rotation;
                t.localScale = new Vector3(cardWidth, cardWidth * CardAspect, 1) * scale;
                slot.view.SetOrder(isSelected ? 50 : 10 + visibleIndex);

                float glow = card.IsReturn ? .55f + Mathf.Sin(Time.time * 6) * .3f : isSelected ? .18f + charge * .5f : 0;
                // A card for this outing only (a trophy, the phone's prize) keeps a soft glow of its own.
                bool temporary = card.Temporary && !world.InPrologue;
                if (temporary) glow = Mathf.Max(glow, .32f + Mathf.Sin(Time.time * 3.5f + index) * .12f);
                slot.view.SetGlow(glow, card.IsReturn ? returnGlow : temporary && !isSelected ? temporaryGlow : world.Accent(card));
            }

        }

        /// <summary>
        /// The Shuffle debuff mixed the hand up: every card jumps up and flies to its new place (the springs carry it).
        /// </summary>
        public void Flutter()
        {
            foreach (var slot in active)
            {
                if (slot.copyId == int.MinValue || !slot.view.Visible) continue;
                slot.position.Kick(new Vector3(Random.Range(-.5f, .5f), Random.Range(1.2f, 1.8f), Random.Range(-.3f, .1f)));
                slot.scale.Kick(Random.Range(-2f, 2f));
            }
        }

        /// <summary>The fan for this many cards on a view of this aspect (width / height).</summary>
        public CardFanLayout Fan(int count, float aspect) => Fan(count, aspect, screenShare);

        /// <summary>
        /// The fan on the real view: kept out of the HUD's bottom corners (the standing controls line on the left, the card
        /// counter on the right) through <see cref="ExpeditionWorld.hud"/>, whatever the resolution and UI scale.
        /// </summary>
        CardFanLayout Fan(int count)
        {
            var eye = world ? world.viewCamera : null;
            float aspect = eye ? eye.aspect : 16f / 9f;
            if (!eye || !world.hud || !world.hud.FanKeepOut(out var left, out var right)) return Fan(count, aspect);
            if (count == cachedCount && aspect == cachedAspect && left == cachedLeft && right == cachedRight) return cachedFan;
            cachedCount = count; cachedAspect = aspect; cachedLeft = left; cachedRight = right;
            return cachedFan = Fan(count, aspect, left, right);
        }

        int cachedCount = -1;
        float cachedAspect;
        Vector2 cachedLeft, cachedRight;
        CardFanLayout cachedFan;

        void OnValidate() => cachedCount = -1;

        /// <summary>
        /// The fan for this many cards kept out of two bottom corners of the screen: no card, resting or raised and magnified as
        /// the selection, reaches left of <paramref name="left"/>.x below <paramref name="left"/>.y or right of
        /// <paramref name="right"/>.x below <paramref name="right"/>.y (x a share of the half-screen width from the middle, y of
        /// the half height). A hand that already keeps clear is <see cref="Fan(int, float)"/> unchanged; a bigger one shrinks
        /// its spread (the cards overlap more), staying centred and symmetric.
        /// </summary>
        public CardFanLayout Fan(int count, float aspect, Vector2 left, Vector2 right)
        {
            var fan = Fan(count, aspect);
            if (count <= 1 || KeepsOut(fan, aspect, left, right)) return fan;
            // The spread grows with the screen share: the widest share that still keeps out.
            float fits = 0, fails = screenShare;
            for (int round = 0; round < 16; round++)
            {
                float share = (fits + fails) * .5f;
                if (KeepsOut(Fan(count, aspect, share), aspect, left, right)) fits = share; else fails = share;
            }
            return Fan(count, aspect, fits);
        }

        CardFanLayout Fan(int count, float aspect, float share) => CardFanLayout.Solve(count, radius, degreesPerCard,
            new Vector2(cardWidth, cardWidth * CardAspect), pivot.z, maxSink, share, fitFieldOfView, aspect);

        bool KeepsOut(CardFanLayout fan, float aspect, Vector2 left, Vector2 right) =>
            Reach(fan, 0, aspect, left.y, -1) <= -left.x + 1e-5f && Reach(fan, fan.Count - 1, aspect, right.y, 1) <= right.x + 1e-5f;

        /// <summary>
        /// How far toward <paramref name="side"/> (-1 left, 1 right) the card at <paramref name="index"/> reaches below
        /// <paramref name="top"/> (a share of the half height from the middle), resting or raised and magnified as the selection,
        /// whichever reaches further: a share of the half-screen width from the middle, signed toward that side;
        /// <see cref="float.NegativeInfinity"/> if the card never comes that low.
        /// </summary>
        public float Reach(CardFanLayout fan, int index, float aspect, float top, float side) =>
            Mathf.Max(Reach(fan, index, aspect, top, side, false), Reach(fan, index, aspect, top, side, true));

        float Reach(CardFanLayout fan, int index, float aspect, float top, float side, bool selected)
        {
            Vector3 centre = Target(fan, index, selected, 0, false);
            float tan = Mathf.Tan(fitFieldOfView * .5f * Mathf.Deg2Rad);
            Vector2 half = new Vector2(cardWidth, cardWidth * CardAspect) * ((selected ? selectedScale : 1) * .5f);
            // The view rolls the card by -Roll around the camera's forward axis; it stays parallel to the screen.
            float roll = -fan.Roll(index) * Mathf.Deg2Rad, cos = Mathf.Cos(roll), sin = Mathf.Sin(roll);
            float reach = float.NegativeInfinity;
            Vector2 previous = Corner(3);
            for (int i = 0; i < 4; i++)
            {
                Vector2 corner = Corner(i);
                if (corner.y <= top) reach = Mathf.Max(reach, corner.x * side);
                // An edge crossing the top: the card reaches that far right at its height.
                if ((corner.y - top) * (previous.y - top) < 0)
                    reach = Mathf.Max(reach, Mathf.Lerp(previous.x, corner.x, (top - previous.y) / (corner.y - previous.y)) * side);
                previous = corner;
            }
            return reach;

            // Corners in order around the card, as shares of the half-screen at the card's depth.
            Vector2 Corner(int i)
            {
                float x = (i == 0 || i == 3 ? -half.x : half.x), y = (i < 2 ? -half.y : half.y);
                return new Vector2((centre.x + x * cos - y * sin) / (centre.z * tan * aspect), (centre.y + x * sin + y * cos) / (centre.z * tan));
            }
        }

        /// <summary>
        /// Camera-space centre of the card at <paramref name="index"/> in the fan: on the arc whose top is
        /// <see cref="radius"/> above <see cref="pivot"/>, raised when selected (or the return card), sunk otherwise.
        /// </summary>
        public Vector3 Target(CardFanLayout fan, int index, bool isSelected, float charge, bool isReturn)
        {
            Vector2 offset = fan.Offset(index);
            Vector3 position = pivot + new Vector3(offset.x, radius + offset.y, -index * .001f);
            if (isSelected || isReturn)
            {
                position += new Vector3(0, selectedLift, -selectedToward);
                position += chargePull * charge;
            }
            else position += Vector3.down * restDrop;
            return position;
        }

        /// <summary>A hand bigger than the slots so far clones the template once per missing card; the clones stay pooled.</summary>
        void Grow(int count)
        {
            if (!slotTemplate) return;
            while (active.Count < count)
            {
                var view = Instantiate(slotTemplate, transform, false);
                view.name = "Card slot " + (active.Count + 1) + " (pooled)";
                view.Hide();
                active.Add(new Slot { view = view });
            }
        }

        Slot Find(int id) { foreach (var slot in active) if (slot.copyId == id) return slot; return null; }
        Slot FreeSlot() { foreach (var slot in active) if (slot.copyId == int.MinValue) return slot; return null; }
        static int IndexOf(IReadOnlyList<CardCopy> hand, int id)
        {
            for (int i = 0; i < hand.Count; i++) if (hand[i].Id == id) return i;
            return -1;
        }
    }
}
