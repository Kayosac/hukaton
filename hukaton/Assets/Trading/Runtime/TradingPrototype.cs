using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Hakaton.Character;
using Hakaton.Economy.Unity;
using Hakaton.Loot;
using Hakaton.ShopBlockout;

namespace Hakaton.Trading.Unity
{
    /// <summary>Playable greybox adapter. All economy rules live in TradingSession.</summary>
    public sealed class TradingPrototype : MonoBehaviour
    {
        [Header("Temporary prototype balance")]
        [Tooltip("A full shift before any upgrade, seconds.")]
        [Min(1)] public float shiftSeconds = 30;
        [Tooltip("Shift upgrades bought on the PC («Время смены»), in order: seconds added to the shift and their price.")]
        public ShiftStep[] shiftSteps =
        {
            new ShiftStep(10, 150), new ShiftStep(10, 250), new ShiftStep(10, 400), new ShiftStep(10, 600), new ShiftStep(10, 850),
            new ShiftStep(10, 1150), new ShiftStep(10, 1500), new ShiftStep(10, 1900), new ShiftStep(10, 2350),
        };
        public int randomSeed = 42;
        [Range(.67f, .99f)] public float minOpeningRatio = .7f;
        [Range(.67f, .99f)] public float maxOpeningRatio = .9f;
        [Range(1, 2)] public float minCeilingRatio = 1;
        [Range(1, 2)] public float maxCeilingRatio = 1.4f;
        [Range(.01f, 2)] public float minOfferCeilingRatio = .95f;
        [Range(.01f, 2)] public float maxOfferCeilingRatio = 1.2f;
        [Range(0, 1)] public float asksPriceProbability = .5f;
        public bool usePresetPatienceCosts;
        [Min(1)] public int minPatience = 3;
        [Min(1)] public int maxPatience = 5;
        [Header("Controls")]
        public float holdThreshold = .35f;
        public float autoPlaceInterval = .16f;
        public Color slotHighlightColor = new Color(.9f, .8f, .35f);
        [Tooltip("After a counter-offer the visitor 'thinks' this long before their answer, face and voice show.")]
        [Min(0)] public float reactionBeat = .38f;

        [Header("Bulk buyer («Скупщик»)")]
        [Tooltip("Chance in percent that a visitor is the bulk buyer: only after the first revenue goal, at most once a shift, " +
                 "with enough goods on display, never in the tutorial.")]
        [Range(0, 100)] public float bulkBuyerChance = 5;
        [Tooltip("Upgrades bought on the PC («Скупщик», shown once the player has met him): percent added to the chance and their price.")]
        public BulkBuyerStep[] bulkBuyerSteps =
        {
            new BulkBuyerStep(5, 500), new BulkBuyerStep(5, 1000), new BulkBuyerStep(5, 2000), new BulkBuyerStep(5, 3000),
            new BulkBuyerStep(5, 5000),
        };
        [Tooltip("He comes only for a display with at least this many items.")]
        [Min(1)] public int bulkBuyerMinDisplay = 3;
        [Tooltip("Share of each item's base price he pays (at least one coin an item).")]
        [Range(.01f, 1)] public float bulkBuyerPriceRatio = .75f;
        [Tooltip("Seconds between the items flying off the display to him.")]
        [Min(0)] public float bulkTransferStagger = .07f;

        /// <summary>One bulk buyer upgrade: the percent it adds to his chance and its price.</summary>
        [System.Serializable]
        public sealed class BulkBuyerStep
        {
            [Range(0, 100)] public float percent = 5;
            [Min(0)] public int price;
            public BulkBuyerStep() { }
            public BulkBuyerStep(float percent, int price) { this.percent = percent; this.price = price; }
        }

        /// <summary>One shift upgrade: the seconds it adds to every shift and its price.</summary>
        [System.Serializable]
        public sealed class ShiftStep
        {
            [Min(1)] public float seconds = 10;
            [Min(0)] public int price;
            public ShiftStep() { }
            public ShiftStep(float seconds, int price) { this.seconds = seconds; this.price = price; }
        }

