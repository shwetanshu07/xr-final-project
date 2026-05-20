# VR Beat Pattern Conducting Trainer

This is a standalone VR application built for the Meta Quest 3s that tries to address a fundamental access problem in music education - **conducting is a spatially complex, embodied motor skill that has traditionally required a live ensemble, a rehearsal space and an instructor present every time a student wants to practise**. This application tries to remove all three dependencies. The user selects a beat pattern from a virtual music stand, traces a 3D spline path in space using a virtual baton and receives immediate colour coded visual feedback, haptic error signals, a per attempt minimap showing their gesture against the ideal path and AI generated coaching feedback powered by GPT-OSS 120B LLM. A separate practice mode allows the student to reproduce the gesture from memory without any visible guide, evaluated through directional segment analysis. **The application supports the 2/4 (Two Beat) and 3/4 (Three Beat) conducting patterns.**

**This project was built as a part of my Spring'26 course @ NYU - CSGY9223 Special Topics: Designing XR Environments for Skill Learning**

### Demo Video - https://youtu.be/qU-jZ5PcdrQ 

## Motivation and Existing Solutions

| Existing Solution | Flaws |
|----------|--------------|
| YouTube Tutorials | Passive; no feedback on your own gesture |
| 2D Conducting Apps | Tap based; no 3D arm movement captured |
| Traditional Lessons | Expensive, instructor dependent, no solo practice |
| Metronome Training | Timing only and ignores spatial gesture shape entirely |
| VR Apps (like Maestro) | Gamified and not designed for structured skill acquisition |

## Learning Principles
This application is grounded in 2 learning science frameworks - 
1. **Constructionism (Papert)** holds that learning deepens when students create observable, reflective artifacts. Every attempt in this application produces a minimap which is a visual record of the student's own gesture constructed through their movement. The student can immediately compare what they drew against the ideal path, reflect on the difference and adjust on the next attempt. 
2. **Embodied Cognition** recognises that motor skills cannot be separated from the physical act of performing them. Conducting beat patterns are learned through the body, not by reading about them or watching a video. The application puts the student's arm in the correct spatial path repeatedly, with haptic pulses grounding the experience physically so that the student feels the error and not just sees it. Repetition builds motor memory through embodied practice.


## Features

### Learn Mode
- 3D spline guide path rendered in the user's physical space
- Colour coded deviation trail — 
  - green (minimal deviation from the actual path)
  - yellow (medium deviation)
  - red (large deviation)
- Continuous haptic pulse when deviation exceeds threshold (red)
- Configurable max attempts and feedback timing (immediate or delayed). Immediate means feedback just after the attempt and in delayed an aggregated feedback is shown after all the attempts are done.
- Per-attempt minimap showing ideal path vs player trace
- Quantitative metrics panel showing path accuracy, beat positions, speed consistency
- AI coaching feedback (via Groq API and model GPT-OSS 120B) which tells the user the following - 
  - Main issue - single most important correction with specific advice on how to correct it
  - String - one thing done well
- Video tutorial per pattern
- Audio tutorial per pattern

### Practice Mode
- In this mode no visible guide path so that student reproduces gesture from memory
- Now that the user is not following a shown path I use Directional segment analysis to evaluate each stroke direction
- Velocity-based ictus detection without fixed spatial markers
- Adaptive timer to make the beat gesture— 60s when struggling, 30s baseline, 15s when performing well
- Per attempt minimap with independently normalised ideal and player paths
- AI coaching feedback

### General
- 2/4 (Two Beat) and 3/4 (Three Beat) pattern support
- Main menu scene with mode selection
- Score stand UI with pattern description, diagram image, and video tutorial
- Success and fail sound effects


## Platform and Requirements

| Requirement | Version / Detail |
|-------------|-----------------|
| Unity | 2022.3.19f1 |
| Render Pipeline | Universal Render Pipeline (URP) |
| XR Interaction Toolkit | 2.5.x (Action-Based) |
| Meta XR SDK | 60.0+ |
| Unity Splines Package | 2.x |
| LLM Model | openai/gpt-oss-120b via Groq |
| Internet | Required for AI feedback only |
| Tested on Headset | Meta Quest 3s |


