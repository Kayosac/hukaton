using System.Collections;
using System.Linq;
using Hakaton.Guide;
using UnityEngine;

namespace Hakaton.Backroom
{
    /// <summary>
    /// The brazier's first opening: the goblin, heard from his hatch, walks the player through the list — the
    /// collection count, the shades, the gas tax, the hold that burns (shown on a pretend dummy card that really flies
    /// into the fire, the collection untouched) and the way out. Nothing can burn while he talks. Once, then never
    /// again (Esc skips; the PC's help can bring it back).
    /// </summary>
    public sealed class BrazierGuide : GuideRunner
    {
        public const string TourId = "brazier";

        public Brazier brazier;
        [Tooltip("The goblin's voice: the linen plate over the list, his mumble.")]
        public GuideSpeech speech;
        public GuideTour tour = new GuideTour { id = TourId };

        [Header("Show")]
        [Tooltip("Ghost pointer timings of the pretend burn: travel, hover before the hold, the hold itself.")]
        [Min(.05f)] public float travelSeconds = .7f, hoverSeconds = .9f;

        OfferCardView pretend;

        protected override IGuideVoice Voice => speech;

        void Awake()
        {
            if (brazier)
            {
                brazier.Opened += OnOpened;
                brazier.Closed += OnClosed;
            }
        }

        void OnDestroy()
        {
            if (!brazier) return;
            brazier.Opened -= OnOpened;
            brazier.Closed -= OnClosed;
        }

        void OnOpened() => Begin(tour);

        void OnClosed() => Abort();

        /// <summary>The PC's help or the cheat menu: the tour comes again at the next opening.</summary>
        public void Forget() => Progress?.Forget(TourId);

        protected override void OnBegin(string id)
        {
            brazier.Guided = true;
            brazier.BurnLocked = true;
        }

        protected override void OnEnd(string id, bool finished)
        {
            RemovePretend();
            if (!brazier) return;
            brazier.Guided = false;
            brazier.BurnLocked = false;
        }

        protected override bool Applies(string id, GuideStep step)
        {
            // Hovering a real card for its tax only when there is one that can burn.
            if (step.key == "tax") return brazier.Tiles.Any(t => t && t.isActiveAndEnabled && t.group.blocksRaycasts);
            return true;
        }

        protected override RectTransform Resolve(string id, string key)
        {
            switch (key)
            {
                case "list":
                case "shades":
                case "tax": return brazier.scroll ? (RectTransform)brazier.scroll.transform : null;
                case "count": return brazier.title ? brazier.title.rectTransform : null;
                case "legend": return brazier.legend ? brazier.legend.rectTransform : null;
                case "burn": return pretend ? pretend.transform as RectTransform : null;
                case "leave": return brazier.closeButton ? (RectTransform)brazier.closeButton.transform : null;
                default: return null;
            }
        }

        protected override void OnStep(string id, GuideStep step)
        {
            if (step.key == "burn") MakePretend();
        }

        protected override bool Done(string id, string key)
        {
            // The gas tax of a real card has been looked at.
            if (key == "tax") return brazier.TaxShownFor && !brazier.TaxShownFor.name.StartsWith("Pretend");
            return true;
        }

        void MakePretend()
        {
            if (pretend) return;
            var dummy = brazier.workshop ? brazier.workshop.config.dummyCard : null;
            pretend = brazier.AddPretendCard(dummy ? dummy.artwork : null, 1);
            // Into view: the pretend card is the last of the list.
            if (brazier.scroll) brazier.scroll.verticalNormalizedPosition = 0;
        }

        void RemovePretend()
        {
            if (pretend && brazier) brazier.RemovePretendCard(pretend);
            pretend = null;
            if (pointer) pointer.Show(false);
        }

        int PretendTax()
        {
            var dummy = brazier.workshop ? brazier.workshop.config.dummyCard : null;
            int paid = dummy ? dummy.purchasePrice : 0;
            return Mathf.CeilToInt(paid * .75f);
        }

        protected override IEnumerator Demo(string id, string key)
        {
            if (key != "burn" || !pointer || !spotlight) yield break;
            if (!pretend) MakePretend();
            if (!pretend) yield break;
            // A breath for the grid to lay the new card out.
            yield return null;
            var card = (RectTransform)pretend.transform;
            var parent = (RectTransform)pointer.transform.parent;
            Vector2 Local(Vector3 world) => parent.InverseTransformPoint(world);
            var corners = new Vector3[4];
            card.GetWorldCorners(corners);
            var over = Local((corners[0] + corners[2]) * .5f);
            var start = over + new Vector2(140, -90);
            pointer.Place(start);
            pointer.Press(false);
            pointer.Show(true);
            yield return pointer.MoveTo(over, travelSeconds);
            brazier.PretendHover(pretend, true, PretendTax());
            yield return GhostPointer.Wait(hoverSeconds);
            // Held: the card heats up and burns.
            pointer.Press(true);
            float t = 0;
            while (t < brazier.holdSeconds)
            {
                t += Time.unscaledDeltaTime;
                brazier.PretendHeat(pretend, t / brazier.holdSeconds);
                yield return null;
            }
            brazier.PretendBurn(pretend, 1);
            pointer.Press(false);
            pointer.Show(false);
            // Into the fire and back to the list.
            while (brazier.Throwing) yield return null;
            yield return GhostPointer.Wait(.3f);
            RemovePretend();
            yield return GhostPointer.Wait(.2f);
            MakePretend();
            spotlight.Focus((RectTransform)pretend.transform, Step != null ? Step.padding : 4);
        }

        protected override void EndDemo(string id, string key)
        {
            if (key != "burn") return;
            if (pointer) { pointer.Press(false); pointer.Show(false); }
            if (pretend) brazier.PretendHover(pretend, false, 0);
            // Shown: the pretend card leaves the list, and the list goes back to its top.
            RemovePretend();
            if (brazier.scroll) brazier.scroll.verticalNormalizedPosition = 1;
        }
    }
}
