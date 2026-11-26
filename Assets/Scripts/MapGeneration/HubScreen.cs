using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Pantalla del HUB entre niveles, sobre el tablero de recompensas del fondo.
// Cada uno de los 4 marcos del tablero es un cartel de SE BUSCA de un tipo de nivel (Normal, Plataformeo, Elite, Jefe).
// Segun el paso de la corrida (RunProgression), los carteles de los niveles que se pueden elegir estan iluminados y
// se pueden presionar; los demas estan apagados con un sello de CERRADO.
// Arriba: el texto de lore del paso. Abajo: "PASO 2 DE 6", el camino de la corrida (cubitos) y el boton MENU.
public class HubScreen : MonoBehaviour
{
    [System.Serializable]
    public class Poster
    {
        public LevelType type;
        public Sprite sprite;
        [Tooltip("Marco del fondo donde va el cartel, en proporcion de la imagen (0..1): x min, y min, x max, y max")]
        public Vector4 frame;
    }

    [SerializeField] private RunProgression progression;
    [SerializeField] private Canvas canvas;
    [Tooltip("La imagen del fondo (el tablero). Los carteles se anclan a ella")]
    [SerializeField] private RectTransform background;
    [SerializeField] private Poster[] posters;
    [SerializeField] private Texture2D fontAtlas;
    [SerializeField] private Sprite cubeFull;
    [SerializeField] private Sprite cubeEmpty;
    [SerializeField] private string menuScene = "menu";

    [Header("Colores")]
    [SerializeField] private Color bandColor = new Color(0.08f, 0.05f, 0.04f, 0.78f);
    [SerializeField] private Color panelColor = new Color(0.10f, 0.07f, 0.06f, 0.85f);
    [SerializeField] private Color borderColor = new Color(0.86f, 0.52f, 0.32f, 0.95f);
    [SerializeField] private Color highlight = new Color(0.36f, 0.79f, 0.65f, 1f);
    [SerializeField] private Color textColor = Color.white;
    [SerializeField] private Color dimText = new Color(0.85f, 0.8f, 0.72f, 1f);
    [SerializeField] private Color closedTint = new Color(0.45f, 0.4f, 0.36f, 1f);
    [SerializeField] private Color stampColor = new Color(0.75f, 0.12f, 0.1f, 1f);

    private RectTransform root;
    private readonly List<GameObject> built = new List<GameObject>();
    private readonly List<Button> choices = new List<Button>();
    private Button menuButton;
    private RectTransform currentNode;
    private bool loading;

    private void Start()
    {
        Time.timeScale = 1f;
        pausa.GameIsPaused = false;
        if (progression == null) { Debug.LogError("HubScreen: asigna el Run Progression."); return; }
        if (canvas == null) canvas = GetComponentInParent<Canvas>();

        // First time in the hub: begin the run. Coming back from a level keeps the progress.
        if (RunProgression.Active != progression) progression.StartRun();

        Build();
        StartCoroutine(FadeIn());
    }

    private IEnumerator FadeIn()
    {
        var fader = CameraRoom.GetFader();
        fader.alpha = 1f;
        yield return null;
        yield return CameraRoom.Fade(fader, 0f, 0.4f);
    }

