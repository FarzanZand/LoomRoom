namespace MFPC
{
    using UnityEngine;

    public class WaterArea_NewInputSystem : MonoBehaviour
    {
        private void OnTriggerEnter(Collider other)
        {
            if (other.TryGetComponent(out PlayerController_NewInputSystem controller))
            {
                controller.SetInWater(true);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.TryGetComponent(out PlayerController_NewInputSystem controller))
            {
                controller.SetInWater(false);
            }
        }
    }
}