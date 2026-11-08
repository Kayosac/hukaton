using System.Collections.Generic;
using System.Linq;
using System.Text;
using Hakaton.Decks;
using Hakaton.Feel;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// Sparse expedition HUD: hearts, a reticle that shows charge and spin, loot toasts, a compact deck strip
    /// and full-screen vignettes. Contextual hints appear only until the player has done the thing once.
    /// </summary>
    public sealed class ExpeditionHud : MonoBehaviour
    {
        public ExpeditionWorld world;
        public Canvas canvas;
        [Header("Hearts")]
        public RectTransform hearts;
        public Image[] heartFills;
        [Header("Reticle")]
        public Image reticle;
        public Image chargeRing;
        [Header("Spin arrow")]
        [Tooltip("Rotates to the spin's direction around the reticle centre.")]
        public RectTransform spinPointer;
        public Image spinShaft, spinHead;
        [Tooltip("Faint circle at full spin strength, shown while Shift steers the spin.")]
        public Image spinLimit;
        [Tooltip("Arrow length at full spin, canvas pixels.")]
        [Min(8)] public float spinArrowLength = 72;
        [Tooltip("The arrow starts outside the charge ring.")]
        [Min(0)] public float spinArrowGap = 40;
        [Header("Text")]
        public TMP_Text prompt;
        public TMP_Text notice, toasts, deck, cardLabel, loot, hint;
        [Header("Stamina")]
        public GameObject staminaPanel;
        public Image staminaFill;
        [Header("Overlays")]
        public Image vignette;
        public GameObject inventoryPanel;
        public TMP_Text inventory;
        [Header("Colours")]
        public Color hurtColour = new Color(.75f, .08f, .05f);
        public Color healColour = new Color(.3f, .85f, .45f);
        public Color activeDeckColour = new Color(1f, .86f, .5f);

        readonly List<(string text, float time)> toastLines = new List<(string, float)>();
        SpringFloat[] heartScales;
        SpringFloat heartsShake, ringScale;
        TextPunch lootPunch;
        float vignetteAmount, cardLabelUntil, lastThrowCharge;
        Color vignetteColour;
        bool subscribed, hintedThrow, hintedDeck, hintedPickup;
        Color[] heartBaseColours;
        Vector2 heartsBase;

        void Awake()
        {
            heartScales = new SpringFloat[heartFills.Length];
            for (int i = 0; i < heartScales.Length; i++) heartScales[i].Snap(1);
            heartBaseColours = heartFills.Select(f => f ? f.color : Color.white).ToArray();
            ringScale.Snap(1);
            lootPunch = new TextPunch(loot.rectTransform);
            if (hearts) heartsBase = hearts.anchoredPosition;
        }

        void OnEnable() => Subscribe(true);
        void OnDisable() { Subscribe(false); if (world && world.sfx) world.sfx.StopLoop(this); }

        void Subscribe(bool on)
        {
            if (!world || on == subscribed) return;
            subscribed = on;
            if (on)
            {
                world.Collected += OnCollected;
                world.vitals.Changed += OnHealthChanged;
                world.thrower.SelectionChanged += OnSelectionChanged;
                world.thrower.Thrown += OnThrown;
            }
            else
            {
                world.Collected -= OnCollected;
                world.vitals.Changed -= OnHealthChanged;
                world.thrower.SelectionChanged -= OnSelectionChanged;
                world.thrower.Thrown -= OnThrown;
            }
        }

        public void BeginOuting()
        {
            toastLines.Clear();
            vignetteAmount = 0;
            cardLabelUntil = Time.time + 2;
            if (inventoryPanel) inventoryPanel.SetActive(false);
        }

        void OnCollected(string itemName, int value)
        {
            toastLines.Add(($"+ {itemName}  <color=#F4D27A>{value}</color>", Time.time));
            if (toastLines.Count > 4) toastLines.RemoveAt(0);
            lootPunch?.Hop(6);
            hintedPickup = true;
        }

        void OnHealthChanged(int delta)
        {
            int from = world.vitals.PreviousHalfHearts, to = world.vitals.HalfHearts;
            for (int i = 0; i < heartScales.Length; i++)
            {
                bool affected = Mathf.Clamp(from - i * 2, 0, 2) != Mathf.Clamp(to - i * 2, 0, 2);
                if (affected) heartScales[i].Kick(delta < 0 ? -9 : 12);
            }
            if (delta < 0) { heartsShake.Kick(60); vignetteColour = hurtColour; vignetteAmount = .65f; }
            else { vignetteColour = healColour; vignetteAmount = .4f; }
        }

        void OnSelectionChanged() { cardLabelUntil = Time.time + 1.3f; hintedDeck |= world.workshop.Model.Current?.ActiveKind == DeckKind.Combat; }
        void OnThrown(Hakaton.Decks.CardCopy card) { hintedThrow = true; ringScale.Kick(-6); }

        void LateUpdate()
        {
            if (!world || !world.Running) return;
            float dt = Time.unscaledDeltaTime;
            UpdateHearts(dt);
            UpdateReticle(dt);
            UpdateTexts(dt);
            UpdateVignette(dt);
            if (staminaPanel)
            {
                float stamina = world.motor.stamina.Normalized;
                staminaPanel.SetActive(stamina < .99f);
                if (staminaFill) staminaFill.fillAmount = stamina;
            }
            bool bag = Keyboard.current != null && Keyboard.current.iKey.isPressed;
            if (inventoryPanel)
            {
                inventoryPanel.SetActive(bag);
                if (bag) inventory.text = world.Loot.Count == 0 ? "Сумка пуста" : string.Join("\n", world.Loot.Select(x => $"{x.name}  ×{x.count}  <color=#F4D27A>{x.value * x.count}</color>"));
            }
        }

        void UpdateHearts(float dt)
        {
            var vitals = world.vitals;
            heartsShake.Step(0, 9, .25f, dt);
            // Shake by whole pixels: rotation would break the pixel-perfect HUD grid.
            if (hearts) hearts.anchoredPosition = heartsBase + new Vector2(Mathf.Round(heartsShake.Value * .12f), 0);
            for (int i = 0; i < heartFills.Length; i++)
            {
                var fill = heartFills[i];
                if (!fill) continue;
                fill.fillAmount = Mathf.Clamp01((vitals.HalfHearts - i * 2) / 2f);
                float scale = heartScales[i].Step(1, 6, .3f, dt);
                if (vitals.Critical && vitals.HalfHearts > i * 2) scale *= 1 + Mathf.Max(0, Mathf.Sin(Time.time * 7)) * .12f;
                fill.transform.parent.localScale = Vector3.one * scale;
                float flash = Mathf.Clamp01(1 - (Time.time - vitals.LastChangeTime) / .35f);
                fill.color = Color.Lerp(heartBaseColours[i], vitals.LastChangeWasDamage ? Color.white : healColour, flash * .8f);
            }
        }

        void UpdateReticle(float dt)
        {
            var preparation = world.thrower.Preparation;
            var card = world.thrower.Selected;
            Color accent = card != null ? world.Accent(card) : Color.white;
            float scale = ringScale.Step(1, 7, .4f, dt);
            if (chargeRing)
            {
                chargeRing.enabled = preparation.Active;
                if (preparation.Active)
                {
                    chargeRing.fillAmount = preparation.Charge;
                    bool full = preparation.Charge >= 1;
                    if (full && lastThrowCharge < 1) ringScale.Kick(10);
                    chargeRing.color = full ? Color.Lerp(accent, Color.white, Mathf.PingPong(Time.time * 4, .5f)) : new Color(1, 1, 1, .85f);
                }
                lastThrowCharge = preparation.Active ? preparation.Charge : 0;
                chargeRing.rectTransform.localScale = Vector3.one * scale;
            }
            UpdateSpinArrow(preparation, accent);
            if (reticle) reticle.color = world.FocusedLoot ? new Color(1f, .86f, .45f) : Color.white;
        }

        /// <summary>The spin as an arrow from the reticle: direction and length follow the mouse while Shift is held.</summary>
        void UpdateSpinArrow(ThrowPreparation preparation, Color accent)
        {
            if (!spinPointer) return;
            var spin = preparation.Active ? new Vector2(preparation.SpinX, preparation.SpinY) : Vector2.zero;
            float amount = Mathf.Clamp01(spin.magnitude);
            bool show = amount > .02f;
            if (spinPointer.gameObject.activeSelf != show) spinPointer.gameObject.SetActive(show);
            if (show)
            {
                spinPointer.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(spin.y, spin.x) * Mathf.Rad2Deg);
                // Whole canvas pixels so the pixel-art shaft stays crisp.
                float length = Mathf.Max(2, Mathf.Round(amount * spinArrowLength));
                spinShaft.rectTransform.anchoredPosition = new Vector2(spinArrowGap, 0);
                spinShaft.rectTransform.sizeDelta = new Vector2(length, spinShaft.rectTransform.sizeDelta.y);
                spinHead.rectTransform.anchoredPosition = new Vector2(spinArrowGap + length, 0);
                Color colour = Color.Lerp(Color.white, accent, amount);
                colour.a = preparation.Steering ? 1 : .55f + .45f * amount;
                spinShaft.color = spinHead.color = colour;
            }
            if (spinLimit)
            {
                bool steering = preparation.Active && preparation.Steering;
                spinLimit.enabled = steering;
                if (steering)
                {
                    float diameter = 2 * (spinArrowGap + spinArrowLength);
                    spinLimit.rectTransform.sizeDelta = new Vector2(diameter, diameter);
                    spinLimit.color = new Color(1, 1, 1, .18f);
                }
            }
        }

        void UpdateTexts(float dt)
        {
            var trip = world.workshop.Model.Current;
            var card = world.thrower.Selected;

            // Contextual prompt: pick-up first, then the Return card.
            if (world.FocusedLoot) prompt.text = $"<b>E</b>  {world.FocusedLoot.displayName}  <color=#F4D27A>{world.FocusedLoot.value}</color>";
            else if (card != null && card.IsReturn) prompt.text = world.thrower.ReturnReady ? "<b>ЛКМ</b>  Домой" : "";
            else prompt.text = "";

            notice.text = Time.time < world.NoticeUntil ? world.Notice : "";

            var builder = new StringBuilder();
            for (int i = toastLines.Count - 1; i >= 0; i--)
            {
                float age = Time.time - toastLines[i].time;
                if (age > 2.8f) { toastLines.RemoveAt(i); continue; }
            }
            foreach (var line in toastLines)
            {
                float age = Time.time - line.time;
                int alpha = Mathf.RoundToInt(255 * Mathf.Clamp01((2.8f - age) / .5f));
                builder.Append($"<alpha=#{alpha:X2}>").AppendLine(line.text);
            }
            toasts.text = builder.ToString();

            if (trip != null)
            {
                var exploration = trip.Get(DeckKind.Exploration);
                var combat = trip.Get(DeckKind.Combat);
                string Format(string title, ExpeditionDeck d, bool active)
                    => active ? $"<color=#{ColorUtility.ToHtmlStringRGB(activeDeckColour)}><b>{title}</b>  {d.Hand.Count}·{d.StockCount}</color>"
                              : $"<alpha=#99>{title}  {d.Hand.Count}·{d.StockCount}";
                deck.text = Format("ПУТЬ", exploration, trip.ActiveKind == DeckKind.Exploration) + "     " + Format("БОЙ", combat, trip.ActiveKind == DeckKind.Combat);
            }

            bool showLabel = card != null && (Time.time < cardLabelUntil || world.thrower.Preparation.Active);
            if (showLabel)
            {
                string name = card.IsReturn ? "Пора домой" : world.workshop.config.Find(card.Spec.Id)?.displayName ?? card.Spec.Name;
                cardLabel.text = card.IsReturn ? name : $"{name}  <alpha=#88>ур. {card.Level}";
            }
            cardLabel.alpha = Mathf.MoveTowards(cardLabel.alpha, showLabel ? 1 : 0, dt * 5);

            loot.text = world.Loot.Count == 0 ? "" : $"Сумка  {world.Loot.Sum(x => x.count)}  <color=#F4D27A>{world.LootValue}</color>";
            lootPunch?.Update(dt);

            if (hint)
            {
                string text = "";
                if (!hintedThrow) text = "Зажмите <b>ЛКМ</b> — бросок. Во время заряда <b>Shift</b> + мышь — подкрутка в любую сторону";
                else if (!hintedDeck && AnyHostileNearby()) text = "<b>Tab</b> — боевая колода";
                else if (!hintedPickup && world.FocusedLoot) text = "<b>E</b> — подобрать";
                hint.text = text;
            }
        }

        bool AnyHostileNearby()
        {
            foreach (var creature in world.creatures)
                if (creature && creature.Alive && creature.gameObject.activeInHierarchy && creature.temperament != Temperament.Timid
                    && Vector3.Distance(creature.transform.position, world.motor.transform.position) < 14) return true;
            return false;
        }

        void UpdateVignette(float dt)
        {
            if (!vignette) return;
            var vitals = world.vitals;
            vignetteAmount = Mathf.MoveTowards(vignetteAmount, 0, dt * .9f);
            float critical = vitals.Critical ? .22f + Mathf.Max(0, Mathf.Sin(Time.time * 7)) * .18f : 0;
            float amount = Mathf.Max(vignetteAmount, critical);
            Color colour = vignetteAmount >= critical ? vignetteColour : hurtColour;
            vignette.color = new Color(colour.r, colour.g, colour.b, amount);
            if (world.sfx && world.cues)
            {
                if (vitals.Critical) world.sfx.Loop(this, world.cues.heartbeat, world.viewCamera.transform.position, .8f);
                else world.sfx.StopLoop(this);
            }
        }
    }
}