    private void Update()
    {
        if (currentNode != null)
        {
            float s = 1.3f + 0.08f * Mathf.Sin(Time.unscaledTime * 4f);
            currentNode.localScale = new Vector3(s, s, 1f);
        }
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == null)
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.anyKey.wasPressedThisFrame) SelectFirst();
        }
    }

    // ---------------- Build ----------------

    private void Build()
    {
        foreach (var go in built) if (go != null) Destroy(go);
        built.Clear();
        choices.Clear();
        bool done = progression.IsComplete;

        root = Rect("Hub UI", canvas.transform, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one; root.sizeDelta = Vector2.zero;
        built.Add(root.gameObject);

        // Posters on the board (anchored to the background, so they stay on their frames at any resolution)
        foreach (var p in posters)
        {
            bool available = !done && progression.IsAvailable(p.type);
            var btn = PosterButton(p, available);
            if (available) choices.Add(btn);
        }

        // Top band: lore
        var top = Band(1f, 84f);
        Text(top, progression.CurrentLore, 3, Vector2.zero, 1600, PixelText.Align.Center, dimText);

        // Bottom band: step, run path, menu
        var bottom = Band(0f, 104f);
        Text(bottom, done ? "¡CORRIDA COMPLETA!" : $"PASO {progression.StepNumber} DE {progression.StepCount}", 5,
             new Vector2(-690, 0), 0, PixelText.Align.Center, textColor);
        BuildPath(bottom, new Vector2(80, 8));
        menuButton = SmallButton(bottom, new Vector2(800, 0), new Vector2(220, 76), "MENU", 4);
        menuButton.onClick.AddListener(() => Load(menuScene));

        if (done)
        {
            var newRun = SmallButton(root, new Vector2(0, -60), new Vector2(560, 110), "NUEVA CORRIDA", 5);
            newRun.onClick.AddListener(() => { progression.StartRun(); Build(); });
            choices.Add(newRun);
        }

        // Navigation: left/right between the available posters, down to the menu
        for (int i = 0; i < choices.Count; i++)
        {
            var nav = new Navigation { mode = Navigation.Mode.Explicit };
            nav.selectOnLeft = choices[(i - 1 + choices.Count) % choices.Count];
            nav.selectOnRight = choices[(i + 1) % choices.Count];
            nav.selectOnDown = menuButton;
            choices[i].navigation = nav;
        }
        menuButton.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = choices.Count > 0 ? choices[0] : null };
        SelectFirst();
    }

    private Button PosterButton(Poster p, bool available)
    {
        Transform parent = background != null ? background : (Transform)root;
        var go = new GameObject("Cartel " + p.type, typeof(RectTransform));
        built.Add(go);
        var r = (RectTransform)go.transform;
        r.SetParent(parent, false);
        r.anchorMin = new Vector2(p.frame.x, p.frame.y);
        r.anchorMax = new Vector2(p.frame.z, p.frame.w);
        r.offsetMin = r.offsetMax = Vector2.zero;

        var img = go.AddComponent<Image>();
        img.sprite = p.sprite;
        img.preserveAspect = true;
        img.color = available ? Color.white : closedTint;

        var btn = go.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.interactable = available;
        if (available)
        {
            btn.onClick.AddListener(() => Choose(p.type));
            // Cube marker under the poster when it is selected
            var m = new GameObject("Cubito", typeof(RectTransform));
            var mr = (RectTransform)m.transform;
            mr.SetParent(r, false);
            mr.anchorMin = mr.anchorMax = new Vector2(0.5f, 0f);
            mr.anchoredPosition = new Vector2(0f, -12f);
            mr.sizeDelta = new Vector2(44, 44);
            var mi = m.AddComponent<Image>(); mi.sprite = cubeFull; mi.preserveAspect = true; mi.raycastTarget = false;
            var fx = go.AddComponent<MenuButtonFx>();
            fx.Setup(null, null, mi, Color.white, Color.white);
        }
        else
        {
            // Red stamp across the poster
            var stamp = Text(r, "CERRADO", 4, Vector2.zero, 0, PixelText.Align.Center, stampColor);
            stamp.transform.localRotation = Quaternion.Euler(0, 0, 18f);
        }
        return btn;
    }

    private void BuildPath(RectTransform parent, Vector2 center)
    {
        int n = progression.StepCount;
        int current = progression.IsComplete ? n : progression.StepNumber - 1;
        float spacing = 110f;
        float x0 = center.x - (n - 1) * spacing / 2f;
        currentNode = null;

        var line = Rect("Linea", parent, new Vector2(0.5f, 0.5f), center, new Vector2((n - 1) * spacing, 6));
        Img(line, null, new Color(0.53f, 0.53f, 0.5f, 0.8f), false);
        if (current > 0)
        {
            float doneW = Mathf.Min(current, n - 1) * spacing;
            var doneLine = Rect("Linea hecha", parent, new Vector2(0.5f, 0.5f), new Vector2(x0 + doneW / 2f, center.y), new Vector2(doneW, 6));
            Img(doneLine, null, highlight, false);
        }
        for (int i = 0; i < n; i++)
        {
            bool past = i < current, now = i == current;
            var node = Rect("Paso " + (i + 1), parent, new Vector2(0.5f, 0.5f), new Vector2(x0 + i * spacing, center.y), new Vector2(52, 52));
            var img = Img(node, past || now ? cubeFull : cubeEmpty, now ? highlight : past ? Color.white : new Color(1f, 1f, 1f, 0.75f), false);
            img.preserveAspect = true;
            if (now) currentNode = node;
            var step = progression.steps[i];
            string label = step.pool.Count == 1 && step.pool[0] == LevelType.Boss ? "JEFE" : (i + 1).ToString();
            Text(parent, label, 2, new Vector2(x0 + i * spacing, center.y - 44), 0, PixelText.Align.Center, now ? highlight : dimText);
        }
    }

    // ---------------- Actions ----------------

    private void Choose(LevelType type)
    {
        if (!progression.IsAvailable(type)) return;
        Load(progression.GetSceneName(type));
    }

    private void Load(string scene)
    {
        if (loading || string.IsNullOrEmpty(scene)) return;
        loading = true;
        StartCoroutine(LoadRoutine(scene));
    }

    private IEnumerator LoadRoutine(string scene)
    {
        yield return CameraRoom.Fade(CameraRoom.GetFader(), 1f, 0.35f);
        Time.timeScale = 1f;
        pausa.GameIsPaused = false;
        SceneManager.LoadScene(scene);
    }

    private void SelectFirst()
    {
        if (EventSystem.current == null) return;
        var first = choices.Count > 0 ? choices[0] : menuButton;
        if (first != null) EventSystem.current.SetSelectedGameObject(first.gameObject);
    }

    // ---------------- UI helpers ----------------

    // A dark band across the whole width, at the top (anchorY = 1) or the bottom (anchorY = 0) of the screen
    private RectTransform Band(float anchorY, float height)
    {
        var go = new GameObject("Franja", typeof(RectTransform));
        var r = (RectTransform)go.transform;
        r.SetParent(root, false);
        r.anchorMin = new Vector2(0f, anchorY); r.anchorMax = new Vector2(1f, anchorY);
        r.pivot = new Vector2(0.5f, anchorY);
        r.anchoredPosition = Vector2.zero;
        r.sizeDelta = new Vector2(0f, height);
        Img(r, null, bandColor, false);
        return r;
    }

    private Button SmallButton(Transform parent, Vector2 pos, Vector2 size, string label, int scale)
    {
        var r = Rect("Boton " + label, parent, new Vector2(0.5f, 0.5f), pos, size);
        Img(r, null, panelColor, true);
        var o = r.gameObject.AddComponent<Outline>();
        o.effectColor = borderColor; o.effectDistance = new Vector2(3, -3);
        var btn = r.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        var text = Text(r, label, scale, Vector2.zero, 0, PixelText.Align.Center, textColor);
        var fx = r.gameObject.AddComponent<MenuButtonFx>();
        fx.Setup(null, text, null, textColor, highlight);
        return btn;
    }

    private PixelText Text(Transform parent, string text, int scale, Vector2 pos, float maxWidth, PixelText.Align align, Color c)
    {
        var r = Rect("Texto", parent, new Vector2(0.5f, 0.5f), pos, Vector2.zero);
        var t = r.gameObject.AddComponent<PixelText>();
        t.Atlas = fontAtlas; t.Scale = scale; t.MaxWidth = maxWidth; t.Alignment = align; t.Color = c; t.Text = text;
        t.Rebuild();
        return t;
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var r = (RectTransform)go.transform;
        r.SetParent(parent, false);
        r.anchorMin = r.anchorMax = anchor;
        r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = pos;
        r.sizeDelta = size;
        return r;
    }

    private static Image Img(RectTransform r, Sprite sprite, Color c, bool raycast)
    {
        var img = r.gameObject.AddComponent<Image>();
        img.sprite = sprite; img.color = c; img.raycastTarget = raycast;
        return img;
    }
}
