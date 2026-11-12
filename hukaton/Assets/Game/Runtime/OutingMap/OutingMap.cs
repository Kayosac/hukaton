using System;
using System.Collections.Generic;
using System.Linq;
using Hakaton.Character;
using Hakaton.Expeditions;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Hakaton.Game
{
    public enum OutingMapUnlock
    {
        [Tooltip("On the map from the start.")] Always,
        [Tooltip("Appears once the shop has made its revenue goal.")] RevenueGoal
    }

    /// <summary>
    /// The map at the door. Shows the opened outing points on the charcoal parchment; a new point grows a short step
    /// away from the one it comes from, joined to it by a dashed route, and burns and blinks until clicked. Clicking a
    /// point opens its card: name, a small view, «Идти» / «Остаться». Escape or the right button steps back.
    /// </summary>
    public sealed class OutingMap : MonoBehaviour
    {
        [Serializable]
        public sealed class Site
        {
            [Tooltip("Stable save key. Never rename after release.")] public string id;
            public OutingPlace place;
            public Sprite icon;
            public Sprite preview;
            [Tooltip("The point this one grows from (its route starts there). Empty: a root point at Home.")]
            public string parentId;
            public OutingMapUnlock unlock;
            [Tooltip("Root points only: where on the field (0..1, from bottom left) the point is drawn.")]
            public Vector2 home = new Vector2(.2f, .3f);
        }

        [Header("Authored parts")]
        public CanvasGroup visibility;
        [Tooltip("The parchment at its native size; scaled uniformly to fit the screen.")]
        public RectTransform sheet;
        [Tooltip("The drawable part of the parchment: points are placed in it, 0..1 both ways.")]
        public RectTransform field;
        public RectTransform routes;
        public RectTransform points;
        public OutingMapSiteView sitePrefab;
        public Image dashPrefab;
        [Tooltip("Dash drawings, used in turn along a route.")]
        public Sprite[] dashSprites;
        public OutingMapCard card;
        public PlayerControlGate control;
        [Header("Points")]
        public Site[] sites;
        [Header("Look")]
        [Range(.5f, 1)] public float screenFill = .94f;
        [Min(1)] public float dashSpacing = 30;
        public Vector2 dashSize = new Vector2(22, 16);
        [Tooltip("Routes stop this far (field pixels) from the icons.")]
        [Min(0)] public float routeTrim = 74;
        [Tooltip("A new route draws itself dash by dash over this time, then its point appears.")]
        [Min(0)] public float revealSeconds = .9f;

        public bool IsOpen { get; private set; }
        public event Action Changed;

        OutingMapSave state = NewState();
        readonly List<OutingMapSiteView> views = new List<OutingMapSiteView>();
        readonly List<(Image dash, float at)> dashes = new List<(Image, float)>();
        Action<OutingPlace> chosen;
        Func<OutingPlace, string> nameOf;
        OutingMapSiteView selected;
        int openedFrame;

        void Awake() { Show(false); }

        static OutingMapSave NewState() => new OutingMapSave { seed = Environment.TickCount };

        // ---------- save ----------

        public OutingMapSave Save() => JsonUtility.FromJson<OutingMapSave>(JsonUtility.ToJson(state));

        /// <summary>Old saves have no map: it is drawn again from its root on the next look.</summary>
        public void Restore(OutingMapSave save)
        {
            state = save != null ? JsonUtility.FromJson<OutingMapSave>(JsonUtility.ToJson(save)) : NewState();
            state.sites ??= new List<OutingMapSiteSave>();
            state.sites.RemoveAll(s => s == null || Definition(s.id) == null);
        }

        public OutingMapSiteSave Opened(string id) => state.sites.FirstOrDefault(s => s.id == id);
        public IReadOnlyList<OutingMapSiteSave> OpenedSites => state.sites;
        Site Definition(string id) => sites?.FirstOrDefault(s => s.id == id);

        /// <summary>
        /// Opens every point that is unlocked and whose parent is already on the map, placing it near the parent.
        /// Returns the points opened just now (they get the drawing-in on this look).
        /// </summary>
        public List<string> Grow(Func<Site, bool> unlocked)
        {
            var added = new List<string>();
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (var site in sites)
                {
                    if (string.IsNullOrEmpty(site.id) || Opened(site.id) != null || !unlocked(site)) continue;
                    Vector2 at;
                    if (string.IsNullOrEmpty(site.parentId)) at = site.home;
                    else
                    {
                        var parent = Opened(site.parentId);
                        if (parent == null) continue;
                        var taken = state.sites.Select(s => new Vector2(s.x, s.y)).ToList();
                        at = OutingMapLayout.PlaceNear(new Vector2(parent.x, parent.y), taken,
                            OutingMapLayout.SeedFor(state.seed, site.id), OutingMapLayout.Rules.Default(FieldAspect));
                    }
                    state.sites.Add(new OutingMapSiteSave { id = site.id, x = at.x, y = at.y });
                    added.Add(site.id);
                    grew = true;
                }
            }
            if (added.Count > 0) Changed?.Invoke();
            return added;
        }

        float FieldAspect => field && field.rect.height > 0 ? field.rect.width / field.rect.height : 4f / 3f;

        // ---------- showing ----------

        /// <summary>Shows the map; <paramref name="onChosen"/> runs after it has closed on «Идти».</summary>
        public void Open(Func<Site, bool> unlocked, Func<OutingPlace, string> placeName, Action<OutingPlace> onChosen)
        {
            if (IsOpen) return;
            var fresh = Grow(unlocked);
            if (state.sites.Count == 0) return;
            chosen = onChosen;
            nameOf = placeName;
            IsOpen = true;
            openedFrame = Time.frameCount;
            if (control) control.SetBlocked(this, true);
            Show(true);
            Fit();
            Draw(fresh);
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            chosen = null;
            if (card) card.Close();
            Clear();
            Show(false);
            if (control) control.SetBlocked(this, false);
        }

        void Draw(List<string> fresh)
        {
            Clear();
            float now = Time.unscaledTime;
            var size = field.rect.size;
            Vector2 Local(OutingMapSiteSave s) => new Vector2((s.x - .5f) * size.x, (s.y - .5f) * size.y);
            int order = 0;
            foreach (var opened in state.sites)
            {
                var site = Definition(opened.id);
                bool isFresh = fresh.Contains(opened.id);
                float appear = 0;
                var parent = string.IsNullOrEmpty(site.parentId) ? null : Opened(site.parentId);
                if (parent != null)
                {
                    var path = OutingMapLayout.DashPoints(Local(parent), Local(opened), dashSpacing, routeTrim,
                        OutingMapLayout.SeedFor(state.seed, site.id + "/route"));
                    float start = isFresh ? order * revealSeconds : 0;
                    for (int i = 0; i < path.Count; i++)
                    {
                        var dash = Instantiate(dashPrefab, routes);
                        if (dashSprites != null && dashSprites.Length > 0) dash.sprite = dashSprites[i % dashSprites.Length];
                        var r = dash.rectTransform;
                        r.anchoredPosition = path[i];
                        r.sizeDelta = dashSize;
                        var next = i + 1 < path.Count ? path[i + 1] : path[i] + (path[i] - path[Mathf.Max(0, i - 1)]);
                        var d = next - path[i];
                        r.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
                        float at = isFresh ? now + start + revealSeconds * i / Mathf.Max(1, path.Count) : 0;
                        dash.enabled = !isFresh;
                        dashes.Add((dash, at));
                    }
                    if (isFresh) appear = start + revealSeconds;
                }
                if (isFresh) order++;
                var view = Instantiate(sitePrefab, points);
                ((RectTransform)view.transform).anchoredPosition = Local(opened);
                view.Bind(opened.id, site.icon, !opened.seen, appear, Pick);
                views.Add(view);
            }
        }

        void Clear()
        {
            foreach (var v in views) if (v) Destroy(v.gameObject);
            foreach (var d in dashes) if (d.dash) Destroy(d.dash.gameObject);
            views.Clear(); dashes.Clear();
            selected = null;
        }

        void Pick(OutingMapSiteView view)
        {
            if (!IsOpen || card && card.IsOpen) return;
            var opened = Opened(view.Id);
            var site = Definition(view.Id);
            if (opened == null || site == null) return;
            if (!opened.seen) { opened.seen = true; view.SetNew(false); Changed?.Invoke(); }
            if (selected) selected.SetSelected(false);
            selected = view;
            view.SetSelected(true);
            if (!card) { Go(site.place); return; }
            card.Open(nameOf != null ? nameOf(site.place) : site.id, site.preview, () => Go(site.place), StepBack);
        }

        void Go(OutingPlace place)
        {
            var callback = chosen;
            Close();
            callback?.Invoke(place);
        }

        /// <summary>«Остаться»: the player stays in the shop.</summary>
        void StepBack() => Close();

        void Update()
        {
            if (!IsOpen) return;
            Fit();
            float now = Time.unscaledTime;
            foreach (var (dash, at) in dashes) if (dash && !dash.enabled && now >= at) dash.enabled = true;
            if (Time.frameCount == openedFrame) return; // The key that opened the door must not answer here.
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            bool back = keyboard != null && keyboard.escapeKey.wasPressedThisFrame || mouse != null && mouse.rightButton.wasPressedThisFrame;
            if (card && card.IsOpen)
            {
                if (back)
                {
                    card.Close();
                    if (selected) { selected.SetSelected(false); selected = null; }
                }
                else if (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame))
                    card.goButton.onClick.Invoke();
            }
            else if (back) Close();
        }

        void OnDisable() { if (IsOpen) Close(); }

        /// <summary>The parchment keeps its proportions and fills the screen's shorter side.</summary>
        void Fit()
        {
            if (!sheet || !(sheet.parent is RectTransform area)) return;
            var room = area.rect.size * screenFill;
            var native = sheet.rect.size;
            if (native.x <= 0 || native.y <= 0) return;
            float scale = Mathf.Min(room.x / native.x, room.y / native.y);
            sheet.localScale = new Vector3(scale, scale, 1);
        }

        void Show(bool visible)
        {
            if (!visibility) return;
            visibility.alpha = visible ? 1 : 0;
            visibility.interactable = visible;
            visibility.blocksRaycasts = visible;
        }
    }
}