## Project Structure

```
Assets/
├── MyAssets/
│   (this contains all my assets incl. images, audio, materials, scene environment objects)
│
│
├── Scenes/
│   ├── MainMenuv2             # Scene 0 — inital scene for mode selection
│   ├── LearnBeatPatternv3     # Scene 1 — learning mode with guided tracing with feedback
│   └── PracticeBeatPattern    # Scene 2 — practice mode
│   (ignore other scenes)
│
├── Scripts/
│   (all scripts under this)
```

## Scenes Overview

### MainMenuv2 (Scene 0)
The entry point of the application. Displays the application name, a short description, and two buttons — **Learn Mode** and **Practice Mode**. Clicking either button loads the corresponding scene. Contains an XR Origin, a World Space canvas, and the MainMenuManager script.

### LearnBeatPatternv3 (Scene 1)
The guided learning scene. The user selects a beat pattern from the score stand, hears an audio introduction, and traces a 3D spline path with their right controller. Real time feedback is provided through the colour coded trail and haptic pulses. After each attempt a feedback panel appears with a minimap, metrics and AI coaching text. Configurable via inspector — max attempts and feedback timing (immediate or delayed).

### PracticeBeatPattern (Scene 2)
It is the free practice scene. No guide path is visible. The user holds the trigger and draws the conducting gesture freely from memory anywhere on the tracing plane. Trigger release ends the gesture. The system evaluates directional accuracy per segment and detects ictus points from velocity direction reversals. An adaptive timer adjusts the allowed gesture duration based on the previous attempt's score.

*There is currently no in scene back button. To return to the main menu restart the application or load the scene from the Unity editor.*


## Scripts Overview

### Tracing System

| Script | Scene | Description |
|--------|-------|-------------|
| `TracingSystem.cs` | Learn | Casts a ray from the right controller onto the TracingPlane each frame. Records hit position, calculates controller velocity, draws the colour-coded trail, detects start orb proximity, end orb proximity, and fires haptic pulses when deviation exceeds threshold. |
| `PracticeTracingSystem.cs` | Practice | Same ray-projection approach but without orb proximity checks. Trigger hold anywhere starts tracing. Trigger release ends the gesture and fires OnTraceComplete. Includes countdown timer display on SpatialLabelCanvas. |

### Metrics

| Script | Scene | Description |
|--------|-------|-------------|
| `MetricsCollector.cs` | Learn | Records path deviation, ictus proximity, and speed per frame. Calculates average deviation, ictus hits, speed CV, and weighted score at end of attempt. |
| `PracticeMetricsCollector.cs` | Practice | Records velocity per frame. Detects ictus by Y-axis velocity direction reversal. Passes velocity samples and ictus frame indices to DirectionalSegmentAnalyser. |
| `DirectionalSegmentAnalyser.cs` | Practice | Static class. Splits velocity samples into segments at detected ictus frames. Computes dominant direction per segment via vector averaging. Compares to expected directions for the selected pattern using dot product. |
| `MinimapGenerator.cs` | Learn | Samples 80 points from the active spline and retrieves recorded trace points from TracingSystem. Calculates shared bounds, renders both paths onto a 256×256 Texture2D using Bresenham's line algorithm. |
| `PracticeMinimapGenerator.cs` | Practice | Same as MinimapGenerator but normalises each path independently to its own bounds so size differences between the ideal path and player trace do not affect visual comparison. |

### Path Management

| Script | Scene | Description |
|--------|-------|-------------|
| `BeatPathManager.cs` | Both | Manages multiple PatternData entries (one per pattern). Each PatternData holds references to a pattern root GameObject containing a SplineContainer, LineRenderer, TracingPlane, StartOrb, and EndOrb. Activates the correct pattern root on selection. Provides spline sampling, nearest-point queries, and ictus world positions to other systems. |

### Feedback