        public TradingSession Session { get; private set; }
        [Header("Scene references — assign in the authored scene")]
        public TradingSceneBindings World;
        public TradingHud hud;
        public CoinWalletHost walletHost;
        public ShopBackroomServices services;
        /// <summary>Raised when the player uses the shop door; the game loop runs the expedition.</summary>
        public event System.Action ExpeditionRequested;
        /// <summary>The way out and the backroom are closed (the tutorial, until its goblin leaves).</summary>
        public bool ExitsLocked { get; set; }
        /// <summary>When set, the shop does not open and this is said instead (the tutorial wants both wares out first).</summary>
        public string OpenRefusal { get; set; }
        public event System.Action<int> ItemPlaced, ItemTaken, SlotHovered;
        public int HoveredSlot => hoveredSlot;
        public bool CustomerReady => Session != null && World.Customers.ReadyForTrade(Session.CurrentCustomer);
        public bool ActionsReady => CustomerReady && !ViewTransitioning && Time.unscaledTime >= actionReadyAt;
        /// <summary>False during the short beat after an offer while the visitor considers it.</summary>
        public bool ReactionRevealed => Time.unscaledTime >= revealAt;
        /// <summary>The open shop is seen from the authored counter view (camera A).</summary>
        public bool UsesDisplayView => World && World.ShopView;
        public bool ViewTransitioning => UsesDisplayView && World.ShopView.IsTransitioning;
        public string CustomerName => World.Customers.VisitorName;
        public string Notice => Time.unscaledTime < noticeUntil ? notice : string.Empty;
        public ItemInstance SelectedItem => Session.Inventory.Count == 0 ? null : Session.Inventory[selectedInventory];
        [System.Serializable]
        public sealed class StartingStock
        {
            public LootItem item;
            [Min(0)] public int count = 1;
        }
        [Header("Catalogue")]
        [Tooltip("Every item the shop knows: name, category, price and how it looks on the shelf (Assets/Loot/Loot catalog.asset).")]
        public LootCatalog lootCatalog;
        [Tooltip("Copies in stock when a new game starts, before any outing.")]
        public StartingStock[] startingStock = new StartingStock[0];
        GameObject[] displayed;
        long[] displayedIds;
        GameObject heldItem;
        int selectedInventory, hoveredSlot = -1, pressedSlot = -1, lastHovered = -1;
        bool autoFilling, previousOpen;
        float pressStarted, nextPlaceAt, actionReadyAt, revealAt;
        TradingInteractionTarget focusedTarget;
        ShopInteractionTarget focusedService;
        public string ContextPrompt { get; private set; } = string.Empty;
        public string ContextDetail { get; private set; } = string.Empty;
        /// <summary>The aim rests on a reachable free display slot and there is stock to put there.</summary>
        public bool PlacementFocused { get; private set; }
        /// <summary>The item the aim describes: the stock about to be placed, or the one on the hovered slot.</summary>
        public ItemInstance FocusedItem { get; private set; }
        public bool GameplayActive => World != null && World.Control != null && World.Control.GameplayActive && Session != null && !Session.IsOpen
            && Session.ExpeditionId == 0;
        /// <summary>The shopkeeper's stamina, for the arc of pips under the aim.</summary>
        public PlayerStamina Stamina => World ? World.Stamina : null;
        public float ContextProgress => pressedSlot >= 0 ? Mathf.Clamp01((Time.unscaledTime - pressStarted) / holdThreshold) : 0;
        int lastSettlement;
        int presentedDealSlot = -1;
        long bulkLineCustomer;
        int bulkLine;

        /// <summary>The line of the bulk buyer's speech on screen (<see cref="Customer.Lines"/>; the last one is his offer).</summary>
        public int BulkLine
        {
            get
            {
                var customer = Session?.CurrentCustomer;
                return customer != null && customer.IsBulkBuyer && customer.Id == bulkLineCustomer ? bulkLine : 0;
            }
        }

        /// <summary>The bulk buyer at the counter has said everything but his offer: a press reads on.</summary>
        public bool BulkBuyerExplaining
        {
            get
            {
                var customer = Session?.CurrentCustomer;
                return customer != null && customer.IsBulkBuyer && BulkLine < customer.Lines.Count - 1;
            }
        }

        /// <summary>The bulk buyer's offer is on screen: sell everything (green) or refuse (red).</summary>
        public bool BulkOfferShown
        {
            get
            {
                var customer = Session?.CurrentCustomer;
                return customer != null && customer.IsBulkBuyer && !BulkBuyerExplaining;
            }
        }
        Coroutine[] itemMotion;
        MaterialPropertyBlock slotHighlight;
        string notice = string.Empty;
        float noticeUntil;

