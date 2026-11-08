using System;
using Hakaton.Character;
using Hakaton.Decks;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// Expedition input and the throw itself. Hold LMB to charge; while charging, hold Shift and move the mouse
    /// to steer the spin in any direction (the HUD draws it as an arrow), release LMB to throw. RMB cancels. Wheel / 1–5 select, Tab switches deck, R discards the hand,
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
        public event Action<CardCopy> Thrown;
        public event Action SelectionChanged;
        public bool HasThrown { get; private set; }

        readonly int[] selectedId = { int.MinValue, int.MinValue };
        readonly int[] selectedIndex = new int[2];
        float returnReadyAt, nextDiscard;
        bool returnWasInHand, fullChargeCued;
        readonly Vector3[] previewPoints = new Vector3[64];

        public CardCopy Selected
        {
            get
            {
                var trip = world.workshop.Model?.Current;
                if (trip == null || trip.Completed) return null;
                var hand = trip.Active.Hand;
                if (hand.Count == 0) return null;
                int kind = (int)trip.ActiveKind;
                for (int i = 0; i < hand.Count; i++)
                    if (hand[i].Id == selectedId[kind]) { selectedIndex[kind] = i; return hand[i]; }
                // The selected copy left the hand: keep the same position in the fan instead of jumping.
                int index = Mathf.Clamp(selectedIndex[kind], 0, hand.Count - 1);
                selectedId[kind] = hand[index].Id;
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
                if (keys.tabKey.wasPressedThisFrame || keys.qKey.wasPressedThisFrame) SwitchDeck();
                if (keys.rKey.wasPressedThisFrame) Discard();
                if (keys.eKey.wasPressedThisFrame) world.TryPickUpFocused();
                if (keys.digit1Key.wasPressedThisFrame) Select(0);
                if (keys.digit2Key.wasPressedThisFrame) Select(1);
                if (keys.digit3Key.wasPressedThisFrame) Select(2);
                if (keys.digit4Key.wasPressedThisFrame) Select(3);
                if (keys.digit5Key.wasPressedThisFrame) Select(4);
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
            selectedId[0] = selectedId[1] = int.MinValue;
            selectedIndex[0] = selectedIndex[1] = 0;
            nextDiscard = 0;
            returnWasInHand = false;
        }

        public void SwitchDeck()
        {
            if (!world.Running) return;
            Cancel();
            var trip = world.workshop.Model.Current;
            trip.Switch(trip.ActiveKind == DeckKind.Exploration ? DeckKind.Combat : DeckKind.Exploration);
            SelectionChanged?.Invoke();
        }

        void Step(int direction)
        {
            var trip = world.workshop.Model.Current;
            int count = trip.Active.Hand.Count;
            if (count < 2) return;
            var current = Selected;
            int index = 0;
            for (int i = 0; i < count; i++) if (trip.Active.Hand[i].Id == current.Id) index = i;
            Select((index + direction + count) % count);
        }

        public void Select(int index)
        {
            if (!world.Running) return;
            var trip = world.workshop.Model.Current;
            var hand = trip.Active.Hand;
            if (index < 0 || index >= hand.Count) return;
            int kind = (int)trip.ActiveKind;
            if (selectedId[kind] == hand[index].Id) return;
            Cancel();
            selectedId[kind] = hand[index].Id;
            selectedIndex[kind] = index;
            world.Play(world.cues ? world.cues.cardSelect : null, null, .6f);
            SelectionChanged?.Invoke();
        }

        public void Discard()
        {
            if (!world.Running || Time.time < nextDiscard) return;
            var trip = world.workshop.Model.Current;
            bool any = false;
            foreach (var card in trip.Active.Hand) if (!card.IsReturn) { any = true; break; }
            if (!any) return;
            Cancel();
            var discarded = trip.DiscardHand();
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
                CardFlight.Advance(ref next, ref nextVelocity, new Vector2(Preparation.SpinX, Preparation.SpinY), world.settings, step);
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
            Color accent = world.Accent(card);
            trajectory.startColor = new Color(accent.r, accent.g, accent.b, .9f);
            trajectory.endColor = new Color(accent.r, accent.g, accent.b, 0);
        }

        void OnDisable() => Cancel();
    }
}
