using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

// Controls the main menu scene.
// Loads Learn or Practice scene on button click.

public class MainMenuManager : MonoBehaviour
{
    // Called by LearnButton onClick in Inspector
    public void LoadLearnScene()
    {
        StartCoroutine(LoadScene("LearnBeatPatternv3"));
    }

    // Called by PracticeButton onClick in Inspector
    public void LoadPracticeScene()
    {
        StartCoroutine(LoadScene("PracticeBeatPattern"));
    }

    private IEnumerator LoadScene(string sceneName)
    {
        // Small delay so button click feels responsive
        yield return new WaitForSeconds(0.2f);
        SceneManager.LoadScene(sceneName);
    }
}