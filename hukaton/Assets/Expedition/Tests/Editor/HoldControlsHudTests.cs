using System.Linq;
using Hakaton.Feel;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>The standing controls line bottom left and the hold ring round the aim, as authored in the HUD prefab.</summary>
    public sealed class HoldControlsHudTests
    {
        static ExpeditionHud Hud => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Expedition HUD.prefab").GetComponent<ExpeditionHud>();

        [Test]
        public void ControlsLineSaysRAndOnTheReturnCardHoldLmbAndNothingInTheCave()
        {
            var hud = Hud;
            Assert.That(hud.discardControl, Is.EqualTo("R — сбросить руку"));
            Assert.That(hud.returnControl, Is.EqualTo("Удерживай ЛКМ — домой"));
            Assert.That(hud.ControlsLine(false, false), Is.EqualTo(hud.discardControl));
            Assert.That(hud.ControlsLine(false, true), Is.EqualTo(hud.returnControl));
            Assert.That(hud.ControlsLine(true, false), Is.Empty, "The cave prologue has no controls line.");
            Assert.That(hud.ControlsLine(true, true), Is.Empty);
            Assert.That(hud.ControlsLine(false, false, false), Is.Empty, "A place whose rules never allow R.");
        }

        [Test]
        public void ControlsLineIsAnIthacaRowAtTheBottomUnderTheOtherHints()
        {
            var hud = Hud;
            var line = hud.controls;
            Assert.That(line, Is.Not.Null, "Authored in the prefab, not made at runtime.");
            Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(line.gameObject), Does.EndWith("Typography/Prefabs/Ithaca Text.prefab"));
            Assert.That(line.font.name, Is.EqualTo("Ithaca Pixel"));
            Assert.That(line.fontSharedMaterial.name, Is.EqualTo("Ithaca Pixel"));
            Assert.That(line.fontSize, Is.EqualTo(16), "A whole multiple of Ithaca's 16 px grid.");
            Assert.That(line.enableAutoSizing, Is.False);
            Assert.That(line.transform.localScale, Is.EqualTo(Vector3.one));
            Assert.That(line.color, Is.EqualTo(hud.context.color), "Same look as the contextual hint.");
            var rect = line.rectTransform;
            Assert.That((rect.anchorMin, rect.anchorMax, rect.pivot), Is.EqualTo((Vector2.zero, Vector2.zero, Vector2.zero)), "Bottom-left corner.");
            Assert.That(rect.parent, Is.SameAs(hud.context.rectTransform.parent));
            Assert.That(rect.anchoredPosition, Is.EqualTo(new Vector2(8, 8)));
            // One stack, bottom up: controls, the contextual hint, the special item's lines — no text over another.
            float row = rect.anchoredPosition.y + rect.sizeDelta.y;
            Assert.That(hud.context.rectTransform.anchoredPosition.y, Is.GreaterThanOrEqualTo(row));
            Assert.That(hud.itemHint.rectTransform.anchoredPosition.y, Is.GreaterThanOrEqualTo(row));
            Assert.That(hud.itemHintRaise, Is.GreaterThanOrEqualTo(hud.context.rectTransform.sizeDelta.y));
            var shadow = rect.parent.GetComponentsInChildren<TextShadowMirror>(true).SingleOrDefault(m => m.source == line);
            Assert.That(shadow, Is.Not.Null, "With its dark copy, like the other hints.");
            Assert.That(shadow.transform.GetSiblingIndex(), Is.LessThan(line.transform.GetSiblingIndex()), "Drawn under it.");
        }

        [Test]
        public void HoldRingIsTheKickRingOnceMoreOutsideTheChargeRing()
        {
            var hud = Hud;
            var hold = hud.holdRing;
            var kick = hud.kickRing;
            Assert.That(hold, Is.Not.Null);
            Assert.That(hold, Is.Not.SameAs(kick));
            Assert.That(hold.transform.parent, Is.SameAs(kick.transform.parent), "Round the aim.");
            Assert.That(hold.sprite, Is.SameAs(kick.sprite));
            Assert.That(hold.material, Is.SameAs(kick.material));
            Assert.That((hold.type, hold.fillMethod, hold.fillOrigin), Is.EqualTo((Image.Type.Filled, Image.FillMethod.Radial360, kick.fillOrigin)));
            Assert.That(hold.enabled, Is.False, "Shown only while a hold is in progress.");
            Assert.That(hold.rectTransform.sizeDelta.x, Is.GreaterThan(hud.chargeRing.rectTransform.sizeDelta.x),
                "Outside the charge ring, so a kick cooldown and a hold read apart.");
        }
    }
}
