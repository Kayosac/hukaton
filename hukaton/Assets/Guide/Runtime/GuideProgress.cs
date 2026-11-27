using System;
using System.Collections.Generic;

namespace Hakaton.Guide
{
    /// <summary>What the first-open guides (the brazier, the PC) have shown: finished tours and where an unfinished one stopped.</summary>
    [Serializable]
    public sealed class GuideSave
    {
        public List<string> done = new List<string>();
        /// <summary>Unfinished tours and the step each stopped at (parallel lists: JsonUtility has no dictionaries).</summary>
        public List<string> started = new List<string>();
        public List<int> steps = new List<int>();
        /// <summary>
        /// The record's layout: 0 — before balance stage 10 (one PC tour per window, the merge inside «Карты»); 1 — the PC's
        /// tours in parts. An older record is brought up to date by the place that split its tours (<see cref="GuideProgress.Restore"/>).
        /// </summary>
        public int version;
    }

    /// <summary>
    /// The guides' memory: a tour is shown once; one left half-way (the game closed, the PC switched off) carries on
    /// from the step it stopped at. Skipping a tour counts as seeing it.
    /// </summary>
    public sealed class GuideProgress
    {
        readonly HashSet<string> done = new HashSet<string>();
        readonly Dictionary<string, int> steps = new Dictionary<string, int>();

        /// <summary>The layout this code saves (<see cref="GuideSave.version"/>).</summary>
        public const int CurrentVersion = 1;

        public event Action Changed;

        public bool IsDone(string tour) => done.Contains(tour);

        /// <summary>The step an unfinished tour stopped at (0: not started).</summary>
        public int StepOf(string tour) => steps.TryGetValue(tour, out var step) ? step : 0;

        public void SetStep(string tour, int step)
        {
            if (IsDone(tour) || StepOf(tour) == step) return;
            steps[tour] = step;
            Changed?.Invoke();
        }

        public void Finish(string tour)
        {
            bool changed = steps.Remove(tour);
            changed |= done.Add(tour);
            if (changed) Changed?.Invoke();
        }

        /// <summary>Every tour whose id starts with <paramref name="prefix"/> is shown again (all of them for "").</summary>
        public void Forget(string prefix = "")
        {
            int before = done.Count + steps.Count;
            done.RemoveWhere(id => id.StartsWith(prefix, StringComparison.Ordinal));
            var keys = new List<string>(steps.Keys);
            foreach (var key in keys)
                if (key.StartsWith(prefix, StringComparison.Ordinal)) steps.Remove(key);
            if (done.Count + steps.Count != before) Changed?.Invoke();
        }

        public GuideSave Save()
        {
            var save = new GuideSave { version = CurrentVersion };
            save.done.AddRange(done);
            save.done.Sort(StringComparer.Ordinal);
            foreach (var pair in steps)
            {
                save.started.Add(pair.Key);
                save.steps.Add(pair.Value);
            }
            return save;
        }

        /// <summary>
        /// Null (a save from before the guides): nothing shown yet. A record of an older layout is handed to
        /// <paramref name="upgradeOlder"/> once loaded (with its version), so a tour split into parts keeps what was seen.
        /// </summary>
        public void Restore(GuideSave save, Action<GuideProgress, int> upgradeOlder = null)
        {
            done.Clear();
            steps.Clear();
            if (save != null)
            {
                if (save.done != null)
                    foreach (var id in save.done)
                        if (!string.IsNullOrEmpty(id)) done.Add(id);
                if (save.started != null && save.steps != null)
                    for (int i = 0; i < save.started.Count && i < save.steps.Count; i++)
                        if (!string.IsNullOrEmpty(save.started[i]) && !done.Contains(save.started[i]) && save.steps[i] > 0)
                            steps[save.started[i]] = save.steps[i];
                if (save.version < CurrentVersion) upgradeOlder?.Invoke(this, save.version);
            }
            Changed?.Invoke();
        }
    }
}
