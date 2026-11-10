using System;
using Hakaton.Backroom;
using Hakaton.Decks;
using Hakaton.Decks.Unity;
using UnityEngine;

namespace Hakaton.ShopBlockout
{
    /// <summary>
    /// Connects the authored backroom to its services: the magical computer (deck, merging) and the goblin card dealer
    /// on his ceiling rig (pull the rope, he comes down, talk to him; leaving sends him back up).
    /// </summary>
    public sealed class ShopBackroomServices : MonoBehaviour
    {
        public SuspendedGoblin goblin;
        public DeckPrototype workshop;

        /// <summary>The dealer's table comes with his rig prefab.</summary>
        public GoblinCardShop cardShop => goblin ? goblin.table : null;

        void Awake()
        {
            // The rig is a prefab without scene references: hand it the shared collection and the player's controls.
            if (cardShop) cardShop.Bind(workshop, workshop ? workshop.control : null);
        }

        public bool IsOpen => workshop && workshop.EmbeddedOpen || cardShop && cardShop.IsOpen;

        /// <summary>Call only for a focused, reachable target while the shop is closed.</summary>
        public bool TryInteract(ShopServiceKind kind)
        {
            if (!isActiveAndEnabled || IsOpen || !workshop) return false;
            switch (kind)
            {
                case ShopServiceKind.Computer:
                    return workshop.OpenEmbedded();
                case ShopServiceKind.GoblinRope:
                case ShopServiceKind.Goblin:
                    if (!goblin || !cardShop) return false;
                    if (goblin.IsDeployed) return cardShop.Open();
                    // He comes only when he may: no reminder otherwise (board).
                    return kind == ShopServiceKind.GoblinRope && goblin.IsStowed && cardShop.TryCall(Environment.TickCount)
                        && goblin.Summon();
                default:
                    return false;
            }
        }

        public string PromptFor(ShopServiceKind kind)
        {
            if (IsOpen) return string.Empty;
            switch (kind)
            {
                case ShopServiceKind.Computer:
                    return "E  Колода и мердж";
                case ShopServiceKind.GoblinRope:
                    if (!goblin || !cardShop || goblin.IsMoving) return string.Empty;
                    return goblin.IsDeployed ? "E  Карточник" : cardShop.Visits.CanCall ? "E  Потянуть за верёвку" : string.Empty;
                case ShopServiceKind.Goblin:
                    return goblin && goblin.IsDeployed && cardShop ? "E  Карточник" : string.Empty;
                default:
                    return string.Empty;
            }
        }

        public void Close()
        {
            if (workshop) workshop.CloseEmbedded();
            if (cardShop) cardShop.Close();
        }

        void OnEnable()
        {
            if (!cardShop) return;
            cardShop.Closed += SendGoblinUp;
            cardShop.Bought += Pleased;
        }

        void OnDisable()
        {
            if (cardShop)
            {
                cardShop.Closed -= SendGoblinUp;
                cardShop.Bought -= Pleased;
            }
            Close();
        }

        void SendGoblinUp()
        {
            if (goblin) goblin.Raise();
        }

        void Pleased(CardCopy copy)
        {
            if (goblin && goblin.drawing) goblin.drawing.Please();
        }
    }
}