        void Awake()
        {
            slotHighlight = new MaterialPropertyBlock();
            if (World == null || hud == null)
            {
                Debug.LogError("TradingPrototype: assign scene bindings and the HUD in the Inspector.", this);
                enabled = false;
                return;
            }
            if (!World.ValidateReferences() || !lootCatalog || lootCatalog.items.Count == 0)
            {
                Debug.LogError("TradingPrototype: scene references are incomplete.", World);
                enabled = false;
                return;
            }
            heldItem = World.HeldItem;
            Session = new TradingSession(new TradeSettings {
                SlotCount = 6, ShiftSeconds = shiftSeconds,
                ShiftUpgradeSeconds = System.Array.ConvertAll(shiftSteps, step => step.seconds),
                ShiftUpgradePrices = System.Array.ConvertAll(shiftSteps, step => step.price),
                MinOpeningRatio = minOpeningRatio, MaxOpeningRatio = maxOpeningRatio,
                MinCeilingRatio = minCeilingRatio, MaxCeilingRatio = maxCeilingRatio,
                MinOfferCeilingRatio = minOfferCeilingRatio, MaxOfferCeilingRatio = maxOfferCeilingRatio,
                AsksPriceProbability = asksPriceProbability,
                UsePresetPatienceCosts = usePresetPatienceCosts,
                MinPatience = minPatience, MaxPatience = maxPatience,
                BulkBuyerChance = bulkBuyerChance / 100f,
                BulkBuyerUpgradeChances = System.Array.ConvertAll(bulkBuyerSteps, step => step.percent / 100f),
                BulkBuyerUpgradePrices = System.Array.ConvertAll(bulkBuyerSteps, step => step.price),
                BulkBuyerMinDisplay = bulkBuyerMinDisplay, BulkBuyerPriceRatio = bulkBuyerPriceRatio
            }, randomSeed, walletHost ? walletHost.Wallet : null);
            RegisterCatalogue();
            displayed = new GameObject[6];
            displayedIds = new long[6];
            itemMotion = new Coroutine[6];
            if (World.ShopView) World.ShopView.Bind(this);
            hud.Bind(this);
            RefreshWorld();
        }

        void Update()
        {
            // A menu is in front: the shift clock, the visitor and the hotkeys all wait.
            if (GamePause.Suspended) return;
            World.Customers.HoldExpression = !ReactionRevealed;
            World.Customers.Sync(Session);
            Session.Tick(Time.unscaledDeltaTime);
            if (Session.IsOpen && Session.CurrentCustomer == null && World.Customers.CanStartNextCustomer)
            {
                Session.AdvanceCustomer();
                actionReadyAt = Mathf.Max(actionReadyAt, Time.unscaledTime + .25f);
            }
            SyncControl();
            if (World.ShopView) World.ShopView.Sync();
            var keyboard = Keyboard.current;
            if (Session.IsOpen)
            {
                ClearFocus();
                if (keyboard != null)
                {
                    // Escape at the counter opens the pause menu; E closes the shop.
                    if (keyboard.eKey.wasPressedThisFrame)
                    {
                        // E reads on through the bulk buyer's explanation, as it does through the goblins' lines.
                        if (BulkBuyerExplaining && CustomerReady) ReadOnBulkBuyer();
                        // Closing mid-conversation would silently drop the visitor: finish or refuse them first.
                        else if (Session.CurrentCustomer != null && CustomerReady) Notify("Сначала закончите сделку · Backspace — отказать");
                        else CloseShop();
                    }
                    HaggleHotkeys(keyboard);
                }
            }
            else if (GameplayActive && !World.Control.CapturedThisFrame)
            {
                UpdateHover();
                UpdatePlacement();
                if (keyboard != null && keyboard.eKey.wasPressedThisFrame) Interact();
                if (keyboard != null && keyboard.fKey.wasPressedThisFrame && hoveredSlot >= 0)
                {
                    if (Session.RemoveItem(hoveredSlot)) ItemTaken?.Invoke(hoveredSlot);
                }
            }
            else
            {
                ClearFocus();
                ResetPlacement();
            }
            ObserveSale();
            SyncControl();
            RefreshWorld();
        }

        void HaggleHotkeys(Keyboard keyboard)
        {
            var deal = Session.CurrentDeal;
            var customer = Session.CurrentCustomer;
            if (customer == null) return;
            if (keyboard.backspaceKey.wasPressedThisFrame) { RefuseCustomer(customer.Id); return; }
            if (customer.IsBulkBuyer)
            {
                bool confirm = keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame;
                if (BulkBuyerExplaining)
                {
                    // Space, Enter or a click read on (E is handled with the shop keys).
                    var mouse = Mouse.current;
                    if (confirm || mouse != null && mouse.leftButton.wasPressedThisFrame) ReadOnBulkBuyer();
                }
                // Space or Enter on the offer sells; while it is still typing the press only finishes it.
                else if (confirm && ActionsReady && !hud.SkipSpeechTyping()) SellToBulkBuyer(customer.Id);
                return;
            }
            if (deal == null) return;
            var digits = new[] { keyboard.digit1Key, keyboard.digit2Key, keyboard.digit3Key, keyboard.digit4Key, keyboard.digit5Key };
            for (int i = 0; i < digits.Length; i++)
                if (digits[i].wasPressedThisFrame) { OfferPrice(deal.Id, deal.Revision, i); return; }
            if ((keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame) && deal.N.HasValue)
                AcceptPrice(deal.Id, deal.Revision);
        }

