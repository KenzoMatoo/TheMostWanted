using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Pantallas de PAUSA y de MUERTE con la estetica del menu principal (letra pixel, titulo con degradado, cubito marcador).
// Se crea sola en cualquier escena con jugador (niveles y tutorial) a partir de Resources/MenusJuego.prefab,
// y apaga el menu de pausa y el DeathManager viejos de la escena.
//  - Esc (o Start del control): pausa / continua.
//  - Al morir: "FIN DEL CAMINO" y solo el boton MENU (la corrida termina). En el tutorial no sale: alli se reaparece.
public class GameMenus : MonoBehaviour
{
    [Header("Arte (mismo estilo que el menu)")]
    [SerializeField] private Texture2D fontAtlas;
    [SerializeField] private Sprite cube;
    [SerializeField] private Sprite titlePause;
    [SerializeField] private Sprite titleDeath;
    [SerializeField] private Sprite labelContinue;
    [SerializeField] private Sprite labelMenu;

    [Header("Ajustes")]
    [SerializeField] private string menuScene = "menu";
    [Tooltip("Segundos entre la muerte y la pantalla (deja ver la animacion de muerte)")]
    [SerializeField] private float deathDelay = 1.1f;
    [SerializeField] private string deathSubtitle = "La recompensa sigue suelta";
    [SerializeField] private Color pauseDim = new Color(0.09f, 0.05f, 0.13f, 0.84f);
    [SerializeField] private Color deathDim = new Color(0.12f, 0.03f, 0.07f, 0.9f);
    [SerializeField] private Color textColor = Color.white;
    [SerializeField] private Color highlight = new Color(0.36f, 0.79f, 0.65f, 1f);
    [SerializeField] private Color dimText = new Color(0.85f, 0.78f, 0.72f, 1f);

    private const int PixelScale = 4;   // same size as the main menu (sprites x4 on a 1920x1080 canvas)

    private CanvasGroup pauseGroup, deathGroup;
    private RectTransform pauseTitle, deathTitle;
    private Button continueButton, pauseMenuButton, deathMenuButton;
    private PlayerStats player;
    private bool paused, dead, loading, deathScreenEnabled;

