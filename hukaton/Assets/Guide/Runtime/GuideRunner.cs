using System;
using System.Collections;
using UnityEngine;

namespace Hakaton.Guide
{
    /// <summary>
    /// Plays a place's first-open tours step by step: lights the step's place, has the voice say its line and waits —
    /// for E or a left click (<see cref="GuideStepKind.Read"/>), for the player to do the thing
    /// (<see cref="GuideStepKind.Do"/>), or for the shown action to have played once (<see cref="GuideStepKind.Watch"/>).
    /// Esc skips. The step reached is saved, so a tour left half-way carries on from there; a finished or skipped one
    /// never comes again (until it is forgotten: the PC's help, the cheat menu). The place's own guide says what each
    /// key lights, when it is done and what is shown for it.
    /// </summary>
    public abstract class GuideRunner : MonoBehaviour
    {
        [Tooltip("The scene's record of shown tours (saved by the game). Without it no tour runs.")]
        public GuideBook book;
        [Tooltip("The dim with the lit hole; stretched over the overlay.")]
        public GuideSpotlight spotlight;
        public GhostPointer pointer;

        Coroutine routine, demo;
        bool skipRequested, shownOnce, continueRequested;
        string demoKey;

        public bool Running => routine != null;
        /// <summary>The tour being played (null when none).</summary>
        public string Tour { get; private set; }
        public GuideStep Step { get; private set; }
        public event Action<string> Finished;

        protected GuideProgress Progress => book ? book.Progress : null;

        /// <summary>As if E were pressed (tests, tools).</summary>
        public void Continue() => continueRequested = true;
        protected abstract IGuideVoice Voice { get; }

        /// <summary>Shown or skipped already (and without a record nothing is shown).</summary>
        public bool Seen(string tour) => Progress == null || Progress.IsDone(tour);

        protected bool Begin(GuideTour tour)
        {
            if (tour == null || Running || !isActiveAndEnabled || Seen(tour.id) || tour.steps == null || tour.steps.Length == 0) return false;
            routine = StartCoroutine(Run(tour));
            return true;
        }

        /// <summary>Breaks off now (the place closed): the step reached is kept, the tour carries on next time.</summary>
        public void Abort()
        {
            if (!Running) return;
            StopCoroutine(routine);
            routine = null;
            End(Tour, false);
        }

        void OnDisable() => Abort();

        IEnumerator Run(GuideTour tour)
        {
            Tour = tour.id;
            skipRequested = false;
            OnBegin(tour.id);
            if (spotlight) { spotlight.Clear(); spotlight.Show(true); }
            int start = Mathf.Clamp(Progress.StepOf(tour.id), 0, tour.steps.Length - 1);
            for (int i = start; i < tour.steps.Length && !skipRequested; i++)
            {
                Progress.SetStep(tour.id, i);
                var step = tour.steps[i];
                if (step == null || !Applies(tour.id, step)) continue;
                yield return RunStep(tour.id, step);
            }
            routine = null;
            if (skipRequested) OnSkip(tour.id);
            Progress.Finish(tour.id);
            End(tour.id, true);
        }

        void End(string tour, bool finished)
        {
            StopDemo();
            Voice?.Hide();
            if (spotlight) { spotlight.Clear(); spotlight.Show(false); }
            if (pointer) pointer.Show(false);
            Step = null;
            Tour = null;
            OnEnd(tour, finished);
            if (finished) Finished?.Invoke(tour);
        }

