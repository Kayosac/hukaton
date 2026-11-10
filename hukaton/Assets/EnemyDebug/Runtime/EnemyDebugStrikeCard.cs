using Hakaton.Decks;
using Hakaton.Decks.Unity;
using Hakaton.Expeditions;
using UnityEngine;

namespace Hakaton.EnemyDebug
{
    /// <summary>One ordinary Strike, granted only to this isolated prologue session. The existing deck returns it after every attempt.</summary>
    [DefaultExecutionOrder(110)]
    public sealed class EnemyDebugStrikeCard : MonoBehaviour, IThrowRules
    {
        public ExpeditionWorld world;
        public CardDefinition definition;
        [Range(1, 3)] public int level = 1;

        public CardCopy Card { get; private set; }

        void Start()
        {
            if (!world || !world.thrower || !world.hand || !definition || !(definition.effectAsset is StrikeEffect)
                || world.workshop.Model.Current == null || !world.workshop.Model.Current.Prologue)
            {
                Debug.LogError("The debug Strike needs its saved world, thrower, hand, Strike definition and isolated prologue session.", this);
                enabled = false;
                return;
            }
            world.thrower.rules = this;
            Card = world.workshop.Model.Current.GrantToHand(definition.ToSpec(), level);
            world.thrower.ResetSelection();
            world.thrower.SelectCopy(Card.Id);
            world.hand.Deal();
        }

        public bool CanSelect(CardCopy card) => card != null && card.Id == Card?.Id;
        public bool CanThrow(CardCopy card) => CanSelect(card);
        // R belongs to the sandbox reset. The gameplay thrower must not also discard the hand.
        public bool CanDiscard() => false;
        public void Discarded() { }

        void OnDisable()
        {
            if (world && world.thrower && ReferenceEquals(world.thrower.rules, this))
            {
                world.thrower.Cancel();
                world.thrower.rules = null;
            }
        }
    }
}