    // ---------------------------------------------------------------- Auto creation

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Spawn();
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m) => Spawn();

    private static void Spawn()
    {
        if (FindAnyObjectByType<GameMenus>() != null) return;
        if (FindAnyObjectByType<PlayerStats>() == null) return;   // menu, HUB...
        var prefab = Resources.Load<GameMenus>("MenusJuego");
        if (prefab == null) { Debug.LogWarning("GameMenus: falta Resources/MenusJuego.prefab"); return; }
        Instantiate(prefab).name = "Menus Juego";
    }

    // ---------------------------------------------------------------- Setup

    private void Awake()
    {
        DisableLegacyMenus();
        player = FindAnyObjectByType<PlayerStats>();
        // The tutorial brings the player back at a checkpoint, so it has no death screen
        deathScreenEnabled = FindAnyObjectByType<TutorialRespawn>() == null;
        if (player != null && deathScreenEnabled) player.OnDeath.AddListener(OnPlayerDeath);

        Build();
        pausa.GameIsPaused = false;
        AudioListener.pause = false;
    }

    private void OnDestroy()
    {
        if (player != null) player.OnDeath.RemoveListener(OnPlayerDeath);
    }

    // The old pause menu (Canvas prefab) and the old death panel would fight with these screens
    private static void DisableLegacyMenus()
    {
        foreach (var p in FindObjectsByType<pausa>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (p.pauseMenuUI != null) p.pauseMenuUI.SetActive(false);
            p.enabled = false;
        }
        var panelField = typeof(DeathManager).GetField("deathPanel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        foreach (var d in FindObjectsByType<DeathManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (panelField != null && panelField.GetValue(d) is GameObject panel && panel != null) panel.SetActive(false);
            d.enabled = false;
        }
    }

    // ---------------------------------------------------------------- Input

    private void Update()
    {
        if (loading || dead) return;
        bool toggle = (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                   || (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame);
        if (toggle) { if (paused) Resume(); else Pause(); }

        // Keep a button selected for keyboard / gamepad if the mouse cleared it
        if (paused && EventSystem.current != null && EventSystem.current.currentSelectedGameObject == null)
        {
            var kb = Keyboard.current;
            if (kb != null && (kb.upArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame))
                Select(continueButton);
        }
    }

    // ---------------------------------------------------------------- Pause

    public void Pause()
    {
        if (paused || dead || loading) return;
        paused = true;
        StopHitStop();
        Time.timeScale = 0f;
        pausa.GameIsPaused = true;
        AudioListener.pause = true;   // sounds and loops freeze with the game
        StopAllCoroutines();
        StartCoroutine(Show(pauseGroup, pauseTitle, 0.18f));
        Select(continueButton);
    }

    public void Resume()
    {
        if (!paused || loading) return;
        paused = false;
        StopHitStop();
        Time.timeScale = 1f;
        pausa.GameIsPaused = false;
        AudioListener.pause = false;
        StopAllCoroutines();
        StartCoroutine(Hide(pauseGroup, 0.12f));
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    // ---------------------------------------------------------------- Death

    private void OnPlayerDeath()
    {
        if (dead) return;
        dead = true;
        if (paused) { paused = false; AudioListener.pause = false; pauseGroup.alpha = 0f; pauseGroup.gameObject.SetActive(false); }
        StartCoroutine(DeathRoutine());
    }

    private IEnumerator DeathRoutine()
    {
        yield return new WaitForSecondsRealtime(deathDelay);
        RunProgression.EndRun();   // the run is lost: the hub starts a new one
        StopHitStop();
        Time.timeScale = 0f;
        pausa.GameIsPaused = true;
        yield return Show(deathGroup, deathTitle, 0.6f);
        Select(deathMenuButton);
    }

    // ---------------------------------------------------------------- Menu

    private void GoToMenu()
    {
        if (loading) return;
        loading = true;
        StartCoroutine(LoadMenu());
    }

    private IEnumerator LoadMenu()
    {
        var fader = CameraRoom.GetFader();
        yield return CameraRoom.Fade(fader, 1f, 0.35f);
        Time.timeScale = 1f;
        pausa.GameIsPaused = false;
        AudioListener.pause = false;
        SceneManager.LoadScene(menuScene);
    }

    // ---------------------------------------------------------------- Animation

    private IEnumerator Show(CanvasGroup g, RectTransform title, float time)
    {
        g.gameObject.SetActive(true);
        g.interactable = true;
        g.blocksRaycasts = true;
        float start = g.alpha;
        for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
        {
            float k = t / time;
            g.alpha = Mathf.Lerp(start, 1f, k);
            // Title drops in with a small bounce, like a stamp
            if (title != null) title.localScale = Vector3.one * Mathf.Lerp(1.35f, 1f, 1f - (1f - k) * (1f - k));
            yield return null;
        }
        g.alpha = 1f;
        if (title != null) title.localScale = Vector3.one;
    }

    private IEnumerator Hide(CanvasGroup g, float time)
    {
        g.interactable = false;
        g.blocksRaycasts = false;
        float start = g.alpha;
        for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
        {
            g.alpha = Mathf.Lerp(start, 0f, t / time);
            yield return null;
        }
        g.alpha = 0f;
        g.gameObject.SetActive(false);
    }

    // ---------------------------------------------------------------- UI building

    private void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 900;   // over the HUD, under the black fader (1000)
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        EnsureEventSystem();

        // Pause
        pauseGroup = Overlay("Pausa", pauseDim);
        Stripes(pauseGroup.transform);
        pauseTitle = Picture(pauseGroup.transform, titlePause, new Vector2(0, 230));
        continueButton = MenuButton(pauseGroup.transform, labelContinue, new Vector2(0, 20), Resume);
        pauseMenuButton = MenuButton(pauseGroup.transform, labelMenu, new Vector2(0, -100), GoToMenu);
        Link(continueButton, pauseMenuButton);
        Text(pauseGroup.transform, "Esc para continuar", 3, new Vector2(0, -330), dimText);

        // Death
        deathGroup = Overlay("Muerte", deathDim);
        Stripes(deathGroup.transform);
        deathTitle = Picture(deathGroup.transform, titleDeath, new Vector2(0, 170));
        Text(deathGroup.transform, deathSubtitle, 4, new Vector2(0, 30), dimText);
        deathMenuButton = MenuButton(deathGroup.transform, labelMenu, new Vector2(0, -170), GoToMenu);
        Link(deathMenuButton, deathMenuButton);
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null) return;
        new GameObject("EventSystem", typeof(EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
    }

    private CanvasGroup Overlay(string name, Color dim)
    {
        var r = Rect(name, transform, Vector2.zero, Vector2.zero);
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        var img = r.gameObject.AddComponent<Image>();
        img.color = dim;
        img.raycastTarget = true;   // blocks clicks to the game
        var g = r.gameObject.AddComponent<CanvasGroup>();
        g.alpha = 0f; g.interactable = false; g.blocksRaycasts = false;
        r.gameObject.SetActive(false);
        return g;
    }

    // Thin warm bands at the top and bottom, like the horizon line of the menu art
    private void Stripes(Transform parent)
    {
        foreach (float y in new[] { 0f, 1f })
        {
            var r = Rect("Franja", parent, Vector2.zero, Vector2.zero);
            r.anchorMin = new Vector2(0f, y); r.anchorMax = new Vector2(1f, y);
            r.pivot = new Vector2(0.5f, y);
            r.sizeDelta = new Vector2(0f, 12f);
            r.anchoredPosition = new Vector2(0f, y == 0f ? 64f : -64f);
            var img = r.gameObject.AddComponent<Image>();
            img.color = new Color(0.9f, 0.45f, 0.27f, 0.55f);
            img.raycastTarget = false;
        }
    }

    private RectTransform Picture(Transform parent, Sprite s, Vector2 pos)
    {
        Vector2 size = s != null ? new Vector2(s.rect.width, s.rect.height) * PixelScale : new Vector2(400, 116);
        var r = Rect(s != null ? s.name : "Titulo", parent, pos, size);
        var img = r.gameObject.AddComponent<Image>();
        img.sprite = s; img.raycastTarget = false;
        if (s == null) img.color = Color.clear;
        return r;
    }

    // Same look as the main menu buttons: pixel label, grows and turns green when selected, cube marker on the left
    private Button MenuButton(Transform parent, Sprite label, Vector2 pos, UnityEngine.Events.UnityAction onClick)
    {
        Vector2 labelSize = label != null ? new Vector2(label.rect.width, label.rect.height) * PixelScale : new Vector2(264, 80);
        var r = Rect("Boton " + (label != null ? label.name : ""), parent, pos, new Vector2(labelSize.x + 180f, labelSize.y + 16f));
        var hit = r.gameObject.AddComponent<Image>();
        hit.color = Color.clear;   // invisible, only for the mouse

        var lr = Rect("Texto", r, Vector2.zero, labelSize);
        var li = lr.gameObject.AddComponent<Image>();
        li.sprite = label; li.raycastTarget = false; li.color = textColor;

        var mr = Rect("Cubito", r, new Vector2(-labelSize.x * 0.5f - 52f, 0f), new Vector2(52, 52));
        var mi = mr.gameObject.AddComponent<Image>();
        mi.sprite = cube; mi.preserveAspect = true; mi.raycastTarget = false;

        var btn = r.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(onClick);
        var fx = r.gameObject.AddComponent<MenuButtonFx>();
        fx.Setup(li, null, mi, textColor, highlight);
        return btn;
    }

    private static void Link(Button a, Button b)
    {
        var na = new Navigation { mode = Navigation.Mode.Explicit, selectOnDown = b, selectOnUp = b };
        var nb = new Navigation { mode = Navigation.Mode.Explicit, selectOnDown = a, selectOnUp = a };
        a.navigation = na;
        if (a != b) b.navigation = nb;
    }

    private PixelText Text(Transform parent, string text, int scale, Vector2 pos, Color c)
    {
        var r = Rect("Texto", parent, pos, Vector2.zero);
        var t = r.gameObject.AddComponent<PixelText>();
        t.Atlas = fontAtlas; t.Scale = scale; t.Alignment = PixelText.Align.Center; t.Color = c; t.Text = text;
        t.Rebuild();
        return t;
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var r = (RectTransform)go.transform;
        r.SetParent(parent, false);
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
        r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = pos;
        r.sizeDelta = size;
        return r;
    }

    private static void Select(Button b)
    {
        if (b != null && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(b.gameObject);
    }

    private static void StopHitStop()
    {
        var h = HitStopManager.Instance;
        if (h != null) h.ForceStop();
    }
}
