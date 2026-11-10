using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.DialogueUI.Editor
{
    [CustomEditor(typeof(StitchedLinenButton)), CanEditMultipleObjects]
    public sealed class StitchedLinenButtonEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("density"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("tone"));
            bool rebuild = EditorGUI.EndChangeCheck();
            // The entire authored surface uses the ColorBlock; a single target graphic
            // and SpriteSwap/Animator transitions do not describe this multipart skin.
            DrawPropertiesExcluding(serializedObject, "m_Script", "m_Transition",
                "m_SpriteState", "m_AnimationTriggers", "m_TargetGraphic", "density", "tone");
            serializedObject.ApplyModifiedProperties();
            if (GUILayout.Button("Rebuild frame")) rebuild = true;
            if (!rebuild) return;
            foreach (var target in targets)
            {
                var button = (StitchedLinenButton)target;
                Undo.RegisterFullObjectHierarchyUndo(button.gameObject, "Rebuild linen button");
                button.Rebuild();
                Record(button);
            }
        }

        /// <summary>Keeps the rebuilt layout as overrides when the button sits inside another prefab or a scene.</summary>
        static void Record(StitchedLinenButton button)
        {
            PrefabUtility.RecordPrefabInstancePropertyModifications(button);
            foreach (var r in button.GetComponentsInChildren<RectTransform>(true)) PrefabUtility.RecordPrefabInstancePropertyModifications(r);
            foreach (var i in button.GetComponentsInChildren<Image>(true)) PrefabUtility.RecordPrefabInstancePropertyModifications(i);
            foreach (var t in button.GetComponentsInChildren<TMP_Text>(true)) PrefabUtility.RecordPrefabInstancePropertyModifications(t);
            EditorUtility.SetDirty(button);
        }
    }
}
