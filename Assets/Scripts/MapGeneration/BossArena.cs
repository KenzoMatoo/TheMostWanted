using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

// Arena del jefe: va en la raiz del prefab del cuarto final del BossLevel (junto a Door y HandmadeRoom).
// - Cuando el jugador entra bien adentro del cuarto y el jefe sigue vivo: las puertas se cierran detras,
//   la camara se aleja para ver toda la arena, aparece el nombre del jefe y su barra de vida.
// - Al morir el jefe: explota en gotas, las puertas se abren y la camara vuelve a la normalidad.
// - Si el jugador sale de la arena durante la pelea (murio y reaparecio afuera), las puertas se abren otra vez.
public class BossArena : MonoBehaviour
{
    [Header("Jefe")]
    [SerializeField] private string bossName = "EL TORO";
    [SerializeField] private string bossTitle = "Jefe";
    [Tooltip("Fuente pixel del juego (Sprites/UI/FuentePixel.png)")]
    [SerializeField] private Texture2D fontAtlas;

    [Header("Entrada")]
    [Tooltip("Tiles que el jugador debe estar dentro del cuarto (lejos de las paredes) para empezar la pelea")]
    [SerializeField] private float insideMargin = 4f;
    [Tooltip("Tamano de la camara durante la pelea. 0 = automatico: justo el ancho del cuarto del jefe")]
    [SerializeField] private float fightZoom = 0f;
    [SerializeField] private float zoomTime = 0.8f;

    [Header("Barra de vida")]
    [SerializeField] private Color barColor = new Color(0.89f, 0.29f, 0.29f, 1f);
    [SerializeField] private Color barBackColor = new Color(0.17f, 0.17f, 0.16f, 0.9f);
    [SerializeField] private Color barTrailColor = new Color(1f, 0.85f, 0.6f, 1f);
    [Tooltip("Color de la barra en cada fase del jefe (fase 1, 2, 3...)")]
    [SerializeField] private Color[] phaseBarColors = { new Color(0.89f, 0.29f, 0.29f, 1f), new Color(1f, 0.55f, 0.2f, 1f), new Color(1f, 0.85f, 0.3f, 1f) };

    private enum State { Waiting, Fighting, Done }
    private State state = State.Waiting;

    private Door door;
    private EnemyBase boss;
    private Vector3 lastBossPosition;
    private Vector3 bossStartPosition;
    private Bounds area;
    private readonly List<GameObject> sealedDoors = new List<GameObject>();

    private CinemachineCamera vcam;
    private CinemachineConfiner2D confiner;
    private float normalZoom;
    private Collider2D normalBounds;
    private BoxCollider2D arenaBounds;
    private Coroutine zoomRoutine;

    private GameObject ui;
    private RectTransform barFill, barTrail;
    private CanvasGroup barGroup, titleGroup;
    private PixelText titleText, subtitleText;
    private UnityEngine.UI.Image barFillImage;
    private float trail = 1f;

    private void Start()
    {
        door = GetComponent<Door>();
        Tilemap walls = null;
        foreach (Transform c in transform)
            if (c.name == HandmadeRoom.WallsName) walls = c.GetComponent<Tilemap>();
        if (walls != null)
        {
            walls.CompressBounds();
            Bounds lb = walls.localBounds;
            area = new Bounds(walls.transform.TransformPoint(lb.center), lb.size);
        }
        else area = new Bounds(transform.position, new Vector3(33, 33, 1));
        CreateArenaBounds();
    }

    // Box covering the boss room: camera limit during the fight, and the only place the boss can teleport to.
    // Must stay enabled (a disabled collider has empty bounds). Trigger on Ignore Raycast so it never blocks anything.
    private void CreateArenaBounds()
    {
        if (arenaBounds != null) return;
        var go = new GameObject("Limites arena");
        go.transform.SetParent(transform, false);
        go.layer = 2;
        go.transform.position = new Vector3(area.center.x, area.center.y, 0f);
        arenaBounds = go.AddComponent<BoxCollider2D>();
        arenaBounds.isTrigger = true;
        arenaBounds.size = new Vector2(area.size.x, area.size.y);
    }

    private void Update()
    {
        if (state == State.Done) return;

        if (boss == null)
        {
            boss = FindBoss();
            if (boss != null)
            {
                bossStartPosition = boss.transform.position;
                if (boss is BossEnemy be)
                {
                    be.onPhaseChanged.AddListener(OnBossPhase);
                    be.SetArenaBounds(arenaBounds);   // it can't teleport out of the room
                }
            }
        }
        if (boss != null && !boss.IsDead()) lastBossPosition = boss.transform.position;

        var player = FindAnyObjectByType<PlayerController>();
        if (player == null) return;

        switch (state)
        {
            case State.Waiting:
                if (boss != null && !boss.IsDead() && IsInside(player.transform.position, insideMargin)) StartFight();
                break;

            case State.Fighting:
                if (boss == null || boss.IsDead()) { EndFight(true); break; }
                if (!IsInside(player.transform.position, 0.5f)) { EndFight(false); break; }
                UpdateBar();
                break;
        }
    }

