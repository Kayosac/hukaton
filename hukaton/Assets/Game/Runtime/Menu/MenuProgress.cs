using UnityEngine;

namespace Hakaton.Game
{
    /// <summary>
    /// What survives a new game: whether the whole tutorial (cave and the two lesson sales) has ever been finished on
    /// this machine. Only then may «Новая игра» skip it. Kept apart from the save slot, so replacing the save keeps it.
    /// </summary>
    public static class MenuProgress
    {
        const string TutorialKey = "penus.tutorialCompleted";
        static bool? tutorialCompleted;

        public static bool TutorialCompleted => tutorialCompleted ??= PlayerPrefs.GetInt(TutorialKey, 0) == 1;

        public static void MarkTutorialCompleted()
        {
            if (TutorialCompleted) return;
            tutorialCompleted = true;
            PlayerPrefs.SetInt(TutorialKey, 1);
            PlayerPrefs.Save();
        }

        /// <summary>Tests and debugging: forget the unlock.</summary>
        public static void ResetForTests()
        {
            tutorialCompleted = null;
            PlayerPrefs.DeleteKey(TutorialKey);
        }
    }
}
