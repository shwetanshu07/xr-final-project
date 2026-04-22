using UnityEngine;

// Controls the baton visual in the player's right hand.
// Purely visual - positions and rotates baton to match controller.
// All tracing logic is handled by TracingSystem using the ray.
// Adjust positionOffset and rotationOffset in Inspector during Play mode
// until the baton sits correctly in the hand.

public class BatonController : MonoBehaviour
{
    [Header("References")]
    // Right Controller transform - found under XR Origin
    [SerializeField] private Transform rightHandTransform;

    [Header("Baton Offset")]
    // Adjust in Inspector during Play mode to position baton correctly in hand
    [SerializeField] private Vector3 positionOffset = new Vector3(0f, -0.05f, 0.1f);
    [SerializeField] private Vector3 rotationOffset = new Vector3(90f, 0f, 0f);

    void LateUpdate()
    {
        if (rightHandTransform == null)
        {
            Debug.LogWarning("[BatonController] Right hand transform not assigned");
            return;
        }

        // Attach baton to right hand with offset
        transform.position = rightHandTransform.position +
                             rightHandTransform.TransformDirection(positionOffset);
        transform.rotation = rightHandTransform.rotation *
                             Quaternion.Euler(rotationOffset);
    }
}