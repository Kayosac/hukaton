using Hakaton.Expeditions;

namespace Hakaton.Tutorial
{
    /// <summary>
    /// The cave's instruction cards: a drawing on paper. Thrown, it flutters into whatever it hits and does nothing
    /// else — the lesson was the throw itself.
    /// </summary>
    public sealed class InstructionNoteEffect : CardEffect
    {
        public override void Apply(in ImpactContext context) => Feedback(context, false);
    }
}
