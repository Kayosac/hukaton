namespace Hakaton.Expeditions
{
    /// <summary>Debug jumps the outing world offers to the cheat menu (F1).</summary>
    public interface IOutingCheats
    {
        /// <summary>Puts the player in front of the nearest dungeon entrance; returns what happened (Russian).</summary>
        string GoToNearestDungeon();
    }
}
