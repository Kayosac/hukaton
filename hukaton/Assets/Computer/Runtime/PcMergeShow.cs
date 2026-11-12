using System;
using System.Collections.Generic;
using Hakaton.Feel;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Hakaton.Computer
{
    /// <summary>
    /// The merge, played right in the card grid. The dropped card winds up and dashes onto the other one, the two
    /// grind together (whole-pixel jitter, accent glow, rising white, the tube wobbling), smash in a white flash with
    /// a Retro Arsenal burst at the cell and a shake of the window, and the card squash-flips showing its new level.
    /// Then the grid settles: the window refreshes and the cards glide to their new cells.
    /// The model is merged before the show starts; only the picture waits. A click, Space or Enter skips to the end;
    /// closing the window or the PC stops it at once. Settings per result level: <see cref="PcMergeFx"/>.
    /// </summary>
    public sealed class PcMergeShow : MonoBehaviour
    {
        const float JitterTicksPerSecond = 30;

        public PcCardsWindow cards;
        [Tooltip("Timings, motion, CRT pulses, bursts and cues per result level (Assets/Computer/Fx/PcMergeFx.asset).")]
        public PcMergeFx settings;
        [Tooltip("In front of the PC camera, on the PC Screen layer: the particles are spawned under it.")]
        public Transform fxRoot;
        [Tooltip("Shaken by the impact: the Cards window.")]
        public RectTransform shakeTarget;

        /// <summary>What to play: the dropped card, the one it was dropped on, and the merged result.</summary>
        public struct Request
        {
            public PcCardTile ghost, target;
            public int resultLevel;
            public bool resultInDeck;
            public Color accent;
        }

        struct Event
        {
            public float at;
            public int burst, cue;
        }

        public bool Playing => playing;
        /// <summary>The show is on, or a click has just skipped it: its release must not also toggle a card.</summary>
        public bool SwallowsClick => playing || swallowRelease;

        bool playing, grindStarted, impactDone, revealStarted, swallowRelease, crtTouched, shaking;
        Request request;
        PcMergeFx.Level level;
        PcMergeTimeline timeline;
        Action<bool> settle;
        float time;
        int seed, side;
        Vector2 from, to, grindFrom, direction, shakeRest;
        readonly List<Event> events = new List<Event>();
        int nextEvent;
        readonly List<PcVfx.Live> live = new List<PcVfx.Live>();
        static bool warnedSettings, warnedRoot, warnedSound;

        PcComputer Computer => cards ? cards.computer : null;

        /// <summary>
        /// Starts the show. The ghost must be held on the drag layer (<see cref="PcCardTile.Hold"/>). <paramref name="settleGrid"/>
        /// runs once at the end, with true when the reveal was shown (false: cut short before it).
        /// </summary>
        public bool Play(Request what, Action<bool> settleGrid)
        {
            if (playing) Finish();
            if (!cards || !what.ghost || !what.target || !what.ghost.Held || what.ghost == what.target) return false;
            level = settings ? settings.For(what.resultLevel) : null;
            if (level == null)
            {
                if (!warnedSettings) Debug.LogWarning("PcMergeShow: no merge settings (PcMergeFx) for level " + what.resultLevel + "; the cards move, without effects or sound.", this);
                warnedSettings = true;
                level = PcMergeFx.Level.Defaults(what.resultLevel);
            }
            timeline = level.Timeline;
            request = what;
            settle = settleGrid;
            playing = true;
            grindStarted = impactDone = revealStarted = false;
            time = 0;
            seed = UnityEngine.Random.Range(1, 1 << 20);

            from = what.ghost.body.anchoredPosition;
            direction = PcMergeMath.Direction(from, TargetOnDragLayer());
            side = direction.x >= 0 ? 1 : -1;
            shaking = false;
            if (shakeTarget) shakeRest = shakeTarget.anchoredPosition;

            Schedule();
            what.ghost.HideMark();
            what.ghost.SetRole(PcCardTile.Role.None);
            what.target.SetRole(PcCardTile.Role.None);
            Fire();
            Step();
            return true;
        }

        /// <summary>Skips to the end: the grid settles now; effects already flying play out.</summary>
        public void Finish()
        {
            if (!playing) return;
            if (!impactDone) PlayCues(PcMergeFx.Beat.Impact);
            End();
        }

        /// <summary>Stops at once and clears every effect; the grid settles (the window or the PC is closing).</summary>
        public void Stop()
        {
            End();
            ClearFx();
        }

        /// <summary>
        /// Drops the show without touching the grid (the hierarchy is being disabled: no re-parenting or refreshing now).
        /// The window refreshes the next time it opens.
        /// </summary>
        public void Abandon()
        {
            if (playing)
            {
                playing = false;
                events.Clear();
                nextEvent = 0;
                settle = null;
                if (request.ghost) request.ghost.EndPose();
                if (request.target) request.target.EndPose();
                request = default;
                ResetCrt();
                ResetShake();
            }
            ClearFx();
        }

        void OnDisable() => Abandon();

        void Schedule()
        {
            events.Clear();
            nextEvent = 0;
            if (level.bursts != null)
                for (int i = 0; i < level.bursts.Length; i++)
                    events.Add(new Event { at = timeline.At(level.bursts[i].beat) + level.bursts[i].delay, burst = i, cue = -1 });
            if (level.cues != null)
                for (int i = 0; i < level.cues.Length; i++)
                    events.Add(new Event { at = timeline.At(level.cues[i].beat) + level.cues[i].delay, burst = -1, cue = i });
            events.Sort((a, b) => a.at.CompareTo(b.at));
        }

        void Update()
        {
            float now = Time.unscaledTime;
            AgeFx(now);
            var mouse = Mouse.current;
            if (swallowRelease && (mouse == null || !mouse.leftButton.isPressed && !mouse.leftButton.wasReleasedThisFrame)) swallowRelease = false;
            if (!playing) return;

            var computer = Computer;
            if (!computer || !computer.Running || !cards.window || !cards.window.IsOpen || !request.ghost || !request.target)
            {
                Stop();
                return;
            }
            if (SkipPressed(out bool byMouse))
            {
                swallowRelease |= byMouse;
                Finish();
                return;
            }
            time += Mathf.Min(Time.unscaledDeltaTime, 1 / 20f);
            Fire();
            Step();
        }

        static bool SkipPressed(out bool byMouse)
        {
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            byMouse = mouse != null && mouse.leftButton.wasPressedThisFrame;
            return byMouse || keyboard != null && (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame);
        }

        // ---------- the picture ----------

        /// <summary>The target cell's middle in the drag layer's space (followed every frame: the grid may scroll).</summary>
        Vector2 TargetOnDragLayer()
        {
            var layer = request.ghost ? request.ghost.body.parent : null;
            return layer ? (Vector2)layer.InverseTransformPoint(request.target.transform.position) : Vector2.zero;
        }

        void Step()
        {
            var ghost = request.ghost;
            var target = request.target;
            if (!impactDone)
            {
                to = TargetOnDragLayer();
                grindFrom = to - direction * level.overlapPixels;
            }
            Color accent = request.accent;
            float tilt = level.tiltDegrees;

            if (time < timeline.FlyEnd)
            {
                // Wind up (a little pull back and swell), then the dash onto the other card.
                float u = timeline.FlyEnd > 0 ? time / timeline.FlyEnd : 1;
                ghost.PlaceHeld(PcMergeMath.Fly(from, grindFrom, u, level.anticipationPixels, level.overshoot, level.arcPixels));
                float lean = Ease.OutCubic(u);
                float swellNow = u < PcMergeMath.WindUp ? Ease.OutCubic(u / PcMergeMath.WindUp) : 1 - Ease.InCubic((u - PcMergeMath.WindUp) / (1 - PcMergeMath.WindUp));
                ghost.Pose(Vector2.zero, -side * tilt * lean, Vector2.one * (1 + level.windUpSwell * swellNow), 0, .35f * lean, accent);
                target.Pose(Vector2.zero, side * tilt * .6f * u, Vector2.one, 0, .5f * u, accent);
                return;
            }

            if (time < timeline.ImpactAt)
            {
                // The grind: closing in, shaking harder, glowing in the card's colour, going white.
                if (!grindStarted)
                {
                    grindStarted = true;
                    target.HideMark();
                }
                float g = timeline.GrindSeconds > 0 ? Mathf.Clamp01((time - timeline.FlyEnd) / timeline.GrindSeconds) : 1;
                float amplitude = Mathf.Lerp(level.jitterPixels.x, level.jitterPixels.y, g * g);
                int tick = Mathf.FloorToInt(time * JitterTicksPerSecond);
                Vector2 mine = PcMergeMath.Jitter(seed, tick, amplitude), theirs = PcMergeMath.Jitter(seed + 7919, tick, amplitude);
                ghost.PlaceHeld(Vector2.Lerp(grindFrom, to, Ease.InCubic(g)) + mine);
                float pulse = .55f + .45f * Mathf.Abs(Mathf.Sin(time * level.glowPulseHz * Mathf.PI));
                float white = level.grindFlash * g * g;
                ghost.Pose(Vector2.zero, -side * tilt, Vector2.one, white, pulse, accent);
                target.Pose(-theirs, side * tilt * .6f, Vector2.one, white, pulse * Mathf.Lerp(.6f, 1, g), accent);
                Crt(level.grindWobble * g * g, level.grindNoise * g * g, 0);
                return;
            }

            if (!impactDone) Impact();
            float since = time - timeline.ImpactAt;
            if (time < timeline.RevealAt)
            {
                // The frozen moment: squashed, white.
                target.Pose(Vector2.zero, 0, level.impactSquash, 1, 1, accent);
            }
            else
            {
                if (!revealStarted) Reveal();
                float r = time - timeline.RevealAt;
                target.Pose(Vector2.zero, 0, Vector2.one, Mathf.Clamp01(1 - r / .3f), Mathf.Clamp01(1 - r / .45f), accent);
            }
            float fade = Mathf.Exp(-since / Mathf.Max(.01f, level.crtFadeSeconds));
            Crt(level.impactWobble * fade, level.grindNoise * fade, level.impactFlash * fade);
            Shake(since);
            if (time >= timeline.End) End();
        }

        void Impact()
        {
            impactDone = true;
            grindStarted = true;
            request.ghost.Vanish();
            request.target.HideMark();
            foreach (var effect in live)
                if (effect.clearOnImpact && effect.root) Kill(effect);
            live.RemoveAll(effect => !effect.root);
            shaking = shakeTarget;
        }

        void Reveal()
        {
            revealStarted = true;
            var target = request.target;
            int resultLevel = request.resultLevel;
            bool inDeck = request.resultInDeck;
            float hop = level.markHopPixels, swell = level.swell;
            target.FlipArt(() =>
            {
                if (!target) return;
                target.ShowLevel(resultLevel, inDeck);
                target.HopMark(hop);
                target.Swell(swell);
            }, level.flipFrequency);
        }

        void End()
        {
            if (!playing) return;
            playing = false;
            events.Clear();
            nextEvent = 0;
            var what = request;
            request = default;
            if (what.ghost)
            {
                if (!impactDone) what.ghost.Vanish();
                what.ghost.EndPose();
            }
            if (what.target) what.target.EndPose();
            foreach (var effect in live)
                if (effect.clearOnImpact && effect.root) Kill(effect);
            live.RemoveAll(effect => !effect.root);
            ResetCrt();
            ResetShake();
            var then = settle;
            settle = null;
            then?.Invoke(revealStarted);
        }

        // ---------- CRT and shake ----------

        void Crt(float wobble, float noise, float flash)
        {
            var computer = Computer;
            if (!computer || !computer.screen || !computer.Running) return;
            computer.screen.wobble = wobble;
            computer.screen.noise = noise;
            computer.screen.flash = flash;
            crtTouched = true;
        }

        void ResetCrt()
        {
            if (!crtTouched) return;
            crtTouched = false;
            // Off the desktop the boot owns the tube (it is switching off right now).
            var computer = Computer;
            if (computer && computer.screen && computer.Running) computer.screen.wobble = computer.screen.noise = computer.screen.flash = 0;
        }

        void Shake(float since)
        {
            if (!shaking || !shakeTarget) return;
            var offset = PcMergeMath.Shake(seed + 31, since, level.windowShakePixels, level.windowShakeSeconds);
            shakeTarget.anchoredPosition = shakeRest + offset;
        }

        void ResetShake()
        {
            if (shaking && shakeTarget) shakeTarget.anchoredPosition = shakeRest;
            shaking = false;
        }

        // ---------- bursts and cues ----------

        void Fire()
        {
            while (nextEvent < events.Count && events[nextEvent].at <= time)
            {
                var e = events[nextEvent++];
                if (e.burst >= 0) Spawn(level.bursts[e.burst]);
                if (e.cue >= 0) PlayCue(level.cues[e.cue]);
            }
        }

        void Spawn(PcMergeFx.Burst burst)
        {
            if (!burst.prefab || !request.target) return;
            if (!fxRoot)
            {
                if (!warnedRoot) Debug.LogWarning("PcMergeShow: no FX root (under the PC camera); merge effects are skipped.", this);
                warnedRoot = true;
                return;
            }
            var cell = request.target.transform;
            // Canvas pixels → world: the canvas is about 100 pixels per unit; scale the effect with it.
            float unitsPerPixel = Mathf.Abs(cell.lossyScale.x) > 1e-6f ? Mathf.Abs(cell.lossyScale.x) : .01f;
            float scale = (burst.scale > 0 ? burst.scale : 1) * unitsPerPixel * 100 / Mathf.Max(1e-4f, Mathf.Abs(fxRoot.lossyScale.x));
            var effect = PcVfx.Spawn(burst.prefab, fxRoot, cell.TransformPoint(burst.offset), Quaternion.Euler(burst.rotation), scale,
                request.accent, burst.accent, burst.speed, burst.hold, Time.unscaledTime);
            if (effect == null) return;
            effect.clearOnImpact = burst.clearOnImpact && !impactDone;
            live.Add(effect);
        }

        void PlayCues(PcMergeFx.Beat beat)
        {
            if (level?.cues == null) return;
            foreach (var cue in level.cues)
                if (cue.beat == beat) PlayCue(cue);
        }

        void PlayCue(PcMergeFx.Cue cue)
        {
            if (!cue.cue) return;
            var computer = Computer;
            if (!computer || !computer.sfx)
            {
                if (!warnedSound) Debug.LogWarning("PcMergeShow: the PC has no SfxPlayer; merge cues are skipped.", this);
                warnedSound = true;
                return;
            }
            computer.sfx.Play(cue.cue, null, cue.volume > 0 ? cue.volume : 1, cue.pitch > 0 ? cue.pitch : 1);
        }

        void AgeFx(float now)
        {
            if (live.Count == 0) return;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var effect = live[i];
                if (!effect.root) { live.RemoveAt(i); continue; }
                if (now >= effect.stopAt) effect.StopEmitting();
                if (now >= effect.dieAt)
                {
                    Destroy(effect.root);
                    live.RemoveAt(i);
                }
            }
        }

        static void Kill(PcVfx.Live effect)
        {
            foreach (var system in effect.systems)
                if (system) system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            Destroy(effect.root);
            effect.root = null;
        }

        void ClearFx()
        {
            foreach (var effect in live)
                if (effect.root) Kill(effect);
            live.Clear();
        }
    }
}
