using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Hakaton.Economy.Unity;
using Hakaton.ShopBlockout;

namespace Hakaton.Trading.Unity
{
    /// <summary>Playable greybox adapter. All economy rules live in TradingSession.</summary>
    public sealed class TradingPrototype : MonoBehaviour
    {
        [Header("Temporary prototype balance")]
        [Min(1)] public float shiftSeconds = 120;
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
        [Tooltip("Size of the selected stock in the shopkeeper's hand relative to the same item on the shelf.")]
        [Range(.1f, 1)] public float heldItemScale = .5f;
        [Tooltip("After a counter-offer the visitor 'thinks' this long before their answer, face and voice show.")]
        [Min(0)] public float reactionBeat = .38f;

        public TradingSession Session { get; private set; }
        [Header("Scene references — assign in the authored scene")]
        public TradingSceneBindings World;
        public TradingHud hud;
        public CoinWalletHost walletHost;
        public ShopBackroomServices services;
        /// <summary>Raised when the player uses the shop door; the game loop runs the expedition.</summary>
        public event System.Action ExpeditionRequested;
        public event System.Action<int> ItemPlaced, ItemTaken, SlotHovered;
        public int HoveredSlot => hoveredSlot;
        public bool CustomerReady => Session != null && World.Customers.ReadyForTrade(Session.CurrentCustomer);
        public bool ActionsReady => CustomerReady && !ViewTransitioning && Time.unscaledTime >= actionReadyAt;
        /// <summary>False during the short beat after an offer while the visitor considers it.</summary>
        public bool ReactionRevealed => Time.unscaledTime >= revealAt;
        public bool UsesDisplayView => World && World.ShopView;
        public bool SelectingFromDisplay => UsesDisplayView && World.ShopView.Selecting;
        public bool ViewTransitioning => UsesDisplayView && World.ShopView.IsTransitioning;
        public bool HasMatchingStock
        {
            get
            {
                if (Session?.CurrentCustomer == null) return false;
                foreach (var item in Session.Slots)
                    if (item != null && item.Definition.Category == Session.CurrentCustomer.RequestedCategory) return true;
                return false;
            }
        }
        public string CustomerName => World.Customers.VisitorName;
        public string Notice => Time.unscaledTime < noticeUntil ? notice : string.Empty;
        public ItemInstance SelectedItem => Session.Inventory.Count == 0 ? null : Session.Inventory[selectedInventory];
        [System.Serializable]
        public sealed class StockEntry
        {
            public string id, displayName, category;
            [Min(1)] public int recommendedPrice = 100;
            [Min(0)] public int count = 4;
            public StockEntry(string id, string displayName, string category, int price)
            { this.id = id; this.displayName = displayName; this.category = category; recommendedPrice = price; }
        }
        [Header("Catalogue — order matches item prefabs and test loot")]
        public StockEntry[] startingStock = {
            new StockEntry("fang", "Клык зверя", "Трофеи", 100),
            new StockEntry("herb", "Лесная трава", "Растения", 40),
            new StockEntry("relic", "Старая реликвия", "Находки", 160)
        };
        ItemDefinition[] catalog;
        GameObject[] displayed;
        long[] displayedIds;
        GameObject heldItem, heldVisual;
        int heldVisualIndex = -1;
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
        public float StaminaFraction => World.Stamina.Normalized;
        public bool ShowStamina => World.Stamina.Normalized < .995f;
        public float ContextProgress => pressedSlot >= 0 ? Mathf.Clamp01((Time.unscaledTime - pressStarted) / holdThreshold) : 0;
        int lastSettlement;
        int presentedDealSlot = -1;
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
            if (!World.ValidateReferences() || startingStock == null || startingStock.Length == 0
                || startingStock.Length != World.ItemPrefabs.Length)
            {
                Debug.LogError("TradingPrototype: scene references are incomplete.", World);
                enabled = false;
                return;
            }
            heldItem = World.HeldItem;
            Session = new TradingSession(new TradeSettings {
                SlotCount = 6, ShiftSeconds = shiftSeconds,
                MinOpeningRatio = minOpeningRatio, MaxOpeningRatio = maxOpeningRatio,
                MinCeilingRatio = minCeilingRatio, MaxCeilingRatio = maxCeilingRatio,
                MinOfferCeilingRatio = minOfferCeilingRatio, MaxOfferCeilingRatio = maxOfferCeilingRatio,
                AsksPriceProbability = asksPriceProbability,
                UsePresetPatienceCosts = usePresetPatienceCosts,
                MinPatience = minPatience, MaxPatience = maxPatience
            }, randomSeed, walletHost ? walletHost.Wallet : null);
            catalog = new ItemDefinition[startingStock.Length];
            int maxCount = 0;
            for (int i = 0; i < startingStock.Length; i++)
            {
                var entry = startingStock[i];
                catalog[i] = new ItemDefinition(entry.id, entry.displayName, entry.category, entry.recommendedPrice);
                Session.SeedInventory(catalog[i], 0);
                maxCount = Mathf.Max(maxCount, entry.count);
            }
            for (int n = 0; n < maxCount; n++)
                for (int i = 0; i < catalog.Length; i++)
                    if (n < startingStock[i].count) Session.SeedInventory(catalog[i], 1);
            displayed = new GameObject[6];
            displayedIds = new long[6];
            itemMotion = new Coroutine[6];
            if (World.ShopView) World.ShopView.Bind(this);
            hud.Bind(this);
            RefreshWorld();
        }

