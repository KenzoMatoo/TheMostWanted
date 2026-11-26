using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneLoader : MonoBehaviour
{

    public void LoadScene(string sceneName)
    {
        Time.timeScale = 1f;
        // The new scene starts playing (not paused). With 'true' the first Esc press in a level did nothing.
        pausa.GameIsPaused = false;

        SceneManager.LoadScene(sceneName);
    }
}

