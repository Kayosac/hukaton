namespace Hakaton.Expeditions
{
    /// <summary>
    /// Every debuff the player can get on an outing (KayoSpace board «Дебаффы»). One <see cref="PlayerStatusDefinition"/>
    /// asset per kind holds its numbers, look and sound; <see cref="PlayerStatuses"/> runs them.
    /// </summary>
    public enum PlayerStatusKind
    {
        /// <summary>Замедление: walking and running speed × strength. Washed off by water.</summary>
        Slow,
        /// <summary>Яд: strength half hearts every interval seconds; can kill. The «Лечение» card cures it.</summary>
        Poison,
        /// <summary>Горение: strength half hearts every interval seconds; can kill. Water or rain puts it out; a wet player does not catch fire.</summary>
        Burn,
        /// <summary>Оглушение: every sound muffled «behind a wall» (low-pass at strength Hz) and a ringing in the ears.</summary>
        Deafen,
        /// <summary>Слепота: the screen darkens from the edges, the world is seen only up to about strength metres.</summary>
        Blind,
        /// <summary>Кривые руки: every thrown card flies off up to strength degrees from the aim.</summary>
        CrookedHands,
        /// <summary>Перетасовка: the hand's order is shuffled at once and again every interval seconds. Nothing is blocked.</summary>
        Shuffle,
        /// <summary>Дырявый карман: while walking or running, strength outing coins fall out every interval metres; they can be picked up.</summary>
        LeakyPocket,
        /// <summary>Хрупкость: every damage × strength and no protection after a hit.</summary>
        Fragile,
        /// <summary>Метка: every aggressive creature within strength metres knows where the player is and comes.</summary>
        Mark,
        /// <summary>Кривизна: the view swims and warps (strength = how hard). Controls stay as they are.</summary>
        Warp,
        /// <summary>Гниль: takes strength half hearts off the maximum until the outing ends; each new one takes more (one heart always stays).</summary>
        Rot
    }
}
