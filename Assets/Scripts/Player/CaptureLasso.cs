using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Feedback de captura (lo maneja CharacterCombat):
//  - "Lazo que se cierra": un anillo de tramos de cuerda de slime alrededor del objetivo. Un tramo por
//    clic necesario. Blanco = clics, naranja = ventaja por aturdido, gris = lo que falta. Se encoge
//    al cargar y se abre al decaer. Gira un poco mas rapido mientras mas cargado esta.
//  - Cada clic: pop del lazo, apreton del enemigo (si su sprite es un hijo), gotitas de slime y un
//    sonido que sube de tono.
//  - Marca de "capturable" sobre el enemigo que tienes bajo el mouse.
public class CaptureLasso : MonoBehaviour
{
    private Sprite segmentSprite, dropSprite, markerSprite;
    private AudioClip clickClip;
    private TMP_FontAsset font;

    private readonly List<SpriteRenderer> segments = new List<SpriteRenderer>();
    private Transform ringRoot;
    private TextMeshPro prompt;
    private SpriteRenderer marker;
    private AudioSource audioSource;

    private Transform target;
    private Collider2D targetCollider;
    private Renderer targetRenderer;
    private Transform squashTarget;     // sprite hijo del enemigo para el apreton (null si el sprite esta en la raiz)
    private Vector3 squashBaseScale;
    private Vector3 squashBasePos;

    private int segmentCount = 7;
    private int stunSegments;
    private float progress;             // 0..1
    private float shownRadius;
    private float punch;
    private float spin;
    private int clicks;
    private bool active;

    private static readonly Color Filled = Color.white;
    private static readonly Color Stun = new Color(0.94f, 0.62f, 0.15f);
    private static readonly Color Missing = new Color(0.35f, 0.35f, 0.33f, 0.7f);

    private class Drop { public Transform t; public SpriteRenderer sr; public Vector2 vel; public float life; }
    private readonly List<Drop> drops = new List<Drop>();

    public static CaptureLasso Create(Sprite segment, Sprite drop, Sprite markerSprite, AudioClip click, TMP_FontAsset font)
    {
        var go = new GameObject("Lazo de captura");
        var l = go.AddComponent<CaptureLasso>();
        l.segmentSprite = segment; l.dropSprite = drop; l.markerSprite = markerSprite; l.clickClip = click; l.font = font;
        l.Build();
        return l;
    }

    void Build()
    {
        ringRoot = new GameObject("Anillo").transform;
        ringRoot.SetParent(transform, false);

        var p = new GameObject("Aviso");
        p.transform.SetParent(transform, false);
        prompt = p.AddComponent<TextMeshPro>();
        if (font != null) prompt.font = font;
        prompt.text = "Clic RUEDA rapido";
        prompt.fontSize = 3f;
        prompt.alignment = TextAlignmentOptions.Center;
        prompt.color = new Color(0.95f, 0.94f, 0.91f);
        prompt.rectTransform.sizeDelta = new Vector2(8f, 1.5f);
        prompt.sortingOrder = 60;
        prompt.gameObject.SetActive(false);

        var m = new GameObject("Marca capturable");
        m.transform.SetParent(transform, false);
        marker = m.AddComponent<SpriteRenderer>();
        marker.sprite = markerSprite;
        marker.sortingOrder = 60;
        marker.enabled = false;

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        audioSource.volume = 0.6f;
    }

    // ---------------------------------------------------------------- API para CharacterCombat

    /// <summary>Empieza un intento de captura. stun01 = progreso inicial que da el aturdimiento.</summary>
    public void Begin(Transform newTarget, float stun01, float chargePerClick)
    {
        RestoreSquash();
        target = newTarget;
        targetCollider = target.GetComponent<Collider2D>();
        targetRenderer = target.GetComponentInChildren<SpriteRenderer>();
        squashTarget = targetRenderer != null && targetRenderer.transform != target ? targetRenderer.transform : null;
        if (squashTarget != null) { squashBaseScale = squashTarget.localScale; squashBasePos = squashTarget.localPosition; }

        segmentCount = Mathf.Clamp(Mathf.CeilToInt(1f / Mathf.Max(0.01f, chargePerClick)), 3, 16);
        stunSegments = Mathf.Clamp(Mathf.FloorToInt(stun01 * segmentCount + 0.001f), 0, segmentCount);
        progress = stun01;
        clicks = 0;
        active = true;
        BuildSegments();
        shownRadius = TargetRadius(0f) * 1.15f;
        ringRoot.gameObject.SetActive(true);

        if (targetRenderer is SpriteRenderer enemySr)
            foreach (var s in segments) { s.sortingLayerID = enemySr.sortingLayerID; s.sortingOrder = enemySr.sortingOrder + 6; }
    }

    public void SetProgress(float p01) => progress = Mathf.Clamp01(p01);

    /// <summary>Cada clic que suma carga.</summary>
    public void OnClick()
    {
        if (!active) return;
        clicks++;
        punch = 1f;
        if (squashTarget != null) squashTarget.localScale = new Vector3(squashBaseScale.x * 1.15f, squashBaseScale.y * 0.82f, squashBaseScale.z);
        SpawnDrops(3);
        if (clickClip != null)
        {
            audioSource.pitch = 0.9f + 0.7f * progress;
            audioSource.PlayOneShot(clickClip, 0.7f);
        }
    }

    /// <summary>Termina el intento. captured = se completo (el lazo se cierra del todo y desaparece).</summary>
    public void End(bool captured)
    {
        if (captured) SpawnDrops(6);
        active = false;
        RestoreSquash();
        ringRoot.gameObject.SetActive(false);
        prompt.gameObject.SetActive(false);
        target = null;
    }

