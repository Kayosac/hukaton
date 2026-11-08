using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

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
        [Min(1)] public int minPatience = 3;
        [Min(1)] public int maxPatience = 5;
        [Header("Controls")]
        public float holdThreshold = .35f;
        public float autoPlaceInterval = .16f;
        public Color slotHighlightColor = new Color(.9f, .8f, .35f);

        public TradingSession Session { get; private set; }
        [Header("Scene references — assign in the authored scene")]
        public TradingSceneBindings World;
        public TradingHud hud;
        public AudioSource sound;
        public AudioClip placeSound, saleSound;
        public int HoveredSlot => hoveredSlot;
        public bool ActionsReady => Time.unscaledTime >= actionReadyAt;
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
        GameObject heldItem;
        int selectedInventory, hoveredSlot = -1, pressedSlot = -1;
        bool autoFilling, previousOpen;
        float pressStarted, nextPlaceAt, actionReadyAt;
        TradingInteractionTarget focusedTarget;
        int focusedLoot = -1;
        public string ContextPrompt { get; private set; } = string.Empty;
        public string ContextDetail { get; private set; } = string.Empty;
        public bool GameplayActive => World != null && World.Control != null && World.Control.GameplayActive && Session != null && !Session.IsOpen;
        public float StaminaFraction => World.Stamina.Normalized;
        public bool ShowStamina => World.Stamina.Normalized < .995f;
        public float ContextProgress => pressedSlot >= 0 ? Mathf.Clamp01((Time.unscaledTime - pressStarted) / holdThreshold) : 0;
        int lastSettlement;
        MaterialPropertyBlock slotHighlight;
        string notice = string.Empty;
        float noticeUntil;

        void Awake()
        {
            slotHighlight = new MaterialPropertyBlock();
            if (World == null || hud == null || sound == null || placeSound == null || saleSound == null)
            {
                Debug.LogError("TradingPrototype: assign scene bindings, HUD and audio assets in the Inspector.", this);
                enabled = false;
                return;
            }
            if (!World.ValidateReferences() || startingStock == null || startingStock.Length == 0
                || startingStock.Length != World.ItemPrefabs.Length || startingStock.Length != World.ItemMaterials.Length)
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
                MinPatience = minPatience, MaxPatience = maxPatience
            }, randomSeed);
            catalog = new ItemDefinition[startingStock.Length];
            int maxCount = 0;
            for (int i = 0; i < startingStock.Length; i++)
            {
                var entry = startingStock[i];
                catalog[i] = new ItemDefinition(entry.id, entry.displayName, entry.category, entry.recommendedPrice);
                maxCount = Mathf.Max(maxCount, entry.count);
            }
            for (int n = 0; n < maxCount; n++)
                for (int i = 0; i < catalog.Length; i++)
                    if (n < startingStock[i].count) Session.SeedInventory(catalog[i], 1);
            displayed = new GameObject[6];
            displayedIds = new long[6];
            hud.Bind(this);
            RefreshWorld();
        }

        void Update()
        {
            Session.Tick(Time.unscaledDeltaTime);
            if (Session.IsOpen && Session.CurrentCustomer == null)
            {
                Session.AdvanceCustomer();
                actionReadyAt = Mathf.Max(actionReadyAt, Time.unscaledTime + .25f);
            }
            SyncControl();
            var keyboard = Keyboard.current;
            if (Session.IsOpen)
            {
                ClearFocus();
                if (keyboard != null && (keyboard.escapeKey.wasPressedThisFrame || keyboard.eKey.wasPressedThisFrame))
                    CloseShop();
            }
            else if (GameplayActive && !World.Control.CapturedThisFrame)
            {
                UpdateHover();
                UpdatePlacement();
                if (keyboard != null && keyboard.eKey.wasPressedThisFrame) Interact();
                if (keyboard != null && keyboard.fKey.wasPressedThisFrame && hoveredSlot >= 0)
                {
                    if (Session.RemoveItem(hoveredSlot)) sound.PlayOneShot(placeSound, .18f);
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

        void SyncControl()
        {
            World.Control.SetBlocked(this, Session.IsOpen);
            if (Session.IsOpen == previousOpen) return;
            ResetPlacement();
            if (Session.IsOpen) World.Movement.Teleport(World.CounterPosition, World.CounterRotation);
            previousOpen = Session.IsOpen;
        }

        void ClearFocus()
        {
            focusedTarget = null;
            hoveredSlot = focusedLoot = -1;
            ContextPrompt = ContextDetail = string.Empty;
        }

        void UpdateHover()
        {
            ClearFocus();
            // The first surface wins: no prompts or interaction through walls.
            var ray = World.ViewCamera.ViewportPointToRay(new Vector3(.5f, .5f));
            if (!Physics.Raycast(ray, out var hit, 5f, ~((1 << 2)), QueryTriggerInteraction.Collide)) return;
            var target = hit.collider.GetComponentInParent<TradingInteractionTarget>();
            // A displayed item belongs to its slot; aiming at the item must not select a slot behind it.
            for (int i = 0; i < displayed.Length; i++)
                if (displayed[i] != null && hit.collider.transform.IsChildOf(displayed[i].transform))
                {
                    hoveredSlot = i;
                    target = World.SlotColliders[i].GetComponentInParent<TradingInteractionTarget>();
                    break;
                }
            if (target == null || hit.distance > target.reach) return;
            focusedTarget = target;
            if (target.kind == TradingTargetKind.DisplaySlot && Session.ExpeditionId == 0)
            {
                if (hoveredSlot < 0)
                    for (int i = 0; i < World.SlotColliders.Length; i++)
                        if (World.SlotColliders[i] == hit.collider) { hoveredSlot = i; break; }
                if (hoveredSlot < 0) return;
                var item = Session.Slots[hoveredSlot];
                if (item != null)
                {
                    ContextPrompt = "F  Забрать" + (Session.RemainingSeconds > 0 ? "     E  Открыть лавку" : string.Empty);
                    ContextDetail = $"{item.Definition.Name} · {item.Definition.RecommendedPrice} монет";
                }
                else
                {
                    ContextPrompt = Session.Inventory.Count > 0 ? "ЛКМ  Положить · удерживать — заполнить" : "Нет товара в запасе";
                    ContextDetail = Session.DisplayCount > 0 && Session.RemainingSeconds > 0 ? "E  Открыть лавку" : string.Empty;
                }
            }
            else if (target.kind == TradingTargetKind.Counter && Session.ExpeditionId == 0)
            {
                ContextPrompt = Session.RemainingSeconds <= 0 ? "Смена окончена" : Session.DisplayCount == 0 ? "Витрина пуста" : "E  Открыть лавку";
                ContextDetail = Session.RemainingSeconds <= 0 ? "Новая добыча восстановит время торговли" : Session.DisplayCount == 0 ? "Положите товар на свободный слот" : string.Empty;
            }
            else if (target.kind == TradingTargetKind.Door)
            {
                ContextPrompt = Session.ExpeditionId == 0 ? "E  Выйти из лавки" : "E  Вернуться в лавку";
                ContextDetail = Session.PendingLootCount > 0 ? $"Добыча: {Session.PendingLootCount}" : string.Empty;
            }
            else if (target.kind == TradingTargetKind.Loot && Session.ExpeditionId != 0)
            {
                for (int i = 0; i < World.LootColliders.Length; i++)
                    if (World.LootColliders[i] == hit.collider) { focusedLoot = i; break; }
                if (focusedLoot >= 0)
                {
                    ContextPrompt = "E  Подобрать";
                    ContextDetail = catalog[focusedLoot % catalog.Length].Name;
                }
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
                sound.PlayOneShot(placeSound, .25f);
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
            if (focusedTarget == null || !GameplayActive) return;
            if (focusedTarget.kind == TradingTargetKind.Door)
            {
                if (Session.ExpeditionId == 0)
                {
                    if (Session.BeginExpedition() != 0)
                    {
                        foreach (var loot in World.LootColliders) loot.gameObject.SetActive(true);
                        Teleport(World.DoorOutside.position, Quaternion.Euler(0, 0, 0));

                    }
                }
                else
                {
                    bool fresh = Session.PendingLootCount > 0;
                    Session.CompleteExpedition(Session.ExpeditionId);
                    Teleport(World.DoorInside.position, Quaternion.Euler(0, 90, 0));
                    Notify(fresh ? "Добыча доставлена · новая смена" : "Без новой добычи · время сохранено");
                }
                return;
            }
            if (Session.ExpeditionId != 0 && focusedLoot >= 0)
            {
                int i = focusedLoot;
                if (Session.CollectLoot(Session.ExpeditionId, catalog[i % catalog.Length]))
                {
                    World.LootColliders[i].gameObject.SetActive(false);
                    sound.PlayOneShot(placeSound, .2f);
                    Notify("+ " + catalog[i % catalog.Length].Name);
                }
                return;
            }
            if (focusedTarget.kind == TradingTargetKind.Counter || focusedTarget.kind == TradingTargetKind.DisplaySlot)
                OpenShop();
        }

        void OpenShop()
        {
            if (!GameplayActive || focusedTarget == null || Session.ExpeditionId != 0) return;
            if (focusedTarget.kind != TradingTargetKind.Counter && focusedTarget.kind != TradingTargetKind.DisplaySlot) return;
            if (!Session.Open()) Notify(Session.LastMessage);
            actionReadyAt = Time.unscaledTime + .25f;
            SyncControl();
        }
        void CloseShop() { Session.Close(); SyncControl(); }
        void Notify(string text) { notice = text; noticeUntil = Time.unscaledTime + 2.6f; }
        void Teleport(Vector3 position, Quaternion rotation)
        {
            ResetPlacement();
            ClearFocus();
            World.Movement.Teleport(position, rotation);
        }
        void OnEnable()
        {
            if (Session != null && World != null && World.Control != null)
                World.Control.SetBlocked(this, Session.IsOpen);
        }
        void OnDisable()
        {
            if (World != null && World.Control != null) World.Control.SetBlocked(this, false);
        }

        void ObserveSale()
        {
            if (Session.SettlementSequence == lastSettlement) return;
            lastSettlement = Session.SettlementSequence;
            sound.PlayOneShot(saleSound, .25f);
            Notify($"Продано: {Session.LastSoldItem.Definition.Name} за {Session.LastSalePrice} монет.");
            for (int i = 0; i < 6; i++)
                if (displayed[i] != null && displayedIds[i] == Session.LastSoldItem.Id)
                {
                    var item = displayed[i]; displayed[i] = null; displayedIds[i] = 0;
                    var collider = item.GetComponent<Collider>(); if (collider != null) collider.enabled = false;
                    StartCoroutine(Transfer(item));
                    break;
                }
        }

        void RefreshWorld()
        {
            ClampSelection();
            World.Customer.SetActive(Session.CurrentCustomer != null);
            for (int i = 0; i < 6; i++)
            {
                var item = Session.Slots[i];
                long id = item == null ? 0 : item.Id;
                if (displayedIds[i] != id)
                {
                    if (displayed[i] != null) Destroy(displayed[i]);
                    displayed[i] = item == null ? null : Instantiate(World.ItemPrefabs[CategoryIndex(item.Definition.Category)], World.SlotAnchors[i].position, World.SlotAnchors[i].rotation, World.SoldItemsRoot);
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
            heldItem.SetActive(!Session.IsOpen && Session.ExpeditionId == 0 && Session.Inventory.Count > 0);
            if (Session.Inventory.Count > 0)
                heldItem.GetComponent<Renderer>().sharedMaterial = World.ItemMaterials[CategoryIndex(Session.Inventory[selectedInventory].Definition.Category)];
        }

        int CategoryIndex(string category) { for (int i = 0; i < catalog.Length; i++) if (catalog[i].Category == category) return i; return 0; }
        IEnumerator Snap(Transform item)
        {
            var end = item.position; var start = end + Vector3.up * .2f;
            for (float t = 0; t < .14f && item != null; t += Time.deltaTime)
            { item.position = Vector3.Lerp(start, end, t / .14f); yield return null; }
            if (item != null) item.position = end;
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
        }

        public void OfferPrice(long dealId, int revision, int optionIndex)
        {
            if (!ActionsReady) return;
            int salesBefore = Session.SettlementSequence;
            if (!Session.Offer(dealId, revision, optionIndex)) return;
            if (Session.CurrentCustomer == null && Session.SettlementSequence == salesBefore)
                Notify(Session.LastMessage + " Товар сохранён.");
            actionReadyAt = Time.unscaledTime + .65f;
            ObserveSale();
        }

        public void AcceptPrice(long dealId, int revision)
        {
            if (!ActionsReady || !Session.Accept(dealId, revision)) return;
            actionReadyAt = Time.unscaledTime + .65f;
            ObserveSale();
        }

        public void RefuseCustomer(long customerId)
        {
            if (!ActionsReady || !Session.Refuse(customerId)) return;
            actionReadyAt = Time.unscaledTime + .65f;
            Notify("Товар остался в лавке");
        }
    }
}
