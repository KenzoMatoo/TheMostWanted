using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Menu principal: Tutorial, Jugar y Salir.
// Cada boton llama a uno de los metodos publicos (se conectan solos en Awake si los botones estan asignados).
// Al elegir una opcion la pantalla se funde a negro y se carga la escena.
public class MainMenu : MonoBehaviour
{
    [Header("Escenas")]
    [SerializeField] private string tutorialScene = "TutorialScene";
    [SerializeField] private string playScene = "HUB";

    [Header("Botones")]
    [SerializeField] private Button tutorialButton;
    [SerializeField] private Button playButton;
    [SerializeField] private Button exitButton;

    [SerializeField] private float fadeTime = 0.35f;

    private bool loading;

    private void Awake()
    {
        Time.timeScale = 1f;
        pausa.GameIsPaused = false;

        if (tutorialButton != null) tutorialButton.onClick.AddListener(PlayTutorial);
        if (playButton != null) playButton.onClick.AddListener(Play);
        if (exitButton != null) exitButton.onClick.AddListener(Exit);
    }

    private void Start()
    {
        // Seleccionar el primer boton para poder usar teclado / control desde el inicio
        Button first = tutorialButton != null ? tutorialButton : playButton;
        if (first != null && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(first.gameObject);
    }

    private void Update()
    {
        // Si el mouse quita la seleccion, el teclado vuelve a tener un boton seleccionado
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == null)
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && (kb.upArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame))
                Start();
        }
    }

    public void PlayTutorial() => Load(tutorialScene);
    public void Play() => Load(playScene);

    public void Exit()
    {
        if (loading) return;
        loading = true;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void Load(string scene)
    {
        if (loading || string.IsNullOrEmpty(scene)) return;
        loading = true;
        StartCoroutine(LoadRoutine(scene));
    }

    private IEnumerator LoadRoutine(string scene)
    {
        var fader = CameraRoom.GetFader();
        yield return CameraRoom.Fade(fader, 1f, fadeTime);
        Time.timeScale = 1f;
        pausa.GameIsPaused = false;
        SceneManager.LoadScene(scene);
        // The fader belongs to this scene; the next one starts clean
    }
}
