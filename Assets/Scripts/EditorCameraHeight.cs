using UnityEngine;
using System.Collections;

/// <summary>
/// Editor only fix for camera height when using Floor tracking mode.
/// Uses one frame delay to run after XR Origin initialisation completes.
/// </summary>
public class EditorCameraHeight : MonoBehaviour
{
    [SerializeField] private float editorEyeHeight = 1.6f;
    [SerializeField] private Transform cameraOffset;

    void Start()
    {
#if UNITY_EDITOR
        StartCoroutine(SetHeightNextFrame());
#endif
    }

#if UNITY_EDITOR
    private IEnumerator SetHeightNextFrame()
    {
        // Wait one frame for XR Origin to finish initialising
        yield return null;

        if (cameraOffset != null)
        {
            Vector3 pos = cameraOffset.localPosition;
            pos.y = editorEyeHeight;
            cameraOffset.localPosition = pos;
            Debug.Log($"[EditorCameraHeight] Camera offset Y set to {editorEyeHeight}");
        }
        else
        {
            Debug.LogError("[EditorCameraHeight] Camera Offset not assigned");
        }
    }
#endif
}