using UnityEditor;

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
            // The entire authored surface uses the ColorBlock; a single target graphic
            // and SpriteSwap/Animator transitions do not describe this multipart skin.
            DrawPropertiesExcluding(serializedObject, "m_Script", "m_Transition",
                "m_SpriteState", "m_AnimationTriggers", "m_TargetGraphic");
            serializedObject.ApplyModifiedProperties();
        }
    }
}
