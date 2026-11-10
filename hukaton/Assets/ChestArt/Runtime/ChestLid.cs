using UnityEngine;

namespace Hakaton.ChestArt
{
    /// <summary>
    /// Opens and closes a chest lid around its hinge. The lid transform sits on the hinge axis with its local X along
    /// the hinges; the closed pose is the authored rotation and opening turns the lid around local X (positive lifts
    /// the front edge). Presentation only: no input, loot or saving. Gameplay calls Open / Close / Toggle.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class ChestLid : MonoBehaviour
    {
        [Tooltip("Lid pivot on the hinge axis (the child 'Lid').")]
        [SerializeField] Transform lid;

        [Tooltip("Lid local rotation when shut, as authored in the prefab.")]
        [SerializeField] Quaternion closedRotation = Quaternion.identity;

        [Tooltip("How far the lid swings back when fully open, degrees around the hinge. The models are checked clear of the body up to 113.")]
        [SerializeField, Range(30f, 113f)] float openAngle = 105f;

        [SerializeField, Min(0.05f)] float openSeconds = 0.55f;
        [SerializeField, Min(0.05f)] float closeSeconds = 0.35f;

        [Tooltip("Current pose: 0 shut, 1 fully open. Drag it to preview in the editor; in a scene it is the starting state.")]
        [SerializeField, Range(0f, 1f)] float openness;

        float target;

        public bool IsOpen => target > 0.5f;
        public bool IsMoving => !Mathf.Approximately(openness, target);
        public float Openness => openness;
        public Transform Lid => lid;

        public void Open() => SetOpen(true);
        public void Close() => SetOpen(false);
        public void Toggle() => SetOpen(!IsOpen);

        public void SetOpen(bool open, bool instant = false)
        {
            target = open ? 1f : 0f;
            if (instant)
            {
                openness = target;
                Apply();
            }
        }

        void Awake()
        {
            target = openness;
            Apply();
        }

        void Update()
        {
            if (!Application.isPlaying)
            {
                target = openness;
                Apply();
                return;
            }
            if (!IsMoving) return;
            float seconds = target > openness ? openSeconds : closeSeconds;
            openness = Mathf.MoveTowards(openness, target, Time.deltaTime / seconds);
            Apply();
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (!Application.isPlaying) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
        }
#endif

        void Apply()
        {
            if (!lid) return;
            float eased = openness * openness * (3f - 2f * openness);
            Quaternion pose = closedRotation * Quaternion.Euler(openAngle * eased, 0f, 0f);
            if (Quaternion.Angle(lid.localRotation, pose) > 0.01f) lid.localRotation = pose;
        }
    }
}
