using UnityEngine;
using Hakaton.Character;

namespace Hakaton.Trading.Unity
{
    /// <summary>Inspector references to the authored shop scene. Does not create scene content.</summary>
    public sealed class TradingSceneBindings : MonoBehaviour
    {
        [Header("Shopkeeper")]
        public Camera ViewCamera;
        public Transform PlayerRoot;
        public CharacterController PlayerController;
        public FirstPersonMotor Movement;
        public PlayerControlGate Control;
        public PlayerStamina Stamina;
        public Transform CounterPose;
        [Tooltip("Every shift, however it ends, leaves the shopkeeper standing at CounterPose, wherever the shift was opened " +
                 "(the move happens behind the blink of the counter view).")]
        public bool returnToCounterOnClose = true;
        [Header("Compact shop presentation (optional for the original test scene)")]
        public TradingShopView ShopView;
        public Transform DealItemAnchor;
        public bool openFromDisplaySlots = true;
        [Header("Held stock")]
        public Transform HeldItemAnchor;
        public GameObject HeldItem;
        public HeldStockView HeldStock;

        [Header("Display — same slot order in all three arrays")]
        public Transform[] SlotAnchors;
        public Renderer[] SlotRenderers;
        public Collider[] SlotColliders;

        [Header("Customer")]
        public GameObject Customer;
        public TradingCustomerPresenter Customers;
        public Transform CustomerItemTarget;
        public Transform SoldItemsRoot;


        public Vector3 CounterPosition => CounterPose.position;
        public Quaternion CounterRotation => CounterPose.rotation;

        public bool ValidateReferences()
        {
            bool valid = ViewCamera && PlayerRoot && PlayerController && Movement && Control && Stamina && CounterPose && HeldItemAnchor
                && HeldItem && HeldStock && HeldStock.IsConfigured && Customer && Customers && Customers.actor && CustomerItemTarget && SoldItemsRoot
                && Complete(SlotAnchors, 6) && Complete(SlotRenderers, 6) && Complete(SlotColliders, 6);
            if (ShopView) valid &= ShopView.IsConfigured && DealItemAnchor;
            if (!valid) Debug.LogError("Trading scene references are incomplete. Check TradingSceneBindings in the Inspector.", this);
            return valid;
        }

        static bool Complete<T>(T[] values, int count) where T : Object
        {
            if (values == null || values.Length != count) return false;
            foreach (var value in values) if (!value) return false;
            return true;
        }
    }
}
