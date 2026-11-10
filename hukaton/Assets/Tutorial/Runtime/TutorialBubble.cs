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
        public TMP_Text continueHint;

        public bool Visible => group && group.alpha > 0;

        public void Say(string line, bool waitForPlayer)
        {
            bubble.SetText(line);
            group.alpha = 1;
            if (continueHint) continueHint.gameObject.SetActive(waitForPlayer);
        }

        public void Hide()
        {
            if (group) group.alpha = 0;
        }

        void Awake() => Hide();
    }
}