        void SyncControl()
        {
            World.Control.AllowPause(this, true);
            World.Control.SetBlocked(this, Session.IsOpen);
            if (Session.IsOpen == previousOpen) return;
            previousOpen = Session.IsOpen;
            ResetPlacement();
            // The view first: entering it holds the current camera pose until the blink is black, so the body is moved
            // only behind the blink. Every way a shift ends (time, last sale, empty display, E, the clock's cross, the
            // tutorial, the bulk buyer) passes here and leaves the shopkeeper behind the counter.
            if (World.ShopView) World.ShopView.Sync();
            if (Session.IsOpen || World.returnToCounterOnClose) World.Movement.Teleport(World.CounterPosition, World.CounterRotation);
        }

        void ClearFocus()
        {
            focusedTarget = null;
            focusedService = null;
            hoveredSlot = -1;
            PlacementFocused = false;
            FocusedItem = null;
            ContextPrompt = ContextDetail = string.Empty;
        }

        void UpdateHover()
        {
            ClearFocus();
            // The first surface wins: no prompts or interaction through walls.
            var ray = World.ViewCamera.ViewportPointToRay(new Vector3(.5f, .5f));
            if (!Physics.Raycast(ray, out var hit, 5f, ~((1 << 2)), QueryTriggerInteraction.Collide)) return;
            if (services && Session.ExpeditionId == 0)
            {
                var service = hit.collider.GetComponentInParent<ShopInteractionTarget>();
                if (service && hit.distance <= service.reach)
                {
                    focusedService = service;
                    ContextPrompt = ExitsLocked ? string.Empty : services.PromptFor(service.kind);
                    return;
                }
            }
            var target = hit.collider.GetComponentInParent<TradingInteractionTarget>();
            int itemSlot = -1;
            // A displayed item belongs to its slot; aiming at the item must not select a slot behind it.
            for (int i = 0; i < displayed.Length; i++)
                if (displayed[i] != null && hit.collider.transform.IsChildOf(displayed[i].transform))
                {
                    itemSlot = i;
                    target = World.SlotColliders[i].GetComponentInParent<TradingInteractionTarget>();
                    break;
                }
            if (target == null || hit.distance > target.reach) return;
            hoveredSlot = itemSlot;
            focusedTarget = target;
            if (target.kind == TradingTargetKind.DisplaySlot && Session.ExpeditionId == 0)
            {
                if (hoveredSlot < 0)
                    for (int i = 0; i < World.SlotColliders.Length; i++)
                        if (World.SlotColliders[i] == hit.collider) { hoveredSlot = i; break; }
                if (hoveredSlot < 0) return;
                if (hoveredSlot != lastHovered) { lastHovered = hoveredSlot; SlotHovered?.Invoke(hoveredSlot); }
                var item = Session.Slots[hoveredSlot];
                if (item != null)
                {
                    // Same presentation as stocking: one bare hint line, the item itself on the paper card.
                    FocusedItem = item;
                    ContextPrompt = "F Забрать" + (World.openFromDisplaySlots && Session.RemainingSeconds > 0 ? " · E Открыть лавку" : string.Empty);
                    ContextDetail = string.Empty;
                }
                else
                {
                    PlacementFocused = Session.Inventory.Count > 0;
                    FocusedItem = PlacementFocused ? SelectedItem : null;
                    ContextPrompt = PlacementFocused ? "ЛКМ Положить · удерживать - заполнить" : "Нет товара в запасе";
                    ContextDetail = World.openFromDisplaySlots && Session.DisplayCount > 0 && Session.RemainingSeconds > 0 ? "E  Открыть лавку" : string.Empty;
                }
            }
            else if (target.kind == TradingTargetKind.Counter && Session.ExpeditionId == 0)
            {
                ContextPrompt = Session.RemainingSeconds <= 0 ? "Смена окончена" : Session.DisplayCount == 0 ? "Витрина пуста" : "E  Открыть лавку";
                ContextDetail = Session.RemainingSeconds <= 0 ? "Новая добыча восстановит время торговли" : Session.DisplayCount == 0 ? "Положите товар на свободный слот" : string.Empty;
            }
            else if (target.kind == TradingTargetKind.Door)
            {
                ContextPrompt = ExitsLocked ? string.Empty : "E  Выйти из лавки";
                ContextDetail = string.Empty;
            }
        }

