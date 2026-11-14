using System;
using UnityEngine;

namespace Hakaton.Guide
{
    public enum GuideStepKind
    {
        /// <summary>A line about the lit place; E or a left click reads on. Clicks do not reach the game.</summary>
        Read,
        /// <summary>The player does it: the lit place takes the pointer, the step ends when it is done.</summary>
        Do,
        /// <summary>The guide shows it (a ghost pointer, a pretend card); E or a click reads on once it has been shown.</summary>
        Watch
    }

    /// <summary>One step of a tour: what is lit (by key, resolved by the place's guide), the line, how it ends.</summary>
    [Serializable]
    public sealed class GuideStep
    {
        [Tooltip("What the step is about; the place's guide knows what to light, what counts as done and what to show for it.")]
        public string key;
        [TextArea(2, 5)] public string text;
        public GuideStepKind kind;
        [Tooltip("Room around the lit place, in its canvas units.")]
        public float padding = 4;
    }

    [Serializable]
    public sealed class GuideTour
    {
        [Tooltip("Saved: renaming it shows the tour again.")]
        public string id;
        public GuideStep[] steps = Array.Empty<GuideStep>();
    }
}
