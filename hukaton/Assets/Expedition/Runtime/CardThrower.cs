using System;
using Hakaton.Character;
using Hakaton.Decks;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Hakaton.Expeditions
{
    /// <summary>Limits on what the player may do with the hand (the tutorial's instruction cards).</summary>
    public interface IThrowRules
    {
        bool CanSelect(CardCopy card);
        bool CanThrow(CardCopy card);
        bool CanDiscard();
        void Discarded();
    }

    /// <summary>
    /// Expedition input and the throw itself. Hold LMB to charge, release LMB to throw; Shift stays a plain sprint,
    /// also while charging. RMB cancels. Wheel / 1–9, 0 (also on the numpad; 0 is the tenth card) select from the one shared hand, R discards the hand (a new one is drawn),
    /// E picks up. Charging never changes card power — only flight speed and range.
    /// </summary>
    [DefaultExecutionOrder(-225)]
    public sealed class CardThrower : MonoBehaviour
    {
        public ExpeditionWorld world;
        public FirstPersonInput input;
        public PlayerControlGate control;
        [Tooltip("Preview of the first moments of flight, drawn like a card's reach ring: dashed ink stroke, black edge, fill in the card's colour (material Fx Aim trajectory, texture fx_area_stroke, Texture Mode Repeat Per Segment: one dash per segment).")]
        public LineRenderer trajectory;
        [Tooltip("Camera-space point where a card physically leaves the hand.")]
        public Transform launchSocket;
        [Tooltip("Stroke width on screen, pixels at 1080 lines; scales with the window height, the same at any distance.")]
        [Min(.5f)] public float previewWidthPixels = 9;
        [Tooltip("Length of one dash (with its gap) in stroke widths: 4.8 keeps the 48 × 10 stroke texture's pixels square.")]
        [Min(.5f)] public float previewDashInWidths = 4.8f;
        [Tooltip("Hand-drawn boil like the reach ring's: the stroke wobbles sideways by up to this many pixels (at 1080 lines)...")]
        [Min(0)] public float previewWobblePixels = 1.5f;
        [Tooltip("...and the wobble is redrawn this many times a second. 0 keeps it still.")]
        [Min(0)] public float previewBoilRate = 7;
        [Tooltip("Bumps along the stroke: noise steps per dash.")]
        [Min(.01f)] public float previewWobbleFrequency = .45f;
        [Tooltip("The preview starts this far along the flight, metres, so it does not sprout from the card in the hand.")]
        [Min(0)] public float previewStartOffset = .25f;

        public ThrowPreparation Preparation { get; } = new ThrowPreparation();
        /// <summary>Optional limits (the cave's instruction cards: strict order, R only after two throws).</summary>
        public IThrowRules rules;
        public event Action<CardCopy> Thrown;
        /// <summary>Every throw of any thrower, right after <see cref="Thrown"/>. Static so card effect assets (Эхо
        /// remembers the last card thrown) can listen without a scene reference.</summary>
        public static event Action<CardThrower, CardCopy> AnyThrown;
        public event Action SelectionChanged;
        public bool HasThrown { get; private set; }

        // Keys for the hand's cards in order: 1–9, then 0 for the tenth (the numpad mirrors them).
        static readonly Key[] CardKeys =
            { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9, Key.Digit0 };
        static readonly Key[] NumpadCardKeys =
            { Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad4, Key.Numpad5, Key.Numpad6, Key.Numpad7, Key.Numpad8, Key.Numpad9, Key.Numpad0 };

        int selectedId = int.MinValue, selectedIndex;
        float returnReadyAt, nextDiscard;
        bool returnWasInHand, fullChargeCued;
        readonly Vector3[] previewPoints = new Vector3[64];
        readonly Gradient previewGradient = new Gradient();
        readonly GradientColorKey[] previewColours = new GradientColorKey[2];
        readonly GradientAlphaKey[] previewAlphas = { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) };
        // The preview as drawn: one vertex per piece, a fixed screen length apart.
        const int MaxDashPoints = 256, WidthKeys = 24;
        readonly Vector3[] dashPoints = new Vector3[MaxDashPoints];
        readonly Keyframe[] widthKeys = new Keyframe[WidthKeys];
        readonly AnimationCurve widthCurve = new AnimationCurve();

        public CardCopy Selected
        {
            get
            {
                var trip = world.workshop.Model?.Current;
                if (trip == null || trip.Completed) return null;
                var hand = trip.Deck.Hand;
                if (hand.Count == 0) return null;
                for (int i = 0; i < hand.Count; i++)
                    if (hand[i].Id == selectedId) { selectedIndex = i; return hand[i]; }
                // The selected copy left the hand: keep the same position in the fan instead of jumping.
                int index = Mathf.Clamp(selectedIndex, 0, hand.Count - 1);
                selectedId = hand[index].Id;
                return hand[index];
            }
        }

        /// <summary>True while the Return card is ready to be used (after its anti-misclick grace).</summary>
        public bool ReturnReady => Selected != null && Selected.IsReturn && Time.time >= returnReadyAt;

        void Update()
        {
            if (!world.Running || !control.GameplayActive) { Cancel(); return; }
            if (control.CapturedThisFrame) return;
            TrackReturnCard();
            var keys = Keyboard.current;
            var mouse = Mouse.current;
            if (mouse == null) return;
            if (keys != null)
            {
                if (keys.rKey.wasPressedThisFrame) Discard();
                if (keys.eKey.wasPressedThisFrame) world.TryPickUpFocused();
                for (int i = 0; i < CardKeys.Length; i++)
                    if (keys[CardKeys[i]].wasPressedThisFrame || keys[NumpadCardKeys[i]].wasPressedThisFrame) { Select(i); break; }
            }
            float wheel = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(wheel) > .1f && !Preparation.Active) Step(wheel > 0 ? -1 : 1);
            if (mouse.rightButton.wasPressedThisFrame && Preparation.Active) { Cancel(); world.Play(world.cues ? world.cues.cancel : null); return; }
            if (mouse.leftButton.wasPressedThisFrame) Begin();
            if (Preparation.Active)
            {
                Preparation.Tick(Time.deltaTime, world.settings.chargeSeconds);
                ChargeFeedback();
                if (mouse.leftButton.wasReleasedThisFrame) Release();
            }
            UpdatePreview();
        }

        void TrackReturnCard()
        {
            var selected = Selected;
            bool hasReturn = selected != null && selected.IsReturn;
            if (hasReturn && !returnWasInHand)
            {
                returnReadyAt = Time.time + world.settings.returnGrace;
                world.Announce("Карта возвращения готова — ЛКМ, чтобы вернуться в лавку", 4);
                world.Play(world.cues ? world.cues.returnAppears : null);
            }
            returnWasInHand = hasReturn;
        }

        public void Begin()
        {
            var card = Selected;
            if (!world.Running || card == null) return;
            if (card.IsReturn)
            {
                if (Time.time >= returnReadyAt) world.ReturnToShop(false);
                return;
            }
            if (rules != null && !rules.CanThrow(card))
            {
                world.Play(world.cues ? world.cues.cancel : null);
                return;
            }
            Preparation.Begin();
            fullChargeCued = false;
            world.Play(world.cues ? world.cues.charge : null, null, .6f);
        }

        void ChargeFeedback()
        {
            if (!fullChargeCued && Preparation.Charge >= 1)
            {
                fullChargeCued = true;
                world.Play(world.cues ? world.cues.chargeReady : null, null, .55f);
            }
        }

        public CardProjectile Release()
        {
            var card = Selected;
            if (!Preparation.Active || !world.Running || card == null || card.IsReturn) return null;
            float charge = Preparation.Charge;
            ComputeLaunch(charge, out var origin, out var velocity);
            // Crooked hands: the card flies off the aim (the preview and the area ring keep showing the aim).
            var statuses = world.statuses;
            if (statuses && statuses.Has(PlayerStatusKind.CrookedHands))
            {
                var eyeAxes = world.viewCamera.transform;
                velocity = PlayerStatusRules.Crook(velocity, eyeAxes.up, eyeAxes.right,
                    statuses.Strength(PlayerStatusKind.CrookedHands, 0), UnityEngine.Random.value, UnityEngine.Random.value);
                var crooked = statuses.Definition(PlayerStatusKind.CrookedHands);
                if (crooked) world.Play(crooked.tickCue);
            }
            Cancel();
            if (world.workshop.Model.Current.Release(card.Id) == null) return null;
            world.hand.Take(card.Id, out var handPose, out var handView);
            var projectile = world.Launch(card, origin, velocity, handView, handView ? handPose : (Pose?)null);

            // The socket can sit inside a wall when hugging geometry: resolve at the wall immediately.
            var eye = world.viewCamera.transform.position;
            if (CardFlight.Sweep(eye, origin, world.settings.cardRadius, out var hit)) projectile.Resolve(hit.collider, hit.point, hit.normal);

            if (world.feel)
            {
                world.feel.Kick(new Vector3(-30 - 40 * charge, UnityEngine.Random.Range(-8f, 8f), UnityEngine.Random.Range(-4f, 4f)));
                world.feel.PunchFov(10 + 25 * charge);
            }
            world.Play(world.cues ? world.cues.cardThrow : null, null, .7f + .3f * charge, Mathf.Lerp(.95f, 1.12f, charge));
            HasThrown = true;
            Thrown?.Invoke(card);
            AnyThrown?.Invoke(this, card);
            return projectile;
        }

        /// <summary>Shared by the real throw and its preview: aim converges on what the crosshair points at.</summary>
        public void ComputeLaunch(float charge, out Vector3 origin, out Vector3 velocity)
        {
            var camera = world.viewCamera.transform;
            origin = launchSocket ? launchSocket.position : camera.position + camera.forward * .45f;
            float speed = Mathf.Lerp(world.settings.minimumSpeed, world.settings.maximumSpeed, charge);
            float distance = world.settings.aimConvergeDistance;
            if (Physics.Raycast(camera.position, camera.forward, out var hit, distance, CardFlight.CollisionMask, QueryTriggerInteraction.Ignore))
                distance = Mathf.Max(2, hit.distance);
            Vector3 aim = camera.position + camera.forward * distance;
            Vector3 direction = (aim - origin).normalized;
            velocity = direction * speed + world.motor.PlanarVelocity * world.settings.inheritVelocity;
        }

        public void Cancel()
        {
            Preparation.Cancel();
            if (trajectory) trajectory.enabled = false;
        }

        public void ResetSelection()
        {
            Cancel();
            selectedId = int.MinValue;
            selectedIndex = 0;
            nextDiscard = 0;
            returnWasInHand = false;
        }

        void Step(int direction)
        {
            var trip = world.workshop.Model.Current;
            int count = trip.Deck.Hand.Count;
            if (count < 2) return;
            var current = Selected;
            int index = 0;
            for (int i = 0; i < count; i++) if (trip.Deck.Hand[i].Id == current.Id) index = i;
            Select((index + direction + count) % count);
        }

        public void Select(int index)
        {
            if (!world.Running) return;
            var trip = world.workshop.Model.Current;
            var hand = trip.Deck.Hand;
            if (index < 0 || index >= hand.Count) return;
            if (selectedId == hand[index].Id) return;
            if (rules != null && !rules.CanSelect(hand[index])) return;
            Cancel();
            selectedId = hand[index].Id;
            selectedIndex = index;
            world.Play(world.cues ? world.cues.cardSelect : null, null, .6f);
            SelectionChanged?.Invoke();
        }

        /// <summary>Selects a particular copy (a card the tutorial just handed over), ignoring the rules.</summary>
        public void SelectCopy(int copyId)
        {
            var hand = world.workshop.Model?.Current?.Deck.Hand;
            if (hand == null) return;
            for (int i = 0; i < hand.Count; i++)
            {
                if (hand[i].Id != copyId) continue;
                Cancel();
                selectedId = copyId;
                selectedIndex = i;
                SelectionChanged?.Invoke();
                return;
            }
        }

        public void Discard()
        {
            if (!world.Running || Time.time < nextDiscard) return;
            if (rules != null && !rules.CanDiscard()) return;
            var trip = world.workshop.Model.Current;
            bool any = false;
            foreach (var card in trip.Deck.Hand) if (!card.IsReturn) { any = true; break; }
            if (!any) return;
            Cancel();
            var discarded = trip.DiscardHand();
            rules?.Discarded();
            nextDiscard = Time.time + world.settings.discardCooldown;
            world.hand.Scatter(discarded);
            world.Play(world.cues ? world.cues.discard : null);
            if (world.feel) world.feel.Kick(new Vector3(-12, 0, 6));
        }

        void UpdatePreview()
        {
            if (!trajectory) return;
            var card = Selected;
            bool show = Preparation.Active && card != null && world.settings.previewSeconds > 0;
            trajectory.enabled = show;
            if (!show) return;
            ComputeLaunch(Preparation.Charge, out var position, out var velocity);
            float seconds = Mathf.Lerp(.12f, world.settings.previewSeconds, Preparation.Charge);
            int count = Mathf.Clamp(Mathf.CeilToInt(seconds / (1f / 50f)), 2, previewPoints.Length);
            float step = seconds / (count - 1);
            previewPoints[0] = position;
            int used = 1;
            for (int i = 1; i < count; i++)
            {
                Vector3 next = position, nextVelocity = velocity;
                CardFlight.Advance(ref next, ref nextVelocity, world.settings, step);
                if (CardFlight.Sweep(position, next, world.settings.cardRadius, out var hit))
                {
                    previewPoints[used++] = hit.point;
                    break;
                }
                position = next;
                velocity = nextVelocity;
                previewPoints[used++] = position;
            }
            var view = world.viewCamera;
            used = TrimStart(used, previewStartOffset);
            int dashes = view ? LayOutDashes(view, used) : 0;
            if (dashes < 2) { trajectory.enabled = false; return; }
            Boil(view, dashes);
            trajectory.textureMode = LineTextureMode.RepeatPerSegment;
            trajectory.positionCount = dashes;
            for (int i = 0; i < dashes; i++) trajectory.SetPosition(i, dashPoints[i]);
            UpdatePreviewWidth(view, dashes);
            // Like the reach ring: the stroke's fill takes the card's accent, its ink edge stays black.
            Color fill = world.Accent(card);
            previewColours[0] = new GradientColorKey(fill, 0);
            previewColours[1] = new GradientColorKey(fill, 1);
            previewGradient.SetKeys(previewColours, previewAlphas);
            trajectory.colorGradient = previewGradient;
        }

        /// <summary>Drops the first <paramref name="offset"/> metres of the sampled path (at most half of it) in place.</summary>
        int TrimStart(int count, float offset)
        {
            float total = 0;
            for (int i = 1; i < count; i++) total += Vector3.Distance(previewPoints[i - 1], previewPoints[i]);
            float skip = Mathf.Min(offset, total * .5f);
            if (skip <= 0) return count;
            float walked = 0;
            for (int first = 1; first < count; first++)
            {
                float segment = Vector3.Distance(previewPoints[first - 1], previewPoints[first]);
                if (walked + segment < skip) { walked += segment; continue; }
                Vector3 start = Vector3.Lerp(previewPoints[first - 1], previewPoints[first], segment > 1e-6f ? (skip - walked) / segment : 1);
                // Reads stay ahead of writes (first >= 1), so the shift is safe in place.
                int kept = 0;
                previewPoints[kept++] = start;
                for (int i = first; i < count; i++) previewPoints[kept++] = previewPoints[i];
                return kept;
            }
            return count;
        }

        /// <summary>
        /// Re-cuts the sampled path into pieces of equal SCREEN length (one dash: <see cref="previewWidthPixels"/> ×
        /// <see cref="previewDashInWidths"/> at 1080 lines),
        /// so the width keys and the line's shading stay even on screen near the hand and far away.
        /// Points are placed perspective-correctly on the world path; the end of the path closes the last dash.
        /// </summary>
        int LayOutDashes(Camera view, int count)
        {
            float aspect = view.aspect, near = view.nearClipPlane;
            // Screen lengths in units of the view height.
            float period = previewWidthPixels * previewDashInWidths / 1080f;
            Vector3 a = previewPoints[0], va = view.WorldToViewportPoint(a);
            if (va.z < near) return 0;
            int dashes = 0;
            dashPoints[dashes++] = a;
            float carry = 0;
            for (int i = 1; i < count; i++)
            {
                Vector3 b = previewPoints[i], vb = view.WorldToViewportPoint(b);
                if (vb.z < near) break;
                float length = Vector2.Distance(new Vector2(va.x * aspect, va.y), new Vector2(vb.x * aspect, vb.y));
                float next = period - carry;
                while (next <= length && dashes < MaxDashPoints - 1)
                {
                    float f = next / length;
                    // Screen fraction → world fraction along a perspective-projected segment.
                    float t = f * va.z / ((1 - f) * vb.z + f * va.z);
                    dashPoints[dashes++] = Vector3.LerpUnclamped(a, b, t);
                    next += period;
                }
                carry = length - (next - period);
                a = b;
                va = vb;
                if (dashes >= MaxDashPoints - 1) break;
            }
            // A tail shorter than a quarter dash would squeeze a whole dash into a sliver: end on the last dash instead.
            if (carry > period * .25f) dashPoints[dashes++] = a;
            return dashes;
        }

        /// <summary>
        /// Hand-drawn boil, like the reach ring: every dash point but the first is nudged sideways on screen by a
        /// smooth noise of up to <see cref="previewWobblePixels"/>, redrawn <see cref="previewBoilRate"/> times a second.
        /// </summary>
        void Boil(Camera view, int count)
        {
            if (previewWobblePixels <= 0 || count < 3) return;
            float frame = previewBoilRate > 0 ? Mathf.Floor(Time.time * previewBoilRate) : 0;
            float seed = frame * 7.31f % 97f;
            var eye = view.transform;
            Vector3 origin = eye.position, forward = eye.forward;
            float perMetreOfDepth = previewWobblePixels / 1080f * 2 * Mathf.Tan(view.fieldOfView * .5f * Mathf.Deg2Rad);
            Vector3 previous = dashPoints[0];
            for (int i = 1; i < count; i++)
            {
                Vector3 point = dashPoints[i];
                Vector3 along = point - previous;
                previous = point;
                Vector3 side = Vector3.Cross(along, point - origin);
                if (side.sqrMagnitude < 1e-10f) continue;
                float depth = Mathf.Max(view.nearClipPlane, Vector3.Dot(point - origin, forward));
                // Eases in over the first dashes so the stroke still leaves from the hand.
                float ease = Mathf.Clamp01(i / 3f);
                float noise = Mathf.PerlinNoise(seed, i * previewWobbleFrequency) * 2 - 1;
                dashPoints[i] = point + side.normalized * (noise * ease * perMetreOfDepth * depth);
            }
        }

        /// <summary>
        /// Width so the stroke is <see cref="previewWidthPixels"/> (at 1080 lines) wide on screen whatever the depth:
        /// proportional to the depth in front of the camera. The LineRenderer reads its width curve by vertex index, and
        /// the dash points are equally spaced on screen, so the keys sit at even index shares.
        /// </summary>
        void UpdatePreviewWidth(Camera view, int count)
        {
            var eye = view.transform;
            Vector3 origin = eye.position, forward = eye.forward;
            float perMetreOfDepth = previewWidthPixels / 1080f * 2 * Mathf.Tan(view.fieldOfView * .5f * Mathf.Deg2Rad);
            float lastTime = -1;
            for (int k = 0; k < WidthKeys; k++)
            {
                float share = k / (WidthKeys - 1f);
                float index = share * (count - 1);
                int i = Mathf.Min((int)index, count - 2);
                Vector3 point = Vector3.Lerp(dashPoints[i], dashPoints[i + 1], index - i);
                float time = share;
                // Keys must keep strictly increasing times.
                time = Mathf.Max(time, lastTime + 1e-4f);
                if (k == WidthKeys - 1) time = Mathf.Max(time, 1);
                lastTime = time;
                float depth = Mathf.Max(view.nearClipPlane, Vector3.Dot(point - origin, forward));
                widthKeys[k] = new Keyframe(time, perMetreOfDepth * depth);
            }
            // Straight lines between the keys.
            for (int k = 0; k < WidthKeys; k++)
            {
                float slopeIn = k > 0 ? (widthKeys[k].value - widthKeys[k - 1].value) / Mathf.Max(1e-4f, widthKeys[k].time - widthKeys[k - 1].time) : 0;
                float slopeOut = k < WidthKeys - 1 ? (widthKeys[k + 1].value - widthKeys[k].value) / Mathf.Max(1e-4f, widthKeys[k + 1].time - widthKeys[k].time) : slopeIn;
                widthKeys[k].inTangent = k > 0 ? slopeIn : slopeOut;
                widthKeys[k].outTangent = slopeOut;
            }
            widthCurve.keys = widthKeys;
            trajectory.widthMultiplier = 1;
            trajectory.widthCurve = widthCurve;
        }

        void OnDisable() => Cancel();
    }
}
