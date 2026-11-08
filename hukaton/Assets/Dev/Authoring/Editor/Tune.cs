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
                // "scene:@" finds the object by its component instead of by name.
                var go = path == "scene:@"
                    ? Object.FindObjectsByType<Component>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(c => c && c.GetType().Name == component).gameObject
                    : Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(t => t.name == path.Substring(6)).gameObject;
                Apply(go.GetComponent(component));
                EditorSceneManager.MarkSceneDirty(go.scene);
                EditorSceneManager.SaveScene(go.scene);
            }
            else if (path.EndsWith(".prefab"))
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                // "Type@Child name" picks the component on a named child.
                var spec = component.Split('@');
                Object target = spec[0] == "GameObject"
                    ? root.GetComponentsInChildren<Transform>(true).First(t => t.name == spec[1]).gameObject
                    : root.GetComponentsInChildren<Component>(true)
                        .First(c => c && c.GetType().Name == spec[0] && (spec.Length == 1 || c.name == spec[1]));
                Apply(target);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
            }
            else Apply(AssetDatabase.LoadAssetAtPath<Object>(path));
            AssetDatabase.SaveAssets();
            return result;
        }

        /// <summary>Writes a prefab's (or scene object's) hierarchy with rect layout to Logs/bridge/dump.txt. args: "path[|child name]".</summary>
        public static string Dump(string args)
        {
            var parts = args.Split('|');
            GameObject root; bool unload = false;
            if (parts[0].StartsWith("scene:"))
                root = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(t => t.name == parts[0].Substring(6)).gameObject;
            else { root = PrefabUtility.LoadPrefabContents(parts[0]); unload = true; }
            var start = parts.Length > 1 ? root.GetComponentsInChildren<Transform>(true).First(t => t.name == parts[1]) : root.transform;
            var b = new System.Text.StringBuilder();
            void Walk(Transform t, int depth)
            {
                string layout = t is RectTransform r
                    ? $" a=({r.anchorMin.x:0.##},{r.anchorMin.y:0.##})-({r.anchorMax.x:0.##},{r.anchorMax.y:0.##}) p=({r.pivot.x:0.##},{r.pivot.y:0.##}) pos=({r.anchoredPosition.x:0.#},{r.anchoredPosition.y:0.#}) size=({r.sizeDelta.x:0.#},{r.sizeDelta.y:0.#})"
                    : $" lp={t.localPosition}";
                var comps = string.Join(",", t.GetComponents<Component>().Where(c => c && !(c is Transform)).Select(c => c.GetType().Name));
                var text = t.GetComponent<TMPro.TMP_Text>();
                b.AppendLine(new string(' ', depth * 2) + t.name + (t.gameObject.activeSelf ? "" : " [off]") + layout + " {" + comps + "}" + (text ? $" \"{text.text.Replace("\n", "/")}\" sz={text.fontSize}" : ""));
                foreach (Transform c in t) Walk(c, depth + 1);
            }
            Walk(start, 0);
            if (unload) PrefabUtility.UnloadPrefabContents(root);
            System.IO.File.WriteAllText("Logs/bridge/dump.txt", b.ToString());
            return "dumped " + b.Length + " chars";
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
