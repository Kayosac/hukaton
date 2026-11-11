using System;
using Hakaton.Character;
using UnityEngine;

namespace Hakaton.Teaser
{
    /// <summary>Brings the shop's real customer actor to the counter during the pre-roll and changes his expression on cue.</summary>
    public sealed class TeaserCustomer : TeaserTrack
    {
        [Serializable]
        public struct Cue
        {
            public float seconds;
            public NpcExpression expression;
        }

        public SpriteNpcActor actor;
        public NpcAppearance appearance;
        public Cue[] cues = new Cue[0];
        [Tooltip("Teaser seconds (x = from, y = to) when the customer's drawing is hidden.")]
        public Vector2[] hidden = new Vector2[0];

        bool visiting;
        NpcExpression shown = NpcExpression.Neutral;

        public override void Evaluate(float seconds)
        {
            if (!actor || !appearance) return;
            if (!visiting) visiting = actor.BeginVisit(appearance);
            if (!visiting) return;
            bool hide = false;
            foreach (var window in hidden)
                if (seconds >= window.x && seconds < window.y) hide = true;
            if (actor.spriteRenderer && actor.spriteRenderer.enabled == hide) actor.spriteRenderer.enabled = !hide;
            var expression = NpcExpression.Neutral;
            foreach (var cue in cues)
                if (seconds >= cue.seconds) expression = cue.expression;
            if (expression != shown || actor.CurrentExpression != expression)
            {
                actor.SetExpression(expression);
                shown = expression;
            }
        }
    }
}
