using UnityEngine;
using UnityEngine.XR;
using System.Collections.Generic;

public class VRDebugLogger : MonoBehaviour
{
    void Start()
    {
        Debug.Log("=== VR Debug: Scene Started ===");
        Debug.Log("XR Settings present device: " + XRSettings.loadedDeviceName);
        Debug.Log("Is device active: " + XRSettings.isDeviceActive);
        Debug.Log("===============================");
    }
}