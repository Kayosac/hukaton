using Hakaton.Character;
using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.Trading.Unity
{
    /// <summary>Two authored first-person viewpoints for the counter and physical display.</summary>
    public sealed class TradingShopView : MonoBehaviour
    {
        public FirstPersonModalView modal;
        public Transform counterCamera, displayCamera;
        public Button browseButton, backButton;
        [Range(40, 100)] public float counterFov = 75, displayFov = 80;
        [Min(0)] public float categoryReadSeconds = .75f;

        public bool Selecting { get; private set; }
        public bool IsTransitioning => modal && modal.IsTransitioning;
        public bool IsConfigured => modal && counterCamera && displayCamera;
        TradingPrototype controller;
        bool wasOpen;
        long seenCustomer;
        float selectAt;
        bool pendingSelection;

        public void Bind(TradingPrototype value)
        {
            if(controller)return;
            controller=value;
            if(browseButton)browseButton.onClick.AddListener(BeginSelection);
            if(backButton)backButton.onClick.AddListener(ReturnToCounter);
        }

        public void Sync()
        {
            if (!controller || controller.Session == null || !IsConfigured) return;
            var session = controller.Session;
            if (session.IsOpen != wasOpen)
            {
                wasOpen = session.IsOpen;
                Selecting = pendingSelection = false;
                seenCustomer = 0;
                if (wasOpen) modal.Enter(counterCamera, counterFov);
                else modal.Exit();
            }
            if (!session.IsOpen) return;
            var customer = session.CurrentCustomer;
            if (customer == null || session.CurrentDeal != null)
            {
                pendingSelection = false;
                if (Selecting) ReturnToCounter();
            }
            if (customer != null && controller.CustomerReady && seenCustomer != customer.Id)
            {
                seenCustomer = customer.Id;
                pendingSelection = session.CurrentDeal == null && controller.HasMatchingStock;
                selectAt = Time.unscaledTime + categoryReadSeconds;
            }
            if (pendingSelection && !IsTransitioning && Time.unscaledTime >= selectAt)
                BeginSelection();
        }

        public void BeginSelection()
        {
            if (!controller || !controller.CustomerReady || !controller.Session.IsOpen
                || controller.Session.CurrentDeal != null || !controller.HasMatchingStock || IsTransitioning) return;
            pendingSelection = false;
            Selecting = true;
            modal.SetPose(displayCamera, displayFov);
        }

        public void ReturnToCounter()
        {
            pendingSelection = false;
            Selecting = false;
            if (modal && wasOpen) modal.SetPose(counterCamera, counterFov);
        }

        public void Close()
        {
            pendingSelection = Selecting = wasOpen = false;
            if (modal) modal.Exit();
        }

        void OnDisable() => Close();
    }
}
