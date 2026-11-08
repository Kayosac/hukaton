using UnityEngine;

namespace Hakaton.Character
{
    public sealed class PlayerStamina : MonoBehaviour
    {
        [Min(1)] public float capacity = 100;
        [Min(0)] public float sprintDrainPerSecond = 20;
        [Min(0)] public float regenerationPerSecond = 25;
        [Min(0)] public float regenerationDelay = 1;
        [Min(0)] public float jumpCost = 12;
        [Range(.01f, 1)] public float exhaustionRecoveryThreshold = .25f;
        StaminaState state;
        public float Value => state == null ? capacity : state.Value;
        public float Normalized => state == null ? 1 : state.Normalized;
        public bool Exhausted => state != null && state.Exhausted;
        public bool CanSprint => state != null && state.CanSprint;

        void Awake() => state = new StaminaState(capacity, sprintDrainPerSecond, regenerationPerSecond,
            regenerationDelay, exhaustionRecoveryThreshold);
        public bool TryJump() => state != null && state.TrySpend(jumpCost);
        public void Tick(float deltaTime, bool sprinting) => state?.Tick(deltaTime, sprinting);
    }
}
