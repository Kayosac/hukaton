using System.Collections.Generic;
using System.Linq;
using Hakaton.Character;
using Hakaton.Decks;
using Hakaton.Decks.Unity;
using Hakaton.DialogueUI;
using Hakaton.Feel;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Hakaton.Backroom
{
    /// <summary>
    /// The gas brazier in the backroom (board, «Гоблин-торговец», 11 and 13): it frees collection places by burning
    /// copies. Interacting lights it and lays every owned copy over the game view in a scrolling grid of four
    /// (level, «в колоде», burn price on hover). Choosing one asks to confirm the card and the sum; agreeing throws the card into the fire and it flares
    /// up. The list stays open for the next one. The three starting copies cannot burn. 75% of what was paid, rounded
    /// up — the rule lives in <see cref="DeckWorkshop.Burn"/>.
    /// </summary>
    public sealed class Brazier : MonoBehaviour
    {
        [Header("Scene")]
        public DeckPrototype workshop;
        public PlayerControlGate control;
        public Camera viewCamera;

        [Header("World")]
        [Tooltip("Where the fire burns and cards land.")]
        public Transform firePoint;
        [Tooltip("Small pilot flame while closed; the full fire while the list is open.")]
        public VfxBurst pilotFlame, fullFlame, burnFlare;
        public Color fireTint = new Color(1, .62f, .25f);
        public AudioSource audioSource;
        public AudioClip igniteSound, burnSound, refuseSound;

        [Header("Overlay")]
        public CanvasGroup visibility;
        [Tooltip("One card of the list; as many are made as the collection needs, and kept for the next opening.")]
        public OfferCardView tilePrefab;
        [Tooltip("The grid inside the scroll view (four to a row).")]
        public RectTransform content;
        public ScrollRect scroll;
        [Tooltip("Above the scroll view's mask: a burned card flies to the fire from here, never clipped.")]
        public RectTransform flightLayer;
        public Button closeButton;
        public TMP_Text title;
        [Tooltip("The confirmation: the card and the sum.")]
        public CanvasGroup confirmGroup;
        public DialogueBubbleLayout confirmBubble;
        public Button confirmButton, cancelButton;

        public bool IsOpen { get; private set; }
        DeckWorkshop Model => workshop.Model;
        int pending;
        VfxBurst pilot, fire;
        readonly List<OfferCardView> tiles = new List<OfferCardView>();

        /// <summary>The cards currently in the list (tests, tools).</summary>
        public IReadOnlyList<OfferCardView> Tiles => tiles;

        void Awake()
        {
            closeButton.onClick.AddListener(Close);
            confirmButton.onClick.AddListener(Confirm);
            cancelButton.onClick.AddListener(CancelConfirm);
            SetVisible(visibility, false);
            SetVisible(confirmGroup, false);
        }

        void OnEnable()
        {
            if (!pilot && firePoint) pilot = VfxBurst.Spawn(pilotFlame, firePoint.position, Quaternion.identity, firePoint, fireTint, .5f);
            if (pilot) pilot.HoldFor(float.MaxValue / 4);
        }

        void OnDisable()
        {
            Close();
            if (pilot) Destroy(pilot.gameObject);
            pilot = null;
        }

        public bool Open()
        {
            if (IsOpen || !isActiveAndEnabled || !workshop || Model.IsAway) return false;
            IsOpen = true;
            pending = 0;
            if (control) control.SetBlocked(this, true);
            SetVisible(visibility, true);
            SetVisible(confirmGroup, false);
            // The gas catches with a whoomp and burns while the list is open.
            fire = VfxBurst.Spawn(fullFlame, firePoint.position, Quaternion.identity, firePoint, fireTint);
            if (fire) fire.HoldFor(float.MaxValue / 4);
            Play(igniteSound);
            Lay(true);
            if (scroll) scroll.verticalNormalizedPosition = 1;
            return true;
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            pending = 0;
            SetVisible(visibility, false);
            SetVisible(confirmGroup, false);
            if (fire) fire.Release(.8f);
            fire = null;
            if (control) control.SetBlocked(this, false);
        }

        void Update()
        {
            if (!IsOpen) return;
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            bool back = keyboard != null && keyboard.escapeKey.wasPressedThisFrame || mouse != null && mouse.rightButton.wasPressedThisFrame;
            if (!back) return;
            if (pending != 0) CancelConfirm();
            else Close();
        }

        // ---------- the list ----------

        void Lay(bool deal)
        {
            var owned = Model.Owned;
            while (tiles.Count < owned.Count)
            {
                var tile = Instantiate(tilePrefab, content);
                tile.name = "Card " + (tiles.Count + 1);
                tile.Clicked += OnTileClicked;
                tile.Hovered += OnTileHovered;
                tiles.Add(tile);
            }
            for (int i = 0; i < tiles.Count; i++)
            {
                var tile = tiles[i];
                // A card that flew into the fire comes back into the grid, in order.
                if (tile.transform.parent != content) tile.transform.SetParent(content, false);
                tile.transform.SetSiblingIndex(i);
                if (i >= owned.Count) { tile.Hide(); continue; }
                var copy = owned[i];
                tile.Slot = copy.Id;
                tile.Deal(workshop.config.Find(copy.Spec.Id)?.artwork, true, deal ? Mathf.Min(i, 12) * .02f : 0, copy.Level);
                // Deck membership stays separate from the upgrade mark on the face.
                tile.SetBadge(Model.IsSelected(copy.Id) ? "<color=#9BE07A>в колоде</color>" : "");
                tile.group.alpha = copy.Protected ? .45f : 1;
            }
            title.text = $"Жаровня  ·  коллекция {owned.Count} / {Model.CollectionLimit}";
        }

        void OnTileHovered(OfferCardView tile)
        {
            var copy = Model.Find(tile.Slot);
            tile.SetPrice(copy == null ? null : copy.Protected ? "основа" : Model.BurnPrice(copy).ToString());
        }

        void OnTileClicked(OfferCardView tile)
        {
            if (!IsOpen || pending != 0) return;
            var copy = Model.Find(tile.Slot);
            string error = Model.CanBurn(copy);
            if (error != null)
            {
                tile.Refuse();
                Play(refuseSound);
                return;
            }
            pending = copy.Id;
            confirmBubble.SetText($"Сжечь «{copy.Spec.Name}» (ур. {copy.Level}) за {Model.BurnPrice(copy)} монет?"
                + (Model.IsSelected(copy.Id) ? "\nКарта уйдёт и из колоды." : ""));
            SetVisible(confirmGroup, true);
        }

        void CancelConfirm()
        {
            pending = 0;
            SetVisible(confirmGroup, false);
        }

        void Confirm()
        {
            if (pending == 0) return;
            int id = pending;
            CancelConfirm();
            var tile = tiles.FirstOrDefault(t => t.gameObject.activeSelf && t.Slot == id);
            // Out of the scroll view's mask first: the throw to the fire must be seen whole.
            if (tile && flightLayer) tile.transform.SetParent(flightLayer, true);
            if (Model.Burn(id) != null)
            {
                if (tile) tile.Refuse();
                Play(refuseSound);
                return;
            }
            Play(burnSound);
            if (tile) tile.FlyTo(FireOnScreen(tile), .05f);
            // The flare when the card reaches the fire.
            Invoke(nameof(Flare), .55f);
            title.text = $"Жаровня  ·  коллекция {Model.Owned.Count} / {Model.CollectionLimit}";
        }

        void Flare()
        {
            if (!firePoint) return;
            VfxBurst.Spawn(burnFlare, firePoint.position, Quaternion.identity, null, fireTint);
        }

        /// <summary>The fire's place in the tile's parent space, clamped to the screen so the throw always reads.</summary>
        Vector2 FireOnScreen(OfferCardView tile)
        {
            var parent = (RectTransform)tile.body.parent;
            Vector3 screen = viewCamera ? viewCamera.WorldToScreenPoint(firePoint.position) : new Vector3(Screen.width / 2f, 0, 1);
            if (screen.z < 0) screen = new Vector3(Screen.width - screen.x, 0, 1);
            screen.x = Mathf.Clamp(screen.x, 0, Screen.width);
            screen.y = Mathf.Clamp(screen.y, 0, Screen.height);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, null, out var local);
            return local;
        }

        void Play(AudioClip clip)
        {
            if (clip && audioSource) audioSource.PlayOneShot(clip, .75f);
        }

        static void SetVisible(CanvasGroup group, bool visible)
        {
            if (!group) return;
            group.alpha = visible ? 1 : 0;
            group.interactable = visible;
            group.blocksRaycasts = visible;
        }
    }
}
