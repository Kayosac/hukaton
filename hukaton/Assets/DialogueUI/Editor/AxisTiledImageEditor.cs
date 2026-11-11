using UnityEditor;
using UnityEditor.UI;

namespace Hakaton.DialogueUI.Editor
{
    [CustomEditor(typeof(AxisTiledImage)), CanEditMultipleObjects]
    public sealed class AxisTiledImageEditor : ImageEditor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("axis"));
            serializedObject.ApplyModifiedProperties();
        }
    }
}
