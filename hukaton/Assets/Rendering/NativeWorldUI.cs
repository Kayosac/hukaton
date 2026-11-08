using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Hukaton.Rendering
{
    /// <summary>World-space UI belongs to the native-resolution pass, including generated TMP submeshes.</summary>
    [DisallowMultipleComponent, AddComponentMenu("Hukaton/Rendering/Native World UI")]
    public sealed class NativeWorldUI : MonoBehaviour
    {
        public const int Layer = 5; // Unity's built-in UI layer.
        public const int Mask = 1 << Layer;
        [System.NonSerialized] readonly Dictionary<GameObject, int> originalLayers = new Dictionary<GameObject, int>();
        [System.NonSerialized] Canvas canvas;
        [System.NonSerialized] Camera originalCamera, assignedCamera;
        static readonly HashSet<NativeWorldUI> active = new HashSet<NativeWorldUI>();

        public static NativeWorldUI Ensure(GameObject target)
        {
            var marker = target.GetComponent<NativeWorldUI>();
            if (!marker) marker = target.AddComponent<NativeWorldUI>();
            marker.RefreshLayers();
            if (marker.isActiveAndEnabled) active.Add(marker);
            return marker;
        }

        public static void RegisterSceneUI()
        {
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (canvas.renderMode == RenderMode.WorldSpace) Ensure(canvas.gameObject);
            foreach (var text in FindObjectsByType<TextMesh>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Ensure(text.gameObject);
            foreach (var text in FindObjectsByType<TextMeshPro>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Ensure(text.gameObject);
        }

        void OnEnable() { active.Add(this); RefreshLayers(); }
        void OnTransformChildrenChanged() => RefreshLayers();
        public void RefreshLayers()
        {
            foreach (var child in GetComponentsInChildren<Transform>(true))
            {
                if (!originalLayers.ContainsKey(child.gameObject)) originalLayers.Add(child.gameObject, child.gameObject.layer);
                child.gameObject.layer = Layer;
            }
            if (!canvas) canvas = GetComponent<Canvas>();
        }

        public static void UseCamera(Camera world, Camera output)
        {
            foreach (var ui in active)
                if (ui && ui.canvas && ui.canvas.renderMode == RenderMode.WorldSpace &&
                    (!ui.canvas.worldCamera || ui.canvas.worldCamera == world || ui.canvas.worldCamera == ui.assignedCamera))
                {
                    if (!ui.assignedCamera) ui.originalCamera = ui.canvas.worldCamera;
                    ui.assignedCamera = output; ui.canvas.worldCamera = output;
                }
        }

        public static void ReleaseCamera(Camera output)
        {
            foreach (var ui in active) if (ui && ui.assignedCamera == output) ui.RestoreCamera();
        }

        void RestoreCamera()
        {
            if (canvas && assignedCamera && canvas.worldCamera == assignedCamera) canvas.worldCamera = originalCamera;
            assignedCamera = null; originalCamera = null;
        }

        void OnDisable()
        {
            active.Remove(this); RestoreCamera();
            foreach (var item in originalLayers) if (item.Key && item.Key.layer == Layer) item.Key.layer = item.Value;
            originalLayers.Clear();
        }
    }
}
