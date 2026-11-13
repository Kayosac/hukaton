using System;
using System.Collections;
using Hakaton.Character;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Hakaton.Tutorial
{
    /// <summary>
    /// A one-off word from the goblin in the shop (the game plays it after the first drowning). He stands at
    /// <see cref="spot"/> in front of the player while the screen is still dark, says his lines on the shared speech
    /// plate as the picture comes back (E or the left button for the next one), then leaves in a poof and the player is
    /// free. Movement is held while he talks, the pause menu may cover it: the same pattern as the shop tutorial's last lines.
    /// Another goblin can be given instead (the counter one, back from the god): he talks where he stands.
    /// </summary>
    public sealed class GoblinLecture : MonoBehaviour
    {
        [Header("Scene")]
        public PlayerControlGate control;
        [Tooltip("The player's view: turned to the goblin while the screen is dark, then held on him while he talks.")]
        public FirstPersonView view;
        [Tooltip("The speech plate shared with the cave and shop tutorials (the scene's TutorialBubble).")]
        public TutorialBubble bubble;
        [Tooltip("A goblin of his own (not the shop tutorial's), inactive until he is needed.")]
        public CounterGoblin goblin;
        [Tooltip("Where he stands: on the floor in front of the spot the player comes home to.")]
        public Transform spot;
        [Header("Timing and framing")]
        [Tooltip("Seconds from the start (the screen still dark and fading in) to the first line.")]
        [Min(0)] public float delayAfterFade = .55f;
        [Tooltip("The view is held on this point, metres above his feet: he stands low in the frame, the plate keeps the top.")]
        [Min(0)] public float lookHeight = .9f;

        /// <summary>He is in the shop and talking (or about to): movement is held, the door stays shut.</summary>
        public bool Active { get; private set; }
        /// <summary>Index of the line on the plate; -1 before the first and after the last.</summary>
        public int Line { get; private set; } = -1;
        /// <summary>Raised once, after the last line is closed and he is gone.</summary>
        public event Action Finished;

        bool continueRequested;
        int suspendedFrame = -10;

        /// <summary>Starts the lecture at once (call it while the screen is still dark). False if one runs or it cannot run.</summary>
        public bool Play(string[] lines) => Play(lines, null);

        /// <summary>
        /// As <see cref="Play(string[])"/>, but <paramref name="standing"/> (when given) talks instead of the lecture's own
        /// goblin, where he stands (on the counter), the view held on his face.
        /// </summary>
        public bool Play(string[] lines, CounterGoblin standing)
        {
            if (Active || lines == null || lines.Length == 0 || !isActiveAndEnabled || !bubble) return false;
            if (!standing && (!goblin || !spot)) return false;
            StartCoroutine(Run(lines, standing));
            return true;
        }

        IEnumerator Run(string[] lines, CounterGoblin standing)
        {
            Active = true;
            Line = -1;
            continueRequested = false;
            var who = standing ? standing : goblin;
            if (!standing) goblin.transform.SetPositionAndRotation(spot.position, spot.rotation);
            who.Show(true);
            if (control) { control.AllowPause(this, true); control.SetBlocked(this, true); }
            Vector3 look = standing ? standing.face.transform.position : spot.position + Vector3.up * lookHeight;
            if (view)
            {
                // Out of the dark already facing him; then the view stays on him while he talks.
                Vector3 toGoblin = look - view.pitchPivot.position;
                if (toGoblin.sqrMagnitude > .0001f) view.ResetPose(Quaternion.LookRotation(toGoblin));
                view.Guide(look);
            }
            // The first line starts typing as the picture comes back.
            float waited = 0;
            while (waited < delayAfterFade)
            {
                if (!GamePause.Suspended) waited += Time.unscaledDeltaTime;
                yield return null;
            }
            for (int i = 0; i < lines.Length; i++)
            {
                Line = i;
                bubble.Say(lines[i], true);
                who.Speak(i == 0);
                continueRequested = false;
                yield return null;
                while (!continueRequested) yield return null;
                continueRequested = false;
                bubble.Hide();
            }
            Line = -1;
            // Gone in a puff of smoke, and the shop is the player's again.
            if (who.isActiveAndEnabled) who.Poof();
            Release();
            Active = false;
            Finished?.Invoke();
        }

        void Update()
        {
            if (!Active) return;
            if (GamePause.Suspended) { suspendedFrame = Time.frameCount; return; }
            // The key or click that closed a menu does not also close the line.
            if (Time.frameCount - suspendedFrame <= 1) return;
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if ((keyboard != null && keyboard.eKey.wasPressedThisFrame) || (mouse != null && mouse.leftButton.wasPressedThisFrame)) Continue();
        }

        /// <summary>Closes the line on screen, as E does; a press while it still types only finishes it.</summary>
        public void Continue()
        {
            if (Active && Line >= 0 && !continueRequested && bubble && !bubble.SkipTyping()) continueRequested = true;
        }

        void Release()
        {
            if (view) view.Guide(null);
            if (control) control.SetBlocked(this, false);
        }

        void OnDisable()
        {
            // Leaving Play or unloading the scene mid-lecture: the player is released; nothing counts as heard.
            if (Active && bubble) bubble.Hide();
            Active = false;
            Line = -1;
            Release();
        }
    }
}
