namespace MFPC
{
    using UnityEngine;

    public class StaminaModule : MonoBehaviour
    {
        [Header("Capacity")]

        [Tooltip("Maximum stamina capacity.")]
        [SerializeField] private float maxStamina = 5f;
        [Tooltip("Stamina amount available at startup.")]
        [SerializeField] private float startingStamina = 5f;


        [Header("Sprint Drain")]

        [Tooltip("Stamina drained per second while sprint speed is actively applied.")]
        [SerializeField] private float sprintDrainPerSecond = 1f;
        [Tooltip("Stamina spent immediately when jumping.")]
        [SerializeField] private float jumpStaminaCost = 0.5f;


        [Header("Regeneration")]

        [Tooltip("Delay before stamina starts regenerating after sprinting stops.")]
        [SerializeField] private float regenerationDelay = 0.6f;
        [Tooltip("Stamina regenerated per second after the delay has elapsed.")]
        [SerializeField] private float regenerationPerSecond = 0.9f;
        [Tooltip("Minimum stamina required before sprint becomes available again after exhaustion.")]
        [SerializeField] private float staminaRecoveryThreshold = 0.75f;


        public float CurrentStamina { get; private set; }
        public float MaxStamina => maxStamina;
        public float NormalizedStamina => maxStamina > 0f ? CurrentStamina / maxStamina : 0f;
        public bool IsExhausted { get; private set; }
        public bool AllowsSprint => !enabled || (!IsExhausted && CurrentStamina > 0f);

        private float regenDelayTimer;


        private void Awake()
        {
            CurrentStamina = Mathf.Clamp(startingStamina, 0f, maxStamina);
            IsExhausted = CurrentStamina <= 0f;
        }

        private void OnValidate()
        {
            maxStamina = Mathf.Max(0.1f, maxStamina);
            startingStamina = Mathf.Clamp(startingStamina, 0f, maxStamina);
            sprintDrainPerSecond = Mathf.Max(0f, sprintDrainPerSecond);
            jumpStaminaCost = Mathf.Max(0f, jumpStaminaCost);
            regenerationDelay = Mathf.Max(0f, regenerationDelay);
            regenerationPerSecond = Mathf.Max(0f, regenerationPerSecond);
            staminaRecoveryThreshold = Mathf.Clamp(staminaRecoveryThreshold, 0f, maxStamina);
        }

        public bool TryConsumeJump()
        {
            if (!enabled)
            {
                return true;
            }

            if (jumpStaminaCost <= 0f)
            {
                return true;
            }

            if (CurrentStamina < jumpStaminaCost)
            {
                return false;
            }

            CurrentStamina = Mathf.Max(0f, CurrentStamina - jumpStaminaCost);
            regenDelayTimer = regenerationDelay;
            if (CurrentStamina <= 0f)
            {
                CurrentStamina = 0f;
                IsExhausted = true;
            }

            return true;
        }

        public void UpdateStamina(bool sprintSpeedApplied, bool isAirborne)
        {
            if (!enabled)
            {
                return;
            }

            if (sprintSpeedApplied)
            {
                regenDelayTimer = regenerationDelay;
                CurrentStamina = Mathf.Max(0f, CurrentStamina - sprintDrainPerSecond * Time.deltaTime);
                if (CurrentStamina <= 0f)
                {
                    CurrentStamina = 0f;
                    IsExhausted = true;
                }

                return;
            }

            if (regenDelayTimer > 0f)
            {
                regenDelayTimer = Mathf.Max(0f, regenDelayTimer - Time.deltaTime);
                return;
            }

            if (isAirborne)
            {
                return;
            }

            CurrentStamina = Mathf.Min(maxStamina, CurrentStamina + regenerationPerSecond * Time.deltaTime);
            if (IsExhausted && CurrentStamina >= staminaRecoveryThreshold)
            {
                IsExhausted = false;
            }
        }

        public void Refill()
        {
            CurrentStamina = maxStamina;
            IsExhausted = false;
            regenDelayTimer = 0f;
        }
    }
}