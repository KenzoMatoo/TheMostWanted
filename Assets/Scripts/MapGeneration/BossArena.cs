using System.Collections;
using System.Collections.Generic;
using TMPro;
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
    [SerializeField] private string bossName = "EL DASHER";
    [SerializeField] private string bossTitle = "Jefe";

    [Header("Entrada")]
    [Tooltip("Tiles que el jugador debe estar dentro del cuarto (lejos de las paredes) para empezar la pelea")]
    [SerializeField] private float insideMargin = 4f;
    [Tooltip("Tamano de la camara durante la pelea (mas grande = se ve mas arena)")]
    [SerializeField] private float fightZoom = 12f;
    [SerializeField] private float zoomTime = 0.8f;

    [Header("Barra de vida")]
    [SerializeField] private Color barColor = new Color(0.89f, 0.29f, 0.29f, 1f);
    [SerializeField] private Color barBackColor = new Color(0.17f, 0.17f, 0.16f, 0.9f);
    [SerializeField] private Color barTrailColor = new Color(1f, 0.85f, 0.6f, 1f);

    private enum State { Waiting, Fighting, Done }
    private State state = State.Waiting;

    private Door door;
    private EnemyBase boss;
    private Vector3 lastBossPosition;
    private Bounds area;
    private readonly List<GameObject> sealedDoors = new List<GameObject>();

    private CinemachineCamera vcam;
    private CinemachineConfiner2D confiner;
    private float normalZoom;
    private Coroutine zoomRoutine;

    private GameObject ui;
    private RectTransform barFill, barTrail;
    private CanvasGroup barGroup, titleGroup;
    private TMP_Text titleText, subtitleText;
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
    }

    private void Update()
    {
        if (state == State.Done) return;

        if (boss == null) boss = FindBoss();
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
            state = State.Waiting;
            if (ui != null) Destroy(ui);
        }
    }

    private IEnumerator Victory()
    {
        if (barFill != null) SetWidth(barFill, 0f);
        yield return FadeGroup(barGroup, 0f, 0.6f);
        yield return ShowTitle("DERROTADO", "La salida esta abierta", 2.5f);
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
        if (zoomRoutine != null) StopCoroutine(zoomRoutine);
        zoomRoutine = StartCoroutine(ZoomTo(fight ? fightZoom : normalZoom));
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

        // Health bar at the bottom of the screen
        var bar = NewRect("Barra", ui.transform, new Vector2(0.5f, 0f), new Vector2(0f, 70f), new Vector2(900f, 30f));
        barGroup = bar.gameObject.AddComponent<CanvasGroup>();
        barGroup.alpha = 0f;
        AddImage(NewRect("Borde", bar, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(908f, 38f)), new Color(0.53f, 0.53f, 0.5f, 1f)).transform.SetAsFirstSibling();
        AddImage(NewRect("Fondo", bar, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 30f)), barBackColor);
        barTrail = Stretch(NewRect("Rastro", bar, Vector2.zero, Vector2.zero, Vector2.zero));
        AddImage(barTrail, barTrailColor);
        barFill = Stretch(NewRect("Vida", bar, Vector2.zero, Vector2.zero, Vector2.zero));
        AddImage(barFill, barColor);
        var nameLabel = NewText("Nombre", bar, bossName, 30, new Vector2(0f, 38f), new Vector2(900f, 40f));
        nameLabel.alignment = TextAlignmentOptions.Left;

        // Big title in the middle of the screen
        var title = NewRect("Titulo", ui.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, 200f), new Vector2(1400f, 220f));
        titleGroup = title.gameObject.AddComponent<CanvasGroup>();
        titleGroup.alpha = 0f;
        titleText = NewText("Nombre", title, bossName, 96, new Vector2(0f, 30f), new Vector2(1400f, 120f));
        subtitleText = NewText("Subtitulo", title, bossTitle, 40, new Vector2(0f, -55f), new Vector2(1400f, 60f));
        subtitleText.color = new Color(0.85f, 0.83f, 0.78f, 1f);
    }

    private IEnumerator ShowTitle(string title, string subtitle, float hold)
    {
        if (titleGroup == null) yield break;
        titleText.text = title;
        subtitleText.text = subtitle;
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

    private static TMP_Text NewText(string name, Transform parent, string text, float size, Vector2 pos, Vector2 box)
    {
        var r = NewRect(name, parent, new Vector2(0.5f, 0.5f), pos, box);
        var t = r.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.alignment = TextAlignmentOptions.Center;
        t.color = Color.white;
        t.raycastTarget = false;
        t.outlineWidth = 0.2f;
        t.outlineColor = new Color32(20, 20, 24, 255);
        return t;
    }
}
