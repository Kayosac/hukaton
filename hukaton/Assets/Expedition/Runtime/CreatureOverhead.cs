using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>Billboard above a creature: alert bubble (! / !!) and health pips after it is hurt.</summary>
    public sealed class CreatureOverhead : MonoBehaviour
    {
        public SpriteRenderer alert;
        public Sprite alertCalm, alertDanger;
        public SpriteRenderer[] pips;
        public Sprite pipFull, pipEmpty;
        [Min(0)] public float healthSeconds = 2.5f;
        [Tooltip("Closer than this the marks shrink with distance, so they never swallow the screen.")]
        [Min(.1f)] public float fullSizeDistance = 7;

        int alertLevel;
        float healthUntil;
        SpringFloat alertScale, pipsScale;
        Camera viewer;

        /// <summary>0 hides, 1 shows a yellow "!", 2 a red "!!" (attack incoming).</summary>
        public void SetAlert(int level)
        {
            if (level == alertLevel) return;
            if (level > alertLevel) alertScale.Kick(14);
            alertLevel = level;
            if (level > 0) alert.sprite = level >= 2 ? alertDanger : alertCalm;
        }

        public void ShowHealth(int current, int maximum)
        {
            healthUntil = Time.time + healthSeconds;
            pipsScale.Kick(8);
            for (int i = 0; i < pips.Length; i++)
            {
                pips[i].gameObject.SetActive(i < maximum);
                pips[i].sprite = i < current ? pipFull : pipEmpty;
            }
        }

        public void Clear()
        {
            alertLevel = 0;
            healthUntil = 0;
            alertScale.Snap(0);
            pipsScale.Snap(0);
            Apply();
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            alertScale.Step(alertLevel > 0 ? 1 : 0, 6, .45f, dt);
            pipsScale.Step(Time.time < healthUntil ? 1 : 0, 6, .6f, dt);
            if (!viewer) viewer = Camera.main;
            float near = 1;
            if (viewer)
            {
                Vector3 toMarks = transform.position - viewer.transform.position;
                transform.rotation = Quaternion.LookRotation(toMarks);
                near = Mathf.Clamp(toMarks.magnitude / fullSizeDistance, .35f, 1);
            }
            Apply(near);
        }

        void Apply(float near = 1)
        {
            float a = Mathf.Max(0, alertScale.Value) * near;
            alert.transform.localScale = Vector3.one * a;
            alert.enabled = a > .02f;
            float p = Mathf.Max(0, pipsScale.Value) * near;
            foreach (var pip in pips) { pip.transform.localScale = Vector3.one * p * .5f; pip.enabled = p > .02f; }
        }
    }
}
