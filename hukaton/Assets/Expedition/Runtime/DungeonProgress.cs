using System;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// What the player has done in the dungeons that outlives an outing. Kept here, where both the world's dungeons and
    /// the game (which saves it, <c>GameSave.anthillPrizeOpened</c>) can see it; the game restores it on every launch and
    /// new game.
    /// </summary>
    public static class DungeonProgress
    {
        /// <summary>
        /// An anthill's prize chest has been opened at least once. Until then every anthill is the small one (one level,
        /// no Queen); after it the kit's own chance of a large one holds. A dungeon already laid this outing stays as it
        /// is: going in again finds it as it was left.
        /// </summary>
        public static bool AnthillPrizeOpened { get; private set; }

        /// <summary>The progress changed during play (the game marks its save dirty).</summary>
        public static event Action Changed;

        /// <summary>From the save (or false for a new game): no <see cref="Changed"/>.</summary>
        public static void Restore(bool anthillPrizeOpened) => AnthillPrizeOpened = anthillPrizeOpened;

        /// <summary>An anthill's prize chest has just been opened.</summary>
        public static void NoteAnthillPrizeOpened()
        {
            if (AnthillPrizeOpened) return;
            AnthillPrizeOpened = true;
            Changed?.Invoke();
        }
    }
}