    private EnemyBase FindBoss()
    {
        foreach (LevelEnemy e in GetComponentsInChildren<LevelEnemy>())
            if (e.IsBoss) return e.GetComponent<EnemyBase>();
        return null;
    }

    private bool IsInside(Vector3 p, float margin)
    {
        return p.x > area.min.x + margin && p.x < area.max.x - margin && p.y > area.min.y + margin && p.y < area.max.y - margin;
    }

    // ---------------- Fight ----------------

    private void StartFight()
    {
        state = State.Fighting;

        // Close the doors the player came through (the door walls are still there, just turned off)
        sealedDoors.Clear();
        if (door != null)
            foreach (GameObject d in door.OpenedDoors)
                if (d != null && !d.activeSelf) { d.SetActive(true); sealedDoors.Add(d); }

        // The boss starts the fight right away (its own detection range can be small)
        if (boss is BossEnemy bossEnemy) bossEnemy.ForceEngage();

        Zoom(true);
        BuildUI();
        trail = 1f;
        UpdateBar();
        StartCoroutine(ShowTitle(bossName, bossTitle, 2.2f));
        StartCoroutine(FadeGroup(barGroup, 1f, 0.4f));
    }

    private void EndFight(bool victory)
    {
        foreach (GameObject d in sealedDoors) if (d != null) d.SetActive(false);
        sealedDoors.Clear();
        Zoom(false);

        if (victory)
        {
            state = State.Done;
            var effects = FindAnyObjectByType<PlayerEffects>();
            if (effects != null) { effects.PlayAbsorbBurst(lastBossPosition); effects.PlayAbsorbBurst(lastBossPosition + Vector3.up); }
            StartCoroutine(Victory());
        }
        else
        {
            // The player died and came back outside: the boss goes back to its place, full health, phase 1
            state = State.Waiting;
            if (ui != null) Destroy(ui);
            if (boss is BossEnemy be && !be.IsDead()) be.ResetBoss(bossStartPosition);
        }
    }

    private IEnumerator Victory()
    {
        if (barFill != null) SetWidth(barFill, 0f);
        yield return FadeGroup(barGroup, 0f, 0.6f);
        yield return ShowTitle("¡DERROTADO!", "La salida está abierta", 2.5f);
        if (ui != null) Destroy(ui);
    }

    // ---------------- Camera ----------------

    private void Zoom(bool fight)
    {
        if (vcam == null)
        {
            vcam = FindAnyObjectByType<CinemachineCamera>();
            if (vcam == null) return;
            confiner = vcam.GetComponent<CinemachineConfiner2D>();
            normalZoom = vcam.Lens.OrthographicSize;
        }
        // During the fight the camera only shows the boss room
        if (confiner != null)
        {
            if (fight)
            {
                CreateArenaBounds();
                if (confiner.BoundingShape2D != arenaBounds) normalBounds = confiner.BoundingShape2D;
                confiner.BoundingShape2D = arenaBounds;
            }
            else if (normalBounds != null) confiner.BoundingShape2D = normalBounds;
            confiner.InvalidateBoundingShapeCache();
        }

        float target = normalZoom;
        if (fight)
        {
            float aspect = Camera.main != null ? Camera.main.aspect : 16f / 9f;
            // A bit less than the room: if the view is exactly as wide as the bounds, Cinemachine's confiner
            // collapses to a single point and the camera stops following the player (also up/down)
            float width = area.size.x - 1f, height = area.size.y - 1f;
            target = fightZoom > 0f ? fightZoom : Mathf.Min(width / (2f * aspect), height / 2f);
        }
        if (zoomRoutine != null) StopCoroutine(zoomRoutine);
        zoomRoutine = StartCoroutine(ZoomTo(target));
    }

    private IEnumerator ZoomTo(float target)
    {
        float start = vcam.Lens.OrthographicSize;
        for (float t = 0f; t < zoomTime; t += Time.deltaTime)
        {
            vcam.Lens.OrthographicSize = Mathf.Lerp(start, target, Mathf.SmoothStep(0f, 1f, t / zoomTime));
            if (confiner != null) confiner.InvalidateLensCache();
            yield return null;
        }
        vcam.Lens.OrthographicSize = target;
        if (confiner != null) confiner.InvalidateLensCache();
    }

    // ---------------- UI ----------------

