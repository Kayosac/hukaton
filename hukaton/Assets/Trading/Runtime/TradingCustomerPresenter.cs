using System.Collections.Generic;
using Hakaton.Character;
using UnityEngine;

namespace Hakaton.Trading.Unity
{
    /// <summary>
    /// Connects trade visits to authored sprite actors. With two or more actors the next visitor walks in
    /// while the previous one is still leaving (board: «следующий покупатель — без ожидания»).
    /// </summary>
    public sealed class TradingCustomerPresenter : MonoBehaviour
    {
        public SpriteNpcActor actor;
        [Tooltip("Additional authored actors on the same route.")]
        public SpriteNpcActor[] extraActors;
        [Tooltip("Who can visit. Shuffled: everyone once per round, never the same visitor twice in a row.")]
        public VisitorRoster roster;
        readonly Dictionary<SpriteNpcActor, long> presented = new Dictionary<SpriteNpcActor, long>();
        VisitorBag bag;

        IEnumerable<SpriteNpcActor> Actors
        {
            get
            {
                if (actor) yield return actor;
                if (extraActors != null) foreach (var extra in extraActors) if (extra) yield return extra;
            }
        }

        SpriteNpcActor Presenting(long customerId)
        {
            foreach (var pair in presented) if (pair.Value == customerId && pair.Key) return pair.Key;
            return null;
        }

        SpriteNpcActor Idle()
        {
            foreach (var candidate in Actors) if (!candidate.IsBusy && !presented.ContainsKey(candidate)) return candidate;
            return null;
        }

        /// <summary>A new visitor may start once nobody is arriving or standing at the counter.</summary>
        public bool CanStartNextCustomer
        {
            get
            {
                foreach (var candidate in Actors)
                    if (candidate.State == NpcActorState.Arriving || candidate.State == NpcActorState.AtCounter) return false;
                return Idle() != null;
            }
        }

        /// <summary>The appearance of the visitor arriving or at the counter, if any.</summary>
        public NpcAppearance VisitorAppearance
        {
            get
            {
                foreach (var pair in presented)
                    if (pair.Key && !pair.Key.IsDeparting && pair.Key.CurrentAppearance) return pair.Key.CurrentAppearance;
                return null;
            }
        }

        public string VisitorName
        {
            get
            {
                foreach (var pair in presented)
                    if (pair.Key && !pair.Key.IsDeparting && pair.Key.CurrentAppearance) return pair.Key.CurrentAppearance.displayName;
                return string.Empty;
            }
        }

        public bool ReadyForTrade(Customer customer)
        {
            var presenter = customer == null ? null : Presenting(customer.Id);
            return presenter && presenter.ReadyForTrade;
        }

        /// <summary>While set, a visitor at the counter keeps their current face (the "thinking" beat).</summary>
        public bool HoldExpression { get; set; }

        public void Sync(TradingSession session)
        {
            if (session == null) return;
            var current = session.CurrentCustomer;
            var finished = new List<SpriteNpcActor>();
            foreach (var pair in presented)
            {
                var visitor = pair.Key;
                if (!visitor) { finished.Add(visitor); continue; }
                if (!visitor.IsBusy) { finished.Add(visitor); continue; }
                if (current == null || current.Id != pair.Value)
                {
                    if (!visitor.IsDeparting)
                    {
                        var result = session.LastDeparture;
                        bool theirs = result != null && result.CustomerId == pair.Value;
                        bool sold = theirs && result.Outcome == CustomerOutcome.Purchased;
                        // The bulk buyer takes a refusal calmly: no anger, he just goes.
                        bool calm = theirs && result.BulkBuyer;
                        visitor.Leave(sold ? NpcExpression.Happy : calm ? NpcExpression.Neutral : NpcExpression.Angry);
                    }
                }
                else if (visitor.ReadyForTrade && !HoldExpression)
                    visitor.SetExpression(ExpressionFor(session.CurrentDeal?.Mood ?? CustomerMood.Neutral));
            }
            foreach (var done in finished) presented.Remove(done);

            if (current != null && !Presenting(current.Id))
            {
                var free = Idle();
                if (!free || !roster) return;
                bag ??= new VisitorBag(() => roster ? roster.visitors : null);
                // The bulk buyer has his own look (Visitor roster → Bulk Buyer) and is no part of the shuffled round.
                var special = current.IsBulkBuyer ? roster.bulkBuyer : null;
                var appearance = special ? special : bag.Peek();
                if (appearance && free.BeginVisit(appearance))
                {
                    presented[free] = current.Id;
                    if (!special) bag.Take(appearance);
                }
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
