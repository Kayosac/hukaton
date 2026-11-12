using System.Collections.Generic;
using Febucci.TextAnimatorForUnity;
using Febucci.TextAnimatorForUnity.TextMeshPro;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.DialogueUI.Editor
{
    /// <summary>
    /// Authoring for the speech the player hears (customers, both goblins, outing creatures):
    /// the goblins' and creatures' bubbles use the button's linen plate (no tail), and every spoken label gets
    /// Text Animator's typewriter and a <see cref="TypedLine"/>.
    /// Run through the bridge: <c>invoke Hakaton.DialogueUI.Editor.TypedSpeechSetup.Apply</c>.
    /// </summary>
    static class TypedSpeechSetup
    {
        const string Root = "Assets/Plugins/Febucci/Text Animator for Unity/";
        static readonly string[] BubblePrefabs =
        {
            "Assets/Game/Prefabs/UI/Creature speech bubble.prefab",
            "Assets/Tutorial/Prefabs/TutorialBubble.prefab",
            "Assets/Backroom/Prefabs/GoblinCardTable.prefab",
        };
        const string Hud = "Assets/Trading/Prefabs/TradingHUD.prefab";
        const string Plate = "Assets/Content/UI/Dialogue/Textures/StitchedLinenPlate9Slice.png";

        static string Apply()
        {
            var report = new List<string>();
            foreach (var path in BubblePrefabs)
                report.Add(Edit(path, root =>
                {
                    // Only the speech bubble the speaker talks through (its "bubble"/"layout" field), not other plates.
                    var labels = new List<TMP_Text>();
                    foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        if (!behaviour || behaviour is DialogueBubbleLayout) continue;
                        var so = new SerializedObject(behaviour);
                        foreach (var field in new[] { "bubble", "layout" })
                            if (so.FindProperty(field)?.objectReferenceValue is DialogueBubbleLayout layout && layout.Dialogue)
                                labels.Add(layout.Dialogue);
                    }
                    foreach (var label in labels) UsePlate(label.GetComponentInParent<DialogueBubbleLayout>(true));
                    // Undo an earlier, too wide pass on the other plates.
                    foreach (var typed in root.GetComponentsInChildren<TypedLine>(true))
                        if (!labels.Contains(typed.GetComponent<TMP_Text>())) Strip(typed.gameObject);
                    return labels;
                }));
            report.Add(Edit(Hud, root =>
            {
                var labels = new List<TMP_Text>();
                foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                    if (behaviour && behaviour.GetType().Name == "TradingHud" &&
                        new SerializedObject(behaviour).FindProperty("speech")?.objectReferenceValue is TMP_Text speech)
                        labels.Add(speech);
                return labels;
            }));
            AssetDatabase.SaveAssets();
            return string.Join("; ", report);
        }

        static string Edit(string path, System.Func<GameObject, List<TMP_Text>> find)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var labels = find(root);
                foreach (var label in labels) Equip(label);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return $"{System.IO.Path.GetFileNameWithoutExtension(path)}: {labels.Count}";
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static void Equip(TMP_Text label)
        {
            var go = label.gameObject;
            var animator = go.GetComponent<TextAnimator_TMP>() ?? go.AddComponent<TextAnimator_TMP>();
            var typewriter = go.GetComponent<TypewriterComponent>() ?? go.AddComponent<TypewriterComponent>();
            var typed = go.GetComponent<TypedLine>() ?? go.AddComponent<TypedLine>();

            // The shared look (letters grow in), but on unscaled time: some talks happen with the game paused.
            var shared = AssetDatabase.LoadAssetAtPath<ScriptableObject>(Root + "Settings/Shared Animator Settings.asset");
            var a = new SerializedObject(animator);
            var local = a.FindProperty("localSettings");
            var from = shared ? new SerializedObject(shared).FindProperty("settings") : null;
            if (from != null)
            {
                var it = from.Copy();
                var end = it.GetEndProperty();
                bool enter = true;
                while (it.Next(enter) && !SerializedProperty.EqualContents(it, end))
                {
                    enter = false;
                    var target = local.FindPropertyRelative(it.name);
                    if (target != null) Copy(it, target);
                }
            }
            a.FindProperty("sharedSettings").objectReferenceValue = null;
            var timeScale = local.FindPropertyRelative("timeScale");
            if (timeScale != null && timeScale.propertyType == SerializedPropertyType.Enum)
                for (int i = 0; i < timeScale.enumNames.Length; i++)
                    if (timeScale.enumNames[i].Contains("Unscaled")) timeScale.enumValueIndex = i;
            a.ApplyModifiedPropertiesWithoutUndo();

            var t = new SerializedObject(typewriter);
            t.FindProperty("sharedSettings").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<ScriptableObject>(Root + "Settings/Shared Typewriter Settings.asset");
            t.FindProperty("timingsScriptableBase").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<ScriptableObject>(Root + "Timings/Delays By Character.asset");
            t.ApplyModifiedPropertiesWithoutUndo();

            var s = new SerializedObject(typed);
            s.FindProperty("typewriter").objectReferenceValue = typewriter;
            s.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>The tailless plate: same frame metrics as the bubble, the bottom padding now equals the top.</summary>
        static void UsePlate(DialogueBubbleLayout layout)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Plate);
            var image = layout.Body ? layout.Body.GetComponent<Image>() : null;
            if (!sprite || !image || image.sprite == sprite) return;
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            var so = new SerializedObject(layout);
            var padding = so.FindProperty("padding");
            var p = padding.vector4Value;
            var minimum = so.FindProperty("minimumHeight");
            minimum.floatValue = Mathf.Max(1, minimum.floatValue - (p.w - p.y));
            p.w = p.y;
            padding.vector4Value = p;
            so.ApplyModifiedPropertiesWithoutUndo();
            layout.RefreshLayout();
        }

        static void Strip(GameObject go)
        {
            Object.DestroyImmediate(go.GetComponent<TypedLine>(), true);
            Object.DestroyImmediate(go.GetComponent<TypewriterComponent>(), true);
            Object.DestroyImmediate(go.GetComponent<TextAnimator_TMP>(), true);
        }

        static void Copy(SerializedProperty from, SerializedProperty to)
        {
            switch (from.propertyType)
            {
                case SerializedPropertyType.Boolean: to.boolValue = from.boolValue; break;
                case SerializedPropertyType.Float: to.floatValue = from.floatValue; break;
                case SerializedPropertyType.Integer: to.intValue = from.intValue; break;
                case SerializedPropertyType.Enum: to.enumValueIndex = from.enumValueIndex; break;
                case SerializedPropertyType.String: to.stringValue = from.stringValue; break;
                case SerializedPropertyType.ObjectReference: to.objectReferenceValue = from.objectReferenceValue; break;
                case SerializedPropertyType.Generic when from.isArray:
                    to.arraySize = from.arraySize;
                    for (int i = 0; i < from.arraySize; i++) Copy(from.GetArrayElementAtIndex(i), to.GetArrayElementAtIndex(i));
                    break;
            }
        }
    }
}
