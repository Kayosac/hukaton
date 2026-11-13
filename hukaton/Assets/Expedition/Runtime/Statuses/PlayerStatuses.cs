using System;
using System.Collections.Generic;
using Hakaton.Expeditions.Cards;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// The player's debuffs (KayoSpace board «Дебаффы»): one <see cref="PlayerStatusDefinition"/> per kind, applied by
    /// enemy attacks and traps (<see cref="StatusInflict"/>), never by the player's own cards. Rules from the board:
    /// a repeat hit only refreshes the timer (Rot takes another heart instead), any number at once, no leniency (they
    /// land during the protection after a hit too), the Bubble keeps new ones off, some can be removed by an action
    /// (water puts out Burn, «Лечение» cures Poison), and everything ends when the outing ends.
    /// Lives on the player rig next to <see cref="PlayerVitals"/>; the HUD shows a badge per debuff
    /// (<see cref="PlayerStatusBadge"/>) and <see cref="PlayerStatusScreen"/> draws the screen effects.
    /// </summary>
    [DefaultExecutionOrder(500)]
    public sealed class PlayerStatuses : MonoBehaviour
    {
        public ExpeditionWorld world;
        [Tooltip("Every debuff the game knows, one asset per kind (lookups by kind, the cheat menu).")]
        public PlayerStatusDefinition[] definitions = new PlayerStatusDefinition[0];

        [Header("HUD")]
        [Tooltip("One badge prefab for every debuff (red ring); the icon comes from the debuff's definition.")]
        public PlayerStatusBadge badgePrefab;

        [Header("Deafen")]
        [Tooltip("Low-pass filter on the audio listener: everything heard goes through it.")]
        public AudioLowPassFilter deafFilter;
        [Tooltip("The ringing in the ears: a looped 2D source that bypasses the listener's filter.")]
        public AudioSource ringing;
        [Range(0, 1)] public float ringingVolume = .3f;
        [Tooltip("Seconds for the sound to go muffled.")]
        [Min(.05f)] public float muffleFadeSeconds = .35f;
        [Tooltip("The last seconds of Deafen: the sound comes back gradually.")]
        [Min(.05f)] public float muffleRecoverSeconds = 1.5f;

        [Header("Blind")]
        public Color blindFogColour = new Color(.02f, .02f, .03f);
        [Min(.05f)] public float blindFadeSeconds = .7f;

        [Header("Warp")]
        [Tooltip("Projection wobble: shear, stretch, lens shift (screen share) and roll (degrees) at strength 1.")]
        public float warpShear = .09f;
        public float warpStretch = .07f, warpShift = .035f, warpRoll = 6;
        [Min(.01f)] public float warpSpeed = 1.2f;
        [Min(.05f)] public float warpFadeSeconds = 1;

        [Header("Burn")]
        [Tooltip("Retro Arsenal flames held low in front of the camera (on the hands) while burning.")]
        public VfxBurst burnFlames;
        public Vector3 burnFlamesOffset = new Vector3(0, -.42f, .9f);
        [Min(.01f)] public float burnFlamesScale = .45f;
        public Color burnFlamesTint = Color.white;

        [Header("Leaky pocket")]
        [Tooltip("What a dropped coin looks like (a chest coin prefab); it lies there until picked up again.")]
        public WorldCoin droppedCoin;
        [Tooltip("Coins fall only while the player moves faster than this, m/s.")]
        [Min(0)] public float leakMinimumSpeed = .6f;

        [Header("Crooked hands")]
        [Tooltip("How much the cards in the hand tremble, metres.")]
        [Min(0)] public float handTremble = .006f;

        /// <summary>A debuff started or was refreshed.</summary>
        public event Action<PlayerStatusDefinition> Applied;
        /// <summary>A debuff ended (ran out or was removed).</summary>
        public event Action<PlayerStatusDefinition> Ended;

        sealed class Active
        {
            public PlayerStatusDefinition definition;
            public float until, strength, nextTick, started;
        }

        readonly Dictionary<PlayerStatusKind, Active> active = new Dictionary<PlayerStatusKind, Active>();
        readonly List<Active> snapshot = new List<Active>();
        readonly System.Random random = new System.Random();
        float walked;
        Vector3 lastFeet;
        bool hadFeet;
        float muffle, blind, warp, warpStrength = 1, deafCutoff = 600;
        bool fogTouched, fogWasOn, warpTouched;
        FogMode fogModeBase;
        float fogDensityBase, fogDensityWritten;
        Color fogColourBase, fogColourWritten;
        VfxBurst flames;

        // ---------------- queries ----------------

        public PlayerStatusDefinition Definition(PlayerStatusKind kind)
        {
            if (definitions != null)
                foreach (var definition in definitions)
                    if (definition && definition.kind == kind) return definition;
            return null;
        }

        public bool Has(PlayerStatusKind kind) => active.ContainsKey(kind);
        public int Count => active.Count;
        /// <summary>No timer: lasts until the outing ends (Rot).</summary>
        public bool IsPermanent(PlayerStatusKind kind) => active.TryGetValue(kind, out var entry) && float.IsPositiveInfinity(entry.until);
        /// <summary>Seconds left (∞ for a permanent one, 0 when not on).</summary>
        public float Remaining(PlayerStatusKind kind)
            => active.TryGetValue(kind, out var entry) ? Mathf.Max(0, entry.until - Time.time) : 0;
        public float Strength(PlayerStatusKind kind, float neutral) => active.TryGetValue(kind, out var entry) ? entry.strength : neutral;
        /// <summary>The debuffs on now.</summary>
        public void ActiveKinds(List<PlayerStatusKind> into)
        {
            into.Clear();
            into.AddRange(active.Keys);
        }

        /// <summary>Slow: walking and running speed multiplier (1 when not slowed).</summary>
        public float MoveSpeedScale => Has(PlayerStatusKind.Slow) ? Mathf.Clamp01(Strength(PlayerStatusKind.Slow, 1)) : 1;
        /// <summary>Fragile: damage multiplier (1 normally).</summary>
        public float DamageScale => Has(PlayerStatusKind.Fragile) ? Mathf.Max(1, Strength(PlayerStatusKind.Fragile, 1)) : 1;
        /// <summary>Fragile: no protection after a hit.</summary>
        public bool Fragile => Has(PlayerStatusKind.Fragile);
        /// <summary>Crooked hands: how much the cards in the hand tremble, metres.</summary>
        public float HandTremble => Has(PlayerStatusKind.CrookedHands) ? handTremble : 0;
        /// <summary>0–1 weights of the screen effects (fade in and out), read by <see cref="PlayerStatusScreen"/>.</summary>
        public float BlindWeight => blind;

        /// <summary>A card zone around the player (the Bubble) keeps new debuffs off.</summary>
        public bool Shielded
        {
            get
            {
                if (!world || !world.motor) return false;
                Vector3 feet = world.motor.transform.position;
                var zones = world.Zones.All;
                for (int i = 0; i < zones.Count; i++)
                    if (zones[i] is IPlayerStatusShield shield && shield.ShieldsPlayer(feet)) return true;
                return false;
            }
        }

        bool PlayerWet => world && world.motor && WetStatus.IsWet(world.motor);

        // ---------------- applying and removing ----------------

        /// <summary>Puts the debuff of this kind on the player with its own defaults (the cheat menu).</summary>
        public bool Apply(PlayerStatusKind kind, float seconds = 0, float strength = 0) => Apply(Definition(kind), seconds, strength);

        /// <summary>
        /// Puts a debuff on the player, or refreshes its timer if it is already on (the new strength replaces the old).
        /// <paramref name="seconds"/>, <paramref name="strength"/>: 0 for the definition's own. False outside an
        /// outing, under the immortality cheat, inside the Bubble, and for a fire on a wet player.
        /// </summary>
        public bool Apply(PlayerStatusDefinition definition, float seconds = 0, float strength = 0)
        {
            if (!definition || !world || !world.Running || !world.vitals || world.vitals.Immortal || world.vitals.HalfHearts <= 0) return false;
            if (Shielded) return false;
            if (definition.blockedWhileWet && PlayerWet) return false;
            float now = Time.time;
            float duration = seconds > 0 ? seconds : definition.seconds;
            float power = strength != 0 ? strength : definition.strength;
            bool fresh = !active.TryGetValue(definition.kind, out var entry);
            if (fresh)
            {
                entry = new Active { definition = definition, started = now, nextTick = now + definition.interval, until = now };
                active[definition.kind] = entry;
            }
            entry.definition = definition;
            entry.strength = power;
            entry.until = definition.Permanent ? float.PositiveInfinity : PlayerStatusRules.RefreshedUntil(entry.until, !fresh, now, duration);
            Begin(entry, fresh);
            world.Play(definition.startCue);
            if (badgePrefab) PlayerStatusBadge.ShowStatus(world, badgePrefab, definition);
            Applied?.Invoke(definition);
            return true;
        }

        /// <summary>Takes a debuff off by an action (put out, washed off, cured): its badge goes at once.</summary>
        public bool Remove(PlayerStatusKind kind) => End(kind, true);

        /// <summary>«Лечение» healed the player: debuffs it cures end.</summary>
        public void Healed()
        {
            snapshot.Clear();
            snapshot.AddRange(active.Values);
            foreach (var entry in snapshot)
                if (entry.definition.curedByHealing) End(entry.definition.kind, true);
        }

        /// <summary>The outing is over: every debuff ends at once, silently, and the screen and sound are back to normal.</summary>
        public void ClearAll()
        {
            snapshot.Clear();
            snapshot.AddRange(active.Values);
            active.Clear();
            foreach (var entry in snapshot)
            {
                Cleanup(entry);
                Ended?.Invoke(entry.definition);
            }
            walked = 0;
            hadFeet = false;
            muffle = blind = warp = 0;
            PresentDeafen(0);
            RestoreFog();
            ResetWarp();
            if (world && world.vitals) world.vitals.ClearRot();
            if (world && world.motor) world.motor.StatusSpeedScale = 1;
        }

        void Begin(Active entry, bool fresh)
        {
            var definition = entry.definition;
            switch (definition.kind)
            {
                case PlayerStatusKind.Rot:
                    world.vitals.Rot(Mathf.Max(1, Mathf.RoundToInt(entry.strength)));
                    break;
                case PlayerStatusKind.Shuffle:
                    ShuffleHand(definition);
                    entry.nextTick = Time.time + definition.interval;
                    break;
                case PlayerStatusKind.Mark:
                    CallCreatures(entry);
                    entry.nextTick = Time.time + definition.interval;
                    break;
                case PlayerStatusKind.Fragile:
                    if (world.feel) { world.feel.AddTrauma(.25f); world.feel.PunchFov(-6); }
                    break;
                case PlayerStatusKind.Deafen:
                    deafCutoff = Mathf.Clamp(entry.strength, 50, 20000);
                    // The blow itself: the head snaps, the view reels (the white flash is on the HUD).
                    if (world.feel)
                    {
                        world.feel.AddTrauma(.5f);
                        world.feel.Kick(new Vector3(-70, UnityEngine.Random.Range(-40f, 40f), UnityEngine.Random.Range(-60f, 60f)));
                        world.feel.PunchFov(-16);
                    }
                    break;
                case PlayerStatusKind.Warp:
                    warpStrength = Mathf.Max(0, entry.strength);
                    break;
                case PlayerStatusKind.LeakyPocket:
                    if (fresh) { walked = 0; hadFeet = false; }
                    break;
            }
        }

        bool End(PlayerStatusKind kind, bool removed)
        {
            if (!active.TryGetValue(kind, out var entry)) return false;
            active.Remove(kind);
            Cleanup(entry);
            if (world)
            {
                world.Play(removed && entry.definition.removedCue ? entry.definition.removedCue : entry.definition.endCue);
                if (removed) PlayerStatusBadge.HideStatus(world, kind);
            }
            Ended?.Invoke(entry.definition);
            return true;
        }

        void Cleanup(Active entry)
        {
            if (world && world.sfx) world.sfx.StopLoop(entry.definition);
            if (entry.definition.kind == PlayerStatusKind.Burn && flames)
            {
                flames.Release(.4f);
                flames = null;
            }
        }

        // ---------------- running ----------------

        void Update()
        {
            if (!world) return;
            if (world.motor) world.motor.StatusSpeedScale = world.Running ? MoveSpeedScale : 1;
            if (!world.Running)
            {
                if (active.Count > 0) ClearAll();
                return;
            }
            if (active.Count == 0) { hadFeet = false; return; }
            float now = Time.time;
            bool wet = PlayerWet;
            Vector3 ear = world.viewCamera ? world.viewCamera.transform.position : transform.position;
            snapshot.Clear();
            snapshot.AddRange(active.Values);
            foreach (var entry in snapshot)
            {
                if (!world.Running) return;
                var definition = entry.definition;
                var kind = definition.kind;
                if (!active.ContainsKey(kind)) continue;
                if (now >= entry.until) { End(kind, false); continue; }
                if (definition.washedByWater && wet) { End(kind, true); continue; }
                if (definition.loopCue && world.sfx) world.sfx.Loop(definition, definition.loopCue, ear);
                if (now < entry.nextTick) continue;
                switch (kind)
                {
                    case PlayerStatusKind.Poison:
                    case PlayerStatusKind.Burn:
                        entry.nextTick += definition.interval;
                        world.Play(definition.tickCue);
                        world.vitals.TickDamage(Mathf.Max(1, Mathf.RoundToInt(entry.strength)), kind == PlayerStatusKind.Burn ? DamageKind.Fire : DamageKind.Other);
                        break;
                    case PlayerStatusKind.Shuffle:
                        entry.nextTick += definition.interval;
                        ShuffleHand(definition);
                        break;
                    case PlayerStatusKind.Mark:
                        entry.nextTick += definition.interval;
                        CallCreatures(entry);
                        break;
                }
            }
            if (world.Running) Leak();
        }

        void ShuffleHand(PlayerStatusDefinition definition)
        {
            var trip = world.workshop && world.workshop.Model != null ? world.workshop.Model.Current : null;
            if (trip == null || !trip.ShuffleHand(random)) return;
            if (world.hand) world.hand.Flutter();
            world.Play(definition.tickCue);
        }

        void CallCreatures(Active entry)
        {
            float radius = Mathf.Max(0, entry.strength);
            Vector3 feet = world.motor.transform.position;
            float seconds = float.IsPositiveInfinity(entry.until) ? entry.definition.interval * 2 : Mathf.Max(.5f, entry.until - Time.time);
            var creatures = world.ActiveCreatures;
            for (int i = 0; i < creatures.Count; i++)
            {
                var creature = creatures[i];
                if (!creature || !creature.Alive || !creature.isActiveAndEnabled) continue;
                if ((creature.Position - feet).sqrMagnitude > radius * radius) continue;
                creature.MarkPlayer(seconds);
            }
        }

        void Leak()
        {
            if (!active.TryGetValue(PlayerStatusKind.LeakyPocket, out var entry)) { hadFeet = false; return; }
            var motor = world.motor;
            Vector3 feet = motor.transform.position;
            if (hadFeet && motor.IsGrounded && motor.PlanarVelocity.magnitude > leakMinimumSpeed)
            {
                Vector3 step = feet - lastFeet;
                step.y = 0;
                walked += Mathf.Min(step.magnitude, 1);
            }
            lastFeet = feet;
            hadFeet = true;
            int drops = PlayerStatusRules.Drops(ref walked, entry.definition.interval);
            for (int i = 0; i < drops; i++) DropCoins(entry, Mathf.Max(1, Mathf.RoundToInt(entry.strength)));
        }

        void DropCoins(Active entry, int amount)
        {
            int lost = world.LoseCoins(amount);
            if (lost <= 0) return;
            var motor = world.motor.transform;
            Vector3 back = -motor.forward;
            Vector3 at = motor.position + Vector3.up * .9f + back * .25f;
            world.Play(entry.definition.tickCue, at);
            if (!droppedCoin) return;
            var coin = Instantiate(droppedCoin, at, UnityEngine.Random.rotation, world.spawnedObjects);
            Vector3 side = motor.right * UnityEngine.Random.Range(-1.2f, 1.2f);
            coin.Drop(world, lost, back * 1.4f + side + Vector3.up * 2.6f);
        }

        // ---------------- presentation ----------------

        void LateUpdate()
        {
            if (!world || !world.viewCamera) return;
            float dt = Time.unscaledDeltaTime;
            bool running = world.Running;

            float deaf = running && Has(PlayerStatusKind.Deafen)
                ? Mathf.Clamp01(Remaining(PlayerStatusKind.Deafen) / muffleRecoverSeconds) : 0;
            muffle = deaf > muffle ? Mathf.MoveTowards(muffle, deaf, dt / muffleFadeSeconds) : deaf;
            PresentDeafen(muffle);

            blind = Mathf.MoveTowards(blind, running && Has(PlayerStatusKind.Blind) ? 1 : 0, dt / blindFadeSeconds);
            if (blind > 0) PresentBlind();
            else RestoreFog();

            warp = Mathf.MoveTowards(warp, running && Has(PlayerStatusKind.Warp) ? 1 : 0, dt / warpFadeSeconds);
            if (warp > 0) PresentWarp(world.viewCamera);
            else ResetWarp();

            PresentFlames(running && Has(PlayerStatusKind.Burn));
        }

        void PresentDeafen(float amount)
        {
            if (deafFilter)
            {
                deafFilter.enabled = amount > .001f;
                // Logarithmic sweep: the ear hears frequency in octaves.
                deafFilter.cutoffFrequency = Mathf.Exp(Mathf.Lerp(Mathf.Log(22000), Mathf.Log(deafCutoff), Ease.InOutSine(amount)));
            }
            if (!ringing) return;
            ringing.volume = ringingVolume * amount;
            if (amount > .001f && !ringing.isPlaying) ringing.Play();
            else if (amount <= .001f && ringing.isPlaying) ringing.Stop();
        }

        void PresentBlind()
        {
            if (!fogTouched)
            {
                fogTouched = true;
                fogWasOn = RenderSettings.fog;
                fogModeBase = RenderSettings.fogMode;
                fogDensityBase = fogWasOn ? RenderSettings.fogDensity : 0;
                fogColourBase = RenderSettings.fogColor;
            }
            else
            {
                // Someone else (the weather, the god's air) moved the fog since our last write: that is the new base.
                if (!Mathf.Approximately(RenderSettings.fogDensity, fogDensityWritten)) fogDensityBase = RenderSettings.fogDensity;
                if (RenderSettings.fogColor != fogColourWritten) fogColourBase = RenderSettings.fogColor;
            }
            RenderSettings.fog = true;
            if (RenderSettings.fogMode == FogMode.Linear) RenderSettings.fogMode = FogMode.ExponentialSquared;
            float weight = Ease.InOutSine(blind);
            float target = PlayerStatusRules.BlindFogDensity(RenderSettings.fogMode, Strength(PlayerStatusKind.Blind, 6), fogDensityBase);
            RenderSettings.fogDensity = fogDensityWritten = Mathf.Lerp(fogDensityBase, target, weight);
            RenderSettings.fogColor = fogColourWritten = Color.Lerp(fogColourBase, blindFogColour, weight);
        }

        void RestoreFog()
        {
            if (!fogTouched) return;
            fogTouched = false;
            if (Mathf.Approximately(RenderSettings.fogDensity, fogDensityWritten)) RenderSettings.fogDensity = fogDensityBase;
            if (RenderSettings.fogColor == fogColourWritten) RenderSettings.fogColor = fogColourBase;
            RenderSettings.fogMode = fogModeBase;
            RenderSettings.fog = fogWasOn;
        }

        void PresentWarp(Camera view)
        {
            if (Has(PlayerStatusKind.Warp)) warpStrength = Mathf.Max(0, Strength(PlayerStatusKind.Warp, 1));
            float k = Ease.InOutSine(warp) * warpStrength;
            float t = Time.time * warpSpeed;
            var p = Matrix4x4.Perspective(view.fieldOfView, view.aspect, view.nearClipPlane, view.farClipPlane);
            p *= Matrix4x4.Rotate(Quaternion.Euler(0, 0, warpRoll * k * Mathf.Sin(t * .7f)));
            p.m01 += warpShear * k * Mathf.Sin(t * 1.3f);
            p.m10 += warpShear * k * Mathf.Sin(t * .9f + 1.7f);
            p.m00 *= 1 + warpStretch * k * Mathf.Sin(t * 1.7f);
            p.m11 *= 1 + warpStretch * k * Mathf.Sin(t * 1.1f + 2.1f);
            p.m02 += warpShift * k * Mathf.Sin(t * 2.3f);
            p.m12 += warpShift * k * Mathf.Sin(t * 1.9f + .6f);
            view.projectionMatrix = p;
            warpTouched = true;
        }

        void ResetWarp()
        {
            if (!warpTouched) return;
            warpTouched = false;
            if (world && world.viewCamera) world.viewCamera.ResetProjectionMatrix();
        }

        void PresentFlames(bool burning)
        {
            if (!burning)
            {
                if (flames) { flames.Release(.4f); flames = null; }
                return;
            }
            var eye = world.viewCamera.transform;
            // Upright like any fire (the flames rise along the world's up), low in front of the eyes.
            Vector3 forward = Vector3.ProjectOnPlane(eye.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.ProjectOnPlane(eye.up, Vector3.up);
            var upright = Quaternion.LookRotation(forward.normalized, Vector3.up);
            Vector3 at = eye.position + upright * burnFlamesOffset;
            if (!flames && burnFlames) flames = world.Spawn(burnFlames, at, upright, burnFlamesTint, burnFlamesScale);
            if (!flames) return;
            flames.transform.SetPositionAndRotation(at, upright);
            flames.HoldFor(1);
        }

        void OnDisable()
        {
            if (world && world.motor) world.motor.StatusSpeedScale = 1;
            PresentDeafen(0);
            RestoreFog();
            ResetWarp();
        }
    }
}
