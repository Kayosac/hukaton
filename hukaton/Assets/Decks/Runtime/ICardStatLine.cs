namespace Hakaton.Decks.Unity
{
    /// <summary>
    /// Implemented by a card's effect asset to name its main number for the workshop detail line
    /// («Длительность  12  →  45 с»). The number then lives in one place — the effect asset. Cards without it
    /// keep the generic power line.
    /// </summary>
    public interface ICardStatLine
    {
        /// <summary>
        /// Label, base and upgraded value, and unit with its leading space (" с", " м", or ""). Return false when the
        /// card has no number worth showing.
        /// </summary>
        bool TryGetStat(out string label, out float baseValue, out float upgradedValue, out string unit);
    }
}
