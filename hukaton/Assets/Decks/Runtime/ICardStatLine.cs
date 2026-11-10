namespace Hakaton.Decks.Unity
{
    /// <summary>
    /// Implemented by a card's effect asset to name its main number for the PC detail line
    /// («Длительность  12 · 25 · 45 с»). The number then lives in one place — the effect asset. Cards without it
    /// show the card definition's power or impulse.
    /// </summary>
    public interface ICardStatLine
    {
        /// <summary>Label, the number at I / II / III, and the unit with its leading space (" с", " м", or "").
        /// Return false when the card has no number worth showing.</summary>
        bool TryGetStat(out string label, out LevelFloat values, out string unit);
    }
}
