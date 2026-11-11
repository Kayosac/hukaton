using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.DialogueUI.Editor
{
    /// <summary>
    /// Authoring: the tutorial goblin and the outing creatures speak on the linen button skin; the stitched
    /// speech bubble stays with the NPC trader only. Swaps the bubble inside a speech prefab for a
    /// <see cref="StitchedLinenButton"/> plate that does nothing but carry the text.
    /// Run through the bridge: <c>invoke Hakaton.DialogueUI.Editor.SpeechPlateSkin.Apply</c>.
    /// </summary>
    static class SpeechPlateSkin
    {
        const string Button = "Assets/Content/UI/Dialogue/Prefabs/StitchedLinenButton.prefab";
        const string Creature = "Assets/Game/Prefabs/UI/Creature speech bubble.prefab";
        const string Tutorial = "Assets/Tutorial/Prefabs/TutorialBubble.prefab";
        const string Trader = "Assets/Backroom/Prefabs/GoblinCardTable.prefab";

        static string Apply()
        {
            // Creatures: a small plate in a world canvas, right over the head.
            string a = Swap(Creature, 16, new Vector4(16, 14, 16, 16), 120, 280, 44, .04f);
            // Tutorial goblin and the trader: the full skin, where their bubble used to hang (top centre).
            string b = Swap(Tutorial, 8, new Vector4(36, 30, 36, 34), 240, 520, 92, -1);
            string c = Swap(Trader, 8, new Vector4(36, 30, 36, 34), 240, 520, 92, -1);
            string d = TopCentre(Tutorial);
            AssetDatabase.SaveAssets();
            return a + "; " + b + "; " + c + "; " + d;
        }

        static string Swap(string path, float density, Vector4 padding, float minWidth, float maxWidth, float minHeight, float headGap)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                DialogueBubbleLayout old = null;
                foreach (var layout in root.GetComponentsInChildren<DialogueBubbleLayout>(true))
                    if (!layout.GetComponent<StitchedLinenButton>()) { old = layout; break; }
                if (!old) return path + ": already a plate";
                var oldRect = (RectTransform)old.transform;
                var oldText = old.Dialogue;

                var plate = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Button), oldRect.parent);
                plate.name = "Speech plate";
                plate.transform.SetSiblingIndex(oldRect.GetSiblingIndex());
                var button = plate.GetComponent<StitchedLinenButton>();
                button.Configure(density, StitchedLinenButton.Tone.Plain);
                button.navigation = new Navigation { mode = Navigation.Mode.None };
                foreach (var graphic in plate.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;

                var rect = (RectTransform)plate.transform;
                rect.anchorMin = oldRect.anchorMin;
                rect.anchorMax = oldRect.anchorMax;
                // A world canvas hangs the plate from its bottom (the head); a screen one keeps the bubble's place.
                bool world = headGap >= 0;
                rect.pivot = world ? new Vector2(.5f, 0) : oldRect.pivot;
                rect.anchoredPosition = world ? Vector2.zero : oldRect.anchoredPosition;
                rect.localScale = Vector3.one;
                rect.localRotation = Quaternion.identity;

                var label = button.Label;
                if (oldText && label)
                {
                    label.font = oldText.font;
                    label.fontSharedMaterial = oldText.fontSharedMaterial;
                    label.fontSize = oldText.fontSize;
                    label.enableAutoSizing = false;
                    label.richText = oldText.richText;
                    label.horizontalAlignment = oldText.horizontalAlignment;
                    label.verticalAlignment = oldText.verticalAlignment;
                    label.textWrappingMode = TextWrappingModes.Normal;
                    label.text = oldText.text;
                }

                var layoutOnPlate = plate.GetComponent<DialogueBubbleLayout>();
                var so = new SerializedObject(layoutOnPlate);
                so.FindProperty("minimumWidth").floatValue = minWidth;
                so.FindProperty("maximumWidth").floatValue = maxWidth;
                so.FindProperty("minimumHeight").floatValue = minHeight;
                so.FindProperty("padding").vector4Value = padding;
                so.ApplyModifiedPropertiesWithoutUndo();

                // A fade group living on the old bubble moves to the plate.
                CanvasGroup oldGroup = old.GetComponent<CanvasGroup>(), newGroup = null;
                if (oldGroup)
                {
                    newGroup = plate.AddComponent<CanvasGroup>();
                    newGroup.alpha = oldGroup.alpha;
                    newGroup.interactable = oldGroup.interactable;
                    newGroup.blocksRaycasts = oldGroup.blocksRaycasts;
                    newGroup.ignoreParentGroups = oldGroup.ignoreParentGroups;
                }

                // Everything that pointed at the old bubble now points at the plate.
                foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (!behaviour || behaviour.transform.IsChildOf(plate.transform)) continue;
                    var refs = new SerializedObject(behaviour);
                    var it = refs.GetIterator();
                    bool changed = false;
                    while (it.Next(true))
                    {
                        if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                        if (it.objectReferenceValue == old) { it.objectReferenceValue = layoutOnPlate; changed = true; }
                        else if (oldGroup && it.objectReferenceValue == oldGroup) { it.objectReferenceValue = newGroup; changed = true; }
                    }
                    if (headGap >= 0 && refs.FindProperty("headGap") is SerializedProperty gap && gap.propertyType == SerializedPropertyType.Float)
                    { gap.floatValue = headGap; changed = true; }
                    if (changed) refs.ApplyModifiedPropertiesWithoutUndo();
                }

                Object.DestroyImmediate(oldRect.gameObject);
                layoutOnPlate.RefreshLayout();
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return path + ": plate";
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        /// <summary>
        /// Speech plates are not buttons: a non-interactable fade group around them would paint the disabled
        /// (dark, "pressed") tint. Paint the plain normal linen once and switch the button behaviour off.
        /// </summary>
        static string Calm()
        {
            var report = new System.Collections.Generic.List<string>();
            foreach (var path in new[] { Creature, Tutorial, Trader })
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    int count = 0;
                    foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        if (!behaviour || behaviour is DialogueBubbleLayout) continue;
                        var so = new SerializedObject(behaviour);
                        foreach (var field in new[] { "bubble", "layout" })
                        {
                            if (!(so.FindProperty(field)?.objectReferenceValue is DialogueBubbleLayout layout)) continue;
                            var button = layout.GetComponent<StitchedLinenButton>();
                            if (!button) continue;
                            button.enabled = true;
                            button.interactable = true;
                            var b = new SerializedObject(button);
                            b.FindProperty("editorPreview").enumValueIndex = (int)StitchedLinenButton.VisualState.Normal;
                            b.ApplyModifiedPropertiesWithoutUndo();
                            button.Rebuild();
                            button.transition = Selectable.Transition.None;
                            button.enabled = false;
                            count++;
                        }
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    report.Add(System.IO.Path.GetFileNameWithoutExtension(path) + ": " + count);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            return string.Join("; ", report);
        }

        /// <summary>The tutorial plate back where the goblin's bubble always hung: top centre, 40 px down.</summary>
        static string TopCentre(string path)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var button = root.GetComponentInChildren<StitchedLinenButton>(true);
                if (!button) return path + ": no plate";
                var rect = (RectTransform)button.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1);
                rect.pivot = new Vector2(.5f, 1);
                rect.anchoredPosition = new Vector2(0, -40);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return path + ": top centre";
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        /// <summary>Renders both speech prefabs with a sample line into Logs/bridge/shots/speech-plates.png.</summary>
        static string Preview()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var rt = new RenderTexture(900, 500, 24);
            try
            {
                var cameraObject = new GameObject("Preview camera");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
                var camera = cameraObject.AddComponent<Camera>();
                camera.scene = scene;
                camera.orthographic = true;
                camera.orthographicSize = 2.5f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.25f, .3f, .28f);
                camera.transform.position = new Vector3(0, 0, -10);
                camera.targetTexture = rt;
                Place(scene, Creature, new Vector3(-2.2f, -.5f, 0), .008f, "Привет. Дыши экономнее.");
                Place(scene, Tutorial, new Vector3(1.6f, -1.2f, 0), .006f, "Эй! Ты чего?! Тут вообще-то занято!");
                Canvas.ForceUpdateCanvases();
                foreach (var layout in Resources.FindObjectsOfTypeAll<DialogueBubbleLayout>()) layout.RefreshLayout();
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = rt;
                var image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                RenderTexture.active = null;
                string path = System.IO.Path.Combine("Logs", "bridge", "shots", "speech-plates.png");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.WriteAllBytes(path, image.EncodeToPNG());
                Object.DestroyImmediate(image);
                return path;
            }
            finally
            {
                Object.DestroyImmediate(rt);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static void Place(UnityEngine.SceneManagement.Scene scene, string path, Vector3 at, float metresPerPixel, string line)
        {
            var instance = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(instance, scene);
            var canvas = instance.GetComponentInChildren<Canvas>(true);
            canvas.renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)canvas.transform;
            rect.position = at;
            rect.localScale = Vector3.one * metresPerPixel;
            foreach (var group in instance.GetComponentsInChildren<CanvasGroup>(true)) group.alpha = 1;
            foreach (var behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = behaviour is Graphic || behaviour is DialogueBubbleLayout || behaviour is StitchedLinenButton;
            var layout = instance.GetComponentInChildren<StitchedLinenButton>(true).GetComponent<DialogueBubbleLayout>();
            var body = (RectTransform)layout.transform;
            body.anchorMin = body.anchorMax = new Vector2(.5f, .5f);
            body.anchoredPosition = Vector2.zero;
            layout.SetText(line);
        }
    }
}