    /// <summary>Muestra la marca sobre lo que se puede capturar (null = ocultar).</summary>
    public void SetHover(Transform hovered)
    {
        bool show = hovered != null && !active;
        marker.enabled = show;
        if (!show) return;
        Bounds b = BoundsOf(hovered);
        float bob = Mathf.Sin(Time.time * 5f) * 0.12f;
        marker.transform.position = new Vector3(b.center.x, b.max.y + 0.6f + bob, 0f);
        var sr = hovered.GetComponentInChildren<SpriteRenderer>();
        if (sr != null) { marker.sortingLayerID = sr.sortingLayerID; marker.sortingOrder = sr.sortingOrder + 10; }
    }

    // ---------------------------------------------------------------- dibujo

    void BuildSegments()
    {
        while (segments.Count < segmentCount)
        {
            var go = new GameObject("Tramo");
            go.transform.SetParent(ringRoot, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = segmentSprite;
            segments.Add(sr);
        }
        for (int i = 0; i < segments.Count; i++) segments[i].gameObject.SetActive(i < segmentCount);
    }

    float TargetRadius(float p)
    {
        Bounds b = BoundsOf(target);
        float tight = Mathf.Max(b.extents.x, b.extents.y) * 1.25f + 0.15f;
        float loose = tight + 1.4f;
        return Mathf.Lerp(loose, tight, p);
    }

    Bounds BoundsOf(Transform t)
    {
        if (t == null) return new Bounds(transform.position, Vector3.one);
        var col = t == target ? targetCollider : t.GetComponent<Collider2D>();
        if (col != null) return col.bounds;
        var r = t == target ? targetRenderer : t.GetComponentInChildren<Renderer>();
        return r != null ? r.bounds : new Bounds(t.position, Vector3.one);
    }

    void LateUpdate()
    {
        UpdateDrops();
        if (!active) return;
        if (target == null || !target.gameObject.activeInHierarchy) { End(false); return; }

        float dt = Time.deltaTime;
        Bounds b = BoundsOf(target);
        punch = Mathf.MoveTowards(punch, 0f, dt * 5f);

        // radio: se cierra con la carga, con un "salto" hacia adentro en cada clic
        float goal = TargetRadius(progress) * (1f - 0.12f * punch);
        shownRadius = Mathf.Lerp(shownRadius, goal, dt * 12f);
        spin += dt * (25f + 140f * progress);

        // el lazo forcejea mas mientras mas cerrado esta
        Vector2 shake = Random.insideUnitCircle * (0.02f + 0.06f * progress);
        ringRoot.position = (Vector3)((Vector2)b.center + shake);

        int filled = Mathf.Clamp(Mathf.CeilToInt(progress * segmentCount - 0.001f), 0, segmentCount);
        float arc = 2f * Mathf.PI * shownRadius / segmentCount;          // largo del tramo + hueco
        Vector2 spriteSize = segmentSprite.bounds.size;
        for (int i = 0; i < segmentCount; i++)
        {
            var s = segments[i];
            float angle = (spin + i * 360f / segmentCount) * Mathf.Deg2Rad;
            s.transform.localPosition = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * shownRadius;
            s.transform.localRotation = Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg + 90f);
            float sx = arc * 0.78f / Mathf.Max(0.01f, spriteSize.x);
            float sy = 1.2f + 0.3f * punch;
            s.transform.localScale = new Vector3(sx, sy, 1f);
            s.color = i < Mathf.Min(stunSegments, filled) ? Stun : i < filled ? Filled : Missing;
        }

        // el enemigo regresa de su apreton
        if (squashTarget != null)
        {
            squashTarget.localScale = Vector3.Lerp(squashTarget.localScale, squashBaseScale, dt * 14f);
            squashTarget.localPosition = squashBasePos + (Vector3)(Random.insideUnitCircle * 0.03f * progress);
        }

        // aviso solo al principio del intento
        bool showPrompt = clicks < 2;
        if (prompt.gameObject.activeSelf != showPrompt) prompt.gameObject.SetActive(showPrompt);
        if (showPrompt) prompt.transform.position = new Vector3(b.center.x, b.center.y - shownRadius - 0.7f, 0f);
    }

    void RestoreSquash()
    {
        if (squashTarget != null) { squashTarget.localScale = squashBaseScale; squashTarget.localPosition = squashBasePos; }
        squashTarget = null;
    }

    // ---------------------------------------------------------------- gotitas

    void SpawnDrops(int count)
    {
        if (dropSprite == null || target == null) return;
        Bounds b = BoundsOf(target);
        for (int i = 0; i < count; i++)
        {
            var go = new GameObject("Gota");
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = dropSprite;
            sr.sortingOrder = 59;
            go.transform.position = b.center + (Vector3)(Random.insideUnitCircle * b.extents.x * 0.6f);
            go.transform.localScale = Vector3.one * Random.Range(0.8f, 1.3f);
            drops.Add(new Drop { t = go.transform, sr = sr, vel = new Vector2(Random.Range(-3f, 3f), Random.Range(2f, 5f)), life = 0.6f });
        }
    }

    void UpdateDrops()
    {
        float dt = Time.deltaTime;
        for (int i = drops.Count - 1; i >= 0; i--)
        {
            var d = drops[i];
            d.life -= dt;
            if (d.life <= 0f || d.t == null) { if (d.t != null) Destroy(d.t.gameObject); drops.RemoveAt(i); continue; }
            d.vel += Vector2.down * 18f * dt;
            d.t.position += (Vector3)(d.vel * dt);
            var c = d.sr.color; c.a = Mathf.Clamp01(d.life / 0.3f); d.sr.color = c;
        }
    }

    void OnDestroy() => RestoreSquash();
}
