using Febucci.TextAnimatorForUnity;
using TMPro;
using UnityEngine;

namespace Hakaton.DialogueUI
{
    /// <summary>
    /// A spoken line printed letter by letter (Text Animator's typewriter on the same TMP label).
    /// Setting the same line again does not restart it; a new line starts typing from the first letter.
    /// </summary>
    [DisallowMultipleComponent, RequireComponent(typeof(TMP_Text))]
    public sealed class TypedLine : MonoBehaviour
    {
        [SerializeField] TypewriterComponent typewriter;

        string line;

        /// <summary>Letters are still appearing.</summary>
        public bool IsTyping => typewriter && typewriter.isActiveAndEnabled && typewriter.IsShowingText;

        public void Show(string text)
        {
            text ??= string.Empty;
            if (text == line) return;
            line = text;
            if (typewriter && typewriter.isActiveAndEnabled) typewriter.ShowText(text);
            else GetComponent<TMP_Text>().text = text;
        }

        /// <summary>Shows the rest of the line at once. True if it was still typing.</summary>
        public bool Skip()
        {
            if (!IsTyping) return false;
            typewriter.SkipTypewriter();
            return true;
        }

        /// <summary>Prints the line on <paramref name="label"/>: typed if it carries a <see cref="TypedLine"/>, plain otherwise.</summary>
        public static void Say(TMP_Text label, string text)
        {
            if (!label) return;
            if (label.TryGetComponent(out TypedLine typed)) typed.Show(text);
            else label.text = text;
        }

        void OnDisable() => line = null;
    }
}
