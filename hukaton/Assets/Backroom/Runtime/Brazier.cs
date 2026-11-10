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
    /// copies. The burner is off until someone comes to it: interacting clicks it alight (a campfire grows in the bowl)
    /// and lays every owned copy over the game view in a scrolling grid of four: copies in the deck as they are, the
    /// rest shaded; the burn price on hover. Choosing one asks to confirm the card and the sum; agreeing throws the card
    /// into the bowl, where it flares up and burns. Leaving turns the burner down until the fire goes out.
    /// The list stays open for the next one. The three starting copies cannot burn. 75% of what was paid, rounded
    /// up — the rule lives in <see cref="DeckWorkshop.Burn"/>.
    /// </summary>
    public sealed class Brazier : MonoBehaviour
    {
        [Header("Scene")]
        public DeckPrototype workshop;
        public PlayerControlGate control;
        public Camera viewCamera;

        [Header("World")]
        [Tooltip("The bowl: where the flame burns and cards land.")]
        public Transform firePoint;
        [Tooltip("The campfire in the bowl; burns all the time.")]
        public Campfire campfirePrefab;
        [Tooltip("Campfire size against the prefab's own.")]
        [Min(.1f)] public float flameSize = 1;
        [Tooltip("How much the fire grows when a card lands in it.")]
        [Min(1)] public float burnJump = 1.9f;
        public AudioSource audioSource;
        [Tooltip("igniteSound: the burner's click and whoomp on opening; burnSound: a card catching in the fire; " +
                 "burnLoop: the fire roaring while the list is open.")]
        public AudioClip igniteSound, burnSound, refuseSound;
        public AudioClip burnLoop;
        [Range(0, 1)] public float burnLoopVolume = .45f;
        [Tooltip("A card under the pointer: its teeth chatter with fear.")]
        public AudioClip fearSound;
        [Range(0, 1)] public float fearVolume = .6f;

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
        Campfire flame;
        AudioSource loop, fearVoice;
        SpringFloat jump;
        float fireTarget;
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

        /// <summary>The burner catches: the fire grows from nothing with a little overshoot.</summary>
        void Ignite()
        {
            if (!campfirePrefab || !firePoint) return;
            if (!flame)
            {
                flame = Instantiate(campfirePrefab, firePoint, false);
                flame.transform.localPosition = Vector3.zero;
                flame.transform.localRotation = Quaternion.identity;
                flame.transform.localScale = Vector3.one * flameSize;
            }
            flame.gameObject.SetActive(true);
            fireTarget = 1;
            jump.Snap(.15f);
            jump.Kick(14);
            flame.Strength = jump.Value;
        }

        void OnDisable()
        {
            Close();
            if (flame) Destroy(flame.gameObject);
            flame = null;
        }

        public bool Open()
        {
            if (IsOpen || !isActiveAndEnabled || !workshop || Model.IsAway) return false;
            IsOpen = true;
            pending = 0;
            if (control) control.SetBlocked(this, true);
            SetVisible(visibility, true);
            SetVisible(confirmGroup, false);
            // The burner clicks, catches with a whoomp and roars while the list is open.
            Play(igniteSound);
            Ignite();
            if (burnLoop && audioSource)
            {
                if (!loop) loop = MakeVoice(true);
                loop.clip = burnLoop;
                loop.volume = 0;
                loop.PlayDelayed(.2f);
            }
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
            if (fearVoice) fearVoice.Stop();
            fireTarget = 0;
            if (control) control.SetBlocked(this, false);
        }

        void Update()
        {
            if (flame && flame.gameObject.activeSelf)
            {
                // Open: the fire holds; closed: the burner is turned down and the fire shrinks away.
                jump.Step(fireTarget, fireTarget > 0 ? 9 : 3, fireTarget > 0 ? .45f : 1, Time.deltaTime);
                flame.Strength = Mathf.Max(0, jump.Value);
                if (fireTarget <= 0 && jump.Value < .03f) flame.gameObject.SetActive(false);
            }
            if (loop)
            {
                loop.volume = Mathf.MoveTowards(loop.volume, IsOpen ? burnLoopVolume : 0, Time.unscaledDeltaTime * (IsOpen ? .9f : 1.5f));
                if (!IsOpen && loop.volume <= 0 && loop.isPlaying) loop.Stop();
            }
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
                tile.trembleOnHover = true;
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
                // In the deck: as is. Not in the deck: shaded.
                tile.SetBadge("");
                tile.SetDimmed(!Model.IsSelected(copy.Id));
            }
            title.text = $"Жаровня  ·  коллекция {owned.Count} / {Model.CollectionLimit}";
        }

        void OnTileHovered(OfferCardView tile)
        {
            var copy = Model.Find(tile.Slot);
            // Over the card: what burning it pays; a starting card says it will not burn.
            tile.SetPrice(copy == null ? null : copy.Protected ? "не горит" : Model.BurnPrice(copy).ToString());
            if (fearSound && audioSource)
            {
                if (!fearVoice) fearVoice = MakeVoice(false);
                fearVoice.Stop();
                fearVoice.pitch = Random.Range(.92f, 1.1f);
                fearVoice.clip = fearSound;
                fearVoice.volume = fearVolume;
                fearVoice.Play();
            }
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
            if (tile) tile.FlyTo(FireOnScreen(tile), .05f);
            // The card catches when it reaches the bowl: a flare and the sound of it burning.
            Invoke(nameof(Flare), .55f);
            title.text = $"Жаровня  ·  коллекция {Model.Owned.Count} / {Model.CollectionLimit}";
        }

        void Flare()
        {
            Play(burnSound);
            if (!flame || !IsOpen) return;
            jump.Snap(Mathf.Max(jump.Value, burnJump));
            jump.Kick(4);
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

        /// <summary>Another source next to the main one, with its settings (loop, fear).</summary>
        AudioSource MakeVoice(bool looping)
        {
            var voice = audioSource.gameObject.AddComponent<AudioSource>();
            voice.outputAudioMixerGroup = audioSource.outputAudioMixerGroup;
            voice.spatialBlend = audioSource.spatialBlend;
            voice.rolloffMode = audioSource.rolloffMode;
            voice.minDistance = audioSource.minDistance;
            voice.maxDistance = audioSource.maxDistance;
            voice.playOnAwake = false;
            voice.loop = looping;
            return voice;
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
