using UnityEngine;

namespace Hakaton.Character
{
    public sealed class PlayerStamina : MonoBehaviour
    {
        [Tooltip("Base maximum, before «Выносливость» steps bought on the PC.")]
        [Min(1)] public float capacity = 100;
        [Min(0)] public float sprintDrainPerSecond = 20;
        [Min(0)] public float regenerationPerSecond = 25;
        [Min(0)] public float regenerationDelay = 1;
        [Min(0)] public float jumpCost = 12;
        [Range(.01f, 1)] public float exhaustionRecoveryThreshold = .25f;

        [Header("«Выносливость» on the PC")]
        [Tooltip("Sprint seconds each bought step adds: one more pip in the arc under the crosshair (a pip is this many seconds).")]
        [Min(.1f)] public float upgradeSecondsPerStep = 1;
        [Tooltip("Price of each step, in order (5 » 6 » 7 » 8 » 9 » 10 s of sprint at the default tuning).")]
        public int[] upgradePrices = { 200, 450, 800, 1250, 1800 };

        StaminaState state;
        StaminaUpgrade upgrade;
        public float Value => state == null ? capacity : state.Value;
        /// <summary>The maximum now: the base capacity plus every bought step.</summary>
        public float Capacity => state == null ? capacity : state.Capacity;
        public float Normalized => state == null ? 1 : state.Normalized;
        public bool Exhausted => state != null && state.Exhausted;
        public bool CanSprint => state != null && state.CanSprint;
        /// <summary>The PC's «Выносливость» for this player; a change resizes the budget and fills it.</summary>
        public StaminaUpgrade Upgrade { get { Build(); return upgrade; } }
        /// <summary>Stamina of one pip in the HUD arc: what one bought step adds.</summary>
        public float StaminaPerPip => Upgrade.StepCapacity;

        void Awake() => Build();

        void Build()
        {
            if (state != null) return;
            state = new StaminaState(capacity, sprintDrainPerSecond, regenerationPerSecond,
                regenerationDelay, exhaustionRecoveryThreshold);
            var prices = upgradePrices ?? new int[0];
            var safePrices = new int[prices.Length];
            for (int i = 0; i < prices.Length; i++) safePrices[i] = Mathf.Max(0, prices[i]);
            upgrade = new StaminaUpgrade(capacity, Mathf.Max(.01f, sprintDrainPerSecond), Mathf.Max(.1f, upgradeSecondsPerStep), safePrices);
            upgrade.Changed += () => state.Resize(upgrade.Capacity);
        }

        float unlimitedUntil = float.NegativeInfinity;
        /// <summary>Seconds left of an endless-stamina boost (the phone's lightnings); 0 when none.</summary>
        public float UnlimitedRemaining => Mathf.Max(0, unlimitedUntil - Time.time);
        public bool Unlimited => Time.time < unlimitedUntil;

        /// <summary>Endless stamina for <paramref name="seconds"/>: full at once, sprinting and jumping cost nothing meanwhile.</summary>
        public void GrantUnlimited(float seconds)
        {
            Build();
            unlimitedUntil = Mathf.Max(unlimitedUntil, Time.time + Mathf.Max(0, seconds));
            state.Resize(state.Capacity);
        }

        /// <summary>Ends an endless-stamina boost at once (the outing is over).</summary>
        public void ClearUnlimited() => unlimitedUntil = float.NegativeInfinity;

        public bool TryJump() => state != null && (Unlimited || state.TrySpend(jumpCost));
        public void Tick(float deltaTime, bool sprinting) => state?.Tick(deltaTime, sprinting && !Unlimited);
    }
}
