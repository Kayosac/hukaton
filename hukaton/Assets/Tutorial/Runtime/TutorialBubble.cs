using Hakaton.DialogueUI;
using TMPro;
using UnityEngine;

namespace Hakaton.Tutorial
{
    /// <summary>The goblin's speech over the game view: the stitched linen bubble and «E — дальше» while a line waits.</summary>
    public sealed class TutorialBubble : MonoBehaviour
    {
        public CanvasGroup group;
        public DialogueBubbleLayout bubble;
        [Tooltip("«E — дальше» in the bottom-right corner while a line waits.")]
        public ContinueHint continueHint;

        public bool Visible => group && group.alpha > 0;

        /// <summary>A press while the line is still typing only finishes the line.</summary>
        public bool SkipTyping() => bubble && bubble.SkipTyping();

        public void Say(string line, bool waitForPlayer)
        {
            bubble.SetText(line);
            group.alpha = 1;
            if (continueHint) continueHint.Show(waitForPlayer);
        }

        public void Hide()
        {
            if (group) group.alpha = 0;
            if (bubble) bubble.ForgetLine();
        }

        void Awake() => Hide();
    }
}
