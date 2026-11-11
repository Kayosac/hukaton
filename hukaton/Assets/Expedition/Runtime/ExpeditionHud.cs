using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Hakaton.Decks;
using Hakaton.Feel;
using TMPro;
using Hakaton.Loot;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// Sparse expedition HUD (2026-11-10 UI handoff, 736 × 368 reference layout): spinning glossy hearts and the
    /// wallet top left with the effect badges under them, the paper X at the aim point with the charge ring and the
    /// three-part spin arrow, the tilted "stock/total" counter beside the hand, pickup rows top right (hold I for every
    /// carried find in the same rows) and one contextual hint bottom left. Nothing about an item is shown on hover.
    /// </summary>
    public sealed class ExpeditionHud : MonoBehaviour
    {
        public ExpeditionWorld world;
        public Canvas canvas;
        [Tooltip("Magnification of the expedition HUD layout (736 × 368 mockup units).")]
        public ReferenceUiScale layout;

        [Header("Hearts")]
        public RectTransform hearts;
        [Tooltip("Red glossy heart of each slot, filled from the left (a half heart is half of the frame).")]
        public Image[] heartFills;

        [Header("Wallet")]
        public TMP_Text wallet;

        [Header("Effect badges")]
        [Tooltip("Row under the wallet; timed effect badges line up here.")]
        public RectTransform statusRow;

        [Header("Reticle")]
        public Image reticle;
        public Image chargeRing;
        public Color reticleColour = new Color32(0xE9, 0xD7, 0xA3, 0xFF);

        [Header("Spin arrow")]
        [Tooltip("Rotates to the spin's direction around the aim point.")]
        public RectTransform spinPointer;
        [Tooltip("Fixed tail and head; only the shaft between them stretches.")]
        public Image spinTail, spinShaft, spinHead;
        [Tooltip("Faint circle at full spin strength, shown while Shift steers the spin.")]
        public Image spinLimit;
        [Tooltip("Arrow height (layout units); tail and head keep their drawn proportions.")]
        [Min(1)] public float spinArrowHeight = 8;
        [Tooltip("Shaft width at the weakest and the strongest spin (layout units).")]
        public Vector2 spinShaftRange = new Vector2(1.5f, 25.6f);
        [Tooltip("The arrow starts outside the charge ring (layout units).")]
        [Min(0)] public float spinArrowGap = 15.333f;
        public Color spinColour = new Color32(0xE9, 0xD7, 0xA3, 0xFF);

        [Header("Card counter")]
        [Tooltip("'Stock/Total' beside the hand: cards still to draw / copies taken on this outing.")]
        public TMP_Text cardCounter;

        [Header("Pickups")]
        [Tooltip("Rows top right: '+ name  value', the name in its rarity colour (common items keep the text colour). Hold I to list every carried find in the same rows.")]
        public TMP_Text pickups;
        [Tooltip("Shadow copy of the rows (no colours).")]
        public TMP_Text pickupsShadow;
        public Color pickupValueColour = new Color32(0xF5, 0xE4, 0xB7, 0xFF);
        [Min(.5f)] public float pickupSeconds = 2.8f;
        [Min(0)] public float pickupFadeSeconds = .5f;
        [Min(1)] public int pickupRows = 4;

        [Header("Text")]
        [Tooltip("The one contextual hint, bottom left: 'E — подобрать', 'Shift + мышь — подкрутка'…")]
        public TMP_Text context;
        public TMP_Text notice, cardLabel;

        [Header("Stamina")]
        public GameObject staminaPanel;
        public Image staminaFill;

        [Header("Overlays")]
        public Image vignette;

        [Header("Colours")]
        public Color hurtColour = new Color(.75f, .08f, .05f);
        public Color healColour = new Color(.3f, .85f, .45f);

        readonly List<(string name, int value, float time, string colour)> toastLines = new List<(string, int, float, string)>();
        SpringFloat[] heartScales;
        SpringFloat heartsShake, ringScale;
        float vignetteAmount, cardLabelUntil, lastThrowCharge;
        Color vignetteColour;
        bool subscribed, hintedDiscard;
        float discardHintUntil;
        Vector2 heartsBase;
        readonly StringBuilder rows = new StringBuilder(), shadowRows = new StringBuilder();

        void Awake()
        {
            heartScales = new SpringFloat[heartFills.Length];
            for (int i = 0; i < heartScales.Length; i++) heartScales[i].Snap(1);
            ringScale.Snap(1);
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
                world.CoinsCollected += OnCoins;
                world.vitals.Changed += OnHealthChanged;
                world.thrower.SelectionChanged += OnSelectionChanged;
                world.thrower.Thrown += OnThrown;
            }
            else
            {
                world.Collected -= OnCollected;
                world.CoinsCollected -= OnCoins;
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
        }

        void OnCollected(LootItem item)
        {
            toastLines.Add((item.displayName, item.price, Time.time, NameColour(item)));
            if (toastLines.Count > pickupRows) toastLines.RemoveAt(0);
        }

        const string CoinsRow = "Монеты";

        /// <summary>Chest coins add up in one row while they keep coming, instead of a row per coin.</summary>
        void OnCoins(int amount, Color colour)
        {
            string hex = ColorUtility.ToHtmlStringRGB(colour);
            int last = toastLines.Count - 1;
            if (last >= 0 && toastLines[last].name == CoinsRow && Time.time - toastLines[last].time < pickupSeconds * .6f)
            {
                toastLines[last] = (CoinsRow, toastLines[last].value + amount, Time.time, hex);
                return;
            }
            toastLines.Add((CoinsRow, amount, Time.time, hex));
            if (toastLines.Count > pickupRows) toastLines.RemoveAt(0);
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

        void OnSelectionChanged() { cardLabelUntil = Time.time + 1.3f; }
        void OnThrown(CardCopy card) { ringScale.Kick(-6); }

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
        }

        static readonly NumberFormatInfo MoneyFormat = new NumberFormatInfo { NumberGroupSeparator = " ", NumberGroupSizes = new[] { 3 } };
        /// <summary>The wallet as shown next to the coin: digits only, a space between thousands ("1 250").</summary>
        public static string FormatMoney(int value) => value.ToString("#,0", MoneyFormat);

        /// <summary>"Stock/Total": copies still in the draw pile over the copies taken on this outing (hand, held and spent excluded).</summary>
        public static string CardCount(ExpeditionDeck deck) => deck == null ? "" : $"{deck.StockCount}/{deck.Total}";

        void UpdateHearts(float dt)
        {
            var vitals = world.vitals;
            heartsShake.Step(0, 9, .25f, dt);
            // Shake by whole pixels: rotation would break the pixel-perfect HUD grid.
            if (hearts) hearts.anchoredPosition = heartsBase + new Vector2(Mathf.Round(heartsShake.Value * .05f), 0);
            for (int i = 0; i < heartFills.Length; i++)
            {
                var fill = heartFills[i];
                if (!fill) continue;
                fill.fillAmount = Mathf.Clamp01((vitals.HalfHearts - i * 2) / 2f);
                float scale = heartScales[i].Step(1, 6, .3f, dt);
                if (vitals.Critical && vitals.HalfHearts > i * 2) scale *= 1 + Mathf.Max(0, Mathf.Sin(Time.time * 7)) * .12f;
                fill.transform.parent.localScale = Vector3.one * scale;
                // The drawn red stays untinted; a hit or a heal flashes it for a moment.
                float flash = Mathf.Clamp01(1 - (Time.time - vitals.LastChangeTime) / .35f);
                fill.color = Color.Lerp(Color.white, vitals.LastChangeWasDamage ? new Color(1, .55f, .5f) : healColour, flash * .7f);
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
            UpdateSpinArrow(preparation);
            if (reticle) reticle.color = reticleColour;
        }

        float TailWidth => 44f / 128f * spinArrowHeight;
        float HeadWidth => 115f / 128f * spinArrowHeight;
        float Overlap => .25f * spinArrowHeight / 8f;

        /// <summary>
        /// The spin as an arrow from the aim point: one root turns to the spin's direction, the drawn tail and head keep
        /// their size and only the shaft between them stretches with the spin's strength.
        /// </summary>
        void UpdateSpinArrow(ThrowPreparation preparation)
        {
            if (!spinPointer) return;
            var spin = preparation.Active ? new Vector2(preparation.SpinX, preparation.SpinY) : Vector2.zero;
            float amount = Mathf.Clamp01(spin.magnitude);
            bool show = amount > .02f;
            if (spinPointer.gameObject.activeSelf != show) spinPointer.gameObject.SetActive(show);
            float h = spinArrowHeight, tail = TailWidth, head = HeadWidth, o = Overlap;
            if (show)
            {
                spinPointer.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(spin.y, spin.x) * Mathf.Rad2Deg);
                float shaft = Mathf.Lerp(spinShaftRange.x, spinShaftRange.y, amount);
                Place(spinTail, spinArrowGap, tail, h);
                Place(spinShaft, spinArrowGap + tail - o, shaft, h);
                Place(spinHead, spinArrowGap + tail + shaft - 2 * o, head, h);
                Color colour = spinColour;
                colour.a = preparation.Steering ? 1 : .55f + .45f * amount;
                spinTail.color = spinShaft.color = spinHead.color = colour;
            }
            if (spinLimit)
            {
                bool steering = preparation.Active && preparation.Steering;
                spinLimit.enabled = steering;
                if (steering)
                {
                    // Reaches the tip of the head at full strength.
                    float diameter = 2 * (spinArrowGap + tail + spinShaftRange.y + head - 2 * o);
                    spinLimit.rectTransform.sizeDelta = new Vector2(diameter, diameter);
                    spinLimit.color = new Color(1, 1, 1, .18f);
                }
            }
        }

        static void Place(Image part, float x, float width, float height)
        {
            var r = part.rectTransform;
            r.anchoredPosition = new Vector2(x, 0);
            r.sizeDelta = new Vector2(width, height);
        }

        void UpdateTexts(float dt)
        {
            var trip = world.workshop.Model.Current;
            var card = world.thrower.Selected;
            var preparation = world.thrower.Preparation;

            if (wallet) wallet.text = FormatMoney(world.workshop.Model.Coins);

            // One contextual hint: an action, never data about the item in focus.
            string hint = "";
            if (world.FocusedLoot) hint = "E — подобрать";
            else if (world.FocusedChest) hint = "E — открыть";
            else if (preparation.Active) hint = "Shift + мышь — подкрутка";
            else if (world.CardPrompt != null) hint = world.CardPrompt;
            else if (card != null && card.IsReturn) hint = world.thrower.ReturnReady ? "ЛКМ — домой" : "";
            else if (!hintedDiscard && trip != null && trip.PlayedCount >= 2 && trip.Deck.StockCount > 0)
            {
                // One shared hand: discarding it is how you dig for a card (and, at the end, for the Return card).
                if (discardHintUntil == 0) discardHintUntil = Time.time + 6;
                if (Time.time > discardHintUntil || trip.DiscardedCount > 0) hintedDiscard = true;
                else hint = "R — сбросить руку";
            }
            if (world.InPrologue && !world.FocusedLoot) hint = "";
            if (context) context.text = hint;

            notice.text = Time.time < world.NoticeUntil ? world.Notice : "";
            UpdatePickups();

            // The cave prologue has no deck to count.
            if (cardCounter) cardCounter.text = world.InPrologue || trip == null ? "" : CardCount(trip.Deck);

            bool showLabel = card != null && (Time.time < cardLabelUntil || preparation.Active);
            if (showLabel)
            {
                string name = card.IsReturn ? "Пора домой" : world.workshop.config.Find(card.Spec.Id)?.displayName ?? card.Spec.Name;
                // The level is on the card; show only remaining charges here.
                string uses = card.Spec.UsesAt(card.Level) > 1 ? $"  ×{card.UsesLeft}" : "";
                cardLabel.text = card.IsReturn ? name : $"{name}<alpha=#88>{uses}";
            }
            cardLabel.alpha = Mathf.MoveTowards(cardLabel.alpha, showLabel ? 1 : 0, dt * 5);
        }

        /// <summary>
        /// Pickup rows top right, fading out after a few seconds. Holding I shows every carried find in the same rows
        /// instead (one row per copy, newest first), as if all were just picked up: a view of the bag, not new pickups.
        /// </summary>
        void UpdatePickups()
        {
            if (!pickups) return;
            for (int i = toastLines.Count - 1; i >= 0; i--)
                if (Time.time - toastLines[i].time > pickupSeconds) toastLines.RemoveAt(i);
            rows.Clear();
            shadowRows.Clear();
            string value = ColorUtility.ToHtmlStringRGB(pickupValueColour);
            bool review = Keyboard.current != null && Keyboard.current.iKey.isPressed;
            if (review)
            {
                // Newest first: the last pickup on top.
                var carried = world.Pickups;
                for (int i = carried.Count - 1; i >= 0; i--)
                    Row(carried[i].name, carried[i].value, 255, value, NameColour(world.lootCatalog ? world.lootCatalog.Find(carried[i].id) : null));
            }
            else
                for (int i = toastLines.Count - 1; i >= 0; i--)
                {
                    var line = toastLines[i];
                    float age = Time.time - line.time;
                    int alpha = Mathf.RoundToInt(255 * Mathf.Clamp01((pickupSeconds - age) / Mathf.Max(.01f, pickupFadeSeconds)));
                    Row(line.name, line.value, alpha, value, line.colour);
                }
            pickups.text = rows.ToString();
            if (pickupsShadow) pickupsShadow.text = shadowRows.ToString();
        }

        /// <summary>Hex colour of the item's rarity for its name, or null for common items (plain text colour).</summary>
        string NameColour(LootItem item)
        {
            if (!item || item.rarity == LootRarity.Common || !world.lootCatalog) return null;
            return ColorUtility.ToHtmlStringRGB(world.lootCatalog.Colour(item.rarity));
        }

        void Row(string name, int value, int alpha, string valueColour, string nameColour)
        {
            string a = $"<alpha=#{alpha:X2}>";
            rows.Append(a).Append("+ ");
            if (nameColour != null) rows.Append("<color=#").Append(nameColour).Append('>').Append(name).Append("</color>").Append(a);
            else rows.Append(name);
            rows.Append("<space=6><color=#").Append(valueColour).Append('>').Append(value).Append("</color>\n");
            shadowRows.Append(a).Append("+ ").Append(name).Append("<space=6>").Append(value).Append('\n');
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
