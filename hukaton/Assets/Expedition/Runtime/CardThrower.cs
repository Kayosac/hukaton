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
    /// Expedition input and the throw itself. Hold LMB to charge; while charging, hold Shift and move the mouse
    /// to steer the spin in any direction (the HUD draws it as an arrow), release LMB to throw. RMB cancels. Wheel / 1–9, 0 (also on the numpad; 0 is the tenth card) select from the one shared hand, R discards the hand (a new one is drawn),
    /// E picks up. Charging never changes card power — only flight speed and range.
    /// </summary>
    [DefaultExecutionOrder(-225)]
    public sealed class CardThrower : MonoBehaviour
    {
        public ExpeditionWorld world;
        public FirstPersonInput input;
        public PlayerControlGate control;
        [Tooltip("Dotted preview of the first moments of flight.")]
        public LineRenderer trajectory;
        [Tooltip("Camera-space point where a card physically leaves the hand.")]
        public Transform launchSocket;

        public ThrowPreparation Preparation { get; } = new ThrowPreparation();
        /// <summary>Optional limits (the cave's instruction cards: strict order, R only after two throws).</summary>
        public IThrowRules rules;
        public event Action<CardCopy> Thrown;
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
                bool modifier = keys != null && (keys.leftShiftKey.isPressed || keys.rightShiftKey.isPressed);
                input.SetLookBlocked(this, modifier);
                Vector2 delta = mouse.delta.ReadValue();
                Preparation.Tick(Time.deltaTime, modifier, delta.x, delta.y, world.settings.chargeSeconds, world.settings.gesturePixels);
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
            input.SetSprintBlocked(this, true);
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
            var spin = new Vector2(Preparation.SpinX, Preparation.SpinY);
            ComputeLaunch(charge, out var origin, out var velocity);
            Cancel();
            if (world.workshop.Model.Current.Release(card.Id) == null) return null;
            world.hand.Take(card.Id, out var handPose, out var handView);
            var projectile = world.Launch(card, origin, velocity, spin, handView, handView ? handPose : (Pose?)null);

            // The socket can sit inside a wall when hugging geometry: resolve at the wall immediately.
            var eye = world.viewCamera.transform.position;
            if (CardFlight.Sweep(eye, origin, world.settings.cardRadius, out var hit)) projectile.Resolve(hit.collider, hit.point, hit.normal);

            if (world.feel)
            {
                world.feel.Kick(new Vector3(-30 - 40 * charge, UnityEngine.Random.Range(-8f, 8f), -20 * spin.x));
                world.feel.PunchFov(10 + 25 * charge);
            }
            world.Play(world.cues ? world.cues.cardThrow : null, null, .7f + .3f * charge, Mathf.Lerp(.95f, 1.12f, charge));
            HasThrown = true;
            Thrown?.Invoke(card);
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
            if (input) { input.SetLookBlocked(this, false); input.SetSprintBlocked(this, false); }
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
            float seconds = Mathf.Lerp(.12f, world.settings.previewSeconds, Preparation.Charge)
                + world.settings.spinPreviewSeconds * Preparation.SpinAmount;
            int count = Mathf.Clamp(Mathf.CeilToInt(seconds / (1f / 50f)), 2, previewPoints.Length);
            float step = seconds / (count - 1);
            previewPoints[0] = position;
            int used = 1;
            var clock = default(CardFlight.Clock);
            for (int i = 1; i < count; i++)
            {
                Vector3 next = position, nextVelocity = velocity;
                CardFlight.Advance(ref next, ref nextVelocity, ref clock, new Vector2(Preparation.SpinX, Preparation.SpinY), world.settings, step);
                if (CardFlight.Sweep(position, next, world.settings.cardRadius, out var hit))
                {
                    previewPoints[used++] = hit.point;
                    break;
                }
                position = next;
                velocity = nextVelocity;
                previewPoints[used++] = position;
            }
            trajectory.positionCount = used;
            for (int i = 0; i < used; i++) trajectory.SetPosition(i, previewPoints[i]);
            // Faint by the hand, strongest where the path bends: the far end is where a spin shows.
            Color accent = world.Accent(card);
            previewGradient.SetKeys(
                new[] { new GradientColorKey(accent, 0), new GradientColorKey(accent, 1) },
                new[] { new GradientAlphaKey(.15f, 0), new GradientAlphaKey(.85f, .3f), new GradientAlphaKey(.75f, 1) });
            trajectory.colorGradient = previewGradient;
        }

        void OnDisable() => Cancel();
    }
}
