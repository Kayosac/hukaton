namespace Hakaton.Guide
{
    /// <summary>Whoever speaks a tour's lines: the goblin's linen plate at the brazier, the PC's helper in its balloon.</summary>
    public interface IGuideVoice
    {
        /// <summary>Shows a line (typed). <paramref name="waitsForPlayer"/>: the «дальше» hint is shown.</summary>
        void Say(string line, bool waitsForPlayer);
        void Hide();
        /// <summary>A press while the line is still typing only finishes it: true then.</summary>
        bool SkipTyping();
        /// <summary>Where the plate stands, so it keeps clear of the lit place (local rect in the guide's overlay, or null).</summary>
        void KeepClearOf(UnityEngine.Rect? lit, UnityEngine.RectTransform overlay);
    }
}
