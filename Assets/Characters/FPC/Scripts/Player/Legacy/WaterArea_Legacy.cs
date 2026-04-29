namespace MFPC
{
    using UnityEngine;

    public class WaterArea_Legacy : MonoBehaviour
    {
        private void OnTriggerEnter(Collider other)
        {
            if (other.TryGetComponent(out PlayerController_Legacy controller))
            {
                controller.SetInState(true);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.TryGetComponent(out PlayerController_Legacy controller))
            {
                controller.SetInState(false);
            }
        }
    }
}