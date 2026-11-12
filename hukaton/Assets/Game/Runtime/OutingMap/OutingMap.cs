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
    /// point opens its card: name, a small view, «Идти» / «Остаться». Escape, the right button or the cross steps back.
    /// The parchment, routes, ring and cross share ONE runtime copy of the creatures' hand-drawn sprite material whose
    /// pixel grid follows the parchment; all icons share a second copy with a finer grid (the asset's grid is the world's,
    /// shared by every creature, so it is not touched). The grids look the same at any resolution; opening sharpens the
    /// pixels from big blocks.
    /// </summary>
    public sealed class OutingMap : MonoBehaviour
    {
        [Serializable]
        public sealed class Site
        {
            [Tooltip("Stable save key. Never rename after release.")] public string id;
            public OutingPlace place;
            public Sprite icon;
            [Tooltip("The same drawing in ember: blinks over the icon while the point is new.")]
            public Sprite emberIcon;
            public Sprite preview;
            [Tooltip("The point this one grows from (its route starts there). Empty: a root point at Home.")]
            public string parentId;
            public OutingMapUnlock unlock;
            [Tooltip("Root points only: where on the field (0..1, from bottom left) the point is drawn.")]
            public Vector2 home = new Vector2(.2f, .3f);
        }

        static readonly int PixelsPerUnitId = Shader.PropertyToID("_PixelsPerUnit");
        static readonly int MaxRefineId = Shader.PropertyToID("_MaxRefine");

        [Header("Authored parts")]
        [Tooltip("Switched off while the map is closed: nothing is drawn or raycast then.")]
        public Canvas canvas;
        public CanvasGroup visibility;
        [Tooltip("The parchment at its native size; scaled uniformly to fit the screen.")]
        public RectTransform sheet;
        public Image paper;
        [Tooltip("The drawable part of the parchment: points are placed in it, 0..1 both ways.")]
        public RectTransform field;
        public RectTransform routes;
        public RectTransform points;
        public OutingMapSiteView sitePrefab;
        public Image dashPrefab;
        [Tooltip("Dash drawings, used in turn along a route.")]
        public Sprite[] dashSprites;
        public Button closeButton;
        public OutingMapCard card;
        public PlayerControlGate control;
        [Header("Points")]
        public Site[] sites;
        [Header("Pixels")]
        [Tooltip("The creatures' hand-drawn sprite material. The map draws with a runtime copy at its own grid.")]
        public Material pixelMaterial;
        [Tooltip("Size of one map pixel in parchment units (the parchment is 1472 wide).")]
        [Min(.5f)] public float pixelSize = 4;
        [Tooltip("Size of one icon pixel in parchment units: the icons (and their ember twins) are drawn finer than the paper.")]
        [Min(.5f)] public float iconPixelSize = 2;
        [Tooltip("Pixel size the map opens from (and closes to).")]
        [Min(1)] public float openPixelSize = 40;
        [Header("Look")]
        [Range(.5f, 1)] public float screenFill = .94f;
        [Tooltip("Room kept free on each side of the parchment (parchment units): the close cross sits there, outside the map.")]
        [Min(0)] public float sideRoom = 160;
        [Min(1)] public float dashSpacing = 30;
        public Vector2 dashSize = new Vector2(22, 16);
        [Tooltip("Routes stop this far (field pixels) from the icons.")]
        [Min(0)] public float routeTrim = 74;
        [Tooltip("A new route draws itself dash by dash over this time, then its point appears.")]
        [Min(0)] public float revealSeconds = .9f;
        [Header("Open / close")]
        [Min(.01f)] public float openSeconds = .45f;
        [Min(.01f)] public float closeSeconds = .25f;
        [Tooltip("Scale the parchment grows from; it settles at 1 with a small overshoot.")]
        [Range(.5f, 1)] public float openScale = .88f;
        [Tooltip("Turn (degrees) the parchment straightens from.")]
        public float openTilt = -4;

        public bool IsOpen { get; private set; }
        public event Action Changed;

        OutingMapSave state = NewState();
        readonly Dictionary<string, OutingMapSiteView> views = new Dictionary<string, OutingMapSiteView>();
        readonly List<Image> pendingDashes = new List<Image>();
        readonly List<float> pendingAt = new List<float>();
        Material shared, iconShared;
        Action<OutingPlace> chosen;
        Func<OutingPlace, string> nameOf;
        OutingMapSiteView selected;
        int openedFrame;
        // 0 closed .. 1 open; animated towards target.
        float shown, shownFrom, animStart;
        bool opening;
        float appliedPixel = -1, appliedScale = -1;

        void Awake()
        {
            if (closeButton) closeButton.onClick.AddListener(Close);
            Show(0);
            if (canvas) canvas.enabled = false;
            if (points) points.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (shared) Destroy(shared);
            if (iconShared) Destroy(iconShared);
        }

        static OutingMapSave NewState() => new OutingMapSave { seed = Environment.TickCount };

        // ---------- save ----------

        public OutingMapSave Save() => JsonUtility.FromJson<OutingMapSave>(JsonUtility.ToJson(state));

        /// <summary>Old saves have no map: it is drawn again from its root on the next look.</summary>
        public void Restore(OutingMapSave save)
        {
            state = save != null ? JsonUtility.FromJson<OutingMapSave>(JsonUtility.ToJson(save)) : NewState();
            state.sites ??= new List<OutingMapSiteSave>();
            state.sites.RemoveAll(s => s == null || Definition(s.id) == null);
            ClearDrawing();
        }

        public OutingMapSiteSave Opened(string id)
        {
            foreach (var s in state.sites) if (s.id == id) return s;
            return null;
        }
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
            Grow(unlocked);
            if (state.sites.Count == 0) return;
            chosen = onChosen;
            nameOf = placeName;
            IsOpen = true;
            openedFrame = Time.frameCount;
            if (control) control.SetBlocked(this, true);
            if (canvas) canvas.enabled = true;
            if (points) points.gameObject.SetActive(true);
            Animate(true);
            EnsureMaterials();
            Fit();
            // Points drawn on an earlier look stay as they were; the new ones draw in once the parchment has opened.
            DrawMissing(openSeconds);
            if (visibility) { visibility.interactable = true; visibility.blocksRaycasts = true; }
        }

        /// <summary>Closes the map at once for input; the parchment folds away by itself.</summary>
        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            chosen = null;
            if (card) card.Close();
            if (selected) { selected.SetSelected(false); selected = null; }
            if (visibility) visibility.blocksRaycasts = false;
            if (control) control.SetBlocked(this, false);
            Animate(false);
        }

        void Animate(bool open)
        {
            opening = open;
            shownFrom = shown;
            animStart = Time.unscaledTime;
        }

        void EnsureMaterials()
        {
            if (shared || !pixelMaterial) return;
            shared = new Material(pixelMaterial) { name = pixelMaterial.name + " (outing map)" };
            shared.SetFloat(MaxRefineId, 0);
            iconShared = new Material(shared) { name = pixelMaterial.name + " (outing map icons)" };
            appliedPixel = -1;
            if (paper) paper.material = shared;
            if (closeButton && closeButton.targetGraphic) closeButton.targetGraphic.material = shared;
        }

        void DrawMissing(float delay)
        {
            float now = Time.unscaledTime;
            var size = field.rect.size;
            Vector2 Local(OutingMapSiteSave s) => new Vector2((s.x - .5f) * size.x, (s.y - .5f) * size.y);
            int order = 0;
            foreach (var opened in state.sites)
            {
                if (views.ContainsKey(opened.id)) continue;
                var site = Definition(opened.id);
                // Only a point the player has not looked at yet draws itself in; the rest are just there.
                bool reveal = !opened.seen;
                float start = reveal ? delay + order * revealSeconds : 0;
                float appear = start;
                var parent = string.IsNullOrEmpty(site.parentId) ? null : Opened(site.parentId);
                if (parent != null)
                {
                    var path = OutingMapLayout.DashPoints(Local(parent), Local(opened), dashSpacing, routeTrim,
                        OutingMapLayout.SeedFor(state.seed, site.id + "/route"));
                    for (int i = 0; i < path.Count; i++)
                    {
                        var dash = Instantiate(dashPrefab, routes);
                        if (dashSprites != null && dashSprites.Length > 0) dash.sprite = dashSprites[i % dashSprites.Length];
                        dash.material = shared;
                        var r = dash.rectTransform;
                        r.anchoredPosition = path[i];
                        r.sizeDelta = dashSize;
                        var next = i + 1 < path.Count ? path[i + 1] : path[i] + (path[i] - path[Mathf.Max(0, i - 1)]);
                        var d = next - path[i];
                        r.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
                        if (reveal)
                        {
                            dash.enabled = false;
                            pendingDashes.Add(dash);
                            pendingAt.Add(now + start + revealSeconds * i / Mathf.Max(1, path.Count));
                        }
                    }
                    if (reveal) appear = start + revealSeconds;
                }
                if (reveal) order++;
                var view = Instantiate(sitePrefab, points);
                ((RectTransform)view.transform).anchoredPosition = Local(opened);
                if (view.ring) view.ring.material = shared;
                view.Bind(opened.id, site.icon, site.emberIcon, !opened.seen, appear, Pick, iconShared);
                views[opened.id] = view;
            }
        }

        void ClearDrawing()
        {
            foreach (var v in views.Values) if (v) Destroy(v.gameObject);
            views.Clear();
            if (routes) foreach (Transform dash in routes) Destroy(dash.gameObject);
            pendingDashes.Clear(); pendingAt.Clear();
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
            card.Open(nameOf != null ? nameOf(site.place) : site.id, site.preview, () => Go(site.place), Close);
        }

        void Go(OutingPlace place)
        {
            var callback = chosen;
            Close();
            callback?.Invoke(place);
        }

        void Update()
        {
            bool animating = opening ? shown < 1 : shown > 0;
            if (!IsOpen && !animating) return;
            float now = Time.unscaledTime;
            if (animating)
            {
                float duration = opening ? openSeconds : closeSeconds;
                float k = Mathf.Clamp01((now - animStart) / duration);
                shown = opening ? Mathf.Lerp(shownFrom, 1, k) : Mathf.Lerp(shownFrom, 0, k);
                Show(shown);
                if (!opening && shown <= 0)
                {
                    // Folded away: nothing is drawn, raycast or updated until the door is opened again.
                    if (canvas) canvas.enabled = false;
                    if (points) points.gameObject.SetActive(false);
                }
            }
            if (!canvas || !canvas.enabled) return;
            Fit();
            ApplyPixels();
            for (int i = pendingDashes.Count - 1; i >= 0; i--)
            {
                if (now < pendingAt[i]) continue;
                if (pendingDashes[i]) pendingDashes[i].enabled = true;
                pendingDashes.RemoveAt(i); pendingAt.RemoveAt(i);
            }
            if (!IsOpen || Time.frameCount == openedFrame) return; // The key that opened the door must not answer here.
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

        static float EaseOutBack(float t) { const float c = 1.6f; t -= 1; return 1 + (c + 1) * t * t * t + c * t * t; }
        static float EaseOutCubic(float t) { t = 1 - t; return 1 - t * t * t; }

        /// <summary>The parchment keeps its proportions and fills the screen's shorter side; the open/close pose on top.</summary>
        void Fit()
        {
            if (!sheet || !(sheet.parent is RectTransform area)) return;
            var room = area.rect.size * screenFill;
            var native = sheet.rect.size;
            if (native.x <= 0 || native.y <= 0) return;
            float fit = Mathf.Min(room.x / (native.x + 2 * sideRoom), room.y / native.y);
            float pose = opening ? EaseOutBack(shown) : EaseOutCubic(shown);
            float scale = fit * Mathf.LerpUnclamped(openScale, 1, pose);
            sheet.localScale = new Vector3(scale, scale, 1);
            sheet.localRotation = Quaternion.Euler(0, 0, Mathf.LerpUnclamped(openTilt, 0, pose));
        }

        /// <summary>One map pixel = <see cref="pixelSize"/> parchment units on screen; opening resolves from big blocks.</summary>
        void ApplyPixels()
        {
            float resolve = EaseOutCubic(shown);
            float units = Mathf.Lerp(openPixelSize, pixelSize, resolve);
            // A screen-space overlay canvas hands the shader its geometry in canvas units, before the canvas scale
            // (the screen-height scaler), so the grid is measured against the canvas, not the screen: otherwise the
            // canvas scale counts twice and a 4K screen gets pixels twice as coarse as Full HD.
            float canvasScale = canvas ? Mathf.Abs(canvas.rootCanvas.transform.lossyScale.x) : 1;
            float worldPerUnit = Mathf.Abs(sheet.lossyScale.x) / Mathf.Max(canvasScale, 1e-4f);
            if (Mathf.Approximately(units, appliedPixel) && Mathf.Approximately(worldPerUnit, appliedScale)) return;
            appliedPixel = units; appliedScale = worldPerUnit;
            if (shared) shared.SetFloat(PixelsPerUnitId, 1f / Mathf.Max(units * worldPerUnit, 1e-4f));
            float iconUnits = Mathf.Lerp(openPixelSize, iconPixelSize, resolve);
            if (iconShared) iconShared.SetFloat(PixelsPerUnitId, 1f / Mathf.Max(iconUnits * worldPerUnit, 1e-4f));
        }

        void Show(float amount)
        {
            if (visibility) visibility.alpha = Mathf.Clamp01(amount * 1.4f);
        }
    }
}
