using Hakaton.Character;
using UnityEngine;

namespace Hakaton.Trading.Unity
{
    /// <summary>Connects completed trade visits to an independently authored sprite actor.</summary>
    public sealed class TradingCustomerPresenter : MonoBehaviour
    {
        public SpriteNpcActor actor;
        public NpcAppearance[] appearances;
        long presentedCustomerId;

        public bool CanStartNextCustomer => presentedCustomerId == 0 && actor && !actor.IsBusy;
        public string VisitorName => actor && actor.CurrentAppearance ? actor.CurrentAppearance.displayName : string.Empty;
        public bool ReadyForTrade(Customer customer) => customer != null && customer.Id == presentedCustomerId
            && actor && actor.ReadyForTrade;

        public void Sync(TradingSession session)
        {
            if (!actor || session == null) return;
            var current = session.CurrentCustomer;
            if (presentedCustomerId != 0)
            {
                if (!actor.IsBusy) presentedCustomerId = 0;
                else if (current == null || current.Id != presentedCustomerId)
                {
                    if (!actor.IsDeparting)
                    {
                        var result = session.LastDeparture;
                        bool sold = result != null && result.CustomerId == presentedCustomerId
                            && result.Outcome == CustomerOutcome.Purchased;
                        actor.Leave(sold ? NpcExpression.Happy : NpcExpression.Angry);
                    }
                }
                else if (actor.ReadyForTrade)
                    actor.SetExpression(ExpressionFor(session.CurrentDeal?.Mood ?? CustomerMood.Neutral));
            }
            if (presentedCustomerId == 0 && current != null && !actor.IsBusy)
            {
                if (appearances == null || appearances.Length == 0) return;
                var appearance = appearances[(int)((current.Id - 1) % appearances.Length)];
                if (actor.BeginVisit(appearance)) presentedCustomerId = current.Id;
            }
        }

        static NpcExpression ExpressionFor(CustomerMood mood)
        {
            switch (mood)
            {
                case CustomerMood.Displeased: return NpcExpression.Displeased;
                case CustomerMood.Angry: return NpcExpression.Angry;
                case CustomerMood.Happy: return NpcExpression.Happy;
                default: return NpcExpression.Neutral;
            }
        }
    }
}
