using Hakaton.Character;
using UnityEngine;
using UnityEngine.Audio;

namespace Hakaton.Game
{
    /// <summary>
    /// Puts <see cref="GameSettings"/> into the game: the overall volume on the listener, music and effects on the
    /// mixer's exposed attenuations, the mouse sensitivity on the player's view. Reapplies whenever a value changes.
    /// </summary>
    public sealed class SettingsApplier : MonoBehaviour
    {
        [Tooltip("Game Audio mixer: every sound in the game goes through its Music or Effects group.")]
        public AudioMixer mixer;
        public string musicParameter = "MusicVolume", effectsParameter = "EffectsVolume";
        public FirstPersonView view;

        void OnEnable()
        {
            GameSettings.Changed += Apply;
            Apply();
        }

        // The mixer ignores SetFloat before its first frame: apply again once it is live.
        void Start() => Apply();

        void OnDisable() => GameSettings.Changed -= Apply;

        public void Apply()
        {
            AudioListener.volume = GameSettings.Master;
            if (mixer)
            {
                mixer.SetFloat(musicParameter, GameSettings.ToDecibels(GameSettings.Music));
                mixer.SetFloat(effectsParameter, GameSettings.ToDecibels(GameSettings.Effects));
            }
            if (view) view.sensitivityScale = GameSettings.Sensitivity;
        }
    }
}
