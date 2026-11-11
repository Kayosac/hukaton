using Febucci.TextAnimatorForUnity;
using Hakaton.DialogueUI;
using TMPro;
using UnityEngine;

namespace Hakaton.Teaser
{
    /// <summary>The customer's line on the shop's own speech plate, typed by its Text Animator typewriter.</summary>
    public sealed class TeaserSpeech : TeaserTrack
    {
        public GameObject plate;
        public TMP_Text label;
        [Tooltip("The label's Text Animator, set to the Script loop: the teaser advances it by exactly one frame of teaser time.")]
        public TextAnimatorComponentBase animator;
        [TextArea] public string line = "А хозяин не придёт?";
        public float showSeconds = 12, hideSeconds = 15;
        [Tooltip("Typing speed relative to the game (the teaser wants the whole line within about 0.7 s).")]
        [Min(.1f)] public float typingSpeed = 1;

        bool said;
        float last = float.NaN;

        public override void Evaluate(float seconds)
        {
            float delta = float.IsNaN(last) ? 0 : Mathf.Max(0, seconds - last);
            last = seconds;
            bool visible = seconds >= showSeconds && seconds < hideSeconds;
            if (plate && plate.activeSelf != visible) plate.SetActive(visible);
            if (visible && !said && label)
            {
                TypedLine.Say(label, line);
                said = true;
            }
            if (visible && animator && animator.animationLoop == AnimationLoop.Script) animator.Animate(delta * typingSpeed);
        }
    }
}
