using Hakaton.Decks;
using Hakaton.Decks.Unity;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// The last card thrown on this outing, for «Эхо»: recorded on every throw (<see cref="CardThrower.AnyThrown"/>),
    /// except throws of Эхо itself; forgotten when the outing ends. Teleport, «Пора домой» and story cards are recorded
    /// like any other (Эхо then has nothing it may repeat: it fizzles rather than reaching back to an older card).
    /// </summary>
    public static class EchoMemory
    {
        static ExpeditionWorld world;
        static ExpeditionWorld clearedBy;

        /// <summary>The last card thrown (null: none yet this outing).</summary>
        public static CardDefinition Last { get; private set; }
        /// <summary>The last card thrown was one of the story's (the cave's instruction cards).</summary>
        public static bool LastIsStory { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Install()
        {
            CardThrower.AnyThrown -= OnThrown;
            CardThrower.AnyThrown += OnThrown;
            Forget();
        }

        static void OnThrown(CardThrower thrower, CardCopy card)
        {
            var owner = thrower ? thrower.world : null;
            if (card == null || card.IsReturn || !owner || !owner.workshop || !owner.workshop.config) return;
            var config = owner.workshop.config;
            var definition = config.Find(card.Spec.Id);
            Remember(owner, definition, definition && System.Array.IndexOf(config.storyCards, definition) >= 0);
        }

        /// <summary>Records a throw (Эхо's own throws are skipped). <paramref name="owner"/> may be null in tests.</summary>
        public static void Remember(ExpeditionWorld owner, CardDefinition definition, bool story)
        {
            if (!definition || definition.effectAsset is EchoEffect) return;
            if (owner && owner != clearedBy)
            {
                if (clearedBy) clearedBy.OutingCleared -= Forget;
                clearedBy = owner;
                owner.OutingCleared += Forget;
            }
            world = owner;
            Last = definition;
            LastIsStory = story;
        }

        /// <summary>The last card thrown in <paramref name="owner"/>'s outing, or null.</summary>
        public static CardDefinition LastIn(ExpeditionWorld owner) => owner == world ? Last : null;

        public static void Forget()
        {
            world = null;
            Last = null;
            LastIsStory = false;
        }
    }
}
