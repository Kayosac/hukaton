namespace Hakaton.Expeditions
{
    /// <summary>
    /// A mark on a creature (a component on its root or a child object) that makes its drop table roll more than once
    /// when it dies: «Мародёр». <see cref="WorldCreature"/> rolls <see cref="WorldCreature.drops"/> as many times as the
    /// largest number any of them asks for (each roll by the table's usual rules); 1 without marks.
    /// </summary>
    public interface ILootRollBonus
    {
        /// <summary>Rolls of the drop table if the creature died now: 1 for none extra.</summary>
        int LootRolls { get; }
    }
}
