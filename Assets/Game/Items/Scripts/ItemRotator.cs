using UnityEngine;

public class ItemRotator : MonoBehaviour
{
    void Awake()
    {
        if (transform.childCount == 0) return;

        Transform child = transform.GetChild(0);

        Vector3 euler = child.localEulerAngles;
        euler.x = -40f;
        child.localEulerAngles = euler;

        Rigidbody rb = child.GetComponent<Rigidbody>();
        if (rb != null)
            Destroy(rb);
    }
}
