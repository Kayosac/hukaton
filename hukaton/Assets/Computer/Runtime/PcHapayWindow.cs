using System.Collections.Generic;
using Hakaton.Feel;
using Hakaton.Hapay;
using TMPro;
using UnityEngine;

namespace Hakaton.Computer
{
    /// <summary>
    /// хаPAY on the PC (board «Спецпредметы»): a «Гоблин Эксплорер» window with an early-2000s auction site — the logo,
    /// a running line, a visit counter, «Вы вошли как Delmon» with his old stars, and the collectors' lots (<see cref="lots"/>).
    /// A full lot sells at once: «ПРОДАНО», coins into the tray, a till, confetti inside the picture. While the window is
    /// open the site's own music replaces the PC's.
    /// </summary>
    public sealed class PcHapayWindow : MonoBehaviour
    {
        public PcComputer computer;
        public PcWindow window;
        public PcHapayLotView[] lots = new PcHapayLotView[0];
        public PcTooltip tooltip;
        [Tooltip("Item pictures: the same baked 32 px icons as «Склад» (one per catalogue item).")]
        public PcStockIcons icons;

        [Header("Page")]
        [Tooltip("The running line; it scrolls inside its parent (a mask).")]
        public RectTransform marquee;
        [Min(1)] public float marqueeSpeed = 36;
        public TMP_Text counter;
        [Min(0)] public int counterStart = 66604;
        [Tooltip("«Вы вошли как Delmon …»: his stars and how many sets went through the account.")]
        public TMP_Text seller;
        [Tooltip("Little «gifs» that blink (NEW!, the under-construction sign...).")]
        public GameObject[] blinkers = new GameObject[0];
        [Min(.05f)] public float blinkSeconds = .5f;
        [Tooltip("Shown when no lot is on the site (should not happen).")]
        public GameObject empty;

        [Header("Sale")]
        public SfxCue sellCue, stampCue;
        [Tooltip("Retro Arsenal bursts inside the picture on a sale (PC variants in Computer/Fx).")]
        public GameObject[] sellBursts = new GameObject[0];
        [Tooltip("Under the PC camera, like the merge show's.")]
        public Transform fxRoot;
        public float burstScale = .6f;

        [Header("Music")]
        [Tooltip("The site's own loop (2000s site MIDI, «заброшенный сайт»): it replaces the PC's music while the window is open.")]
        public AudioSource music;
        [Range(0, 1)] public float musicVolume = .2f;
        [Min(.01f)] public float musicFadeSeconds = .6f;

        public HapayHost Host => computer ? computer.Hapay : null;

        readonly List<PcVfx.Live> live = new List<PcVfx.Live>();
        readonly HapayLot[] shown = new HapayLot[8];
        Vector2 marqueeHome;
        float marqueeX;
        bool selling;

        void Awake()
        {
            if (window) window.Opened += Refresh;
            foreach (var view in lots)
            {
                if (!view) continue;
                view.SellClicked += Sell;
                view.SetTooltip(tooltip);
            }
            if (marquee) marqueeHome = marquee.anchoredPosition;
            if (music) { music.volume = 0; music.loop = true; }
        }

        /// <summary>The site is the player's (the account is bought).</summary>
        public bool Available => Host && Host.Open;

        public void Refresh()
        {
            var host = Host;
            if (!host) return;
            host.TryFirstLot();
            host.Market.Fill();
            var market = host.Market;
            for (int i = 0; i < lots.Length; i++)
            {
                var lot = i < market.Lots.Count ? market.Lots[i] : null;
                if (i < shown.Length) shown[i] = lot;
                if (lots[i] && !lots[i].Stamping) lots[i].Bind(lot, host, icons);
            }
            if (empty) empty.SetActive(market.Lots.Count == 0);
            if (seller)
            {
                int sold = market.Sold;
                // Delmon's old account: four stars; his feedback stalls, the player's sales add to it.
                seller.text = $"Вы вошли как <b>Delmon</b>  <color=#d9a400>****</color><color=#9a9a9a>*</color>  ({172 + sold} отзыва)";
            }
            UpdateStatus();
        }

        void UpdateStatus()
        {
            var host = Host;
            if (!host || !window) return;
            int ready = 0;
            foreach (var lot in host.Market.Lots) if (host.Complete(lot)) ready++;
            window.SetStatus(ready > 0 ? $"Можно продать: {ready}" : "Собери набор целиком — коллекционер заберёт всё разом.");
        }

