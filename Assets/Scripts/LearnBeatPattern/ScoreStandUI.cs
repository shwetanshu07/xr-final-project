using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;

// Manages all UI on the score stand.
// Canvas 1 - beat selection buttons
// Canvas 2 - beat info (heading, image, description)
// Controls label toggle
// Fires OnBeatSelected event when user picks a pattern
// SceneManager listens to this event to start the beat selected sequence

public class ScoreStandUI : MonoBehaviour
{
    [Header("Canvas 1 - Beat Selection")]
    [SerializeField] private GameObject canvas1BeatSelection;
    [SerializeField] private Button buttonTwoBeat;
    [SerializeField] private Button buttonThreeBeat;
    [SerializeField] private Button buttonFourBeat;

    [Header("Canvas 2 - Beat Info")]
    [SerializeField] private GameObject canvas2BeatInfo;
    [SerializeField] private TextMeshProUGUI beatHeadingText;
    [SerializeField] private Image beatPatternImage;
    [SerializeField] private TextMeshProUGUI beatDescriptionText;
    [SerializeField] private Button controlsButton;

    [Header("Beat Pattern Images")]
    [SerializeField] private Sprite twoBeatSprite;
    [SerializeField] private Sprite threeBeatSprite;
    [SerializeField] private Sprite fourBeatSprite;

    [Header("Controls Label")]
    [SerializeField] private GameObject controlsLabelCanvas;

    [Header("Try Again")]
    [SerializeField] private Button tryAgainButton;

    // SceneManager subscribes to this event
    public event Action<BeatPattern> OnBeatSelected;

    private bool controlsLabelVisible = false;

    [Header("GIF Tutorial")]
    // The canvas that appears when tutorial button is clicked
    [SerializeField] private GameObject  tutorialCanvas;
    // RawImage inside tutorialCanvas that displays each frame
    [SerializeField] private RawImage    tutorialDisplay;
    // Button on Canvas 2 to open/close tutorial
    [SerializeField] private Button      tutorialBtn;
    // Close button on the tutorial canvas
    [SerializeField] private Button      tutorialCloseBtn;
    // Frames for each pattern — drag all PNGs in order
    [SerializeField] private Texture2D[] twoBeatFrames;
    [SerializeField] private Texture2D[] threeBeatFrames;
    // How many frames per second to play
    [SerializeField] private float frameRate = 10f;

    private bool      isTutorialPlaying = false;
    private Coroutine animCoroutine;
    private BeatPattern currentPattern = BeatPattern.None;

    [Header("Audio Tutorial")]
    [SerializeField] private Button      audioTutorialBtn;
    [SerializeField] private AudioSource tutorialAudioSource;
    [SerializeField] private AudioClip   twoBeatAudioClip;
    [SerializeField] private AudioClip   threeBeatAudioClip;

    private bool isAudioPlaying = false;

    // ── UNITY LIFECYCLE ───────────────────────────────────────────

    void Start()
    {
        buttonTwoBeat.onClick.AddListener(()  => HandleBeatButtonClicked(BeatPattern.TwoBeat));
        buttonThreeBeat.onClick.AddListener(() => HandleBeatButtonClicked(BeatPattern.ThreeBeat));
        buttonFourBeat.onClick.AddListener(()  => HandleBeatButtonClicked(BeatPattern.FourBeat));
        controlsButton.onClick.AddListener(HandleControlsButtonClicked);

        if (tutorialBtn != null)
            tutorialBtn.onClick.AddListener(HandleTutorialButtonClicked);
        if (tutorialCloseBtn != null)
            tutorialCloseBtn.onClick.AddListener(StopTutorial);
        if (audioTutorialBtn != null)
            audioTutorialBtn.onClick.AddListener(HandleAudioButtonClicked);

        ShowBeatSelection();
    }

    void OnDestroy()
    {
        buttonTwoBeat.onClick.RemoveAllListeners();
        buttonThreeBeat.onClick.RemoveAllListeners();
        buttonFourBeat.onClick.RemoveAllListeners();
        controlsButton.onClick.RemoveAllListeners();

        if (tutorialBtn != null)     tutorialBtn.onClick.RemoveAllListeners();
        if (tutorialCloseBtn != null) tutorialCloseBtn.onClick.RemoveAllListeners();
        if (audioTutorialBtn != null) audioTutorialBtn.onClick.RemoveAllListeners();
    }

    // ── PUBLIC METHODS ────────────────────────────────────────────

    public void ShowBeatSelection()
    {
        canvas1BeatSelection.SetActive(true);
        canvas2BeatInfo.SetActive(false);

        if (controlsLabelCanvas != null)
            controlsLabelCanvas.SetActive(false);

        controlsLabelVisible = false;
    }

    public void ShowBeatInfo(BeatPattern pattern)
    {
        Debug.Log($"[BeatPatternDebug] ShowBeatInfo called for {pattern}");

        canvas1BeatSelection.SetActive(false);
        canvas2BeatInfo.SetActive(true);

        beatHeadingText.text     = GetHeadingForPattern(pattern);
        beatDescriptionText.text = GetDescriptionForPattern(pattern);
        beatPatternImage.sprite  = GetSpriteForPattern(pattern);

        if (controlsLabelCanvas != null)
            controlsLabelCanvas.SetActive(false);

        controlsLabelVisible = false;

        currentPattern = pattern;
        StopTutorial();
        StopAudioTutorial();
    }

