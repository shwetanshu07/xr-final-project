using UnityEngine;

// Simple audio manager for playing sound effects in Scene 2.
// Singleton pattern - access from anywhere with AudioManager.Instance
// Add AudioSource components in Inspector for each sound category.
//
// HOW TO ADD A NEW SOUND:
// 1. Add an AudioClip field with SerializeField
// 2. Add a public method to play it
// 3. Assign the clip in Inspector

public class AudioManager : MonoBehaviour
{
    // Singleton instance - only one AudioManager exists in the scene
    public static AudioManager Instance { get; private set; }

    [Header("Audio Source")]
    // One AudioSource handles all SFX
    [SerializeField] private AudioSource sfxSource;

    [Header("Sound Effects")]
    [SerializeField] private AudioClip successClip;
    [SerializeField] private AudioClip failClip;
    // [SerializeField] private AudioClip welcomeClip;

    void Awake()
    {
        // Set up singleton
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    // Plays success sound - called by SceneManager on successful trace
    public void PlaySuccess()
    {
        PlayClip(successClip, "success");
    }

    // Plays fail sound - called by SceneManager on failed trace
    public void PlayFail()
    {
        PlayClip(failClip, "fail");
    }

    // Plays welcome audio when scene loads
    // public void PlayWelcome()
    // {
    //     PlayClip(welcomeClip, "welcome");
    // }

    // Generic clip player with null check and log
    public void PlayClip(AudioClip clip, string clipName)
    {
        if (clip == null)
        {
            Debug.LogWarning($"[AudioManager] {clipName} clip not assigned");
            return;
        }

        sfxSource.PlayOneShot(clip);
        Debug.Log($"[AudioManager] Playing {clipName}");
    }
}