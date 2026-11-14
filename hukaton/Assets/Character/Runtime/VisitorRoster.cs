using UnityEngine;

namespace Hakaton.Character
{
    /// <summary>
    /// Every visitor who can walk into the shop. Add a new NPC's appearance here: it joins the next shuffled round,
    /// no scene edit needed. An EditMode test fails if an appearance in Content/NPCs is missing from the roster.
    /// </summary>
    [CreateAssetMenu(menuName = "Hakaton/Character/Visitor roster")]
    public sealed class VisitorRoster : ScriptableObject
    {
        public NpcAppearance[] visitors;
        [Tooltip("The rare bulk buyer («Скупщик»): never part of the shuffled round, he comes only when the shop sends for " +
                 "him. Keep his appearance outside Content/NPCs (the roster test lists every NPC there as a regular visitor).")]
        public NpcAppearance bulkBuyer;
        [Tooltip("The stranger who comes at the end of a shift and only talks (once, after the first outing into the endless " +
                 "world). May be a regular visitor's look (the blacksmith): he is sent for by the game loop, never shuffled in.")]
        public NpcAppearance stranger;
    }
}
