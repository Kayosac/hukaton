using Hakaton.Character;
using UnityEngine;

namespace Hakaton.Trading.Unity
{
    /// <summary>
    /// The authored first-person viewpoint behind the counter (camera A): entered when the shop opens, left when it
    /// closes. Visitors name the goods they want themselves, so the view never moves to the display.
    /// </summary>
    public sealed class TradingShopView : MonoBehaviour
    {
        public FirstPersonModalView modal;
        public Transform counterCamera;
        [Range(40, 100)] public float counterFov = 75;

        public bool IsTransitioning => modal && modal.IsTransitioning;
        public bool IsConfigured => modal && counterCamera;
        TradingPrototype controller;
        bool wasOpen;

        public void Bind(TradingPrototype value)
        {
            if(controller)return;
            controller=value;
        }

        public void Sync()
        {
            if (!controller || controller.Session == null || !IsConfigured) return;
            var session = controller.Session;
            if (session.IsOpen == wasOpen) return;
            wasOpen = session.IsOpen;
            if (wasOpen) modal.Enter(counterCamera, counterFov);
            else modal.Exit();
        }

        public void Close()
        {
            wasOpen = false;
            if (modal) modal.Exit();
        }

        void OnDisable() => Close();
    }
}
