using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// How a creature is heard. Every sound it makes goes through the managed creature buses of the sound player
    /// (<see cref="SfxBus"/>) with a priority: what it does to the player matters most, a fight between beasts less,
    /// a calm creature's grunt least - that one is rationed, heard closer and ducked while something attacks.
    /// </summary>
    public sealed partial class WorldCreature
    {
        enum VoiceKind { Step, Alert, Threat, Attack, Hurt, Death }

        /// <summary>1 = busy with the player, ~.55 = fleeing or fighting someone else, 0 = minding its own business.</summary>
        float Engagement
        {
            get
            {
                if (!world) return 0;
                if (Brawling || State == CreatureState.Flee) return .55f;
                bool player = ReferenceEquals(Target, world.Player);
                if (IsCalm) return Lurking && player ? 1 : 0;
                return player ? 1 : .55f;
            }
        }

        static float KindWeight(VoiceKind kind)
        {
            switch (kind)
            {
                case VoiceKind.Attack: return 1;
                case VoiceKind.Death: return .95f;
                case VoiceKind.Hurt: return .9f;
                case VoiceKind.Threat: return .8f;
                case VoiceKind.Alert: return .7f;
                default: return .6f;
            }
        }

        float VoicePriority(VoiceKind kind)
        {
            float engagement = Engagement;
            // A hit or a death is feedback of a fight - mostly the player's own.
            if (kind == VoiceKind.Hurt || kind == VoiceKind.Death) engagement = Mathf.Max(engagement, .7f);
            return KindWeight(kind) * (.35f + .65f * engagement);
        }

        AudioSource Voice(VoiceKind kind, SfxCue cue, Vector3 at, float volume = 1, float pitch = 1)
        {
            if (!world || !cue) return null;
            var bus = kind == VoiceKind.Step ? SfxBus.CreatureStep : SfxBus.Creature;
            // Beasts fighting each other (a beast brawl, «Раздор») are heard as one fight, not as every beast at once.
            var brawl = Brawling ? Referee as Object : null;
            if (!brawl || !world.settings) return world.PlayIn(bus, this, VoicePriority(kind), cue, at, volume, pitch);
            var settings = world.settings;
            if (kind != VoiceKind.Death) volume *= settings.brawlVolume;
            return world.PlayIn(bus, this, VoicePriority(kind), cue, at, volume, pitch, brawl, settings.brawlVoices, BrawlMuffle);
        }

        /// <summary>From afar the brawl sounds as from behind a wall; it opens up as the player comes close.</summary>
        float BrawlMuffle
        {
            get
            {
                var settings = world.settings;
                float distance = Flat(PlayerPosition - arenaCentre).magnitude;
                return Mathf.SmoothStep(0, 1, Mathf.InverseLerp(settings.brawlClearDistance, Mathf.Max(settings.brawlClearDistance + .1f, settings.brawlMuffledDistance), distance));
            }
        }

        /// <summary>The body loop (a swarm's buzz) as a request: only the few loudest that matter really sound.</summary>
        void SoundBodyLoop(float volume)
        {
            world.sfx.Loop(this, bodyLoop, Centre, volume, SfxBus.CreatureLoop, .3f + .7f * Engagement);
        }
    }
}
