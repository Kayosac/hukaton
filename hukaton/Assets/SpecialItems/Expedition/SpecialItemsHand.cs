using System.Collections.Generic;
using Hakaton.Expeditions;
using Hakaton.Feel;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Hakaton.SpecialItems
{
    /// <summary>
    /// Q on an outing (board «Спецпредметы», «Управление на Q»): a short press takes the last chosen item out or puts it
    /// away; held, it opens the menu, the mouse points at an item and letting go of Q takes that one out (let go in the
    /// middle: nothing changes). Time does not slow down meanwhile. Only bought items are in the menu; with none, Q
    /// does nothing. Not in the shop, not in the cave prologue.
    /// </summary>
    public sealed class SpecialItemsHand : MonoBehaviour
    {
        public ExpeditionWorld world;
        public SpecialItemsHost items;
        public SpecialItemMenu menu;
        [Tooltip("Every special item's tool on the player, one per item.")]
        public SpecialItemTool[] tools = new SpecialItemTool[0];
        [Tooltip("Q held this long opens the menu; shorter is a tap.")]
        [Min(.05f)] public float holdSeconds = .2f;
        public SfxCue menuOpenSound, menuPickSound;

        /// <summary>The tool in the hand now (or on its way in), or null.</summary>
        public SpecialItemTool Current { get; private set; }
        public bool MenuOpen => menu && menu.IsOpen;

        float pressedAt = -1;
        bool holding;

        void Awake()
        {
            foreach (var tool in tools) if (tool) tool.Hand = this;
        }

        void OnEnable()
        {
            if (!world) return;
            world.OutingBegan += OnOutingBegan;
            world.OutingCleared += OnOutingCleared;
        }

        void OnDisable()
        {
            if (world)
            {
                world.OutingBegan -= OnOutingBegan;
                world.OutingCleared -= OnOutingCleared;
            }
            CloseMenu();
        }

        void OnOutingBegan()
        {
            Current = null;
            foreach (var tool in tools) if (tool) tool.OutingBegan();
        }

        void OnOutingCleared()
        {
            CloseMenu();
            pressedAt = -1;
            holding = false;
            Current = null;
            if (world) world.HeldItemHint = null;
            foreach (var tool in tools) if (tool) tool.OutingEnded();
        }

        bool Usable => world && items && world.Running && !world.InPrologue && world.control.GameplayActive;

        /// <summary>The other half of every item's hint: Q puts it away, held Q chooses another.</summary>
        [TextArea] public string stowHint = "Q — убрать\nзажать Q — другой предмет";

        void Update()
        {
            if (world)
            {
                var hint = Current && Current.IsOut ? Current.ControlHint : null;
                world.HeldItemHint = string.IsNullOrEmpty(hint) ? null : hint + "\n" + stowHint;
            }
            if (!Usable)
            {
                CloseMenu();
                pressedAt = -1;
                holding = false;
                return;
            }
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            var q = keyboard.qKey;
            if (q.wasPressedThisFrame)
            {
                pressedAt = Time.unscaledTime;
                holding = true;
            }
            if (!holding) return;
            if (q.isPressed)
            {
                if (!MenuOpen && Time.unscaledTime - pressedAt >= holdSeconds) OpenMenu();
                return;
            }
            // Let go.
            holding = false;
            if (MenuOpen)
            {
                var chosen = menu.Highlighted;
                CloseMenu();
                if (chosen) { Take(chosen); world.Play(menuPickSound); }
            }
            else Tap();
        }

        /// <summary>A short Q: the last chosen item goes in or out of the hand.</summary>
        public void Tap()
        {
            var owned = items.OwnedItems();
            if (owned.Count == 0) return;
            if (Current && Current.IsOut) { Current.Stow(); Current = null; return; }
            var last = items.Find(items.Inventory.LastSelected);
            Take(last && owned.Contains(last) ? last : owned[0]);
        }

        /// <summary>Takes <paramref name="item"/> out (another one in the hand is put away first).</summary>
        public void Take(SpecialItemDefinition item)
        {
            if (!item || !items.Owns(item)) return;
            items.Inventory.Select(item.id);
            var tool = ToolFor(item);
            if (!tool) { Debug.LogWarning($"No tool on the player for the special item '{item.id}'.", this); return; }
            if (Current && Current != tool && Current.IsOut) Current.Stow();
            Current = tool;
            if (!tool.IsOut) tool.Draw();
        }

        public SpecialItemTool ToolFor(SpecialItemDefinition item)
        {
            foreach (var tool in tools) if (tool && tool.definition == item) return tool;
            return null;
        }

        void OpenMenu()
        {
            var owned = items.OwnedItems();
            if (owned.Count == 0 || !menu) return;
            menu.Open(owned, Current && Current.IsOut ? Current.definition : null);
            world.motor.input.SetLookBlocked(this, true);
            world.Play(menuOpenSound);
        }

        void CloseMenu()
        {
            if (menu && menu.IsOpen) menu.Close();
            if (world && world.motor && world.motor.input) world.motor.input.SetLookBlocked(this, false);
        }

        /// <summary>For tests and cheats: the owned items' tools.</summary>
        public IEnumerable<SpecialItemTool> Tools => tools;
    }
}
