namespace Hakaton.Expeditions
{
    /// <summary>
    /// A fighter whose walking speed a card can scale for a while (tar puddle ×0.5). <see cref="WorldCreature"/> has
    /// the same method and is handled directly; allies (awakened trees) implement this interface so tar slows them
    /// too. Zones refresh it every tick while the fighter stands in them, so a short duration ends the effect soon
    /// after stepping off. Same rule as <see cref="Hakaton.Character.TimedModifier"/>: values never multiply.
    /// Added by cluster B (tar puddle); see tmp/new-cards/API.md.
    /// </summary>
    public interface IMoveSpeedScalable
    {
        void ApplyMoveSpeedScale(float scale, float seconds);
    }
}
