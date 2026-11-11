using System;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// Per-instance speech timing with an injected clock and random source. It never changes AI, input or camera.
    /// Quiet encounters contain exactly two opportunities: a greeting and one follow-up. Empty pools are skipped.
    /// </summary>
    public sealed class CreatureSpeechController
    {
        enum Encounter { Greeting, Followup, Complete }

        readonly System.Random random;
        readonly string[] previousLines = new string[5];
        CreatureSpeechProfile currentProfile;
        Encounter encounter;
        CreatureSpeechMood observedMood, pendingMood;
        bool wasAvailable, pendingReaction;
        float lineUntil, shownAt, nextEncounterLine, nextReactionAt, pendingAt, pendingUntil;
        float outsideSince = float.NaN;

        public string CurrentLine { get; private set; }
        public CreatureSpeechMood? CurrentMood { get; private set; }

        public CreatureSpeechController(System.Random random = null) => this.random = random ?? new System.Random();

        public void Reset()
        {
            currentProfile = null;
            encounter = Encounter.Greeting;
            wasAvailable = pendingReaction = false;
            lineUntil = shownAt = nextEncounterLine = nextReactionAt = pendingAt = pendingUntil = 0;
            outsideSince = float.NaN;
            Array.Clear(previousLines, 0, previousLines.Length);
            Hide();
        }

        /// <summary>
        /// Updates speech and returns whether the visible text changed. Unavailable hides immediately but retains
        /// cooldowns. Only time outside the radius (inRange=false), not occlusion, rearms the quiet encounter.
        /// </summary>
        public bool Step(float now, CreatureSpeechProfile profile, CreatureSpeechMood? mood, bool available, bool inRange = true)
        {
            string before = CurrentLine;
            if (currentProfile != profile)
            {
                Reset();
                currentProfile = profile;
            }
            if (!profile)
            {
                Hide();
                return before != CurrentLine;
            }

            if (!inRange)
            {
                if (float.IsNaN(outsideSince)) outsideSince = now;
            }
            else if (!float.IsNaN(outsideSince))
            {
                if (now - outsideSince >= Mathf.Max(0, profile.reencounterDelay))
                {
                    encounter = Encounter.Greeting;
                    nextEncounterLine = now;
                }
                outsideSince = float.NaN;
            }

            if (!available || !inRange || !mood.HasValue)
            {
                wasAvailable = pendingReaction = false;
                Hide();
                return before != CurrentLine;
            }

            if (!wasAvailable)
            {
                wasAvailable = true;
                observedMood = mood.Value;
                float delay = Between(profile.initialDelay);
                nextEncounterLine = Mathf.Max(nextEncounterLine, now + delay);
                if (mood.Value != CreatureSpeechMood.Neutral)
                {
                    encounter = Encounter.Complete;
                    QueueReaction(mood.Value, now, delay, profile);
                }
            }
            else if (mood.Value != observedMood)
            {
                observedMood = mood.Value;
                if (mood.Value == CreatureSpeechMood.Neutral)
                {
                    pendingReaction = false;
                    if (CurrentMood != CreatureSpeechMood.Neutral) Hide();
                }
                else
                {
                    encounter = Encounter.Complete;
                    QueueReaction(mood.Value, now, 0, profile);
                }
            }

            if (CurrentLine != null && now >= lineUntil) Hide();
            if (pendingReaction && now > pendingUntil) pendingReaction = false;

            bool canReact = CurrentLine == null || CurrentMood == CreatureSpeechMood.Neutral &&
                now >= shownAt + Mathf.Max(0, profile.minimumReadSeconds);
            if (pendingReaction && canReact && now >= pendingAt && now >= nextReactionAt)
            {
                CreatureSpeechMood reaction = pendingMood;
                pendingReaction = false;
                SpeakReaction(now, reaction, profile);
            }

            if (CurrentLine == null)
            {
                if (mood.Value == CreatureSpeechMood.Neutral && now >= nextEncounterLine)
                    SpeakEncounter(now, profile);
                else if (mood.Value != CreatureSpeechMood.Neutral && !pendingReaction && now >= nextReactionAt)
                    SpeakReaction(now, mood.Value, profile);
            }
            return before != CurrentLine;
        }

        void QueueReaction(CreatureSpeechMood mood, float now, float delay, CreatureSpeechProfile profile)
        {
            // Keep a brief attack while Recover/Chase follows it, rather than replacing it with a generic taunt.
            if (pendingReaction && pendingMood == CreatureSpeechMood.Attack && mood == CreatureSpeechMood.Aggression && now <= pendingUntil)
                return;
            pendingReaction = true;
            pendingMood = mood;
            pendingAt = now + delay;
            pendingUntil = now + Mathf.Max(delay, Mathf.Max(0, profile.reactionMemorySeconds));
        }

        void SpeakEncounter(float now, CreatureSpeechProfile profile)
        {
            if (encounter == Encounter.Complete) return;
            if (encounter == Encounter.Greeting)
            {
                encounter = Encounter.Followup;
                string greeting = Pick(profile.greetings, 4);
                if (greeting != null)
                {
                    Show(greeting, CreatureSpeechMood.Neutral, now, profile);
                    nextEncounterLine = lineUntil + Mathf.Max(0, profile.encounterFollowupDelay);
                    return;
                }
            }
            // A missing greeting still allows one authored neutral line; a missing follow-up ends the encounter.
            encounter = Encounter.Complete;
            string followup = Pick(profile.neutral, (int)CreatureSpeechMood.Neutral);
            if (followup != null) Show(followup, CreatureSpeechMood.Neutral, now, profile);
        }

        void SpeakReaction(float now, CreatureSpeechMood mood, CreatureSpeechProfile profile)
        {
            string line = Pick(profile.GetLines(mood), (int)mood);
            // Also throttle an intentionally empty reaction pool, so it is not scanned every frame.
            nextReactionAt = now + Mathf.Max(.1f, profile.displaySeconds) + Between(profile.reactionPause);
            if (line != null) Show(line, mood, now, profile);
        }

        void Show(string line, CreatureSpeechMood mood, float now, CreatureSpeechProfile profile)
        {
            CurrentLine = line;
            CurrentMood = mood;
            shownAt = now;
            lineUntil = now + Mathf.Max(.1f, profile.displaySeconds);
        }

        void Hide()
        {
            CurrentLine = null;
            CurrentMood = null;
        }

        string Pick(string[] lines, int pool)
        {
            if (lines == null) return null;
            string previous = previousLines[pool];
            bool hasAlternative = false;
            foreach (string candidate in lines)
                if (!string.IsNullOrWhiteSpace(candidate) && candidate.Trim() != previous) { hasAlternative = true; break; }

            // Reservoir sampling is uniform over authored entries without allocating a filtered list.
            string selected = null;
            int count = 0;
            foreach (string candidate in lines)
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                string clean = candidate.Trim();
                if (hasAlternative && clean == previous) continue;
                if (random.Next(++count) == 0) selected = clean;
            }
            if (selected != null) previousLines[pool] = selected;
            return selected;
        }

        float Between(Vector2 interval)
        {
            float low = Mathf.Max(0, Mathf.Min(interval.x, interval.y));
            float high = Mathf.Max(low, Mathf.Max(interval.x, interval.y));
            return Mathf.Lerp(low, high, (float)random.NextDouble());
        }

        public static CreatureSpeechMood? MoodFor(CreatureState state, CreatureSpeechMood? previousMood = null)
        {
            switch (state)
            {
                case CreatureState.Alert:
                case CreatureState.Flee: return CreatureSpeechMood.Fear;
                case CreatureState.Threat:
                case CreatureState.Chase:
                case CreatureState.Search:
                case CreatureState.Reposition: return CreatureSpeechMood.Aggression;
                case CreatureState.Windup:
                case CreatureState.Strike:
                case CreatureState.Aim:
                case CreatureState.Shoot: return CreatureSpeechMood.Attack;
                case CreatureState.Recover:
                    return previousMood.HasValue && previousMood != CreatureSpeechMood.Neutral
                        ? previousMood : CreatureSpeechMood.Aggression;
                case CreatureState.Frozen:
                case CreatureState.Stunned:
                case CreatureState.Dying: return null;
                default: return CreatureSpeechMood.Neutral;
            }
        }
    }
}
