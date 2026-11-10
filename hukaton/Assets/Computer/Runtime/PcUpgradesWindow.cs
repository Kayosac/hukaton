using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Hakaton.Computer
{
    /// <summary>«Прокачка»: one segmented button per improvement (<see cref="PcUpgradeTrack.For"/>).</summary>
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

        void Build()
        {
            if (rows.Count > 0) return;
            foreach (var track in PcUpgradeTrack.For(computer.workshop.Model))
            {
                var row = Instantiate(rowPrefab, rowsRoot);
                row.gameObject.SetActive(true);
                row.name = track.Title;
                row.Bind(track);
                row.Pressed += OnPressed;
                rows.Add(row);
            }
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
            if (window.IsOpen && computer.Running && Time.frameCount % 15 == 0) Refresh();
        }
    }
}