        void Update()
        {
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
                if (SelectingFromDisplay && !ViewTransitioning) UpdateDisplaySelection();
                if (keyboard != null)
                {
                    if (keyboard.escapeKey.wasPressedThisFrame && SelectingFromDisplay) World.ShopView.ReturnToCounter();
                    else if (keyboard.escapeKey.wasPressedThisFrame || keyboard.eKey.wasPressedThisFrame)
                    {
                        // Closing mid-conversation would silently drop the visitor: finish or refuse them first.
                        if (Session.CurrentCustomer != null && CustomerReady) Notify("Сначала закончите сделку · Backspace — отказать");
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
            if (deal == null) return;
            var digits = new[] { keyboard.digit1Key, keyboard.digit2Key, keyboard.digit3Key, keyboard.digit4Key, keyboard.digit5Key };
            for (int i = 0; i < digits.Length; i++)
                if (digits[i].wasPressedThisFrame) { OfferPrice(deal.Id, deal.Revision, i); return; }
            if ((keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame) && deal.N.HasValue)
                AcceptPrice(deal.Id, deal.Revision);
        }

        void SyncControl()
        {
            World.Control.SetBlocked(this, Session.IsOpen);
            if (Session.IsOpen == previousOpen) return;
            ResetPlacement();
            if (Session.IsOpen && !World.ShopView) World.Movement.Teleport(World.CounterPosition, World.CounterRotation);
            if (World.ShopView) World.ShopView.Sync();
            previousOpen = Session.IsOpen;
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
                    ContextPrompt = services.PromptFor(service.kind);
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
                ContextPrompt = "E  Выйти из лавки";
                ContextDetail = string.Empty;
            }
        }

        void UpdateDisplaySelection()
        {
            var mouse = Mouse.current;
            if (mouse == null || !ActionsReady || Session.CurrentDeal != null || hud.ContainsBlockingPanel(mouse.position.ReadValue())) return;
            // The view camera may render into a low-resolution World Resolution texture, so its pixel
            // space is not the screen's. Viewport coordinates are the same in both cases.
            Vector2 pointer = mouse.position.ReadValue();
            var ray = World.ViewCamera.ViewportPointToRay(new Vector3(pointer.x / Screen.width, pointer.y / Screen.height));
            if (!Physics.Raycast(ray, out var hit, 4, ~(1 << 2), QueryTriggerInteraction.Collide)) return;
            for (int i = 0; i < displayed.Length; i++)
            {
                var item = Session.Slots[i];
                if (item == null || item.Definition.Category != Session.CurrentCustomer.RequestedCategory) continue;
                bool overItem = displayed[i] && hit.collider.transform.IsChildOf(displayed[i].transform);
                if (!overItem && hit.collider != World.SlotColliders[i]) continue;
                hoveredSlot = i;
                ContextPrompt = "ЛКМ  Предложить";
                ContextDetail = $"{item.Definition.Name} · {item.Definition.RecommendedPrice} монет";
                if (mouse.leftButton.wasPressedThisFrame) SelectCustomerItem(i, Session.CurrentCustomer.Id);
                break;
            }
        }

        void UpdatePlacement()
        {
            var mouse = Mouse.current;
            if (mouse == null || !GameplayActive || World.Control.CapturedThisFrame) return;
            if (pressedSlot >= 0 && hoveredSlot != pressedSlot) ResetPlacement();
            float scroll = mouse.scroll.ReadValue().y;
            if (scroll != 0 && Session.Inventory.Count > 0)
                selectedInventory = (selectedInventory + (scroll > 0 ? -1 : 1) + Session.Inventory.Count) % Session.Inventory.Count;
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
            if (Session.PlaceItem(selectedInventory, slot))
            {
                ItemPlaced?.Invoke(slot);
                // After the selected item, continue in inventory order.
                selectedInventory = Session.Inventory.Count == 0 ? 0 : selectedInventory % Session.Inventory.Count;
                RefreshWorld();
                if (displayed[slot] != null) StartCoroutine(Snap(displayed[slot].transform));
            }
        }

        void ResetPlacement() { pressedSlot = -1; autoFilling = false; }
        void ClampSelection() { selectedInventory = Mathf.Clamp(selectedInventory, 0, Mathf.Max(0, Session.Inventory.Count - 1)); }

        void Interact()
        {
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
            if (!Session.Open()) Notify(Session.LastMessage);
            actionReadyAt = Time.unscaledTime + .25f;
            World.Customers.Sync(Session);
            SyncControl();
        }
        void CloseShop() { Session.Close(); World.Customers.Sync(Session); SyncControl(); }
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
                    displayed[i] = item == null ? null : Instantiate(World.ItemPrefabs[ItemIndex(item.Definition.Id)], World.SlotAnchors[i].position, World.SlotAnchors[i].rotation, World.SoldItemsRoot);
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
            if (Session.Inventory.Count > 0) PresentHeldItem(ItemIndex(Session.Inventory[selectedInventory].Definition.Id));
        }