        void UpdatePlacement()
        {
            var mouse = Mouse.current;
            if (mouse == null || !GameplayActive || World.Control.CapturedThisFrame) return;
            if (pressedSlot >= 0 && hoveredSlot != pressedSlot) ResetPlacement();
            // The wheel steps between kinds of goods, not between single copies.
            float scroll = mouse.scroll.ReadValue().y;
            if (scroll != 0 && Session.Inventory.Count > 0)
                selectedInventory = StockSelection.StepKind(Session.Inventory, selectedInventory, scroll > 0 ? -1 : 1);
            if (mouse.leftButton.wasPressedThisFrame && hoveredSlot >= 0 && Session.ExpeditionId == 0)
            {
                pressedSlot = hoveredSlot;
                pressStarted = Time.unscaledTime;
                autoFilling = false;
            }
            if (pressedSlot >= 0 && mouse.leftButton.isPressed && Time.unscaledTime - pressStarted >= holdThreshold)
            {
                if (!autoFilling) { autoFilling = true; nextPlaceAt = 0; }
                if (Time.unscaledTime >= nextPlaceAt)
                {
                    int slot = NextEmptySlot(pressedSlot);
                    if (slot >= 0) Place(slot);
                    nextPlaceAt = Time.unscaledTime + autoPlaceInterval;
                }
            }
            if (mouse.leftButton.wasReleasedThisFrame && pressedSlot >= 0)
            {
                if (!autoFilling && hoveredSlot == pressedSlot) Place(pressedSlot);
                ResetPlacement();
            }
        }

        int NextEmptySlot(int start)
        {
            for (int n = 0; n < 6; n++)
                if (Session.Slots[(start + n) % 6] == null) return (start + n) % 6;
            return -1;
        }

        void Place(int slot)
        {
            ClampSelection();
            string placedId = Session.Inventory.Count > 0 ? Session.Inventory[selectedInventory].Definition.Id : null;
            if (Session.PlaceItem(selectedInventory, slot))
            {
                // The same goods stay in hand while copies remain; then the next kind, so holding fills the shelf.
                selectedInventory = StockSelection.AfterPlacing(Session.Inventory, selectedInventory, placedId);
                ItemPlaced?.Invoke(slot);
                RefreshWorld();
                if (displayed[slot] != null) StartCoroutine(Snap(displayed[slot].transform));
            }
        }

        void ResetPlacement() { pressedSlot = -1; autoFilling = false; }
        void ClampSelection() { selectedInventory = Mathf.Clamp(selectedInventory, 0, Mathf.Max(0, Session.Inventory.Count - 1)); }

        void Interact()
        {
            if (ExitsLocked && (focusedService || focusedTarget && focusedTarget.kind == TradingTargetKind.Door))
            {
                Notify("Сначала дела в лавке.");
                return;
            }
            if (focusedService && GameplayActive && Session.ExpeditionId == 0)
            {
                services.TryInteract(focusedService.kind);
                ResetPlacement();
                return;
            }
            if (focusedTarget == null || !GameplayActive) return;
            if (focusedTarget.kind == TradingTargetKind.Door)
            {
                if (ExpeditionRequested != null) ExpeditionRequested.Invoke();
                else Notify("Вылазка не подключена к двери.");
                return;
            }
            if (focusedTarget.kind == TradingTargetKind.Counter || (World.openFromDisplaySlots && focusedTarget.kind == TradingTargetKind.DisplaySlot))
                OpenShop();
        }

        void OpenShop()
        {
            if (!GameplayActive || focusedTarget == null || Session.ExpeditionId != 0) return;
            if (focusedTarget.kind != TradingTargetKind.Counter && focusedTarget.kind != TradingTargetKind.DisplaySlot) return;
            if (focusedTarget.kind == TradingTargetKind.DisplaySlot && !World.openFromDisplaySlots) return;
            if (!string.IsNullOrEmpty(OpenRefusal)) { Notify(OpenRefusal); return; }
            if (!Session.Open()) Notify(Session.LastMessage);
            actionReadyAt = Time.unscaledTime + .25f;
            World.Customers.Sync(Session);
            SyncControl();
        }
        void CloseShop() { Session.Close(); World.Customers.Sync(Session); SyncControl(); }

