using System.Linq;
using Hakaton.Expeditions;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>Checks the saved assets used by both the apiary and spawned debug creatures.</summary>
    public sealed class HumanoidSpeechAssetTests
    {
        const string Prefabs = "Assets/Game/Prefabs/";
        const string Typography = "Assets/Content/UI/Typography/";

        [TestCase("Teen leshy skater")]
        [TestCase("Zombie beekeeper")]
        [TestCase("Rooster baseball")]
        public void PeacefulHumanoidHasAnAuthoredSharedBubbleAndOwnVoice(string name)
        {
            string path = Prefabs + name + ".prefab";
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(root, Is.Not.Null, path);
            var creature = root.GetComponent<WorldCreature>();
            var speech = root.GetComponent<HumanoidSpeech>();
            Assert.That(speech, Is.Not.Null, name);
            Assert.That(speech.creature, Is.SameAs(creature));
            Assert.That(creature.temperament, Is.EqualTo(Temperament.Peaceful), "Adding speech must preserve the peaceful combat behaviour.");
            Assert.That(speech.profile, Is.Not.Null);
            Assert.That(AssetDatabase.Contains(speech.profile), Is.True, "The editable voice must be a saved asset.");
            Assert.That(speech.profile.reactionDistance, Is.EqualTo(4));
            Assert.That(speech.bubble, Is.Not.Null);
            Assert.That(speech.bubble.transform.IsChildOf(root.transform), Is.True);
            Assert.That(speech.bubble.spritePlane, Is.SameAs(creature.view));
            Assert.That(speech.bubble.gameObject.activeSelf, Is.True, "The authored presentation must be able to show a line.");

            var sharedBubble = PrefabUtility.GetCorrespondingObjectFromSource(speech.bubble);
            Assert.That(sharedBubble, Is.Not.Null, "The bubble must remain a reusable nested prefab.");
            Assert.That(AssetDatabase.GetAssetPath(sharedBubble), Is.Not.EqualTo(path));
            Assert.That(root.GetComponentsInChildren<MonoBehaviour>(true).All(component => component), Is.True, "Missing script in the saved creature prefab.");
            if (name == "Rooster baseball") Assert.That(creature.HasRangedAttack, Is.True);
        }

        [TestCase("Teen leshy skater")]
        [TestCase("Zombie beekeeper")]
        [TestCase("Rooster baseball")]
        public void BubbleIsWorldSpaceNonInteractiveAndUsesTheSavedPixelFont(string name)
        {
            var speech = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + name + ".prefab").GetComponent<HumanoidSpeech>();
            Assert.That(speech, Is.Not.Null);
            var bubble = speech.bubble;
            Assert.That(bubble, Is.Not.Null);
            Assert.That(bubble.bubbleCanvas, Is.Not.Null);
            Assert.That(bubble.bubbleCanvas.renderMode, Is.EqualTo(RenderMode.WorldSpace));
            Assert.That(bubble.content, Is.Not.Null);
            Assert.That(bubble.content.alpha, Is.Zero, "Saved prefabs start with no unsolicited sample text.");
            Assert.That(bubble.content.interactable, Is.False);
            Assert.That(bubble.content.blocksRaycasts, Is.False);
            Assert.That(bubble.IsVisible, Is.False);
            Assert.That(bubble.bubbleCanvas.GetComponent<GraphicRaycaster>(), Is.Null, "An ambient line must not capture player input.");
            Assert.That(bubble.bubbleCanvas.GetComponents<MonoBehaviour>().Any(component => component && component.GetType().FullName == "Hukaton.Rendering.NativeWorldUI"), Is.True,
                "Runtime-spawned copies must join the native world UI rendering pass.");
            foreach (var child in bubble.bubbleCanvas.GetComponentsInChildren<Transform>(true))
                Assert.That(child.gameObject.layer, Is.EqualTo(5), child.name);

            var serializedBubble = new SerializedObject(bubble);
            Assert.That(serializedBubble.FindProperty("layout").objectReferenceValue, Is.Not.Null);
            var labels = bubble.bubbleCanvas.GetComponentsInChildren<TMP_Text>(true);
            Assert.That(labels.Length, Is.EqualTo(1), "A short ambient bubble needs only its speech label.");
            var label = labels.Single();
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Typography + "Resources/Fonts/PixeloidSans Pixel.asset");
            var material = AssetDatabase.LoadAssetAtPath<Material>(Typography + "Materials/PixeloidSans Pixel.mat");
            Assert.That(label.font, Is.SameAs(font));
            Assert.That(label.fontSharedMaterial, Is.SameAs(material));
            Assert.That(label.fontSize, Is.EqualTo(18));
            Assert.That(label.enableAutoSizing, Is.False);
            Assert.That(label.transform.localScale, Is.EqualTo(Vector3.one));
            Assert.That(label.raycastTarget, Is.False);
            Assert.That(font.atlasRenderMode, Is.EqualTo(GlyphRenderMode.RASTER));
            Assert.That(font.atlasPopulationMode, Is.EqualTo(AtlasPopulationMode.Static));
            Assert.That(material.shader.name, Is.EqualTo("TextMeshPro/Mobile/Bitmap"));
            Assert.That(material.mainTexture, Is.SameAs(font.atlasTexture));
        }

        [TestCase("Teen leshy skater")]
        [TestCase("Zombie beekeeper")]
        [TestCase("Rooster baseball")]
        public void EveryVoicePoolHasVarietyAndAllCharactersExistInItsFont(string name)
        {
            var speech = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + name + ".prefab").GetComponent<HumanoidSpeech>();
            Assert.That(speech, Is.Not.Null);
            var font = speech.bubble.bubbleCanvas.GetComponentInChildren<TMP_Text>(true).font;
            var profile = speech.profile;
            VerifyPool(profile.greetings, 6, font, name + " greetings");
            VerifyPool(profile.neutral, 8, font, name + " neutral");
            VerifyPool(profile.fear, 6, font, name + " fear");
            VerifyPool(profile.aggression, 6, font, name + " aggression");
            VerifyPool(profile.attack, 6, font, name + " attack");
        }

        static void VerifyPool(string[] lines, int minimum, TMP_FontAsset font, string context)
        {
            Assert.That(lines, Is.Not.Null, context);
            Assert.That(lines.Length, Is.GreaterThanOrEqualTo(minimum), context);
            Assert.That(lines.All(line => !string.IsNullOrWhiteSpace(line)), Is.True, context);
            Assert.That(lines.Distinct().Count(), Is.EqualTo(lines.Length), context + ": duplicate lines");
            foreach (var line in lines)
            {
                Assert.That(line.Contains('<') || line.Contains('>'), Is.False, context + ": dialogue is plain text");
                foreach (char c in line)
                    if (!char.IsWhiteSpace(c)) Assert.That(font.HasCharacter(c), Is.True, context + ": missing glyph " + c);
            }
        }

        [Test]
        public void HumanoidsHaveSeparateEditableVoiceProfiles()
        {
            var names = new[] { "Teen leshy skater", "Zombie beekeeper", "Rooster baseball" };
            var profiles = names.Select(name => AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + name + ".prefab").GetComponent<HumanoidSpeech>().profile).ToArray();
            Assert.That(profiles.All(profile => profile), Is.True);
            Assert.That(profiles.Distinct().Count(), Is.EqualTo(names.Length), "Editing one personality must not overwrite another.");
        }

        [Test]
        public void BubbleTracksTheDrawnHeadAndPlaneWithoutSquashingItsCanvas()
        {
            var root = new GameObject("Speech geometry test");
            try
            {
                root.transform.localScale = Vector3.one * 1.4f;
                var drawing = new GameObject("Drawing");
                drawing.transform.SetParent(root.transform, false);
                var view = drawing.AddComponent<CreatureSpriteView>();
                view.height = 1.6f;
                drawing.transform.localPosition = new Vector3(.1f, .3f, 0);
                drawing.transform.localScale = new Vector3(.8f, 1.2f, 1);
                drawing.transform.rotation = Quaternion.Euler(0, 57, -8);
                var presentation = new GameObject("Speech canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
                presentation.transform.SetParent(root.transform, false);
                var bubble = presentation.AddComponent<CreatureSpeechBubble>();
                bubble.spritePlane = view;
                bubble.bubbleCanvas = presentation.GetComponent<Canvas>();
                bubble.content = presentation.GetComponent<CanvasGroup>();
                bubble.headGap = .23f;
                bubble.metresPerPixel = .008f;
                bubble.FollowSprite();
                Vector3 expectedHead = drawing.transform.TransformPoint(new Vector3(0, view.height + bubble.headGap, 0));
                Assert.That(Vector3.Distance(presentation.transform.position, expectedHead), Is.LessThan(.0001f));
                Assert.That(Quaternion.Angle(presentation.transform.rotation, drawing.transform.rotation), Is.LessThan(.001f));
                Assert.That(presentation.transform.lossyScale.x, Is.EqualTo(.008f).Within(.0001f));
                Assert.That(presentation.transform.lossyScale.y, Is.EqualTo(.008f).Within(.0001f));

                drawing.transform.localScale = new Vector3(1.3f, .7f, 1);
                drawing.transform.localPosition += Vector3.up * .2f;
                bubble.FollowSprite();
                expectedHead = drawing.transform.TransformPoint(new Vector3(0, view.height + bubble.headGap, 0));
                Assert.That(Vector3.Distance(presentation.transform.position, expectedHead), Is.LessThan(.0001f));
                Assert.That(presentation.transform.lossyScale.x, Is.EqualTo(.008f).Within(.0001f));
                Assert.That(presentation.transform.lossyScale.y, Is.EqualTo(.008f).Within(.0001f));
                bubble.content.alpha = 1;
                bubble.Hide();
                Assert.That(bubble.IsVisible, Is.False);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