| Script | Scene | Description |
|--------|-------|-------------|
| `FeedbackPanel.cs` | Both | Controls the feedback panel. Supports immediate mode (show after each attempt) and delayed mode (store all attempts, show together at end with Prev/Next navigation). Stores minimaps, results, and AI text per attempt. Updates AI text when LLM response arrives. |
| `LLMFeedbackManager.cs` | Both | Builds structured prompts from MetricsResult data and sends POST requests to the Groq API. Parses the JSON response and calls FeedbackPanel.SetStoredAIFeedback(). Includes a separate practice prompt (RequestPracticeFeedback) and a deterministic fallback if the API is unavailable. |

### Scene Management

| Script | Scene | Description |
|--------|-------|-------------|
| `LearnBeatPatternSceneManager.cs` | Learn | Master controller for the Learn scene. Manages scene state machine (Loading → BeatSelection → CompanionIntro → TraceReady → Tracing → TraceSuccess/TraceFailed). Handles feedback timing configuration, attempt counting, session completion, and audio playback sequencing. |
| `PracticeSceneManager.cs` | Practice | Master controller for the Practice scene. Always immediate feedback, unlimited attempts. Manages adaptive timer logic and calls PracticeTracingSystem and PracticeMetricsCollector. |
| `MainMenuManager.cs` | MainMenu | Loads LearnBeatPattern or PracticeBeatPattern scene on button click. |

### UI

| Script | Scene | Description |
|--------|-------|-------------|
| `ScoreStandUI.cs` | Both | Manages the score stand canvas — beat pattern selection (Canvas 1) and beat info (Canvas 2). Handles GIF tutorial animation (sprite sheet frame playback), audio tutorial (looping AudioClip per pattern), and fires OnBeatSelected event to SceneManager. |
| `SpatialLabelManager.cs` | Both | Controls the floating text label above the tracing area. SetText() shows instructional text. Hide() removes it. Used for attempt numbers, timer countdown, and status messages. |
| `BatonController.cs` | Both | Positions and rotates the baton visual to match the right controller with a configurable position and rotation offset. Purely visual — all gesture data comes from the controller itself. |
| `AudioManager.cs` | Both | Singleton. Plays success and fail sound effects via PlayOneShot. Also exposes a public PlayClip(AudioClip, string) method for welcome and pattern intro audio called from SceneManager. |


## Core Metrics

The application tracks 3 metrics per attempt in Learn mode and 2 in Practice mode.

---

### Metric 1 — Path Deviation (Learn mode only)

**What it measures:** How closely the user's hand followed the ideal conducting path.

**How it works:** Every frame during tracing the system finds the nearest point on the ideal spline to the current ray hit position using Unity's SplineUtility.GetNearestPoint. The straight line distance between them is the frame deviation in metres. At the end of the attempt all frame deviations are averaged.

**How it scores:** The average deviation is converted to a 0 to 100 score using the formula:

```
Path Score = max(0, min(100, (1 - deviation_cm / 10) × 100))
```

A deviation of 0cm scores 100. A deviation of 10cm or more scores 0. The score scales linearly between these bounds.

**Weight in overall score:** 35%

---

### Metric 2 — Ictus Accuracy

**What it measures:** Whether the user's hand reached the correct ictus positions which are the specific points in space where a conducting gesture must land and reverse direction.

**How it works in Learn mode:** Before tracing begins the system retrieves the world positions of designated ictus knots from the spline. Every frame the distance from the current hand position to each ictus point is measured. The minimum distance ever reached for each ictus is tracked across the full attempt. Any ictus where the closest approach was within 5cm is counted as a hit.

**How it works in Practice mode:** There are no fixed ictus positions because there is no guide path. Instead the system detects ictus by monitoring the Y axis component of the controller velocity. When the hand transitions from moving downward to moving upward (sign change crossing defined thresholds) a direction reversal is detected and counted as an ictus. The frame index of each reversal is also stored for use by DirectionalSegmentAnalyser.

**Weight in overall score:** 40% (weighted highest because landing the beat position is the most fundamental skill in conducting)

---

### Metric 3 — Speed Consistency