        /// <summary>Closes the shop from outside (the tutorial ends its last lesson).</summary>
        public void CloseShopNow()
        {
            if (Session.IsOpen) CloseShop();
        }

        /// <summary>Puts this stock item in the player's hands (the next one to place).</summary>
        public void SelectStock(string definitionId)
        {
            for (int i = 0; i < Session.Inventory.Count; i++)
                if (Session.Inventory[i].Definition.Id == definitionId) { selectedInventory = i; return; }
        }
        void Notify(string text) { notice = text; noticeUntil = Time.unscaledTime + 2.6f; }
        void OnEnable()
        {
            if (Session != null && World != null && World.Control != null)
                World.Control.SetBlocked(this, Session.IsOpen);
        }
        void OnDisable()
        {
            if (World != null && World.Control != null) World.Control.SetBlocked(this, false);
            if (World != null && World.ShopView) World.ShopView.Close();
            if (services) services.Close();
        }

        void ObserveSale()
        {
            if (Session.SettlementSequence == lastSettlement) return;
            lastSettlement = Session.SettlementSequence;
            if (Session.LastSaleBulk)
            {
                ObserveBulkSale();
                return;
            }
            Notify($"Продано: {Session.LastSoldItem.Definition.Name} за {Session.LastSalePrice} монет.");
            for (int i = 0; i < 6; i++)
                if (displayed[i] != null && displayedIds[i] == Session.LastSoldItem.Id)
                {
                    var item = displayed[i]; displayed[i] = null; displayedIds[i] = 0;
                    if (itemMotion[i] != null) { StopCoroutine(itemMotion[i]); itemMotion[i] = null; }
                    var collider = item.GetComponent<Collider>(); if (collider != null) collider.enabled = false;
                    StartCoroutine(Transfer(item));
                    break;
                }
        }

        /// <summary>The bulk buyer took the whole display: every item flies to him, one after another.</summary>
        void ObserveBulkSale()
        {
            var sold = Session.LastSoldItems;
            Notify(TradingSession.BulkSaleNotice(sold.Count, Session.LastSalePrice));
            int order = 0;
            for (int i = 0; i < 6; i++)
            {
                if (displayed[i] == null) continue;
                bool taken = false;
                foreach (var item in sold) if (item.Id == displayedIds[i]) { taken = true; break; }
                if (!taken) continue;
                var shown = displayed[i]; displayed[i] = null; displayedIds[i] = 0;
                if (itemMotion[i] != null) { StopCoroutine(itemMotion[i]); itemMotion[i] = null; }
                var collider = shown.GetComponent<Collider>(); if (collider != null) collider.enabled = false;
                StartCoroutine(Transfer(shown, order++ * bulkTransferStagger));
            }
        }

        void RefreshWorld()
        {
            ClampSelection();
            World.Customers.Sync(Session);
            for (int i = 0; i < 6; i++)
            {
                var item = Session.Slots[i];
                long id = item == null ? 0 : item.Id;
                if (displayedIds[i] != id)
                {
                    if (itemMotion[i] != null) { StopCoroutine(itemMotion[i]); itemMotion[i] = null; }
                    if (displayed[i] != null) Destroy(displayed[i]);
                    displayed[i] = item == null ? null : CreateShelfItem(item.Definition.Id, World.SlotAnchors[i]);
                    displayedIds[i] = id;
                }
                if (i == hoveredSlot)
                {
                    slotHighlight.SetColor("_BaseColor", slotHighlightColor);
                    slotHighlight.SetColor("_Color", slotHighlightColor);
                    World.SlotRenderers[i].SetPropertyBlock(slotHighlight);
                }
                else World.SlotRenderers[i].SetPropertyBlock(null);
            }
            PresentDealItem();
            heldItem.SetActive(GameplayActive && Session.ExpeditionId == 0 && Session.Inventory.Count > 0);
            if (Session.Inventory.Count > 0) World.HeldStock.Show(lootCatalog, Item(Session.Inventory[selectedInventory].Definition.Id));
        }

        /// <summary>Every catalogue item becomes a shop definition; then the optional starting stock, interleaved.</summary>
        void RegisterCatalogue()
        {
            foreach (var item in lootCatalog.items)
            {
                if (!item) continue;
                try { Session.SeedInventory(new ItemDefinition(item.id, item.displayName, item.category, item.price), 0); }
                catch (System.ArgumentException error)
                {
                    Debug.LogError($"Loot item '{item.name}' cannot be sold: {error.Message} Check it in the Inspector.", item);
                }
            }
            int maxCount = 0;
            foreach (var entry in startingStock)
                if (entry != null && entry.item) maxCount = Mathf.Max(maxCount, entry.count);
            for (int n = 0; n < maxCount; n++)
                foreach (var entry in startingStock)
                {
                    if (entry == null || !entry.item || n >= entry.count) continue;
                    var definition = Session.FindDefinition(entry.item.id);
                    if (definition != null) Session.SeedInventory(definition, 1);
                }
        }

