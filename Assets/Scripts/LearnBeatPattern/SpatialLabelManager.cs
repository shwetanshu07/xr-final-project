using UnityEngine;
using TMPro;

// Controls the floating spatial label above the beat path.
// Shows current instruction or status text.
// Called by SceneManager to update text at each stage.

public class SpatialLabelManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject labelCanvas;
    [SerializeField] private TextMeshProUGUI labelText;

    // Shows the label with the given text
    public void SetText(string text)
    {
        if (labelCanvas == null || labelText == null)
        {
            Debug.LogWarning("[SpatialLabel] References not assigned");
            return;
        }

        labelText.text = text;
        labelCanvas.SetActive(true);
        Debug.Log($"[SpatialLabel] Text set to: {text}");
    }

    // Hides the label completely
    public void Hide()
    {
        if (labelCanvas != null)
            labelCanvas.SetActive(false);
    }
}