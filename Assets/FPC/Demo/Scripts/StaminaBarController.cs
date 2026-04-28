namespace MFPC
{
    using UnityEngine;
    using UnityEngine.UI;

    public class StaminaBarController : MonoBehaviour
    {
        [SerializeField] StaminaModule staminaModule;
        [SerializeField] Slider staminaSlider;

        void LateUpdate()
        {
            if (staminaModule == null || staminaSlider == null) return;

            staminaSlider.value = staminaModule.CurrentStamina / staminaModule.MaxStamina;
        }
    }
}