        /// <summary>The item's shelf look standing on the anchor, with a collider so the aim can find it.</summary>
        GameObject CreateShelfItem(string id, Transform anchor)
        {
            var made = lootCatalog.CreateVisual(Item(id), World.SoldItemsRoot, true);
            if (!made) return null;
            made.transform.SetPositionAndRotation(anchor.position, anchor.rotation);
            EnsureCollider(made);
            return made;
        }

        /// <summary>
        /// Models without a collider and drawings get a box around their renderers. It is a trigger: the aim finds
        /// triggers, while a solid box would turn with a drawing that faces the camera and shove the player.
        /// </summary>
        static void EnsureCollider(GameObject item)
        {
            if (item.GetComponentInChildren<Collider>()) return;
            var renderers = item.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            Bounds bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            var box = item.AddComponent<BoxCollider>();
            box.isTrigger = true;
            var root = item.transform;
            box.center = root.InverseTransformPoint(bounds.center);
            Vector3 scale = root.lossyScale;
            box.size = new Vector3(bounds.size.x / Mathf.Max(.0001f, Mathf.Abs(scale.x)),
                bounds.size.y / Mathf.Max(.0001f, Mathf.Abs(scale.y)), bounds.size.z / Mathf.Max(.0001f, Mathf.Abs(scale.z)));
        }

        void PresentDealItem()
        {
            if (!World.DealItemAnchor) return;
            int desired = CustomerReady && Session.CurrentDeal != null ? Session.CurrentDeal.SlotIndex : -1;
            if (desired == presentedDealSlot) return;
            if (presentedDealSlot >= 0 && displayed[presentedDealSlot])
                MovePresentedItem(presentedDealSlot, World.SlotAnchors[presentedDealSlot]);
            presentedDealSlot = desired;
            if (desired >= 0 && displayed[desired]) MovePresentedItem(desired, World.DealItemAnchor);
        }

        void MovePresentedItem(int slot, Transform destination)
        {
            if (itemMotion[slot] != null) StopCoroutine(itemMotion[slot]);
            itemMotion[slot] = StartCoroutine(MoveToAnchor(displayed[slot].transform, destination));
        }

        IEnumerator MoveToAnchor(Transform item, Transform destination)
        {
            Vector3 start = item.position;
            Quaternion rotation = item.rotation;
            // Nothing in flight can be aimed at or bumped into; the item collides again where it lands.
            SetCollidersEnabled(item, false);
            const float duration = .24f;
            for (float t = 0; t < duration && item; t += Time.deltaTime)
            {
                float f = Mathf.SmoothStep(0, 1, t / duration);
                item.position = Vector3.Lerp(start, destination.position, f) + Vector3.up * (Mathf.Sin(f * Mathf.PI) * .09f);
                item.rotation = Quaternion.Slerp(rotation, destination.rotation, f);
                yield return null;
            }
            if (!item) yield break;
            item.SetPositionAndRotation(destination.position, destination.rotation);
            SetCollidersEnabled(item, true);
        }

        static void SetCollidersEnabled(Transform item, bool on)
        {
            foreach (var collider in item.GetComponentsInChildren<Collider>(true)) collider.enabled = on;
        }

        LootItem Item(string id)
        {
            var item = lootCatalog.Find(id);
            if (!item) throw new System.InvalidOperationException("Missing loot item in the catalogue: " + id);
            return item;
        }

        /// <summary>The catalogue item behind a shop definition id, or null.</summary>
        public LootItem FindItem(string id) => lootCatalog ? lootCatalog.Find(id) : null;

        public ItemDefinition FindStock(string id) => Session?.FindDefinition(id);

        public long BeginExternalExpedition()
        {
            if (!isActiveAndEnabled || Session == null || Session.IsOpen
                || (services && services.IsOpen)) return 0;
            long id = Session.BeginExpedition();
            if (id != 0)
            {
                ResetPlacement(); ClearFocus();
                RefreshWorld();
            }
            return id;
        }

        public bool CompleteExternalExpedition(long id, IReadOnlyList<StockDelivery> delivery)
        {
            if (Session == null || !Session.CompleteExpedition(id, delivery)) return false;
            // The game loop announces the outcome of the outing; the shop stays quiet to avoid a double notice.
            RefreshPresentation();
            return true;
        }