    public void ShowTryAgain(bool show)
    {
        if (tryAgainButton != null)
            tryAgainButton.gameObject.SetActive(show);
    }

    // ── PRIVATE HELPERS ───────────────────────────────────────────

    private void HandleBeatButtonClicked(BeatPattern pattern)
    {
        Debug.Log($"[ScoreStandUI] Beat selected: {pattern}");
        OnBeatSelected?.Invoke(pattern);
    }

    private bool isProcessingClick = false;

    private void HandleControlsButtonClicked()
    {
        if (isProcessingClick) return;
        isProcessingClick    = true;
        controlsLabelVisible = !controlsLabelVisible;
        controlsLabelCanvas.SetActive(controlsLabelVisible);
        StartCoroutine(ResetClickFlag());
    }

    private IEnumerator ResetClickFlag()
    {
        yield return new WaitForSeconds(0.3f);
        isProcessingClick = false;
    }

    private string GetHeadingForPattern(BeatPattern pattern)
    {
        switch (pattern)
        {
            case BeatPattern.TwoBeat:   return "2/4 — Two Beat";
            case BeatPattern.ThreeBeat: return "3/4 — Three Beat";
            case BeatPattern.FourBeat:  return "4/4 — Four Beat";
            default:                    return "";
        }
    }

    private string GetDescriptionForPattern(BeatPattern pattern)
    {
        switch (pattern)
        {
            case BeatPattern.TwoBeat:
                return "Move your hand in a simple two-stroke down-up motion,\n" +
                       "creating a mirrored J shape. Beat 1 goes straight down (downbeat),\n" +
                       "and beat 2 moves up (upbeat) creating a 1-2, 1-2 pulse.";

            case BeatPattern.ThreeBeat:
                return "Move DOWN for beat 1.\n" +
                       "Move RIGHT for beat 2.\n" +
                       "Move UP for beat 3.\n" +
                       "Three beats per measure — common in waltz music.";

            case BeatPattern.FourBeat:
                return "Move your baton DOWN, LEFT, RIGHT, then UP.\n" +
                       "Four beats per measure.\n" +
                       "The most common pattern in orchestral music.";

            default:
                return "";
        }
    }

    private Sprite GetSpriteForPattern(BeatPattern pattern)
    {
        switch (pattern)
        {
            case BeatPattern.TwoBeat:   return twoBeatSprite;
            case BeatPattern.ThreeBeat: return threeBeatSprite;
            case BeatPattern.FourBeat:  return fourBeatSprite;
            default:                    return null;
        }
    }

    // ── GIF TUTORIAL ──────────────────────────────────────────────

    private void HandleTutorialButtonClicked()
    {
        if (isProcessingClick) return;
        isProcessingClick = true;
        StartCoroutine(ResetClickFlag());

        if (isTutorialPlaying)
        {
            StopTutorial();
            return;
        }
        PlayTutorialForPattern(currentPattern);
    }

    private void PlayTutorialForPattern(BeatPattern pattern)
    {
        Texture2D[] frames = pattern == BeatPattern.TwoBeat
            ? twoBeatFrames
            : threeBeatFrames;

        if (frames == null || frames.Length == 0)
        {
            Debug.LogWarning($"[ScoreStandUI] No frames assigned for {pattern}");
            return;
        }
        if (tutorialCanvas == null || tutorialDisplay == null) return;

        tutorialCanvas.SetActive(true);
        isTutorialPlaying = true;
        animCoroutine     = StartCoroutine(PlayFrames(frames));

        Debug.Log($"[ScoreStandUI] Playing GIF tutorial for {pattern} — {frames.Length} frames at {frameRate}fps");
    }

    private IEnumerator PlayFrames(Texture2D[] frames)
    {
        float delay = 1f / frameRate;

        while (isTutorialPlaying)
        {
            for (int i = 0; i < frames.Length; i++)
            {
                if (!isTutorialPlaying) yield break;
                tutorialDisplay.texture = frames[i];
                yield return new WaitForSeconds(delay);
            }
        }
    }

    public void StopTutorial()
    {
        if (animCoroutine != null)
        {
            StopCoroutine(animCoroutine);
            animCoroutine = null;
        }
        if (tutorialCanvas != null)
            tutorialCanvas.SetActive(false);

        isTutorialPlaying = false;
    }

    // ── AUDIO TUTORIAL ────────────────────────────────────────────

    private void HandleAudioButtonClicked()
    {
        if (isProcessingClick) return;
        isProcessingClick = true;
        StartCoroutine(ResetClickFlag());

        if (isAudioPlaying)
        {
            StopAudioTutorial();
            return;
        }
        PlayAudioForPattern(currentPattern);
    }

    private void PlayAudioForPattern(BeatPattern pattern)
    {
        AudioClip clip = pattern == BeatPattern.TwoBeat
            ? twoBeatAudioClip
            : threeBeatAudioClip;

        if (clip == null)
        {
            Debug.LogWarning($"[ScoreStandUI] No audio clip for {pattern}");
            return;
        }
        if (tutorialAudioSource == null) return;

        tutorialAudioSource.clip = clip;
        tutorialAudioSource.loop = true;
        tutorialAudioSource.Play();
        isAudioPlaying = true;

        Debug.Log($"[ScoreStandUI] Playing audio for {pattern}");
    }

    public void StopAudioTutorial()
    {
        if (tutorialAudioSource != null)
            tutorialAudioSource.Stop();
        isAudioPlaying = false;
    }
}