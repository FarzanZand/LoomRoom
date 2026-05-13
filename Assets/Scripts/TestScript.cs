using UnityEngine;

public class TestScript : MonoBehaviour, IInteractable
{
    public void TestEvent()
    {
        Debug.Log("TestEvent");
    }

    public void Interact(GameObject interactor)
    {
        Debug.Log("Interactasdasda");
    }
}
