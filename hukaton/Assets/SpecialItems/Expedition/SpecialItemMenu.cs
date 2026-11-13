using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Hakaton.SpecialItems
{
    /// <summary>
    /// The menu Q held opens: the bought items on a ring around the aim, a small pointer moved by the mouse (the view
    /// stands still meanwhile). The item the pointer leans towards lights up; in the middle nothing is chosen.
    /// </summary>
    public sealed class SpecialItemMenu : MonoBehaviour
    {
        public CanvasGroup group;
        public RectTransform ring;
        [Tooltip("One authored slot (backing plate + icon); cloned for every further item.")]
        public SpecialItemMenuSlot slotTemplate;
        public RectTransform pointer;
        [Tooltip("Distance of the slots from the middle, reference pixels.")]
        [Min(0)] public float radius = 150;
        [Tooltip("How far the pointer may travel from the middle, reference pixels.")]
        [Min(1)] public float pointerReach = 120;
        [Tooltip("The pointer must leave this circle to choose anything.")]
        [Min(0)] public float deadZone = 28;
        [Tooltip("Reference pixels per mouse count.")]
        [Min(.01f)] public float sensitivity = .6f;

        readonly List<SpecialItemMenuSlot> slots = new List<SpecialItemMenuSlot>();
        readonly List<SpecialItemDefinition> shown = new List<SpecialItemDefinition>();
        Vector2 aim;

        public bool IsOpen { get; private set; }
        /// <summary>The item the pointer leans towards, or null in the middle.</summary>
        public SpecialItemDefinition Highlighted { get; private set; }

        void Awake()
        {
            if (slotTemplate) { slots.Add(slotTemplate); slotTemplate.gameObject.SetActive(false); }
            SetVisible(false);
        }

        public void Open(IReadOnlyList<SpecialItemDefinition> items, SpecialItemDefinition inHand)
        {
            shown.Clear();
            shown.AddRange(items);
            while (slots.Count < shown.Count) slots.Add(Instantiate(slotTemplate, slotTemplate.transform.parent));
            for (int i = 0; i < slots.Count; i++)
            {
                bool used = i < shown.Count;
                slots[i].gameObject.SetActive(used);
                if (!used) continue;
                // The first item at the top, the rest clockwise.
                float angle = 90 - 360f * i / shown.Count;
                var at = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * radius;
                slots[i].Rect.anchoredPosition = new Vector2(Mathf.Round(at.x), Mathf.Round(at.y));
                slots[i].Show(shown[i], shown[i] == inHand);
            }
            aim = Vector2.zero;
            Highlighted = null;
            IsOpen = true;
            SetVisible(true);
            Refresh();
        }

        public void Close()
        {
            IsOpen = false;
            Highlighted = null;
            SetVisible(false);
        }

        void Update()
        {
            if (!IsOpen) return;
            var mouse = Mouse.current;
            if (mouse != null) aim = Vector2.ClampMagnitude(aim + mouse.delta.ReadValue() * sensitivity, pointerReach);
            Refresh();
        }

        void Refresh()
        {
            if (pointer) pointer.anchoredPosition = new Vector2(Mathf.Round(aim.x), Mathf.Round(aim.y));
            Highlighted = null;
            if (aim.magnitude >= deadZone && shown.Count > 0)
            {
                float best = float.MaxValue;
                for (int i = 0; i < shown.Count; i++)
                {
                    float gap = Vector2.Angle(aim, slots[i].Rect.anchoredPosition);
                    if (gap < best) { best = gap; Highlighted = shown[i]; }
                }
            }
            for (int i = 0; i < shown.Count; i++) slots[i].SetHighlighted(shown[i] == Highlighted);
        }

        void SetVisible(bool visible)
        {
            if (!group) return;
            group.alpha = visible ? 1 : 0;
            group.interactable = group.blocksRaycasts = false;
        }
    }
}
