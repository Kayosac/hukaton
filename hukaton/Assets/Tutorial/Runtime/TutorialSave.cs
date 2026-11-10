using System;

namespace Hakaton.Tutorial
{
    public enum ShopTutorialStage
    {
        /// <summary>The goblin greets the partner on the counter.</summary>
        Arrival = 0,
        /// <summary>The cap and the fang are given; waiting for both on the display.</summary>
        Stocking = 1,
        /// <summary>Both are out: open the shop and sell them.</summary>
        Trading = 2,
        /// <summary>Two sales made: the goblin's last words, then poof.</summary>
        Finale = 3,
        Done = 4
    }

    /// <summary>Saved progress of the shop part of the tutorial (board, «Туториал», 08).</summary>
    [Serializable]
    public sealed class TutorialSave
    {
        public int stage;
        /// <summary>Lines of the current explanation closed with E; an unread line is shown again whole.</summary>
        public int linesRead;
        public bool stockGiven;
    }
}