    private void UpdateBar()
    {
        if (boss == null || barFill == null) return;
        float pct = boss.GetMaxHealth() > 0 ? Mathf.Clamp01(boss.GetHealthPercentage()) : 0f;
        SetWidth(barFill, pct);
        trail = pct > trail ? pct : Mathf.MoveTowards(trail, pct, Time.deltaTime * 0.5f);
        SetWidth(barTrail, trail);
    }

    private static void SetWidth(RectTransform r, float pct)
    {
        if (r == null) return;
        r.anchorMax = new Vector2(pct, 1f);
    }

    private void BuildUI()
    {
        if (ui != null) Destroy(ui);
        ui = new GameObject("UI Jefe");
        var canvas = ui.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = ui.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // Health bar at the top center (between the HUD cubes and the minimap)
        var bar = NewRect("Barra", ui.transform, new Vector2(0.5f, 1f), new Vector2(0f, -95f), new Vector2(800f, 28f));
        barGroup = bar.gameObject.AddComponent<CanvasGroup>();
        barGroup.alpha = 0f;
        AddImage(NewRect("Borde", bar, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(808f, 36f)), new Color(0.53f, 0.53f, 0.5f, 1f)).transform.SetAsFirstSibling();
        AddImage(NewRect("Fondo", bar, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(800f, 28f)), barBackColor);
        barTrail = Stretch(NewRect("Rastro", bar, Vector2.zero, Vector2.zero, Vector2.zero));
        AddImage(barTrail, barTrailColor);
        barFill = Stretch(NewRect("Vida", bar, Vector2.zero, Vector2.zero, Vector2.zero));
        barFillImage = AddImage(barFill, PhaseColor());
        var nameLabel = NewText("Nombre", bar, bossName, 4, new Vector2(0f, 42f), PixelText.Align.Left);
        // left-aligned above the bar
        var nr = (RectTransform)nameLabel.transform;
        nr.anchorMin = nr.anchorMax = new Vector2(0f, 0.5f); nr.pivot = new Vector2(0f, 0.5f); nr.anchoredPosition = new Vector2(0f, 42f);

        // Big title in the middle of the screen
        var title = NewRect("Titulo", ui.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, 200f), new Vector2(1400f, 220f));
        titleGroup = title.gameObject.AddComponent<CanvasGroup>();
        titleGroup.alpha = 0f;
        titleText = NewText("Nombre", title, bossName, 12, new Vector2(0f, 40f), PixelText.Align.Center);
        subtitleText = NewText("Subtitulo", title, bossTitle, 4, new Vector2(0f, -60f), PixelText.Align.Center);
        subtitleText.Color = new Color(0.85f, 0.83f, 0.78f, 1f);
    }

    private IEnumerator ShowTitle(string title, string subtitle, float hold)
    {
        if (titleGroup == null) yield break;
        titleText.Text = title;
        subtitleText.Text = subtitle;
        titleText.Rebuild(); subtitleText.Rebuild();
        yield return FadeGroup(titleGroup, 1f, 0.35f);
        yield return new WaitForSeconds(hold);
        yield return FadeGroup(titleGroup, 0f, 0.6f);
    }

    private static IEnumerator FadeGroup(CanvasGroup g, float target, float time)
    {
        if (g == null) yield break;
        float start = g.alpha;
        for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
        {
            if (g == null) yield break;
            g.alpha = Mathf.Lerp(start, target, t / time);
            yield return null;
        }
        if (g != null) g.alpha = target;
    }

    private static RectTransform NewRect(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
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

    private static RectTransform Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = r.offsetMax = Vector2.zero;
        r.pivot = new Vector2(0f, 0.5f);
        return r;
    }

    private static Image AddImage(RectTransform r, Color c)
    {
        var img = r.gameObject.AddComponent<Image>();
        img.color = c;
        img.raycastTarget = false;
        return img;
    }

    private PixelText NewText(string name, Transform parent, string text, int scale, Vector2 pos, PixelText.Align align)
    {
        var r = NewRect(name, parent, new Vector2(0.5f, 0.5f), pos, Vector2.zero);
        var t = r.gameObject.AddComponent<PixelText>();
        t.Atlas = fontAtlas; t.Scale = scale; t.Alignment = align; t.Color = Color.white; t.Text = text;
        t.Rebuild();
        return t;
    }

    // ---------------- Phases ----------------

    private Color PhaseColor()
    {
        int i = boss is BossEnemy be ? be.PhaseNumber - 1 : 0;
        if (phaseBarColors == null || phaseBarColors.Length == 0) return barColor;
        return phaseBarColors[Mathf.Clamp(i, 0, phaseBarColors.Length - 1)];
    }

    private void OnBossPhase(int phase)
    {
        if (state != State.Fighting) return;
        if (barFillImage != null) barFillImage.color = PhaseColor();
        StartCoroutine(ShowTitle($"FASE {phase}", bossName, 1.2f));
    }
}
