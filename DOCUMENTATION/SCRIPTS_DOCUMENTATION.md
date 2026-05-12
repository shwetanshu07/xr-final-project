# XR Conducting Trainer — Scripts Technical Documentation

> Covers all C# scripts in `Assets/Scripts/LearnBeatPattern/` (13 scripts) and `Assets/Scripts/PracticeBeatPattern/` (5 scripts).

---

## Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [Shared Types](#shared-types)
   - [BeatPatternTypes.cs](#beatpatterntypescs)
3. [LearnBeatPattern Scripts](#learnbeatpattern-scripts)
   - [LearnBeatPatternSceneManager.cs](#learnbeatpatternscenemanagercs)
   - [TracingSystem.cs](#tracingsystemcs)
   - [BeatPathManager.cs](#beatpathmanagercs)
   - [MetricsCollector.cs](#metricscollectorcs)
   - [FeedbackPanel.cs](#feedbackpanelcs)
   - [LLMFeedbackManager.cs](#llmfeedbackmanagercs)
   - [MinimapGenerator.cs](#minimapgeneratorcs)
   - [ScoreStandUI.cs](#scorestanduics)
   - [AudioManager.cs](#audiomanagercs)
   - [SpatialLabelManager.cs](#spatiallabelmanagercs)
   - [BatonController.cs](#batoncontrollercs)
   - [MinimapPOC.cs](#minimappoccs)
4. [PracticeBeatPattern Scripts](#practicebeatpattern-scripts)
   - [PracticeSceneManager.cs](#practicescenemanagercs)
   - [PracticeTracingSystem.cs](#practicetracingsystemcs)
   - [PracticeMetricsCollector.cs](#practicemetricscollectorcs)
   - [DirectionalSegmentAnalyser.cs](#directionalsegmentanalysercs)
   - [PracticeMinimapGenerator.cs](#practiceminimapgeneratorcs)
5. [Cross-Script Dependency Map](#cross-script-dependency-map)

---

## Architecture Overview

The project has two XR scenes, each with its own orchestrator and tracing system, but sharing several components.

```
LearnBeatPattern Scene                PracticeBeatPattern Scene
─────────────────────────             ─────────────────────────
LearnBeatPatternSceneManager          PracticeSceneManager
  │                                     │
  ├── ScoreStandUI (beat selection)      ├── ScoreStandUI (shared)
  ├── BeatPathManager (spline path)      ├── BeatPathManager (shared)
  ├── TracingSystem                      ├── PracticeTracingSystem
  │     └── MetricsCollector            │     └── PracticeMetricsCollector
  │           (deviation + ictus)       │           (velocity + direction reversals)
  ├── MinimapGenerator                  │     └── DirectionalSegmentAnalyser
  ├── FeedbackPanel                     ├── PracticeMinimapGenerator
  ├── LLMFeedbackManager                ├── FeedbackPanel (shared)
  ├── AudioManager (singleton)          ├── LLMFeedbackManager (shared)
  ├── SpatialLabelManager               ├── AudioManager (singleton, shared)
  └── BatonController                   └── SpatialLabelManager (shared)
```

**Shared across both scenes:** `BeatPatternTypes`, `BeatPathManager`, `ScoreStandUI`, `FeedbackPanel`, `LLMFeedbackManager`, `AudioManager`, `SpatialLabelManager`.

---

## Shared Types

### BeatPatternTypes.cs
**Path:** `Assets/Scripts/LearnBeatPattern/BeatPatternTypes.cs`
**Type:** Plain C# — no MonoBehaviour. Pure data definitions.

**Purpose:** Central definition file for all enums and the `MetricsResult` data class. Nearly every other script in the project depends on this file.

#### Enums

| Enum | Values | Used By |
|---|---|---|
| `BeatPattern` | `None`, `TwoBeat`, `ThreeBeat`, `FourBeat` | All scripts that need to know which conducting pattern is active |
| `FailReason` | `TriggerReleased`, `Timeout` | `TracingSystem`, `LearnBeatPatternSceneManager` |

#### `MetricsResult` Class

Holds all performance data from a single trace attempt. Populated by `MetricsCollector` (Learn mode) or `PracticeMetricsCollector` (Practice mode) and read by `FeedbackPanel`, `LLMFeedbackManager`, and both scene managers.

| Field Group | Fields | Description |
|---|---|---|
| **Metric 1 — Path Deviation** | `averagePathDeviation`, `maxPathDeviation`, `percentageWithinGoodRange` | How close the hand stayed to the ideal spline path (metres) |
| **Metric 2 — Ictus Accuracy** | `ictusHits`, `ictusTotal`, `ictusAccuracyPercent`, `closestDistancePerIctus[]` | How accurately the hand passed through each beat position |
| **Metric 3 — Speed Consistency** | `meanSpeed`, `speedStdDev`, `speedCV`, `speedConsistencyScore` | Coefficient of variation of hand speed; lower CV = more consistent |
| **Overall** | `isSuccess`, `failReason`, `traceDurationSeconds`, `totalFramesRecorded`, `mainIssue` | Completion status and primary feedback string |
| **Practice only** | `correctDirectionalSegments`, `totalDirectionalSegments`, `segmentDirectionCorrect[]`, `directionalAccuracyPercent`, `nextAttemptTimeoutSeconds` | Directional gesture accuracy per stroke segment |

---

## LearnBeatPattern Scripts

### LearnBeatPatternSceneManager.cs
**Path:** `Assets/Scripts/LearnBeatPattern/LearnBeatPatternSceneManager.cs`
**Type:** MonoBehaviour — master scene controller.

**Purpose:** The top-level orchestrator for the Learn Beat Pattern scene. It owns the scene state machine and wires together all subsystems by subscribing to their events and calling their methods in the correct order.

#### Serialized Fields

| Field | Type | Description |
|---|---|---|
| `scoreStandUI` | `ScoreStandUI` | Beat selection and info UI |
| `spatialLabel` | `SpatialLabelManager` | Floating instruction text |
| `beatPathManager` | `BeatPathManager` | Spline path and orb visualization |
| `tracingSystem` | `TracingSystem` | XR tracing input and trail |
| `metricsCollector` | `MetricsCollector` | Performance recording |
| `feedbackPanel` | `FeedbackPanel` | Post-attempt feedback display |
| `llmFeedbackManager` | `LLMFeedbackManager` | AI coaching feedback |
| `minimapGenerator` | `MinimapGenerator` | Texture minimap generation |
| `maxAttempts` | `int` | Session attempt cap (`-1` = unlimited) |
| `immediateFeedback` | `bool` | If `true`, show feedback after each attempt; if `false`, show all at end |
| `welcomeClip`, `twoBeatIntroClip`, `threeBeatIntroClip` | `AudioClip` | Narration clips played during intro sequences |

#### State Machine

```
Loading → BeatSelection → CompanionIntro → TraceReady → Tracing
                                                          │
                                               ┌──────────┴───────────┐
                                          TraceSuccess            TraceFailed
                                               │                       │
                                          (show feedback)         (show feedback)
                                               └──────────┬───────────┘
                                                    loop or Exiting
```

#### Key Methods

| Method | Description |
|---|---|
| `Activate()` | Entry point called externally to boot the scene |
| `SetMode(bool delayedFeedback)` | Configures whether feedback is immediate or delayed |
| `HandleBeatSelected(BeatPattern)` | Event handler — runs `BeatSelectedSequence` coroutine |
| `HandleTraceSuccess(MetricsResult)` | Event handler — runs `SuccessSequence` coroutine |
| `HandleTraceFailed(FailReason)` | Event handler — runs `FailSequence` coroutine |
| `HandleTraceStarted()` | Event handler — hides score stand while tracing |
| `HandleTryAgainFromButton()` | Called by UI button — runs `ResetForNewAttempt` coroutine |
| `BeatSelectedSequence(BeatPattern)` | Coroutine: shows companion intro audio, activates path and tracing |
| `SuccessSequence(MetricsResult)` | Coroutine: generates minimap, shows feedback, requests LLM, plays audio |
| `FailSequence(FailReason)` | Coroutine: plays fail sound, shows reason text, offers retry |
| `ResetForNewAttempt()` | Coroutine: clears trail, resets all systems for next attempt |
| `ExitToMainMenu()` | Coroutine: transitions back to main menu scene |

#### Connections to Other Scripts

| Direction | Script | How |
|---|---|---|
| **Listens to** | `ScoreStandUI` | `OnBeatSelected` event → `HandleBeatSelected` |
| **Listens to** | `TracingSystem` | `OnTraceSuccess`, `OnTraceFailed`, `OnTraceStarted` events |
| **Calls** | `BeatPathManager` | `ShowPath()`, `HidePath()`, `ActivateStartOrb()` |
| **Calls** | `TracingSystem` | `Activate()`, `ResetTrace()` |
| **Calls** | `FeedbackPanel` | `SetMode()`, `ShowImmediate()`, `ShowAllAttempts()`, `ClearSession()` |
| **Calls** | `LLMFeedbackManager` | `RequestFeedback()` |
| **Calls** | `MinimapGenerator` | `Generate()` |
| **Calls** | `AudioManager` | `PlayClip()`, `PlaySuccess()`, `PlayFail()` |
| **Calls** | `SpatialLabelManager` | `SetText()`, `Hide()` |
| **Calls** | `ScoreStandUI` | `ShowBeatSelection()`, `ShowBeatInfo()`, `ShowTryAgain()` |
| **Uses** | `UnityEngine.SceneManager` | `LoadScene()` to exit to main menu |

---

### TracingSystem.cs
**Path:** `Assets/Scripts/LearnBeatPattern/TracingSystem.cs`
**Type:** MonoBehaviour — XR input and trail rendering.

**Purpose:** Handles all user interaction during tracing. Casts a ray from the right XR controller onto an invisible tracing plane, draws a color-coded trail reflecting proximity to the ideal path, and manages a state machine that controls when tracing starts and ends.

#### State Machine

```
Inactive → Idle → NearOrb (hovering start orb) → Tracing → Finished
```
- The trigger must be held while the hand is near the start orb to begin tracing.
- Tracing ends when the hand reaches the end point, the trigger is released mid-trace, or the 30-second timeout expires.

#### Serialized Fields

| Field | Type | Description |
|---|---|---|
| `beatPathManager` | `BeatPathManager` | Source of tracing plane and spline queries |
| `metricsCollector` | `MetricsCollector` | Receives per-frame position and velocity data |
| `inputActions` | `InputActionAsset` | XR input binding asset |
| `rayOriginTransform` | `Transform` | Right controller transform for ray origin |
| `trailWidth` | `float` | Width of the drawn trail in metres |
| `colorClose/Medium/Far` | `Color` | Trail color for each deviation band |
| `closeThreshold`, `mediumThreshold` | `float` | Deviation thresholds in metres for color changes |
| `timeoutSeconds` | `float` | Max trace duration before automatic fail |
| `hapticAmplitude/Duration/Interval` | `float` | XR haptic feedback settings |

#### Events

| Event | Payload | Fired When |
|---|---|---|
| `OnTraceSuccess` | `MetricsResult` | Hand reached end point with trigger held |
| `OnTraceFailed` | `FailReason` | Trigger released mid-trace or timeout |
| `OnTraceStarted` | — | Tracing state begins |

#### Key Methods

| Method | Description |
|---|---|
| `Activate()` | Enables input actions and sets state to Idle |
| `ResetTrace()` | Destroys trail GameObjects, clears recorded points, resets metrics |
| `GetTracePoints()` / `GetTraceColors()` | Returns recorded positions and colors for minimap generation |
| `UpdateIdleAndNearOrb()` | Per-frame: casts ray, checks proximity to start orb, changes orb color |
| `UpdateTracing()` | Per-frame: casts ray, calculates deviation, calls `MetricsCollector.RecordFrame`, draws trail, checks timeout and end proximity |
| `DrawTrailPoint(Vector3, float)` | Adds a point to the current `LineRenderer` trail segment |
| `HandleSuccess()` / `HandleFail(FailReason)` | Finish tracing state, compute `MetricsResult`, fire event |
| `TriggerHaptic()` / `StopHaptic()` | XR controller haptic pulse control |

#### Connections to Other Scripts

| Direction | Script | How |
|---|---|---|
| **Calls** | `BeatPathManager` | `GetTracingPlane()`, `GetNearestPointOnSpline()`, `IsNearStartOrb()`, `IsNearEndPoint()`, `GetIctusPositions()`, `SetOrbHoverState()`, `ActivateStartOrb()`, `ShowEndOrb()` |
| **Calls** | `MetricsCollector` | `StartRecording()`, `RecordFrame()`, `GetResult()`, `Reset()` |
| **Fires events to** | `LearnBeatPatternSceneManager` | `OnTraceSuccess`, `OnTraceFailed`, `OnTraceStarted` |
| **Uses** | `UnityEngine.InputSystem` | For trigger button binding |
| **Uses** | `UnityEngine.XR` | For haptic feedback |

---

### BeatPathManager.cs
**Path:** `Assets/Scripts/LearnBeatPattern/BeatPathManager.cs`
**Type:** MonoBehaviour — path visualization and spatial queries.

**Purpose:** Manages all beat path assets for every supported pattern. Activates and deactivates pattern root GameObjects, drives the `LineRenderer` guide path from a Unity Spline, animates the start orb, and provides spatial query methods used by `TracingSystem` and `MetricsCollector`.

#### Inner Class: `PatternData`
Serializable struct holding all GameObjects for one beat pattern:

| Field | Description |
|---|---|
| `pattern` | Which `BeatPattern` this data belongs to |
| `patternRoot` | Parent GameObject — activate to show this pattern |
| `guidePathRenderer` | `LineRenderer` drawn along the spline |
| `tracingPlane` | Invisible `Transform` (a quad) used as the ray-cast target |
| `startOrb` / `endOrb` | Visual spheres at path start and end |
| `ictusKnotIndices[]` | Spline knot indices that correspond to beat positions |

#### Key Methods

| Method | Description |
|---|---|
| `ShowPath(BeatPattern)` | Activates the matching `PatternData` root, builds the LineRenderer from the spline, places orbs, activates start orb pulse |
| `HidePath()` | Deactivates the current pattern root |
| `ActivateStartOrb()` / `DeactivateStartOrb()` | Start/stop pulsing animation on start orb |
| `SetOrbHoverState(bool)` | Switches start orb between active color and hover color |
| `ShowEndOrb()` | Makes end orb visible on successful trace |
| `GetTracingPlane()` | Returns the invisible tracing plane `Transform` |
| `GetNearestPointOnSpline(Vector3)` | Uses `SplineUtility` to find the closest point on the spline to a world position |
| `GetStartPosition()` / `GetEndPosition()` | World positions of path endpoints |
| `GetIctusPositions()` | Evaluates spline at each `ictusKnotIndices` entry, returns world positions |
| `GetSplineSamplePoints(int)` | Returns evenly spaced sample points along the spline (used by minimap generators) |
| `IsNearStartOrb(Vector3)` / `IsNearEndPoint(Vector3)` | Distance threshold checks for orb proximity |
| `HideGuidePath()` | Disables the `LineRenderer` (used in practice mode to hide the guide) |

#### Connections to Other Scripts

| Direction | Script | How |
|---|---|---|
| **Called by** | `TracingSystem` | Spatial queries and orb control |
| **Called by** | `PracticeTracingSystem` | `GetTracingPlane()` |
| **Called by** | `LearnBeatPatternSceneManager` | `ShowPath()`, `HidePath()`, `ActivateStartOrb()` |
| **Called by** | `PracticeSceneManager` | `ShowPath()`, `HidePath()`, `HideGuidePath()` |
| **Called by** | `MinimapGenerator` | `GetSplineSamplePoints()` |
| **Called by** | `PracticeMinimapGenerator` | `GetSplineSamplePoints()` |
| **Called by** | `MinimapPOC` | `GetSplineSamplePoints()` |
| **Uses** | `UnityEngine.Splines` | `SplineContainer`, `SplineUtility.GetNearestPoint()` |

---

### MetricsCollector.cs
**Path:** `Assets/Scripts/LearnBeatPattern/MetricsCollector.cs`
**Type:** MonoBehaviour — frame-by-frame metrics recorder.

**Purpose:** Records deviation from the ideal path, proximity to each ictus (beat) position, and hand speed every frame during a trace attempt. Computes a final `MetricsResult` when the attempt ends.

#### Serialized Fields

| Field | Default | Description |
|---|---|---|
| `maxCVForSpeedScore` | `1.0` | Coefficient of variation at which speed consistency score = 0 |
| `maxDeviationForScore` | `0.10` | Deviation in metres at which path score = 0 |
| `ictusHitRadius` | `0.05` | How close (metres) the hand must get to count an ictus as "hit" |

#### Key Methods

| Method | Description |
|---|---|
| `StartRecording(Vector3[] ictusWorldPositions)` | Clears all buffers and stores ictus target positions |
| `RecordFrame(Vector3 tipPosition, float deviation, Vector3 tipVelocity)` | Called once per `Update` while tracing; stores deviation and speed, updates closest-distance tracking for each ictus |
| `Reset()` | Clears all recorded data without beginning a new recording |
| `GetResult(bool isSuccess)` | Runs all three metric calculations and returns a populated `MetricsResult` |
| `CalculateMetric1(MetricsResult)` | Fills path deviation fields from `deviationSamples` |
| `CalculateMetric2(MetricsResult)` | Fills ictus accuracy fields from `closestDistanceToIctus` |
| `CalculateMetric3(MetricsResult)` | Calculates speed mean, stddev, CV, and `speedConsistencyScore` |
| `DetermineMainIssue(MetricsResult)` | Sets `result.mainIssue` to the worst-performing metric's feedback string |

#### Connections to Other Scripts

| Direction | Script | How |
|---|---|---|
| **Called by** | `TracingSystem` | `StartRecording()`, `RecordFrame()`, `GetResult()`, `Reset()` |
| **Produces** | `MetricsResult` | Returned to `TracingSystem`, passed to `LearnBeatPatternSceneManager` |

---

### FeedbackPanel.cs
**Path:** `Assets/Scripts/LearnBeatPattern/FeedbackPanel.cs`
**Type:** MonoBehaviour — feedback UI controller.

**Purpose:** Displays post-attempt feedback in a panel that shows the minimap texture, score metrics, and AI coaching text. Supports two modes: *immediate* (show after every attempt) and *delayed* (store all attempts, show all at the end with prev/next navigation).

#### Serialized Fields

| Field | Description |
|---|---|
| `feedbackBackground` | Root panel `GameObject` — shown/hidden to open/close panel |
| `attemptLabel` | `TextMeshProUGUI` showing "Attempt 1", "Attempt 2", etc. |
| `minimapImage` | `RawImage` displaying the minimap `Texture2D` |
| `metricsText` | `TextMeshProUGUI` with formatted score breakdown |
| `aiFeedbackText` | `TextMeshProUGUI` for LLM coaching text |
| `navigationRow` | Container for prev/next buttons (only shown in delayed mode) |
| `prevButton` / `nextButton` | Navigate between stored attempts |

#### Events

| Event | Fired When |
|---|---|
| `OnPanelClosed` | Panel's hide/close logic runs (for scene manager to know when to proceed) |

#### Key Methods

| Method | Description |
|---|---|
| `SetMode(bool delayedFeedback)` | Must be called before the session starts to configure mode |
| `ShowImmediate(MetricsResult, Texture2D, int)` | Shows one attempt's feedback immediately (Learn mode) |
| `ShowImmediatePractice(MetricsResult, Texture2D, int)` | Practice-mode variant of `ShowImmediate` |
| `StoreAttempt(MetricsResult, Texture2D)` | Caches attempt data for delayed mode |
| `SetStoredAIFeedback(int, string)` | Updates AI feedback text for a specific stored attempt index |
| `SetImmediateAIFeedback(string)` | Updates AI text for the currently shown immediate attempt |
| `ShowAllAttempts()` | Displays all stored attempts (delayed mode), enables navigation |
| `Hide()` | Hides the panel |
| `ClearSession()` | Wipes all stored data for a fresh session |
| `BuildMetricsText(MetricsResult)` | Formats deviation, ictus, and speed scores into display string (Learn mode) |
| `BuildPracticeMetricsText(MetricsResult)` | Practice variant — includes directional accuracy |
| `CalculateOverallScore(MetricsResult)` | Weighted score: 35% path, 40% ictus, 25% speed |
| `CalculatePracticeScore(MetricsResult)` | Weighted score: 50% directional, 25% ictus, 25% speed |

#### Connections to Other Scripts

| Direction | Script | How |
|---|---|---|
| **Called by** | `LearnBeatPatternSceneManager` | `SetMode()`, `ShowImmediate()`, `StoreAttempt()`, `ShowAllAttempts()`, `ClearSession()` |
| **Called by** | `PracticeSceneManager` | `ShowImmediatePractice()`, `ClearSession()` |
| **Called by** | `LLMFeedbackManager` | `SetStoredAIFeedback()`, `SetImmediateAIFeedback()` |
| **Reads** | `MetricsResult` | For display and score calculation |

---

### LLMFeedbackManager.cs
**Path:** `Assets/Scripts/LearnBeatPattern/LLMFeedbackManager.cs`
**Type:** MonoBehaviour — Groq API client.

**Purpose:** After a successful trace, sends the `MetricsResult` to the Groq LLM API with a structured coaching prompt and writes the AI response text into the `FeedbackPanel`. Falls back to deterministic text if the API call fails.

#### Serialized Fields

| Field | Description |
|---|---|
| `apiKey` | Groq API key string (set in Inspector) |
| `feedbackPanel` | `FeedbackPanel` reference — receives the AI text |
| `pathAccuracyWeight` | `0.35` — weight for path deviation in score sent to LLM |
| `ictusAccuracyWeight` | `0.40` — weight for ictus accuracy |
| `speedConsistencyWeight` | `0.25` — weight for speed consistency |

#### Constants

| Constant | Value |
|---|---|
| `API_URL` | `https://api.groq.com/openai/v1/chat/completions` |
| `MODEL` | `openai/gpt-oss-120b` |

#### Key Methods

| Method | Description |
|---|---|
| `RequestFeedback(MetricsResult, BeatPattern, int attemptIndex)` | Public entry point; starts `SendRequest` coroutine if not already requesting |
| `SendRequest(...)` | Coroutine: builds prompt, POSTs to Groq API via `UnityWebRequest`, parses JSON response, calls `feedbackPanel.SetStoredAIFeedback()` |
| `BuildPrompt(MetricsResult, BeatPattern)` | Assembles detailed coaching prompt including pattern name, metric values, and weighted score |
| `BuildRequestBody(string prompt)` | Wraps prompt in Groq-compatible JSON request format |
| `ParseResponse(string json)` | Extracts `choices[0].message.content` from the JSON response |
| `CalculateWeightedScore(MetricsResult)` | Returns 0–100 score using the three configurable weights |
| `BuildFallbackFeedback(MetricsResult)` | Returns a hardcoded feedback string based on `result.mainIssue` |
| `RequestPracticeFeedback(...)` | Practice mode variant of `RequestFeedback` |
| `SendPracticeRequest(...)` | Coroutine: uses `BuildPracticePrompt`, includes directional accuracy metrics |
| `BuildPracticePrompt(MetricsResult, BeatPattern)` | Practice mode prompt — highlights stroke direction correctness |

#### Connections to Other Scripts

| Direction | Script | How |
|---|---|---|
| **Called by** | `LearnBeatPatternSceneManager` | `RequestFeedback()` after each successful trace |
| **Called by** | `PracticeSceneManager` | `RequestPracticeFeedback()` after each trace complete |
| **Calls** | `FeedbackPanel` | `SetStoredAIFeedback()` or `SetImmediateAIFeedback()` |
| **Reads** | `MetricsResult` | To build the prompt and calculate scores |
| **Uses** | `UnityEngine.Networking` | `UnityWebRequest` for HTTP POST |

---

### MinimapGenerator.cs
**Path:** `Assets/Scripts/LearnBeatPattern/MinimapGenerator.cs`
**Type:** MonoBehaviour — Texture2D factory.

**Purpose:** Generates a 256×256 (configurable) `Texture2D` image showing the ideal spline path and the player's actual trace overlaid, using a shared bounding box so both paths are drawn at the same scale.

#### Serialized Fields

| Field | Default | Description |
|---|---|---|
| `beatPathManager` | — | Source of ideal path sample points |
| `tracingSystem` | — | Source of recorded trace points and colors |
| `textureSize` | `256` | Pixel resolution of output texture |
| `backgroundColor` | Black | Canvas fill color |
| `idealPathColor` | White | Color for the ideal spline path |
| `idealPathThickness` | `2` | Line thickness in pixels |
| `traceThickness` | `2` | Player trace line thickness |

#### Key Methods

| Method | Description |
|---|---|
| `Generate()` | Main method: gets ideal path from `BeatPathManager`, gets trace from `TracingSystem`, computes shared bounds, draws both paths, calls `Apply()`, returns `Texture2D` |
| `CalculateBounds(List<Vector3>, List<Vector3>)` | Computes a `Bounds` that encloses both paths for consistent scaling |
| `DrawPath(Texture2D, List<Vector3>, List<Color>, Bounds, Color, int)` | Projects 3D points to 2D texture coordinates and draws segments |
| `DrawLine(Texture2D, int, int, int, int, Color, int)` | Bresenham's line algorithm with thickness |

#### Connections to Other Scripts

| Direction | Script | How |
|---|---|---|
| **Called by** | `LearnBeatPatternSceneManager` | `Generate()` — result passed to `FeedbackPanel` |
| **Calls** | `BeatPathManager` | `GetSplineSamplePoints()` |
| **Calls** | `TracingSystem` | `GetTracePoints()`, `GetTraceColors()` |

---

### ScoreStandUI.cs
**Path:** `Assets/Scripts/LearnBeatPattern/ScoreStandUI.cs`
**Type:** MonoBehaviour — score stand UI controller.

**Purpose:** Controls two canvases on the score stand prop: the beat selection canvas (three buttons) and the beat info canvas (description, image, tutorial access). Also manages an animated GIF-style tutorial and an audio tutorial for each pattern.

#### Serialized Fields (Key)

| Field Group | Fields | Description |
|---|---|---|
| **Canvas 1** | `canvas1BeatSelection`, `buttonTwoBeat`, `buttonThreeBeat`, `buttonFourBeat` | Beat selection screen |
| **Canvas 2** | `canvas2BeatInfo`, `beatHeadingText`, `beatPatternImage`, `beatDescriptionText`, `controlsButton` | Beat info screen |
| **Pattern sprites** | `twoBeatSprite`, `threeBeatSprite`, `fourBeatSprite` | Pattern diagram images |
| **Try Again** | `tryAgainButton` | Shown after a failed attempt |
| **GIF tutorial** | `tutorialCanvas`, `tutorialDisplay`, `tutorialBtn`, `tutorialCloseBtn`, `twoBeatFrames[]`, `threeBeatFrames[]`, `frameRate` | Frame-animated pattern tutorial |
| **Audio tutorial** | `audioTutorialBtn`, `tutorialAudioSource`, `twoBeatAudioClip`, `threeBeatAudioClip` | Voice tutorial per pattern |

#### Events

| Event | Payload | Fired When |
|---|---|---|
| `OnBeatSelected` | `BeatPattern` | User clicks a beat selection button |

#### Key Methods

| Method | Description |
|---|---|
| `ShowBeatSelection()` | Activates canvas 1, deactivates canvas 2 |
| `ShowBeatInfo(BeatPattern)` | Activates canvas 2 with the correct heading, image, and description |
| `ShowTryAgain(bool)` | Shows or hides the Try Again button |
| `HandleBeatButtonClicked(BeatPattern)` | Debounces click, fires `OnBeatSelected` event |
| `HandleTutorialButtonClicked()` | Toggles GIF tutorial on/off |
| `PlayTutorialForPattern(BeatPattern)` | Starts frame animation coroutine for the selected pattern |
| `PlayFrames(Texture2D[])` | Coroutine cycling frames at `frameRate` FPS |
| `HandleAudioButtonClicked()` | Toggles audio tutorial on/off |
| `PlayAudioForPattern(BeatPattern)` | Plays the correct `AudioClip` for the pattern |

#### Connections to Other Scripts

| Direction | Script | How |
|---|---|---|
| **Fires events to** | `LearnBeatPatternSceneManager` | `OnBeatSelected` |
| **Fires events to** | `PracticeSceneManager` | `OnBeatSelected` |
| **Uses** | `BeatPattern` enum | To determine which content to show |

---

### AudioManager.cs
**Path:** `Assets/Scripts/LearnBeatPattern/AudioManager.cs`
**Type:** MonoBehaviour — singleton audio service.

**Purpose:** Provides a singleton audio service for playing sound effects from anywhere in the scene without needing a direct reference. Has dedicated methods for success and fail sounds, plus a generic clip player.

#### Singleton

`AudioManager.Instance` — set in `Awake()` with `DontDestroyOnLoad` semantics (stays alive across scenes within the same session).

#### Serialized Fields

| Field | Description |
|---|---|
| `sfxSource` | Single `AudioSource` component used for all clips |
| `successClip` | Clip played on successful trace |
| `failClip` | Clip played on failed trace |

#### Key Methods

| Method | Description |
|---|---|
| `PlaySuccess()` | Calls `PlayClip(successClip, "Success")` |
| `PlayFail()` | Calls `PlayClip(failClip, "Fail")` |
| `PlayClip(AudioClip, string)` | Null-checks the clip, then calls `sfxSource.PlayOneShot()` |

#### Connections to Other Scripts

| Direction | Script | How |
|---|---|---|
| **Called by** | `LearnBeatPatternSceneManager` | `PlaySuccess()`, `PlayFail()`, `PlayClip()` |
| **Called by** | `PracticeSceneManager` | `PlayClip()` |

---

### SpatialLabelManager.cs
**Path:** `Assets/Scripts/LearnBeatPattern/SpatialLabelManager.cs`
**Type:** MonoBehaviour — floating UI label.

**Purpose:** Controls a world-space canvas that floats above the beat path and displays instruction text. Used by both scene managers to guide the user through each step.

#### Serialized Fields

| Field | Description |
|---|---|
| `labelCanvas` | Root `GameObject` of the floating label |
| `labelText` | `TextMeshProUGUI` component displaying the text |

#### Key Methods

| Method | Description |
|---|---|
| `SetText(string)` | Activates `labelCanvas` and sets `labelText.text` |
| `Hide()` | Deactivates `labelCanvas` |

#### Connections to Other Scripts

| Direction | Script | How |
|---|---|---|
| **Called by** | `LearnBeatPatternSceneManager` | `SetText()`, `Hide()` throughout the scene flow |
| **Called by** | `PracticeSceneManager` | `SetText()`, `Hide()` |
| **Called by** | `PracticeTracingSystem` | `SetText()` during tracing state changes |

---

### BatonController.cs
**Path:** `Assets/Scripts/LearnBeatPattern/BatonController.cs`
**Type:** MonoBehaviour — visual controller attachment.

**Purpose:** Purely visual. Positions and rotates the baton mesh to match the right XR controller with configurable offsets. Contains no tracing logic and fires no events.

#### Serialized Fields

| Field | Default | Description |
|---|---|---|
| `rightHandTransform` | — | Right XR controller `Transform` |
| `positionOffset` | `(0, -0.05, 0.1)` | Local offset from hand origin |
| `rotationOffset` | `(90, 0, 0)` | Euler rotation offset |

#### Key Methods

| Method | Description |
|---|---|
| `LateUpdate()` | Sets `transform.position` and `transform.rotation` each frame after physics, matching `rightHandTransform` with offsets applied |

#### Connections to Other Scripts

No connections to other scripts — reads only the controller transform set in the Inspector.

---

### MinimapPOC.cs
**Path:** `Assets/Scripts/LearnBeatPattern/MinimapPOC.cs`
**Type:** MonoBehaviour — proof of concept (not wired into production).

**Purpose:** An early proof-of-concept minimap generator used for testing the texture-drawing pipeline. Not called by any scene manager. Superseded by `MinimapGenerator.cs`.

#### Serialized Fields

| Field | Description |
|---|---|
| `beatPathManager` | Reference for ideal path sampling |
| `displayImage` | `RawImage` to display the generated texture |
| `textureSize` | Texture resolution |
| `backgroundColor`, `idealPathColor`, `traceColor` | Rendering colors |

#### Key Methods

| Method | Description |
|---|---|
| `GenerateMinimap(List<Vector3>, List<Color>)` | Generates and applies minimap texture; must be called manually |
| `DrawPathOnTexture(...)` | Draws a list of points onto a texture |
| `DrawLine(...)` | Bresenham's line algorithm |

> **Note:** This script is not referenced by any other script in the project. It exists as a standalone test tool.

---

## PracticeBeatPattern Scripts

### PracticeSceneManager.cs
**Path:** `Assets/Scripts/PracticeBeatPattern/PracticeSceneManager.cs`
**Type:** MonoBehaviour — practice scene orchestrator.

**Purpose:** Master controller for the Practice Beat Pattern scene. Similar role to `LearnBeatPatternSceneManager` but with key differences: it hides the guide path so the player gestures freely, does not enforce a start orb, ends on trigger release (not end-orb proximity), and adapts the timeout for next attempt based on the player's score.

#### Serialized Fields

| Field | Description |
|---|---|
| `scoreStandUI` | `ScoreStandUI` — shared with Learn scene |
| `spatialLabel` | `SpatialLabelManager` — shared |
| `beatPathManager` | `BeatPathManager` — shared |
| `tracingSystem` | `PracticeTracingSystem` — practice-specific |
| `metricsCollector` | `PracticeMetricsCollector` — practice-specific |
| `feedbackPanel` | `FeedbackPanel` — shared |
| `llmFeedbackManager` | `LLMFeedbackManager` — shared |
| `minimapGenerator` | `PracticeMinimapGenerator` — practice-specific |
| `baseTimeout` | `30f` — starting timer value |
| `highScoreThreshold` | `70f` — score above which timer decreases |
| `lowScoreThreshold` | `40f` — score below which timer increases |
| `maxTimeout` | `60f` — upper cap on adaptive timer |
| `minTimeout` | `15f` — lower cap on adaptive timer |

#### State Machine

```
Loading → BeatSelection → CompanionIntro → TraceReady → Tracing
                                                          │
                                                    TraceComplete
                                                          │
                                              (adapt timer, show feedback)
                                                          │
                                                    loop or Exiting
```

#### Key Methods

| Method | Description |
|---|---|
| `InitialiseScene()` | Coroutine: sets up all event subscriptions, shows beat selection |
| `HandleBeatSelected(BeatPattern)` | Runs `BeatSelectedSequence` coroutine |
| `HandleTraceComplete(MetricsResult, BeatPattern)` | Runs `CompleteSequence` coroutine |
| `CompleteSequence(MetricsResult, BeatPattern)` | Generates minimap, shows feedback, requests LLM feedback, calls `UpdateAdaptiveTimer` |
| `UpdateAdaptiveTimer(float score, MetricsResult)` | Increases timeout if score < `lowScoreThreshold`, decreases if score > `highScoreThreshold`, stores result in `MetricsResult.nextAttemptTimeoutSeconds` |
| `CalculatePracticeScore(MetricsResult)` | 50% directional + 25% ictus + 25% speed |
| `HandleTryAgainFromButton()` | Called by UI button; runs `ResetForNewAttempt` coroutine |
| `ResetForNewAttempt()` | Clears trail, resets systems, applies adaptive timeout |

#### Connections to Other Scripts

| Direction | Script | How |
|---|---|---|
| **Listens to** | `ScoreStandUI` | `OnBeatSelected` event |
| **Listens to** | `PracticeTracingSystem` | `OnTraceComplete`, `OnTraceStarted` events |
| **Calls** | `BeatPathManager` | `ShowPath()`, `HidePath()`, `HideGuidePath()` |
| **Calls** | `PracticeTracingSystem` | `Activate()`, `ResetTrace()`, `SetTimeout()` |
| **Calls** | `PracticeMetricsCollector` | `Reset()` |
| **Calls** | `FeedbackPanel` | `ShowImmediatePractice()`, `ClearSession()` |
| **Calls** | `LLMFeedbackManager` | `RequestPracticeFeedback()` |
| **Calls** | `PracticeMinimapGenerator` | `Generate()` |
| **Calls** | `AudioManager` | `PlayClip()` |
| **Calls** | `SpatialLabelManager` | `SetText()`, `Hide()` |

---

### PracticeTracingSystem.cs
**Path:** `Assets/Scripts/PracticeBeatPattern/PracticeTracingSystem.cs`
**Type:** MonoBehaviour — practice mode tracing input.

**Purpose:** Handles XR controller input and trail drawing for practice mode. Key differences from `TracingSystem`: there is no start-orb proximity requirement — the trace begins when the trigger is *pressed* and ends when the trigger is *released* (not on reaching an end orb). The trail uses a single color (no deviation coloring). Supports an adaptive timeout set by `PracticeSceneManager`.

#### State Machine

```
Inactive → Idle → Tracing → Finished
```

#### Serialized Fields

| Field | Description |
|---|---|
| `beatPathManager` | Source of tracing plane transform |
| `metricsCollector` | `PracticeMetricsCollector` for velocity recording |
| `inputActions` | XR input binding asset |
| `rayOriginTransform` | Right controller transform |
| `trailColor` | Single color for all trail segments |
| `timeoutSeconds` | Current timeout (updated via `SetTimeout()`) |
| `spatialLabel` | `SpatialLabelManager` for in-trace status messages |

#### Events

| Event | Payload | Fired When |
|---|---|---|
| `OnTraceComplete` | `MetricsResult`, `BeatPattern` | Trigger released or timeout |
| `OnTraceStarted` | — | Trace begins |

#### Key Methods

| Method | Description |
|---|---|
| `Activate(BeatPattern)` | Stores pattern, enables input, sets state to Idle |
| `ResetTrace()` | Destroys trail, clears recorded points, resets state |
| `SetTimeout(float)` | Updates `timeoutSeconds` (called by `PracticeSceneManager` with adaptive value) |
| `GetTracePoints()` / `GetTraceColors()` | Returns recorded data for minimap generation |
| `OnTriggerPerformed(...)` | Input callback — starts tracing |
| `OnTriggerCanceled(...)` | Input callback — ends tracing, fires `OnTraceComplete` |
| `UpdateTracing()` | Per-frame: ray cast, velocity calculation, metrics recording, trail drawing, timeout check |
| `HandleComplete()` | Calls `PracticeMetricsCollector.GetResult()`, fires `OnTraceComplete` |

#### Connections to Other Scripts

| Direction | Script | How |
|---|---|---|
| **Calls** | `BeatPathManager` | `GetTracingPlane()` |
| **Calls** | `PracticeMetricsCollector` | `Reset()`, `StartRecording()`, `RecordFrame()`, `GetResult()` |
| **Calls** | `SpatialLabelManager` | `SetText()` during tracing |
| **Fires events to** | `PracticeSceneManager` | `OnTraceComplete`, `OnTraceStarted` |
| **Uses** | `UnityEngine.InputSystem` | For trigger binding |

---

### PracticeMetricsCollector.cs
**Path:** `Assets/Scripts/PracticeBeatPattern/PracticeMetricsCollector.cs`
**Type:** MonoBehaviour — practice metrics recorder.

**Purpose:** Records hand velocity every frame during practice tracing. Detects ictus points (beat positions) through **direction reversals** (Y-axis velocity sign changes) rather than by proximity to fixed world positions. At the end of an attempt, calls `DirectionalSegmentAnalyser` to score each stroke segment and calculates speed consistency.

#### Serialized Fields

| Field | Default | Description |
|---|---|---|
| `maxCVForSpeedScore` | `1.0` | CV at which speed consistency = 0 |
| `downstrokeThreshold` | `-0.05` | Negative Y velocity threshold to detect a downstroke |
| `upstrokeThreshold` | `0.05` | Positive Y velocity threshold to detect an upstroke |

#### Key Methods

| Method | Description |
|---|---|
| `StartRecording()` | Resets all buffers, records `traceStartTime` |
| `RecordFrame(Vector3 tipVelocity)` | Stores velocity sample, calculates speed, detects direction reversal to log an ictus frame index |
| `Reset()` | Clears all data |
| `GetResult(BeatPattern)` | Calculates speed consistency, calls `DirectionalSegmentAnalyser.Analyse()`, returns `MetricsResult` |
| `GetExpectedIctusCount(BeatPattern)` | Returns 2 for TwoBeat, 3 for ThreeBeat, 4 for FourBeat |
| `CalculateSpeedConsistency(MetricsResult)` | Computes mean, stddev, CV, and maps to 0–100 score |
| `DetermineMainIssue(MetricsResult)` | Sets `mainIssue` based on which metric scored lowest |

#### Connections to Other Scripts

| Direction | Script | How |
|---|---|---|
| **Called by** | `PracticeTracingSystem` | `StartRecording()`, `RecordFrame()`, `GetResult()`, `Reset()` |
| **Calls** | `DirectionalSegmentAnalyser` | `Analyse()` static method |
| **Produces** | `MetricsResult` | Returned to `PracticeTracingSystem` → `PracticeSceneManager` |

---

### DirectionalSegmentAnalyser.cs
**Path:** `Assets/Scripts/PracticeBeatPattern/DirectionalSegmentAnalyser.cs`
**Type:** Static utility class — no MonoBehaviour, no instance needed.

**Purpose:** Given velocity samples, detected ictus frame indices, and a `BeatPattern`, splits the trace into segments between ictus points and compares each segment's dominant movement direction to the expected direction for that stroke. Populates the directional accuracy fields of a `MetricsResult`.

#### Constants

| Constant | Value | Description |
|---|---|---|
| `MinMeaningfulSpeed` | `0.05f` | Frames below this speed (m/s) are ignored in direction averaging |
| `DirectionThreshold` | `0.3f` | Dot product threshold (~72°) for a segment to be counted as correct |

#### Expected Directions Per Pattern

| Pattern | Expected Stroke Directions |
|---|---|
| `TwoBeat` | DOWN → UP |
| `ThreeBeat` | DOWN → RIGHT → UP |
| `FourBeat` | DOWN → LEFT → RIGHT → UP |

#### Key Methods

| Method | Signature | Description |
|---|---|---|
| `Analyse` | `static void Analyse(List<Vector3> velocitySamples, List<int> ictusFrameIndices, BeatPattern pattern, MetricsResult result)` | Main entry point. Divides velocity samples into segments at ictus indices, scores each against expected directions, writes results into `result` |
| `GetDominantDirection` | `private static Vector3 GetDominantDirection(List<Vector3> velocities, int start, int end)` | Averages velocity vectors for frames above `MinMeaningfulSpeed` in the range `[start, end]` |
| `GetExpectedDirections` | `private static Vector3[] GetExpectedDirections(BeatPattern pattern)` | Returns the array of expected normalized direction vectors for the given pattern |

#### Connections to Other Scripts

| Direction | Script | How |
|---|---|---|
| **Called by** | `PracticeMetricsCollector` | `DirectionalSegmentAnalyser.Analyse(...)` static call |
| **Reads** | `BeatPattern` enum | To select expected directions |
| **Writes to** | `MetricsResult` | `totalDirectionalSegments`, `segmentDirectionCorrect[]`, `correctDirectionalSegments`, `directionalAccuracyPercent` |

---

### PracticeMinimapGenerator.cs
**Path:** `Assets/Scripts/PracticeBeatPattern/PracticeMinimapGenerator.cs`
**Type:** MonoBehaviour — practice mode texture factory.

**Purpose:** Generates a `Texture2D` minimap for practice mode. Key difference from `MinimapGenerator`: each path (ideal and player trace) is **normalized independently** to its own bounding box before drawing, so size differences between the ideal path and a short/partial trace don't cause one path to appear tiny.

#### Serialized Fields

| Field | Default | Description |
|---|---|---|
| `beatPathManager` | — | Source of ideal path points |
| `tracingSystem` | `PracticeTracingSystem` | Source of player trace points and colors |
| `textureSize` | `256` | Pixel resolution |
| `backgroundColor` | `(0.1, 0.1, 0.15)` | Dark canvas fill |
| `idealPathColor` | `(0.4, 0.4, 0.5)` | Grey ideal path |
| `traceColor` | White | Player trace color |
| `idealPathThickness` | `1` | Ideal path line thickness |
| `traceThickness` | `2` | Player trace line thickness |

#### Key Methods

| Method | Description |
|---|---|
| `Generate()` | Gets ideal path from `BeatPathManager`, gets trace from `PracticeTracingSystem`, draws both (independently normalized), returns `Texture2D` |
| `DrawPathNormalised(Texture2D, List<Vector3>, List<Color>, Color, int)` | Computes bounds from the path itself, maps each point to texture space, draws segments |
| `DrawLine(Texture2D, int, int, int, int, Color, int)` | Bresenham's line algorithm with thickness |

#### Connections to Other Scripts

| Direction | Script | How |
|---|---|---|
| **Called by** | `PracticeSceneManager` | `Generate()` — result passed to `FeedbackPanel` |
| **Calls** | `BeatPathManager` | `GetSplineSamplePoints()` |
| **Calls** | `PracticeTracingSystem` | `GetTracePoints()`, `GetTraceColors()` |

---

## Cross-Script Dependency Map

### Who calls whom

| Caller | Calls / Listens to | Via |
|---|---|---|
| `LearnBeatPatternSceneManager` | `ScoreStandUI` | `OnBeatSelected` event |
| `LearnBeatPatternSceneManager` | `TracingSystem` | `OnTraceSuccess`, `OnTraceFailed`, `OnTraceStarted` events; `Activate()`, `ResetTrace()` |
| `LearnBeatPatternSceneManager` | `BeatPathManager` | `ShowPath()`, `HidePath()`, `ActivateStartOrb()` |
| `LearnBeatPatternSceneManager` | `FeedbackPanel` | `SetMode()`, `ShowImmediate()`, `StoreAttempt()`, `ShowAllAttempts()`, `ClearSession()` |
| `LearnBeatPatternSceneManager` | `LLMFeedbackManager` | `RequestFeedback()` |
| `LearnBeatPatternSceneManager` | `MinimapGenerator` | `Generate()` |
| `LearnBeatPatternSceneManager` | `AudioManager` | `PlayClip()`, `PlaySuccess()`, `PlayFail()` |
| `LearnBeatPatternSceneManager` | `SpatialLabelManager` | `SetText()`, `Hide()` |
| `PracticeSceneManager` | `ScoreStandUI` | `OnBeatSelected` event |
| `PracticeSceneManager` | `PracticeTracingSystem` | `OnTraceComplete`, `OnTraceStarted` events; `Activate()`, `ResetTrace()`, `SetTimeout()` |
| `PracticeSceneManager` | `BeatPathManager` | `ShowPath()`, `HidePath()`, `HideGuidePath()` |
| `PracticeSceneManager` | `FeedbackPanel` | `ShowImmediatePractice()`, `ClearSession()` |
| `PracticeSceneManager` | `LLMFeedbackManager` | `RequestPracticeFeedback()` |
| `PracticeSceneManager` | `PracticeMinimapGenerator` | `Generate()` |
| `PracticeSceneManager` | `AudioManager` | `PlayClip()` |
| `PracticeSceneManager` | `SpatialLabelManager` | `SetText()`, `Hide()` |
| `PracticeSceneManager` | `PracticeMetricsCollector` | `Reset()` |
| `TracingSystem` | `BeatPathManager` | Spatial queries, orb control |
| `TracingSystem` | `MetricsCollector` | `StartRecording()`, `RecordFrame()`, `GetResult()`, `Reset()` |
| `PracticeTracingSystem` | `BeatPathManager` | `GetTracingPlane()` |
| `PracticeTracingSystem` | `PracticeMetricsCollector` | `StartRecording()`, `RecordFrame()`, `GetResult()`, `Reset()` |
| `PracticeTracingSystem` | `SpatialLabelManager` | `SetText()` |
| `PracticeMetricsCollector` | `DirectionalSegmentAnalyser` | `Analyse()` static call |
| `MinimapGenerator` | `BeatPathManager` | `GetSplineSamplePoints()` |
| `MinimapGenerator` | `TracingSystem` | `GetTracePoints()`, `GetTraceColors()` |
| `PracticeMinimapGenerator` | `BeatPathManager` | `GetSplineSamplePoints()` |
| `PracticeMinimapGenerator` | `PracticeTracingSystem` | `GetTracePoints()`, `GetTraceColors()` |
| `LLMFeedbackManager` | `FeedbackPanel` | `SetStoredAIFeedback()`, `SetImmediateAIFeedback()` |

### Shared data flow (MetricsResult)

```
Learn Mode:
  TracingSystem → MetricsCollector.GetResult() → MetricsResult
    → LearnBeatPatternSceneManager
      → FeedbackPanel (display)
      → LLMFeedbackManager (AI prompt)

Practice Mode:
  PracticeTracingSystem → PracticeMetricsCollector.GetResult()
    → PracticeMetricsCollector calls DirectionalSegmentAnalyser.Analyse()
    → MetricsResult
      → PracticeSceneManager
        → FeedbackPanel (display)
        → LLMFeedbackManager (AI prompt)
        → UpdateAdaptiveTimer (adjust next timeout)
```

### Scripts shared between both scenes

| Script | Learn Scene Role | Practice Scene Role |
|---|---|---|
| `BeatPathManager` | Shows guide path + start/end orbs | Shows path momentarily; guide hidden during tracing |
| `ScoreStandUI` | Beat selection + tutorial | Beat selection + tutorial (identical) |
| `FeedbackPanel` | Immediate or delayed feedback | Immediate feedback only (practice variant methods) |
| `LLMFeedbackManager` | `RequestFeedback()` | `RequestPracticeFeedback()` (different prompt) |
| `AudioManager` | Singleton, plays all clips | Singleton, plays all clips |
| `SpatialLabelManager` | Instruction text above path | Instruction text above path |
| `BeatPatternTypes` | Enums + MetricsResult | Enums + MetricsResult |