        /// <summary>The hand shows a copy of the same stock prefab that goes on the shelf.</summary>
        void PresentHeldItem(int index)
        {
            if (heldVisual && heldVisualIndex == index) return;
            if (heldVisual) Destroy(heldVisual);
            heldVisualIndex = index;
            heldVisual = Instantiate(World.ItemPrefabs[index], heldItem.transform, false);
            heldVisual.transform.localScale = Vector3.one * heldItemScale;
            foreach (var collider in heldVisual.GetComponentsInChildren<Collider>()) collider.enabled = false;
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
            const float duration = .24f;
            for (float t = 0; t < duration && item; t += Time.deltaTime)
            {
                float f = Mathf.SmoothStep(0, 1, t / duration);
                item.position = Vector3.Lerp(start, destination.position, f) + Vector3.up * (Mathf.Sin(f * Mathf.PI) * .09f);
                item.rotation = Quaternion.Slerp(rotation, destination.rotation, f);
                yield return null;
            }
            if (item) item.SetPositionAndRotation(destination.position, destination.rotation);
        }

        int ItemIndex(string id) { for (int i = 0; i < catalog.Length; i++) if (catalog[i].Id == id) return i; throw new System.InvalidOperationException("Missing stock presentation: " + id); }

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
        IEnumerator Transfer(GameObject item)
        {
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
            selectedInventory = (selectedInventory + direction + Session.Inventory.Count) % Session.Inventory.Count;
        }

        public void SelectCustomerItem(int slotIndex, long customerId)
        {
            if (!ActionsReady || Session.CurrentCustomer == null) return;
            if (Session.SelectItem(slotIndex, customerId)) actionReadyAt = Time.unscaledTime + .3f;
            World.Customers.Sync(Session);
            if (World.ShopView && Session.CurrentDeal != null) World.ShopView.ReturnToCounter();
        }

        public void OfferPrice(long dealId, int revision, int optionIndex)
        {
            if (!ActionsReady || SelectingFromDisplay) return;
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
            if (!ActionsReady || SelectingFromDisplay || !Session.Accept(dealId, revision)) return;
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