**What it measures:** Whether the user maintained a steady even arm speed throughout the gesture or was jerky and irregular.

**How it works:** The magnitude of the controller velocity vector is recorded every frame as a speed sample in metres per second. Frames below 0.1 m/s are filtered out because these represent stationary moments and should not penalise consistency. The Coefficient of Variation (CV) is calculated on the remaining samples — standard deviation divided by mean. A CV of zero means perfectly consistent speed.

**How it scores:**
```
Speed Score = max(0, min(100, (1 - CV / maxCV) × 100))
```
Where maxCV is 1.0 by default (configurable in Inspector). Lower CV produces a higher score.

**Weight in overall score:** 25%

---

### Overall Weighted Score

```
Overall = (Path Score × 0.35) + (Ictus Accuracy % × 0.40) + (Speed Score × 0.25)
```

The overall score drives the adaptive timer in Practice mode

---

### Directional Segment Analysis (Practice mode only)

**What it measures:** Whether each stroke of the conducting gesture moved in the correct direction.

**How it works:** The velocity samples recorded during tracing are split into segments at each detected ictus frame index. For each segment the dominant movement direction is calculated by averaging the normalised velocity vectors of all frames in that segment. This dominant direction is compared to the expected direction for that segment using a dot product. A dot product above 0.3 (within approximately 72 degrees of the expected direction) counts as correct.

Expected directions per pattern:
```
2/4: Segment 1 = DOWN, Segment 2 = UP
3/4: Segment 1 = DOWN, Segment 2 = RIGHT, Segment 3 = UP
```

The result is a per segment boolean (correct or incorrect) and an overall directional accuracy percentage.


## Setup and Installation

- Clone the repository: `git clone https://github.com/your-username/vr-conducting-trainer.git`
- Open Unity Hub → Add → select the cloned folder → open with Unity **2022.3.19f1**
- In **Window → Package Manager** confirm these are installed: XR Interaction Toolkit 2.5.x, Unity Splines 2.x, Universal RP 14.x, Meta XR SDK 60.0+
- Switch platform: **File → Build Settings → Android**
- In **Project Settings → Player → Android → Other Settings** set Internet Access to **Required** and Target Architecture to **ARM64**
- Get a free API key from [console.groq.com](https://console.groq.com) → paste it into the **Api Key** field on the LLMFeedbackManager component in both LearnBeatPattern and PracticeBeatPattern scenes
- Enable Developer Mode on Quest
- Connect Quest via USB → **File → Build Settings → Build and Run**

> AI feedback requires internet. All other functionality works fully offline.

## How to Use the Application

### Starting the application

The main menu appears as a floating canvas in front of you. Point the right controller ray at a button and press the trigger to select a mode.

### Learn Mode

```
1. Point ray at 2/4 or 3/4 button → press trigger to select
2. Read the pattern description on the score stand
3. Click the video tutorial button to see the correct gesture animated
4. Click the audio tutorial button to hear the beat
5. Wait for the audio introduction to finish
6. A glowing yellow orb appears at the start of the path
7. Point your ray near the orb — it turns green when you are close
8. Hold the trigger to begin tracing
9. Move your hand along the guide path. The trail shows how you are doing : Green = on track, Yellow = drifting and Red = too far off. Controller vibrates when trail turns red
10. Reach the end orb to complete the gesture
11. Feedback panel appears - view your minimap, metrics and AI coaching
12. Click Try Again on the score stand to attempt again
```

### Practice Mode

```
1. Select a beat pattern from the score stand
2. No guide path will appear as this mode tests recall from memory
3. Hold the trigger anywhere in front of you to begin drawing
4. Move your hand through the correct conducting gesture from memory
5. Release the trigger when your gesture is complete
6. Feedback panel appears and minimap shows your trace vs the ideal shape
7. The timer counts down during your gesture so complete it before it runs out and your score affects the next attempt timer.
8. Click Try Again to attempt again
```

## Future Work
- Integration of other modules like gesture based tempo control
- 3d hand tracking instead of ray based projection
- Adaptive difficulty
