using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Hakaton.Computer
{
    /// <summary>
    /// «Прокачка»: one segmented button per improvement (<see cref="PcUpgradeTrack.For"/>), in a scroll view: three rows
    /// show at once, more scroll with the wheel or the XP scrollbar.
    /// </summary>
    public sealed class PcUpgradesWindow : MonoBehaviour
    {
        public PcComputer computer;
        public PcWindow window;
        public RectTransform rowsRoot;
        public PcUpgradeRow rowPrefab;
        public TMP_Text coins;

        readonly List<PcUpgradeRow> rows = new List<PcUpgradeRow>();
        public IReadOnlyList<PcUpgradeRow> Rows => rows;

        void Awake() => window.Opened += OnOpened;

        void OnOpened()
        {
            Build();
            Refresh();
            window.SetStatus("Нажмите на улучшение, чтобы купить следующий уровень.");
        }

        /// <summary>
        /// Lays out one row per track. The list can grow between visits (the bulk buyer's track appears once he has been
        /// met), so the rows are rebuilt whenever the tracks differ from the ones shown.
        /// </summary>
        void Build()
        {
            var tracks = PcUpgradeTrack.For(computer.workshop.Model, computer.Shop);
            if (SameTracks(tracks)) return;
            foreach (var old in rows)
            {
                old.Pressed -= OnPressed;
                old.gameObject.SetActive(false);
                Destroy(old.gameObject);
            }
            rows.Clear();
            foreach (var track in tracks)
            {
                var row = Instantiate(rowPrefab, rowsRoot);
                row.gameObject.SetActive(true);
                row.name = track.Title;
                row.Bind(track);
                row.Pressed += OnPressed;
                rows.Add(row);
            }
        }

        bool SameTracks(IReadOnlyList<PcUpgradeTrack> tracks)
        {
            if (rows.Count != tracks.Count) return false;
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].Track == null || rows[i].Track.Title != tracks[i].Title) return false;
            return true;
        }

        public void Refresh()
        {
            int balance = computer.workshop.Model.Coins;
            if (coins) coins.text = $"Монеты: {balance}";
            foreach (var row in rows) row.Refresh(balance);
        }

        void OnPressed(PcUpgradeRow row)
        {
            if (!computer.Running) return;
            if (row.Track.Maxed)
            {
                row.Refuse();
                window.SetStatus(row.Track.Title + ": уже максимум.", true);
                computer.Ui(PcComputer.UiSound.Error);
                return;
            }
            string error = row.Track.Buy();
            if (error != null)
            {
                row.Refuse();
                window.SetStatus(error, true);
                computer.Ui(PcComputer.UiSound.Error);
                return;
            }
            Refresh();
            row.Celebrate();
            computer.Play(computer.workshop.config.coinSound);
            window.SetStatus(row.Track.Bought);
        }

        void Update()
        {
            if (!window.IsOpen || !computer.Running || Time.frameCount % 15 != 0) return;
            // A window left open between visits to the PC still picks up a track unlocked meanwhile.
            Build();
            Refresh();
        }
    }
}