        void Update()
        {
            FadeMusic();
            AgeFx();
            if (!window || !window.IsOpen) return;
            var host = Host;
            if (!host) return;
            // A lot left (its time is up) or a new one came: show the site as it is now.
            var market = host.Market;
            bool changed = market.Lots.Count != CountShown();
            for (int i = 0; !changed && i < market.Lots.Count && i < shown.Length; i++) changed = shown[i] != market.Lots[i];
            if (changed && !selling) Refresh();
            foreach (var view in lots) if (view && view.Lot != null) view.Live(host, icons);
            if (Time.frameCount % 15 == 0) UpdateStatus();
            ScrollMarquee();
            Blink();
            if (counter) counter.text = (counterStart + (int)(Time.realtimeSinceStartup / 7)).ToString("000000");
        }

        int CountShown()
        {
            int n = 0;
            foreach (var lot in shown) if (lot != null) n++;
            return n;
        }

        void ScrollMarquee()
        {
            if (!marquee) return;
            var parent = marquee.parent as RectTransform;
            float width = parent ? parent.rect.width : 600;
            marqueeX -= marqueeSpeed * Time.unscaledDeltaTime;
            if (marqueeX < -marquee.rect.width) marqueeX = width;
            // Whole pixels only: the PC picture is pixel-exact.
            marquee.anchoredPosition = new Vector2(Mathf.Round(marqueeHome.x + marqueeX), marqueeHome.y);
        }

        void Blink()
        {
            bool on = Mathf.Repeat(Time.unscaledTime, blinkSeconds * 2) < blinkSeconds;
            foreach (var blinker in blinkers) if (blinker) blinker.SetActive(on);
        }

        // ---------- the sale ----------

        public void Sell(PcHapayLotView view)
        {
            var host = Host;
            if (!host || !view || view.Lot == null || selling) return;
            int price = host.PriceOf(view.Lot);
            if (!host.Sell(view.Lot))
            {
                computer.Ui(PcComputer.UiSound.Error);
                window.SetStatus("Не хватает товара для этого набора.", true);
                return;
            }
            selling = true;
            if (computer.sfx) { computer.sfx.Play(stampCue); computer.sfx.Play(sellCue); }
            window.SetStatus($"Продано! +{price} монет.");
            Burst((RectTransform)view.transform);
            view.Stamp(() => { selling = false; Refresh(); });
        }

        /// <summary>For the guide and tests: the first lot that can be sold now (null if none).</summary>
        public PcHapayLotView FirstSellable()
        {
            foreach (var view in lots) if (view && view.Lot != null && view.CanSell) return view;
            return null;
        }

        void Burst(RectTransform at)
        {
            if (!fxRoot) return;
            float unitsPerPixel = Mathf.Abs(at.lossyScale.x) > 1e-6f ? Mathf.Abs(at.lossyScale.x) : .01f;
            float scale = burstScale * unitsPerPixel * 100 / Mathf.Max(1e-4f, Mathf.Abs(fxRoot.lossyScale.x));
            foreach (var prefab in sellBursts)
            {
                var effect = PcVfx.Spawn(prefab, fxRoot, at.TransformPoint(at.rect.center), Quaternion.identity, scale,
                    Color.white, 0, 1, 0, Time.unscaledTime);
                if (effect != null) live.Add(effect);
            }
        }

        void AgeFx()
        {
            float now = Time.unscaledTime;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var effect = live[i];
                if (!effect.root) { live.RemoveAt(i); continue; }
                if (now >= effect.stopAt) effect.StopEmitting();
                if (now >= effect.dieAt) { Destroy(effect.root); live.RemoveAt(i); }
            }
        }

        // ---------- music ----------

        /// <summary>The site plays while its window is open and the PC is running; the PC's music steps aside meanwhile.</summary>
        public bool MusicWanted => window && window.IsOpen && computer && computer.Running && !computer.Hushed && Available;

        void FadeMusic()
        {
            if (!music || !music.clip) return;
            bool wanted = MusicWanted;
            if (computer) computer.MusicOverridden = wanted;
            float target = wanted ? musicVolume : 0;
            if (wanted && !music.isPlaying) { music.volume = 0; music.Play(); }
            music.volume = Mathf.MoveTowards(music.volume, target, musicVolume / musicFadeSeconds * Time.unscaledDeltaTime);
            if (!wanted && music.isPlaying && music.volume <= 0) music.Pause();
        }

        void OnDisable()
        {
            if (music) { music.volume = 0; music.Pause(); }
            foreach (var effect in live) if (effect.root) Destroy(effect.root);
            live.Clear();
        }
    }
}
