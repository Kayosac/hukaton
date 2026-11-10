using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.Computer
{
    /// <summary>
    /// One improvement as a big XP button: its name, the value change, the price and a segmented XP progress bar.
    /// A press buys one more segment.
    /// </summary>
    public sealed class PcUpgradeRow : MonoBehaviour
    {
        public Button button;
        public RectTransform shakeRoot;
        public TMP_Text title, values, price;
        public GameObject priceGroup;
        [Tooltip("The segments' container inside the XP progress frame.")]
        public RectTransform segments;
        public Image segmentPrefab;
        public Sprite filled, empty;

        public PcUpgradeTrack Track { get; private set; }
        public event Action<PcUpgradeRow> Pressed;

        readonly List<Image> cells = new List<Image>();
        float shakeTime = -1, popTime = -1;
        int popIndex = -1;

        void Awake() => button.onClick.AddListener(() => Pressed?.Invoke(this));

        public void Bind(PcUpgradeTrack track)
        {
            Track = track;
            while (cells.Count < track.Steps)
            {
                var cell = Instantiate(segmentPrefab, segments);
                cell.gameObject.SetActive(true);
                cell.name = "Segment " + (cells.Count + 1);
                cells.Add(cell);
            }
            for (int i = 0; i < cells.Count; i++) cells[i].gameObject.SetActive(i < track.Steps);
            Refresh(0);
        }

        public void Refresh(int coins)
        {
            if (Track == null) return;
            title.text = Track.Title;
            values.text = Track.Values;
            bool maxed = Track.Maxed;
            if (priceGroup) priceGroup.SetActive(!maxed);
            price.text = maxed ? "" : Track.Price.ToString();
            price.color = coins >= Track.Price ? new Color(.1f, .1f, .1f, 1) : new Color(.75f, .1f, .05f, 1);
            if (maxed) values.text = Track.Values + "  ·  МАКС";
            for (int i = 0; i < Track.Steps && i < cells.Count; i++) cells[i].sprite = i < Track.Step ? filled : empty;
        }

        /// <summary>The new segment lights up.</summary>
        public void Celebrate()
        {
            popIndex = Track.Step - 1;
            popTime = 0;
        }

        public void Refuse() => shakeTime = 0;

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (shakeTime >= 0 && shakeRoot)
            {
                shakeTime += dt;
                float x = Mathf.Sin(shakeTime * 70) * 4 * Mathf.Max(0, 1 - shakeTime / .35f);
                shakeRoot.anchoredPosition = new Vector2(Mathf.Round(x), 0);
                if (shakeTime > .35f) { shakeTime = -1; shakeRoot.anchoredPosition = Vector2.zero; }
            }
            if (popTime >= 0 && popIndex >= 0 && popIndex < cells.Count)
            {
                popTime += dt;
                float k = Mathf.Max(0, 1 - popTime / .45f);
                // Blink brighter while it settles in.
                cells[popIndex].color = Color.Lerp(Color.white, new Color(1.6f, 1.6f, 1.6f, 1), k * (Mathf.Sin(popTime * 40) > 0 ? 1 : .4f));
                if (popTime > .45f) { cells[popIndex].color = Color.white; popTime = -1; }
            }
        }
    }
}
