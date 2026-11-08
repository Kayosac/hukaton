using System;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Hakaton.Authoring
{
    /// <summary>
    /// Sets serialized fields for tuning through the bridge.
    /// Prefab: <c>invoke Hakaton.Authoring.Tune.Set Assets/x.prefab|Component|field|value[;field|value]</c>
    /// Asset: same with a .asset path and component "-". Scene object (edit mode): path "scene:Object name".
    /// </summary>
    static class Tune
    {
        public static string Set(string args)
        {
            var parts = args.Split('|');
            string path = parts[0], component = parts[1];
            var assignments = string.Join("|", parts.Skip(2)).Split(';').Select(a => a.Split('|')).ToArray();
            string result = "";
            void Apply(Object target)
            {
                var so = new SerializedObject(target);
                foreach (var a in assignments)
                {
                    var property = so.FindProperty(a[0]);
                    if (property == null) { result += $"no field {a[0]}; "; continue; }
                    Assign(property, a[1]);
                    result += $"{a[0]}={a[1]}; ";
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                if (PrefabUtility.IsPartOfPrefabInstance(target)) PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                EditorUtility.SetDirty(target);
            }
            if (path.StartsWith("scene:"))
            {
                var go = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(t => t.name == path.Substring(6)).gameObject;
                Apply(go.GetComponent(component));
                EditorSceneManager.MarkSceneDirty(go.scene);
                EditorSceneManager.SaveScene(go.scene);
            }
            else if (path.EndsWith(".prefab"))
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                var target = root.GetComponentsInChildren<Component>(true).First(c => c && c.GetType().Name == component);
                Apply(target);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
            }
            else Apply(AssetDatabase.LoadAssetAtPath<Object>(path));
            AssetDatabase.SaveAssets();
            return result;
        }

        static void Assign(SerializedProperty p, string value)
        {
            float[] Floats() => value.Split(',').Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            switch (p.propertyType)
            {
                case SerializedPropertyType.Float: p.floatValue = float.Parse(value, CultureInfo.InvariantCulture); break;
                case SerializedPropertyType.Integer: p.intValue = int.Parse(value); break;
                case SerializedPropertyType.Boolean: p.boolValue = bool.Parse(value); break;
                case SerializedPropertyType.String: p.stringValue = value; break;
                case SerializedPropertyType.Vector2: { var f = Floats(); p.vector2Value = new Vector2(f[0], f[1]); break; }
                case SerializedPropertyType.Vector3: { var f = Floats(); p.vector3Value = new Vector3(f[0], f[1], f[2]); break; }
                case SerializedPropertyType.Color: { var f = Floats(); p.colorValue = new Color(f[0], f[1], f[2], f.Length > 3 ? f[3] : 1); break; }
                case SerializedPropertyType.Enum: p.enumValueIndex = int.Parse(value); break;
                default: throw new Exception("Unsupported field type " + p.propertyType);
            }
        }
    }
}
