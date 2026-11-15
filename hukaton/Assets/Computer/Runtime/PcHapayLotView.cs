using System;
using System.Collections;
using System.Collections.Generic;
using Hakaton.Hapay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.Computer
{
    /// <summary>
    /// One lot on хаPAY: the collector (avatar, nick, his line), the wanted items with «есть / нужно», the price, the
    /// clock and «Продать» (green, live only when everything is there). A sale stamps «ПРОДАНО» across it.
    /// </summary>
    public sealed class PcHapayLotView : MonoBehaviour
    {
        public Image avatar;
        public TMP_Text nick, quote, price, timer;
        public Button sell;
        public PcHapayItemCell[] cells = Array.Empty<PcHapayItemCell>();
        [Tooltip("«ПРОДАНО», hidden until a sale.")]
        public RectTransform stamp;
        public CanvasGroup group;
        [Tooltip("The clock turns this colour and blinks over the last minute.")]
        public Color timerColour = Color.black, hurryColour = new Color(.8f, .05f, .05f, 1);
        [Min(1)] public float hurrySeconds = 60;
        [Min(.05f)] public float stampSeconds = .9f;

        public event Action<PcHapayLotView> SellClicked;
        public HapayLot Lot { get; private set; }
        public bool Stamping { get; private set; }
        public bool CanSell => sell && sell.interactable;
        /// <summary>The PC's item pictures (the same 32 px icons as «Склад»).</summary>
        public PcStockIcons Icons { get; private set; }

        void Awake()
        {
            if (sell) sell.onClick.AddListener(() => SellClicked?.Invoke(this));
            if (stamp) stamp.gameObject.SetActive(false);
        }

        public void SetTooltip(PcTooltip tooltip)
        {
            foreach (var cell in cells) if (cell) cell.Tooltip = tooltip;
        }

        /// <summary>Shows a lot (null: an empty place).</summary>
        public void Bind(HapayLot lot, HapayHost host, PcStockIcons icons = null)
        {
            Lot = lot;
            Icons = icons;
            if (stamp && !Stamping) stamp.gameObject.SetActive(false);
            if (group) group.alpha = lot == null ? 0 : 1;
            if (lot == null || host == null) { foreach (var cell in cells) if (cell) cell.Hide(); return; }
            var collection = lot.Collection;
            if (avatar) avatar.sprite = host.pool ? host.pool.Avatar(collection.avatar) : null;
            if (nick) nick.text = collection.nick;
            if (quote) quote.text = "«" + collection.quote + "»";
            Live(host, Icons);
        }

        /// <summary>Clock, counts and the button, every frame while the site is open.</summary>
        public void Live(HapayHost host, PcStockIcons icons = null)
        {
            if (Lot == null || host == null) return;
            var wants = HapayMarket.Merge(Lot.Wants);
            bool complete = true;
            for (int i = 0; i < cells.Length; i++)
            {
                var cell = cells[i];
                if (!cell) continue;
                if (i >= wants.Count) { cell.Hide(); continue; }
                var want = wants[i];
                int stored = host.Stock != null ? host.Stock.Stored(want.item) : 0;
                int shown = host.Stock != null ? host.Stock.Displayed(want.item) : 0;
                bool fromDisplay = stored < want.count && shown > 0;
                cell.Bind(IconOf(want.item, host, icons ? icons : Icons), host.NameOf(want.item), stored + shown, want.count, fromDisplay);
                if (stored + shown < want.count) complete = false;
            }
            if (price) price.text = host.PriceOf(Lot) + " монет";
            if (timer)
            {
                float left = Mathf.Max(0, Lot.SecondsLeft);
                int s = Mathf.CeilToInt(left);
                timer.text = $"{s / 60:00}:{s % 60:00}";
                bool hurry = left <= hurrySeconds;
                timer.color = hurry ? hurryColour : timerColour;
                timer.alpha = hurry && Mathf.Repeat(Time.unscaledTime, .8f) > .55f ? .25f : 1;
            }
            if (sell) sell.interactable = complete && !Stamping;
        }

        static Sprite IconOf(string item, HapayHost host, PcStockIcons icons)
        {
            var baked = icons ? icons.Baked(item) : null;
            return baked ? baked : host.IconOf(item);
        }

        /// <summary>«ПРОДАНО» slams onto the lot; <paramref name="done"/> when it has been seen.</summary>
        public void Stamp(Action done)
        {
            if (!stamp) { done?.Invoke(); return; }
            StartCoroutine(StampRoutine(done));
        }

        IEnumerator StampRoutine(Action done)
        {
            Stamping = true;
            if (sell) sell.interactable = false;
            stamp.gameObject.SetActive(true);
            var baseRotation = stamp.localEulerAngles;
            // Falls in big, hits, a little bounce (whole-pixel shake of the lot).
            var self = (RectTransform)transform;
            var home = self.anchoredPosition;
            for (float t = 0; t < .12f; t += Time.unscaledDeltaTime)
            {
                stamp.localScale = Vector3.one * Mathf.Lerp(2.4f, 1, t / .12f);
                yield return null;
            }
            stamp.localScale = Vector3.one;
            for (float t = 0; t < .18f; t += Time.unscaledDeltaTime)
            {
                float k = 1 - t / .18f;
                self.anchoredPosition = home + new Vector2(Mathf.Round(UnityEngine.Random.Range(-2f, 2f) * k), Mathf.Round(UnityEngine.Random.Range(-2f, 2f) * k));
                yield return null;
            }
            self.anchoredPosition = home;
            for (float t = 0; t < stampSeconds; t += Time.unscaledDeltaTime) yield return null;
            stamp.localEulerAngles = baseRotation;
            Stamping = false;
            done?.Invoke();
        }
    }
}
