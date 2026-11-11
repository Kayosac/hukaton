using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Hakaton.Computer
{
    /// <summary>Switches the authored pages of the single tutorial application.</summary>
    public sealed class PcTutorialWindow : MonoBehaviour
    {
        [Serializable]
        public sealed class Tab
        {
            public string title;
            public Button button;
            public GameObject page;
            public Image tabImage;
        }

        public PcWindow window;
        public PcComputer computer;
        public Tab[] tabs = Array.Empty<Tab>();

        [Header("Tab appearance")]
        public Sprite activeTab, inactiveTab;
        public Color activeTint = new Color(.76f, .86f, 1f, 1f);
        public Color inactiveTint = Color.white;

        [SerializeField, Min(0)] int selectedTab;
        public int SelectedTab => selectedTab;

        readonly List<(Button button, UnityAction listener)> listeners = new List<(Button, UnityAction)>();
        bool initialized;

        void Awake()
        {
            if (initialized) return;
            initialized = true;
            if (window) window.Opened += Refresh;
            if (tabs == null) return;

            for (int i = 0; i < tabs.Length; i++)
            {
                int index = i;
                var button = tabs[i]?.button;
                if (!button) continue;
                UnityAction listener = () => SelectTab(index);
                button.onClick.AddListener(listener);
                listeners.Add((button, listener));
            }

            if (!Valid(selectedTab))
            {
                for (int i = 0; i < tabs.Length; i++)
                    if (Valid(i)) { selectedTab = i; break; }
            }
            Refresh();
        }

        /// <summary>Changes the visible page without opening, closing or focusing another window.</summary>
        public void SelectTab(int index)
        {
            if (!Valid(index)) return;
            bool changed = selectedTab != index;
            selectedTab = index;
            Refresh();
            if (changed && computer && computer.Running) computer.Ui(PcComputer.UiSound.Click);
        }

        bool Valid(int index) => tabs != null && index >= 0 && index < tabs.Length
            && tabs[index] != null && tabs[index].page;

        void Refresh()
        {
            if (!Valid(selectedTab)) return;
            for (int i = 0; i < tabs.Length; i++)
            {
                var tab = tabs[i];
                if (tab == null) continue;
                bool selected = i == selectedTab;
                if (tab.page) tab.page.SetActive(selected);
                if (tab.tabImage)
                {
                    var sprite = selected ? activeTab : inactiveTab;
                    if (sprite) tab.tabImage.sprite = sprite;
                }
                if (tab.button)
                {
                    var colours = tab.button.colors;
                    colours.normalColor = selected ? activeTint : inactiveTint;
                    colours.highlightedColor = Color.Lerp(colours.normalColor, Color.white, .35f);
                    colours.pressedColor = colours.normalColor * new Color(.85f, .85f, .85f, 1f);
                    // Keyboard focus must not make an inactive tab look like the selected page.
                    colours.selectedColor = colours.normalColor;
                    tab.button.colors = colours;
                }
            }
            if (window) window.SetStatus($"Раздел {selectedTab + 1}/{tabs.Length} — {tabs[selectedTab].title}");
        }

        void OnDestroy()
        {
            if (window) window.Opened -= Refresh;
            foreach (var binding in listeners)
                if (binding.button) binding.button.onClick.RemoveListener(binding.listener);
            listeners.Clear();
            initialized = false;
        }
    }
}