        IEnumerator RunStep(string tour, GuideStep step)
        {
            Step = step;
            OnStep(tour, step);
            var lit = Resolve(tour, step.key);
            if (spotlight)
            {
                var area = lit ? null : ResolveArea(tour, step.key);
                if (lit) spotlight.Focus(lit, step.padding);
                else if (area.HasValue) spotlight.Focus(Grow(area.Value, step.padding));
                else spotlight.Clear();
                spotlight.PassThrough = step.kind == GuideStepKind.Do;
            }
            if (step.kind == GuideStepKind.Do && Done(tour, step.key)) yield break;
            // A frame for the light to reach its place before the plate decides where it stands.
            yield return null;
            Voice?.KeepClearOf(spotlight ? spotlight.Hole : null, spotlight ? spotlight.rectTransform : null);
            Voice?.Say(step.text, step.kind != GuideStepKind.Do);
            if (step.kind == GuideStepKind.Watch) StartDemo(tour, step.key);
            // The press that ended the last step must not also end this one.
            yield return null;
            continueRequested = false;
            while (true)
            {
                if (GuideInput.Skip) { skipRequested = true; break; }
                if (step.kind == GuideStepKind.Do)
                {
                    if (Done(tour, step.key)) break;
                }
                else if ((GuideInput.Advance || continueRequested) && !(Voice != null && Voice.SkipTyping()))
                {
                    if (step.kind != GuideStepKind.Watch || shownOnce) break;
                }
                continueRequested = false;
                Voice?.KeepClearOf(spotlight ? spotlight.Hole : null, spotlight ? spotlight.rectTransform : null);
                yield return null;
            }
            StopDemo();
            if (spotlight) spotlight.PassThrough = false;
        }

        void StartDemo(string tour, string key)
        {
            shownOnce = false;
            demoKey = key;
            demo = StartCoroutine(DemoLoop(tour, key));
        }

        IEnumerator DemoLoop(string tour, string key)
        {
            while (true)
            {
                yield return Demo(tour, key);
                shownOnce = true;
                yield return GhostPointer.Wait(.6f);
            }
        }

        void StopDemo()
        {
            if (demo != null) StopCoroutine(demo);
            demo = null;
            if (demoKey != null) EndDemo(Tour, demoKey);
            demoKey = null;
        }

        // ---------- the place's own part ----------

        /// <summary>What the step lights (null: everything dimmed, nothing lit).</summary>
        protected abstract RectTransform Resolve(string tour, string key);

        /// <summary>When <see cref="Resolve"/> has no single rect: an area in the spotlight's local space (several
        /// things at once, a pretend card pair), or null for nothing lit.</summary>
        protected virtual Rect? ResolveArea(string tour, string key) => null;

        /// <summary>Several rects together, in the spotlight's local space.</summary>
        protected Rect? Union(params RectTransform[] rects)
        {
            Rect? all = null;
            foreach (var rect in rects)
            {
                if (!rect || !rect.gameObject.activeInHierarchy || !spotlight) continue;
                var r = spotlight.LocalRectOf(rect);
                all = all.HasValue ? Rect.MinMaxRect(Mathf.Min(all.Value.xMin, r.xMin), Mathf.Min(all.Value.yMin, r.yMin),
                    Mathf.Max(all.Value.xMax, r.xMax), Mathf.Max(all.Value.yMax, r.yMax)) : r;
            }
            return all;
        }

        static Rect Grow(Rect r, float by) => Rect.MinMaxRect(r.xMin - by, r.yMin - by, r.xMax + by, r.yMax + by);

        /// <summary>A «сделай сам» step is done (checked every frame).</summary>
        protected virtual bool Done(string tour, string key) => true;

        /// <summary>One run of a «смотри» step's show; played again and again until the player reads on.</summary>
        protected virtual IEnumerator Demo(string tour, string key) { yield return GhostPointer.Wait(1); }

        /// <summary>The show was cut (read on, skipped, closed): put back whatever it borrowed.</summary>
        protected virtual void EndDemo(string tour, string key) { }

        /// <summary>A step that does not fit now (nothing to burn, no second row) is passed over.</summary>
        protected virtual bool Applies(string tour, GuideStep step) => true;

        protected virtual void OnBegin(string tour) { }
        protected virtual void OnStep(string tour, GuideStep step) { }
        /// <summary>Esc: the place may count its other tours as seen too.</summary>
        protected virtual void OnSkip(string tour) { }
        protected virtual void OnEnd(string tour, bool finished) { }
    }
}
