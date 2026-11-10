using Hakaton.Decks.Unity;
using UnityEngine;

namespace Hakaton.ShopBlockout
{
    /// <summary>Connects authored backroom objects to the embedded, shared deck workshop.</summary>
    public sealed class ShopBackroomServices : MonoBehaviour
    {
        public SuspendedGoblin goblin;
        public DeckPrototype workshop;

        public bool IsOpen => workshop && workshop.EmbeddedOpen;

        /// <summary>Call only for a focused, reachable target while the shop is closed.</summary>
        public bool TryInteract(ShopServiceKind kind)
        {
            if (!isActiveAndEnabled || IsOpen || !workshop) return false;
            switch (kind)
            {
                case ShopServiceKind.Computer:
                    return workshop.OpenEmbedded();
                case ShopServiceKind.GoblinRope:
                    if (!goblin) return false;
                    return !goblin.IsDeployed && goblin.Summon();
                case ShopServiceKind.Goblin:
                    return false;
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
                    return "E  Колоды и улучшения";
                case ShopServiceKind.GoblinRope:
                    if (!goblin) return string.Empty;
                    return goblin.IsMoving  ? "Карточник спускается…"
                        : goblin.IsDeployed ? "E  Купить карты"
                                            : "E  Потянуть за верёвку";
                case ShopServiceKind.Goblin:
                    return goblin && goblin.IsDeployed ? "E  Купить карты" : string.Empty;
                default:
                    return string.Empty;
            }
        }

        public void Close()
        {
            if (workshop) workshop.CloseEmbedded();
        }

        void OnDisable() => Close();
    }
}
