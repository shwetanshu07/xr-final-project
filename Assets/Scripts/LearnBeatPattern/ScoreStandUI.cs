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
    // Assign these in Inspector - drag sprite for each pattern
    [SerializeField] private Sprite twoBeatSprite;
    [SerializeField] private Sprite threeBeatSprite;
    [SerializeField] private Sprite fourBeatSprite;

    [Header("Controls Label")]
    [SerializeField] private GameObject controlsLabelCanvas;

    // SceneManager subscribes to this event
    // Fired when user clicks a beat pattern button
    public event Action<BeatPattern> OnBeatSelected;

    // Tracks if controls label is currently visible
    private bool controlsLabelVisible = false;

    void Start()
    {
        // Wire up button click listeners
        buttonTwoBeat.onClick.AddListener(() => HandleBeatButtonClicked(BeatPattern.TwoBeat));
        buttonThreeBeat.onClick.AddListener(() => HandleBeatButtonClicked(BeatPattern.ThreeBeat));
        buttonFourBeat.onClick.AddListener(() => HandleBeatButtonClicked(BeatPattern.FourBeat));
        controlsButton.onClick.AddListener(HandleControlsButtonClicked);

        // Start with canvas 1 visible, canvas 2 hidden
        ShowBeatSelection();
    }

    void OnDestroy()
    {
        // Clean up button listeners
        buttonTwoBeat.onClick.RemoveAllListeners();
        buttonThreeBeat.onClick.RemoveAllListeners();
        buttonFourBeat.onClick.RemoveAllListeners();
        controlsButton.onClick.RemoveAllListeners();
    }

    // Called by SceneManager during initialisation
    // Shows canvas 1, hides canvas 2
    public void ShowBeatSelection()
    {
        canvas1BeatSelection.SetActive(true);
        canvas2BeatInfo.SetActive(false);

        // Hide controls label if visible
        if (controlsLabelCanvas != null)
            controlsLabelCanvas.SetActive(false);

        controlsLabelVisible = false;
    }

    // Called by SceneManager after user selects a beat
    // Hides canvas 1, shows canvas 2 with correct content
    public void ShowBeatInfo(BeatPattern pattern)
    {
        canvas1BeatSelection.SetActive(false);
        canvas2BeatInfo.SetActive(true);

        // Set heading text
        beatHeadingText.text = GetHeadingForPattern(pattern);

        // Set description text
        beatDescriptionText.text = GetDescriptionForPattern(pattern);

        // Set image
        beatPatternImage.sprite = GetSpriteForPattern(pattern);

        // Hide controls label when switching canvas
        if (controlsLabelCanvas != null)
            controlsLabelCanvas.SetActive(false);

        controlsLabelVisible = false;
    }

    // Called when a beat pattern button is clicked
    // Fires event so SceneManager can start the beat selected sequence
    private void HandleBeatButtonClicked(BeatPattern pattern)
    {
        Debug.Log($"[ScoreStandUI] Beat selected: {pattern}");
        OnBeatSelected?.Invoke(pattern);
    }

    
    // SHOW CONTROLS 
    // Tracks if we are currently processing a click
    // Prevents double click from XR ray interactor
    private bool isProcessingClick = false;
    // Toggles the controls label on and off
    private void HandleControlsButtonClicked()
    {
        // Ignore the second click that XR fires immediately after the first
        if (isProcessingClick) return;
        
        isProcessingClick = true;
        controlsLabelVisible = !controlsLabelVisible;
        controlsLabelCanvas.SetActive(controlsLabelVisible);
        Debug.Log($"[ScoreStandUI] Controls label visible: {controlsLabelVisible}");
        
        // Reset flag after a short delay so button can be clicked again
        StartCoroutine(ResetClickFlag());
    }
    private IEnumerator ResetClickFlag()
    {
        yield return new WaitForSeconds(0.3f);
        isProcessingClick = false;
    }

    // Returns display heading for each pattern
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

    // Returns description text for each pattern
    // Edit these strings to change what appears on canvas 2
    private string GetDescriptionForPattern(BeatPattern pattern)
    {
        switch (pattern)
        {
            case BeatPattern.TwoBeat:
                return "Move your baton DOWN then UP.\n" +
                       "Two beats per measure.\n" +
                       "Feel the strong beat on the downstroke.";

            case BeatPattern.ThreeBeat:
                return "Move your baton DOWN, then RIGHT, then UP.\n" +
                       "Three beats per measure.\n" +
                       "Common in waltz music.";

            case BeatPattern.FourBeat:
                return "Move your baton DOWN, LEFT, RIGHT, then UP.\n" +
                       "Four beats per measure.\n" +
                       "The most common pattern in orchestral music.";

            default:
                return "";
        }
    }

    // Returns the correct sprite for each pattern
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
}