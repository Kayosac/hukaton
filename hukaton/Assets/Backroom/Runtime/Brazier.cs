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
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Hakaton.Backroom
{
    /// <summary>
    /// The gas brazier in the backroom (board, «Гоблин-торговец», 11 and 13): it frees collection places by burning
    /// copies. The burner is off until someone comes to it: interacting clicks it alight (a campfire grows in the bowl)
    /// and lays every owned copy over the game view in a scrolling grid of four: copies in the deck as they are, the
    /// rest shaded. Under the pointer a card shivers with fear and a plate over it names the gas tax (what burning it
    /// pays). A click burns it at once: the list fades, the card flies a real arc through the room into the bowl and
    /// burns there with a flare, and the list comes back for the next one. The three starting copies cannot burn: they
    /// are shaded under the ministry's sign and do not answer the pointer. Leaving turns the burner off and the fire dies down.
    /// 75% of what was paid, rounded up — the rule lives in <see cref="DeckWorkshop.Burn"/>.
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
        [Tooltip("igniteSound: the burner's click and whoomp on opening; extinguishSound: the burner turned off on " +
                 "leaving; burnSound: a card catching in the fire; burnLoop: the fire roaring while the list is open.")]
        public AudioClip igniteSound, extinguishSound, burnSound, refuseSound;
        public AudioClip burnLoop;
        [Range(0, 1)] public float burnLoopVolume = .45f;
        [Tooltip("A card under the pointer: its teeth chatter with fear.")]
        public AudioClip fearSound;
        [Range(0, 1)] public float fearVolume = .45f;

        [Header("Throw")]
        [Tooltip("A two-sided world card (the expedition's Card debris); only its CardView is used.")]
        public GameObject worldCardPrefab;
        [Tooltip("Burst where the card lands in the fire (Retro Arsenal Card burn).")]
        public VfxBurst cardBurnFx;
        [Tooltip("Sign drawn over the cards that cannot burn (a crossed-out circle), and the plain font of its line.")]
        public Texture guardIcon;
        public TMP_FontAsset guardFont;
        [Tooltip("The spinning coin of the balance window, drawn after the sum on the gas tax plate.")]
        public Sprite[] coinFrames = new Sprite[0];
        [Min(.01f)] public float coinFrameSeconds = .16f;

        [Header("Overlay")]
        public CanvasGroup visibility;
        [Tooltip("One card of the list; as many are made as the collection needs, and kept for the next opening.")]
        public OfferCardView tilePrefab;
        [Tooltip("The grid inside the scroll view (four to a row).")]
        public RectTransform content;
        public ScrollRect scroll;
        public Button closeButton;
        public TMP_Text title;
        [Tooltip("The old confirmation group: kept hidden. The gas tax plate over the card under the pointer is a " +
                 "plain linen button made from the close button, beside it in the same scaled layout.")]
        [FormerlySerializedAs("confirmGroup")] public CanvasGroup taxGroup;

        public bool IsOpen { get; private set; }
        DeckWorkshop Model => workshop.Model;
        int flying;
        OfferCardView handingOver;
        float overlayAlpha;
        Campfire flame;
        StitchedLinenButton taxPlate;
        CanvasGroup taxView;
        RectTransform taxCoin;
        const int CoinSize = 16;
        /// <summary>Seconds the list takes to fade over the world card that takes the thrown one's place.</summary>
        const float HandOver = .12f;
        AudioSource loop, fearVoice;
        SpringFloat jump;
        float fireTarget;
        readonly List<OfferCardView> tiles = new List<OfferCardView>();

        /// <summary>The cards currently in the list (tests, tools).</summary>
        public IReadOnlyList<OfferCardView> Tiles => tiles;

        void Awake()
        {
            closeButton.onClick.AddListener(Close);
            // The old confirmation (dim, bubble, buttons) stays hidden; the tax plate is a linen button made from
            // the close one, so it has the same density, font and padding, in the plain tone.
            if (taxGroup) taxGroup.gameObject.SetActive(false);
            // The collection count sits over the corner of the cards, so it is drawn after them.
            if (title) title.transform.SetAsLastSibling();
            var template = closeButton ? closeButton.GetComponent<StitchedLinenButton>() : null;
            if (template)
            {
                taxPlate = Instantiate(template, template.transform.parent, false);
                taxPlate.name = "Gas tax";
                taxPlate.Configure(template.Density, StitchedLinenButton.Tone.Plain);
                taxPlate.navigation = new Navigation { mode = Navigation.Mode.None };
                // Never pressed, but drawn in its normal state (a non-interactable group would grey it out).
                taxView = taxPlate.gameObject.AddComponent<CanvasGroup>();
                taxView.blocksRaycasts = false;
                taxCoin = MakeCoin(taxPlate.Label);
            }
            SetVisible(visibility, false);
            HideTax();
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
            if (control) control.SetBlocked(this, true);
            SetVisible(visibility, true);
            overlayAlpha = 1;
            HideTax();
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
            SetVisible(visibility, false);
            HideTax();
            if (fearVoice) fearVoice.Stop();
            // The burner is turned off: a click and the fire dies down.
            Play(extinguishSound);
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
            // While a card is in the air the list steps aside so the throw is seen in the room.
            float wanted = flying > 0 ? 0 : 1;
            overlayAlpha = Mathf.MoveTowards(overlayAlpha, wanted, Time.unscaledDeltaTime * (wanted > 0 ? 4 : 1 / HandOver));
            // The list's card stays until the list has faded over the world card taking its place.
            if (handingOver && overlayAlpha <= 0) { handingOver.Hide(); handingOver = null; }
            if (visibility)
            {
                visibility.alpha = overlayAlpha;
                visibility.interactable = visibility.blocksRaycasts = overlayAlpha > .99f;
            }
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            bool back = keyboard != null && keyboard.escapeKey.wasPressedThisFrame || mouse != null && mouse.rightButton.wasPressedThisFrame;
            if (back) Close();
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
                tile.Unhovered += _ => HideTax();
                // The card shivers under the pointer: hover is read from its still slot, not the moving card.
                tile.UseStillHitArea();
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
                // In the deck: as is. Not in the deck: shaded. Starting cards: shaded too, under the ministry's
                // sign, and untouchable.
                tile.SetBadge("");
                tile.SetPrice(null);
                tile.trembleOnHover = true;
                tile.SetDimmed(copy.Protected || !Model.IsSelected(copy.Id));
                tile.SetGuarded(copy.Protected, guardIcon, "защищено министерством карт", guardFont);
            }
            title.text = $"{owned.Count}/{Model.CollectionLimit}";
        }

        void OnTileHovered(OfferCardView tile)
        {
            var copy = Model.Find(tile.Slot);
            if (copy == null || copy.Protected || flying > 0) return;
            ShowTax(tile, Model.BurnPrice(copy));
            if (fearSound && audioSource)
            {
                if (!fearVoice) fearVoice = MakeVoice(false);
                fearVoice.Stop();
                fearVoice.pitch = Random.Range(.95f, 1.06f);
                fearVoice.clip = fearSound;
                fearVoice.volume = fearVolume;
                fearVoice.Play();
            }
        }

        // ---------- the gas tax plate ----------

        void ShowTax(OfferCardView tile, int price)
        {
            if (!taxPlate) return;
            // Room after the sum for the spinning coin.
            var label = $"НАЛОГ НА ГАЗ: {price}";
            var layout = taxPlate.GetComponent<DialogueBubbleLayout>();
            if (layout) layout.SetText(label);
            else if (taxPlate.Label) taxPlate.Label.text = label;
            // Over the card's top edge, in the plate's parent space (the list is a screen overlay).
            var corners = new Vector3[4];
            tile.body.GetWorldCorners(corners);
            var top = (corners[1] + corners[2]) * .5f;
            var parent = (RectTransform)taxPlate.transform.parent;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, RectTransformUtility.WorldToScreenPoint(null, top), null, out var local);
            var plate = (RectTransform)taxPlate.transform;
            plate.pivot = new Vector2(.5f, 0);
            plate.anchorMin = plate.anchorMax = new Vector2(.5f, .5f);
            // Clear of the card's lift and grow under the pointer.
            plate.localPosition = local + new Vector2(0, 20);
            plate.SetAsLastSibling();
            taxView.alpha = 1;
        }

        /// <summary>The balance window's spinning coin, sitting at the right end of a label.</summary>
        RectTransform MakeCoin(TMP_Text label)
        {
            if (!label || coinFrames == null || coinFrames.Length == 0) return null;
            var coin = new GameObject("Coin", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            coin.SetParent(label.rectTransform, false);
            coin.anchorMin = coin.anchorMax = coin.pivot = new Vector2(1, .5f);
            coin.anchoredPosition = Vector2.zero;
            coin.sizeDelta = new Vector2(CoinSize, CoinSize);
            // The label keeps room for it on the right, so the plate is laid out around text and coin together.
            label.margin = new Vector4(label.margin.x, label.margin.y, CoinSize + 4, label.margin.w);
            var image = coin.GetComponent<Image>();
            image.sprite = coinFrames[0];
            image.preserveAspect = true;
            image.raycastTarget = false;
            var flipbook = coin.gameObject.AddComponent<UiSpriteFlipbook>();
            flipbook.frames = coinFrames;
            flipbook.frameSeconds = coinFrameSeconds;
            return coin;
        }

        void HideTax()
        {
            if (taxView) taxView.alpha = 0;
        }

        // ---------- the throw ----------

        void OnTileClicked(OfferCardView tile)
        {
            if (!IsOpen || flying > 0) return;
            var copy = Model.Find(tile.Slot);
            if (copy == null || copy.Protected) return;
            if (Model.CanBurn(copy) != null || Model.Burn(copy.Id) != null)
            {
                tile.Refuse();
                Play(refuseSound);
                return;
            }
            HideTax();
            if (fearVoice) fearVoice.Stop();
            title.text = $"{Model.Owned.Count}/{Model.CollectionLimit}";

            // The card leaves the list where it lay and flies into the bowl in the room.
            var corners = new Vector3[4];
            tile.body.GetWorldCorners(corners);
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, (corners[0] + corners[2]) * .5f);
            float width = Vector2.Distance(RectTransformUtility.WorldToScreenPoint(null, corners[0]), RectTransformUtility.WorldToScreenPoint(null, corners[3]));
            var camera = viewCamera ? viewCamera : Camera.main;
            var thrown = BrazierThrow.Launch(worldCardPrefab, tile.face.sprite ? tile.face.sprite.texture : null, tile.back ? tile.back.texture : null,
                copy.Level, camera, screen, width, firePoint.position + Vector3.up * .04f, cardBurnFx, Landed, HandOver, tile.ShadeTint);
            // The list's card holds still and fades out with the list over the world card; then its place closes
            // up in the grid (the order is laid again on the next opening).
            tile.trembleOnHover = false;
            flying++;
            if (thrown) handingOver = tile;
            else { tile.Hide(); Landed(); }
        }

        /// <summary>The card reached the bowl: the fire flares up and it burns; the list comes back after.</summary>
        void Landed()
        {
            Play(burnSound);
            if (flame && IsOpen)
            {
                jump.Snap(Mathf.Max(jump.Value, burnJump));
                jump.Kick(4);
            }
            Invoke(nameof(Return), .55f);
        }

        void Return()
        {
            flying = Mathf.Max(0, flying - 1);
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