        public void RefreshPresentation()
        {
            if (Session == null) return;
            lastSettlement = Session.SettlementSequence;
            ResetPlacement(); ClearFocus();
            SyncControl(); RefreshWorld();
        }

        public void ShowNotice(string text) => Notify(text);
        IEnumerator Snap(Transform item)
        {
            // Drop in, then a squash-and-settle: the "clack" of putting goods on a shelf.
            var end = item.position; var start = end + Vector3.up * .25f;
            var scale = item.localScale;
            for (float t = 0; t < .12f && item != null; t += Time.deltaTime)
            { float k = t / .12f; item.position = Vector3.Lerp(start, end, k * k); yield return null; }
            for (float t = 0; t < .22f && item != null; t += Time.deltaTime)
            {
                float k = t / .22f, squash = Mathf.Sin(k * Mathf.PI * 2.5f) * (1 - k) * .22f;
                item.position = end;
                item.localScale = new Vector3(scale.x * (1 + squash), scale.y * (1 - squash), scale.z * (1 + squash));
                yield return null;
            }
            if (item != null) { item.position = end; item.localScale = scale; }
        }
        IEnumerator Transfer(GameObject item, float delay = 0)
        {
            if (delay > 0) yield return new WaitForSeconds(delay);
            if (!item) yield break;
            var start = item.transform.position;
            for (float t = 0; t < .35f; t += Time.deltaTime)
            {
                item.transform.position = Vector3.Lerp(start, World.CustomerItemTarget.position, t / .35f) + Vector3.up * Mathf.Sin(t / .35f * Mathf.PI) * .3f;
                yield return null;
            }
            Destroy(item);
        }

        public void ToggleShop()
        {
            if (Session == null) return;
            if (Session.IsOpen) CloseShop(); else OpenShop();
        }

        public void CycleInventory(int direction)
        {
            if (Session == null || Session.IsOpen || Session.Inventory.Count == 0) return;
            selectedInventory = StockSelection.StepKind(Session.Inventory, selectedInventory, direction);
        }

        public void OfferPrice(long dealId, int revision, int optionIndex)
        {
            if (!ActionsReady) return;
            int salesBefore = Session.SettlementSequence;
            if (!Session.Offer(dealId, revision, optionIndex)) return;
            if (Session.CurrentCustomer == null && Session.SettlementSequence == salesBefore)
                Notify(Session.LastMessage + " Товар сохранён.");
            bool countered = Session.CurrentDeal != null;
            // A counter-offer lands after a short "thinking" beat; sales and walk-outs land at once.
            revealAt = countered ? Time.unscaledTime + reactionBeat : 0;
            actionReadyAt = Time.unscaledTime + (countered ? reactionBeat + .05f : .4f);
            World.Customers.HoldExpression = countered;
            ObserveSale();
            World.Customers.Sync(Session);
        }

        public void AcceptPrice(long dealId, int revision)
        {
            if (!ActionsReady || !Session.Accept(dealId, revision)) return;
            actionReadyAt = Time.unscaledTime + .4f;
            ObserveSale();
            World.Customers.Sync(Session);
        }

        /// <summary>The next line of the bulk buyer's explanation; a press while the line is still typing only finishes it.</summary>
        void ReadOnBulkBuyer()
        {
            if (!ActionsReady || !BulkBuyerExplaining) return;
            if (hud.SkipSpeechTyping()) return;
            var customer = Session.CurrentCustomer;
            if (customer.Id != bulkLineCustomer) { bulkLineCustomer = customer.Id; bulkLine = 0; }
            bulkLine++;
            // The offer is a decision about the whole display: a moment's pause so a reading press never sells by accident.
            actionReadyAt = Time.unscaledTime + (BulkOfferShown ? .45f : .12f);
        }

        /// <summary>The green answer to the bulk buyer: he takes the whole display for his offer.</summary>
        public void SellToBulkBuyer(long customerId)
        {
            if (!ActionsReady || !BulkOfferShown || !Session.AcceptBulkOffer(customerId)) return;
            actionReadyAt = Time.unscaledTime + .4f;
            ObserveSale();
            World.Customers.Sync(Session);
        }

        public void RefuseCustomer(long customerId)
        {
            if (!ActionsReady || !Session.Refuse(customerId)) return;
            actionReadyAt = Time.unscaledTime + .4f;
            Notify("Товар остался в лавке");
            World.Customers.Sync(Session);
        }
    }
}
