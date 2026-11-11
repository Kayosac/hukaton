using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Hakaton.Computer.Tests
{
    public sealed class PcTutorialWindowTests
    {
        GameObject root;
        PcWindow window;
        PcTutorialWindow tutorial;
        Texture2D texture;
        Sprite active, inactive;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Tutorial fixture");
            root.SetActive(false);
            var host = Child("tutorial", typeof(RectTransform), typeof(CanvasGroup));
            window = host.AddComponent<PcWindow>();
            window.group = host.GetComponent<CanvasGroup>();
            tutorial = host.AddComponent<PcTutorialWindow>();
            tutorial.window = window;
            tutorial.tabs = new PcTutorialWindow.Tab[6];
            texture = new Texture2D(2, 2);
            active = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero);
            inactive = Sprite.Create(texture, new Rect(1, 1, 1, 1), Vector2.zero);
            tutorial.activeTab = active;
            tutorial.inactiveTab = inactive;
            for (int i = 0; i < tutorial.tabs.Length; i++)
            {
                var buttonObject = Child("Tab " + i, typeof(RectTransform), typeof(Image), typeof(Button));
                var image = buttonObject.GetComponent<Image>();
                var button = buttonObject.GetComponent<Button>();
                button.targetGraphic = image;
                tutorial.tabs[i] = new PcTutorialWindow.Tab
                {
                    title = "Раздел " + i,
                    button = button,
                    tabImage = image,
                    page = Child("Page " + i, typeof(RectTransform))
                };
            }
        }

        GameObject Child(string name, params System.Type[] components)
        {
            var go = new GameObject(name, components);
            go.transform.SetParent(root.transform, false);
            return go;
        }

        static void Lifecycle(PcTutorialWindow component, string name) => typeof(PcTutorialWindow)
            .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);

        void StartAt(int index = 0)
        {
            typeof(PcTutorialWindow).GetField("selectedTab", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(tutorial, index);
            Lifecycle(tutorial, "Awake");
        }

        void AssertOnlyPage(int index)
        {
            Assert.That(tutorial.SelectedTab, Is.EqualTo(index));
            for (int i = 0; i < tutorial.tabs.Length; i++)
                Assert.That(tutorial.tabs[i].page.activeSelf, Is.EqualTo(i == index), "Page " + i);
        }

        [TearDown]
        public void TearDown()
        {
            if (tutorial) Lifecycle(tutorial, "OnDestroy");
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(active);
            Object.DestroyImmediate(inactive);
            Object.DestroyImmediate(texture);
        }

        [Test]
        public void InitialSelectionShowsExactlyOneAuthoredPage()
        {
            StartAt(2);
            AssertOnlyPage(2);
            Assert.That(window.IsOpen, Is.False);
        }

        [Test]
        public void ClickingTabsSwitchesOnlyTheirPagesAndPreservesTheWindow()
        {
            StartAt();
            window.SetOpen(true);
            window.SetFocused(true);
            int sibling = window.transform.GetSiblingIndex();
            int childCount = root.transform.childCount;
            tutorial.tabs[4].button.onClick.Invoke();
            AssertOnlyPage(4);
            Assert.That(window.IsOpen, Is.True);
            Assert.That(window.Focused, Is.True);
            Assert.That(window.transform.GetSiblingIndex(), Is.EqualTo(sibling));
            Assert.That(root.transform.childCount, Is.EqualTo(childCount), "No runtime UI is created");
            tutorial.tabs[0].button.onClick.Invoke();
            AssertOnlyPage(0);
        }

        [Test]
        public void ClosingAndReopeningKeepsTheSelectedTab()
        {
            StartAt();
            window.SetOpen(true);
            tutorial.SelectTab(3);
            window.SetOpen(false);
            AssertOnlyPage(3);
            window.SetOpen(true);
            AssertOnlyPage(3);
        }

        [TestCase(-1)]
        [TestCase(6)]
        [TestCase(int.MaxValue)]
        public void InvalidSelectionLeavesTheCurrentPageAlone(int invalid)
        {
            StartAt(1);
            Assert.DoesNotThrow(() => tutorial.SelectTab(invalid));
            AssertOnlyPage(1);
        }

        [Test]
        public void SelectionAppearanceMovesWithThePageWithoutDisablingTheButtons()
        {
            StartAt();
            tutorial.SelectTab(5);
            for (int i = 0; i < tutorial.tabs.Length; i++)
            {
                var tab = tutorial.tabs[i];
                Color expected = i == 5 ? tutorial.activeTint : tutorial.inactiveTint;
                Assert.That(tab.tabImage.sprite, Is.SameAs(i == 5 ? active : inactive));
                Assert.That(tab.button.colors.normalColor, Is.EqualTo(expected));
                Assert.That(tab.button.colors.selectedColor, Is.EqualTo(expected));
                Assert.That(tab.button.interactable, Is.True);
            }
        }

        [Test]
        public void SelectingTheSameTabTwiceDoesNotOpenOrFocusAWindow()
        {
            StartAt(2);
            tutorial.SelectTab(2);
            tutorial.SelectTab(2);
            AssertOnlyPage(2);
            Assert.That(window.IsOpen, Is.False);
            Assert.That(window.Focused, Is.False);
        }

        [Test]
        public void DestroyDetachesButtonAndWindowListeners()
        {
            StartAt(2);
            Lifecycle(tutorial, "OnDestroy");
            tutorial.tabs[0].button.onClick.Invoke();
            AssertOnlyPage(2);
            tutorial.tabs[2].page.SetActive(false);
            window.SetOpen(true);
            Assert.That(tutorial.tabs[2].page.activeSelf, Is.False, "Opened must no longer call the removed listener");
        }

        [Test]
        public void RepeatedInitializationDoesNotLeaveDuplicateWindowListeners()
        {
            StartAt(1);
            Lifecycle(tutorial, "Awake");
            Lifecycle(tutorial, "OnDestroy");
            tutorial.tabs[1].page.SetActive(false);
            window.SetOpen(true);
            Assert.That(tutorial.tabs[1].page.activeSelf, Is.False);
            tutorial.tabs[4].button.onClick.Invoke();
            Assert.That(tutorial.SelectedTab, Is.EqualTo(1));
        }
    }

    public sealed class PcTutorialPrefabTests
    {
        const string TutorialPath = "Assets/Computer/Prefabs/PcTutorialWindow.prefab";
        const string PcPath = "Assets/Computer/Prefabs/BackroomPC.prefab";
        const string FontsPath = "Assets/Content/UI/Typography/Resources/Fonts/";

        static void Inspect(string path, Action<GameObject> inspect)
        {
            var prefab = PrefabUtility.LoadPrefabContents(path);
            try { inspect(prefab); }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
        }

        static PcTutorialWindow App(GameObject prefab)
        {
            var apps = prefab.GetComponentsInChildren<PcTutorialWindow>(true);
            Assert.That(apps, Has.Length.EqualTo(1), "One tutorial application must be authored in the prefab");
            return apps[0];
        }

        [Test]
        public void DesktopRegistersOneTutorialWithAllThreeLaunchersAndItsIcon()
        {
            Inspect(PcPath, prefab =>
            {
                var desktop = prefab.GetComponentInChildren<PcDesktop>(true);
                Assert.That(desktop, Is.Not.Null);
                var programs = desktop.programs.Where(p => p != null && p.window
                    && string.Equals(p.window.title, "tutorial", StringComparison.OrdinalIgnoreCase)).ToArray();
                Assert.That(programs, Has.Length.EqualTo(1));
                var program = programs[0];
                Assert.That(program.window.title, Is.EqualTo("tutorial"));
                Assert.That(program.window.icon, Is.Not.Null);
                Assert.That(program.window, Is.SameAs(App(prefab).window));
                foreach (var launcher in new[] { program.icon, program.menuEntry, program.taskButton })
                {
                    Assert.That(launcher, Is.Not.Null, "Desktop, Start menu and taskbar must all be wired");
                    Assert.That(launcher.GetComponentsInChildren<Image>(true)
                        .Any(i => i.sprite == program.window.icon), Is.True, "Each launcher must show the tutorial icon");
                }
            });
        }

        [Test]
        public void TutorialIsANestedReusablePrefabWithSixDistinctAuthoredTabs()
        {
            Inspect(PcPath, prefab =>
            {
                var app = App(prefab);
                Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(app.gameObject), Is.EqualTo(TutorialPath));
                Assert.That(app.window, Is.Not.Null);
                Assert.That(app.window.group, Is.Not.Null);
                Assert.That(app.window.closeButton, Is.Not.Null);
                Assert.That(app.tabs, Has.Length.EqualTo(6));
                foreach (var tab in app.tabs)
                {
                    Assert.That(tab, Is.Not.Null);
                    Assert.That(tab.page, Is.Not.Null);
                    Assert.That(tab.button, Is.Not.Null);
                    Assert.That(tab.tabImage, Is.Not.Null);
                    Assert.That(tab.page.transform.IsChildOf(app.transform), Is.True);
                    Assert.That(tab.button.transform.IsChildOf(app.transform), Is.True);
                }
                Assert.That(app.tabs.Select(t => t.page).Distinct().Count(), Is.EqualTo(app.tabs.Length));
                Assert.That(app.tabs.Select(t => t.button).Distinct().Count(), Is.EqualTo(app.tabs.Length));
                Assert.That(app.tabs.Count(t => t.page.activeSelf), Is.EqualTo(1));
                Assert.That(app.tabs[app.SelectedTab].page.activeSelf, Is.True);
            });
        }

        [Test]
        public void EveryPageUsesEditableTextAndSeparateIllustrations()
        {
            Inspect(TutorialPath, prefab =>
            {
                var app = App(prefab);
                var bodyFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontsPath + "PixeloidSans Pixel.asset");
                Assert.That(bodyFont, Is.Not.Null);
                foreach (var tab in app.tabs)
                {
                    var labels = tab.page.GetComponentsInChildren<TMP_Text>(true);
                    Assert.That(labels.Any(t => t.font == bodyFont && !string.IsNullOrWhiteSpace(t.text)), Is.True,
                        tab.title + ": instructions must remain native editable text");
                    var illustrations = tab.page.GetComponentsInChildren<Image>(true)
                        .Where(i => i.sprite && i.sprite.rect.width > 16 && i.sprite.rect.height > 16)
                        .Select(i => (rect: i.rectTransform, source: (Object)i.sprite))
                        .Concat(tab.page.GetComponentsInChildren<RawImage>(true)
                            .Where(i => i.texture && i.texture.width > 16 && i.texture.height > 16)
                            .Select(i => (rect: i.rectTransform, source: (Object)i.texture))).ToArray();
                    Assert.That(illustrations.Length, Is.GreaterThanOrEqualTo(2),
                        tab.title + ": requires separate illustrations alongside editable text");
                    var pageRect = tab.page.GetComponent<RectTransform>().rect;
                    foreach (var illustration in illustrations)
                    {
                        var rect = illustration.rect.rect;
                        Assert.That(rect.width < pageRect.width * .8f || rect.height < pageRect.height * .8f, Is.True,
                            tab.title + ": an illustration must not replace the whole authored page");
                        string path = AssetDatabase.GetAssetPath(illustration.source).Replace('\\', '/').ToLowerInvariant();
                        Assert.That(path, Is.Not.Empty, illustration.rect.name + ": illustration must use a saved source asset");
                        Assert.That(path.Contains("player-guide") && path.Contains("/pages/"), Is.False,
                            tab.title + ": a captured guide page is not an individual illustration");
                    }
                }
            });
        }

        [Test]
        public void TutorialTypographyUsesSavedPixelFontsAtNativeSizesWithoutScaling()
        {
            Inspect(TutorialPath, prefab =>
            {
                var app = App(prefab);
                var heading = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontsPath + "Ithaca Pixel.asset");
                var body = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontsPath + "PixeloidSans Pixel.asset");
                Assert.That(heading, Is.Not.Null);
                Assert.That(body, Is.Not.Null);
                var labels = app.GetComponentsInChildren<TMP_Text>(true);
                Assert.That(labels, Is.Not.Empty);
                foreach (var label in labels)
                {
                    Assert.That(label.font == heading || label.font == body, Is.True, label.name);
                    Assert.That(label.enableAutoSizing, Is.False, label.name);
                    if (label.font == body) Assert.That(label.fontSize, Is.EqualTo(9), label.name);
                    else Assert.That(label.fontSize, Is.EqualTo(16).Or.EqualTo(32), label.name);
                    for (var transform = label.transform; transform && transform != app.transform.parent; transform = transform.parent)
                        Assert.That(transform.localScale, Is.EqualTo(Vector3.one), transform.name);
                }
            });
        }

        [Test]
        public void AllOpenProgramsFitTheNativeTaskbar()
        {
            Inspect(PcPath, prefab =>
            {
                var pc = prefab.GetComponentInChildren<PcComputer>(true);
                Assert.That(pc, Is.Not.Null);
                Assert.That(pc.screen.content, Is.Not.Null);
                var buttons = pc.desktop.programs.Select(p => p.taskButton).ToArray();
                Assert.That(buttons, Has.All.Not.Null);
                // Closed windows correctly hide their task buttons in the saved prefab.
                // Measure the all-open state; LayoutUtility ignores inactive LayoutElements.
                foreach (var button in buttons) button.gameObject.SetActive(true);
                var area = buttons[0].transform.parent as RectTransform;
                Assert.That(area, Is.Not.Null);
                Assert.That(buttons.All(b => b.transform.parent == area), Is.True);
                var layout = area.GetComponent<HorizontalLayoutGroup>();
                Assert.That(layout, Is.Not.Null);
                var canvas = area.GetComponentInParent<Canvas>(true);
                Assert.That(canvas, Is.Not.Null);
                float available = NativeWidth(area, canvas.transform as RectTransform, pc.screen.content.width);
                float required = layout.padding.horizontal + layout.spacing * Mathf.Max(0, buttons.Length - 1);
                foreach (var button in buttons)
                {
                    float width = LayoutUtility.GetPreferredWidth(button.transform as RectTransform);
                    Assert.That(width, Is.GreaterThan(0), button.name);
                    required += width;
                }
                Assert.That(required, Is.LessThanOrEqualTo(available + .01f),
                    "Opening tutorial alongside Cards and Upgrades must not push a task button into the tray");
            });
        }

        static float NativeWidth(RectTransform rect, RectTransform canvas, float width)
        {
            if (rect == canvas) return width;
            var parent = rect.parent as RectTransform;
            Assert.That(parent, Is.Not.Null, "Taskbar must be under the PC canvas");
            return (rect.anchorMax.x - rect.anchorMin.x) * NativeWidth(parent, canvas, width) + rect.sizeDelta.x;
        }
    }
}
