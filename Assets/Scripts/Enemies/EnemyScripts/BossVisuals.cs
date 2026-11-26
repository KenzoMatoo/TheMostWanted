using System.Collections.Generic;
using UnityEngine;

// Efectos visuales del jefe (va junto a BossEnemy). No cambia nada del comportamiento.
// - Mueve el sprite a un objeto hijo para poder animarlo sin tocar la fisica:
//   respira quieto, tiembla al avisar un ataque, se inclina y deja estela al embestir, se aplasta al aterrizar.
// - Destello blanco al recibir dano.
// - Burst: un anillo que crece hasta el radio del golpe mientras carga (asi se ve hasta donde llega), y una onda al explotar.
// - Teletransporte: nube de humo donde desaparece y donde aparece.
// - Particulas: polvo al pisar y al aterrizar; desde la fase 2 brasas que suben de las grietas de lava,
//   estela de llamas al embestir, chispas al pisar y una llamarada al hacer el burst. Al morir estalla en brasas.
[DefaultExecutionOrder(100)]   // after BossEnemy sets this frame's sprite
[RequireComponent(typeof(BossEnemy))]
public class BossVisuals : MonoBehaviour
{
    [Header("Animacion")]
    [SerializeField] private float breatheAmount = 0.03f;
    [SerializeField] private float breatheSpeed = 2.5f;
    [SerializeField] private float telegraphShake = 0.03f;
    [SerializeField] private float dashLean = 6f;
    [SerializeField] private float landSquash = 0.18f;

    [Header("Estela al embestir")]
    [SerializeField] private float afterimageInterval = 0.04f;
    [SerializeField] private float afterimageLife = 0.25f;
    [SerializeField] private Color afterimageColor = new Color(1f, 0.45f, 0.3f, 0.55f);

    [Header("Destello al recibir dano")]
    [SerializeField] private float flashTime = 0.08f;

    [Header("Particulas")]
    [Tooltip("Brasas por segundo segun la fase (1, 2, 3...). La fase 1 es de hueso: sin brasas.")]
    [SerializeField] private float[] embersPerSecond = { 0f, 10f, 26f };
    [SerializeField] private float flameTrailPerSecond = 90f;
    [SerializeField] private float stepInterval = 0.28f;
    [SerializeField] private Color[] fireColors =
    {
        new Color(1f, 0.93f, 0.55f), new Color(1f, 0.7f, 0.25f), new Color(1f, 0.45f, 0.15f), new Color(0.85f, 0.2f, 0.12f)
    };
    [SerializeField] private Color[] dustColors =
    {
        new Color(0.78f, 0.7f, 0.6f), new Color(0.62f, 0.54f, 0.46f), new Color(0.9f, 0.84f, 0.74f)
    };

    [Header("Temblores de pantalla")]
    [SerializeField] private float landShake = 0.12f;
    [SerializeField] private float burstShake = 0.4f;
    [SerializeField] private float phaseShake = 0.5f;

    private BossEnemy boss;
    private SpriteRenderer rootRenderer, visual, flash;
    private Transform visualT;
    private float afterimageTimer, flashTimer, squash;
    private bool wasAirborne, wasChargingBurst;
    private Transform burstRing;
    private SpriteRenderer burstRingRenderer;
    private float burstChargeTime;

    private ParticleSystem fire, dust;
    private Rigidbody2D body;
    private float emberAccum, trailAccum, stepTimer;
    private bool wasDead;

    private static Sprite ringSprite, discSprite, squareSprite;
    private static Material silhouette;

    private void Start()
    {
        boss = GetComponent<BossEnemy>();
        rootRenderer = GetComponent<SpriteRenderer>();
        if (rootRenderer == null) { enabled = false; return; }

        // The art moves to a child; the root keeps the collider and the physics
        var go = new GameObject("Visual");
        visualT = go.transform;
        visualT.SetParent(transform, false);
        visual = go.AddComponent<SpriteRenderer>();
        CopyRenderer(rootRenderer, visual);
        rootRenderer.enabled = false;
        boss.SetVisualRenderer(visual);

        // White silhouette on top for the hit flash
        var f = new GameObject("Destello");
        f.transform.SetParent(visualT, false);
        flash = f.AddComponent<SpriteRenderer>();
        flash.sharedMaterial = Silhouette();
        flash.sortingLayerID = visual.sortingLayerID;
        flash.sortingOrder = visual.sortingOrder + 1;
        flash.color = new Color(1f, 1f, 1f, 0f);

        boss.Hurt += () => flashTimer = flashTime;
        boss.TeleportedFrom += Smoke;
        boss.TeleportedTo += Smoke;
        boss.onEnergyBurst.AddListener(OnBurst);
        boss.onPhaseChanged.AddListener(_ => { ScreenShake.Shake(phaseShake, 0.6f); flashTimer = flashTime * 3f; FireBurst(transform.position, 70, 2f, 8f); });

        body = GetComponent<Rigidbody2D>();
        fire = CreateParticles("Llamas", -0.35f, false, UnlitMaterial(), visual.sortingOrder + 1);
        dust = CreateParticles("Polvo", 1.2f, true, visual.sharedMaterial, visual.sortingOrder + 1);
    }

    private static void CopyRenderer(SpriteRenderer from, SpriteRenderer to)
    {
        to.sprite = from.sprite;
        to.color = from.color;
        to.flipX = from.flipX;
        to.flipY = from.flipY;
        to.sharedMaterial = from.sharedMaterial;
        to.sortingLayerID = from.sortingLayerID;
        to.sortingOrder = from.sortingOrder;
        to.maskInteraction = from.maskInteraction;
    }

    private void LateUpdate()
    {
        if (visual == null) return;
        float t = Time.time;

        // ---- body animation ----
        Vector3 scale = Vector3.one;
        Vector3 offset = Vector3.zero;
        float lean = 0f;

        bool airborne = boss.IsAirborneNow;
        if (wasAirborne && !airborne && !boss.IsDead())
        {
            squash = 1f;
            ScreenShake.Shake(landShake, 0.15f);
            Dust(transform.position);
            DustKick(0f, 22, 2f, 6f);
            if (Fiery) Sparks(14);
        }
        wasAirborne = airborne;

        if (boss.IsDashing)
        {
            lean = -dashLean;   // head down, forward (the root flip mirrors it)
            afterimageTimer -= Time.deltaTime;
            if (afterimageTimer <= 0f) { afterimageTimer = afterimageInterval; Afterimage(); }
        }
        else if (boss.IsTelegraphing || boss.IsChargingBurst || boss.IsInPhaseTransition)
        {
            offset = (Vector3)(Random.insideUnitCircle * telegraphShake);
        }
        else if (!airborne && !boss.IsDead())
        {
            float b = Mathf.Sin(t * breatheSpeed) * breatheAmount;
            scale = new Vector3(1f - b * 0.5f, 1f + b, 1f);
        }

        if (squash > 0f)
        {
            squash = Mathf.MoveTowards(squash, 0f, Time.deltaTime * 6f);
            scale = Vector3.Scale(scale, new Vector3(1f + landSquash * squash, 1f - landSquash * squash, 1f));
        }

        // Keep the feet on the ground when the height changes (sprite pivot is the center)
        float h = visual.sprite != null ? visual.sprite.bounds.extents.y : 0.5f;
        offset.y -= (1f - scale.y) * h;
        visualT.localScale = scale;
        visualT.localPosition = offset;
        visualT.localRotation = Quaternion.Euler(0f, 0f, lean);

        // ---- hit flash ----
        flash.sprite = visual.sprite;
        flash.flipX = visual.flipX;
        flash.enabled = visual.enabled;
        if (flashTimer > 0f) flashTimer -= Time.deltaTime;
        flash.color = new Color(1f, 1f, 1f, flashTimer > 0f ? 0.85f : 0f);

        // ---- burst warning: a ring grows to the real radius while it charges ----
        bool charging = boss.IsChargingBurst;
        if (charging && !wasChargingBurst) burstChargeTime = 0f;
        if (charging)
        {
            burstChargeTime += Time.deltaTime;
            EnsureBurstRing();
            float k = Mathf.Clamp01(burstChargeTime / Mathf.Max(0.05f, boss.BurstWindup));
            float r = boss.BurstRadius * Mathf.Lerp(0.3f, 1f, k);
            burstRing.position = transform.position;
            burstRing.localScale = Vector3.one * r * 2f;
            Color c = boss.BurstColor;
            c.a = 0.35f + 0.45f * (0.5f + 0.5f * Mathf.Sin(t * 30f));
            burstRingRenderer.color = c;
            burstRingRenderer.enabled = true;
        }
        else if (burstRingRenderer != null) burstRingRenderer.enabled = false;
        wasChargingBurst = charging;

        UpdateParticles();
        UpdateEffects();
    }

    // ---------------- Particles ----------------

    private bool Fiery => boss.PhaseNumber >= 2;

    private void UpdateParticles()
    {
        if (fire == null) return;
        float dt = Time.deltaTime;

        // Death: one last explosion of embers and ash
        bool dead = boss.IsDead();
        if (dead && !wasDead)
        {
            FireBurst(visual.bounds.center, 160, 2f, 11f);
            DustKick(0f, 30, 1f, 5f);
            // Detach them so the explosion survives the boss object being destroyed
            foreach (var ps in new[] { fire, dust })
            {
                ps.transform.SetParent(null, true);
                Destroy(ps.gameObject, 2.5f);
            }
            fire = dust = null;
            wasDead = true;
            return;
        }
        wasDead = dead;
        if (dead || !visual.enabled) return;

        Bounds b = visual.bounds;

        // Embers rising from the lava cracks, more in each phase
        int phase = Mathf.Clamp(boss.PhaseNumber - 1, 0, embersPerSecond.Length - 1);
        emberAccum += embersPerSecond[phase] * dt;
        while (emberAccum >= 1f)
        {
            emberAccum -= 1f;
            Vector2 pos = new Vector2(Random.Range(b.min.x + b.size.x * 0.15f, b.max.x - b.size.x * 0.15f),
                                      Random.Range(b.min.y + b.size.y * 0.25f, b.max.y - b.size.y * 0.15f));
            EmitFire(pos, new Vector2(Random.Range(-0.4f, 0.4f), Random.Range(0.6f, 1.8f)), Random.Range(0.6f, 1.3f), Random.Range(0.07f, 0.14f));
        }

        float vx = body != null ? body.linearVelocity.x : 0f;
        float back = -Mathf.Sign(Mathf.Abs(vx) > 0.01f ? vx : transform.localScale.x);

        // Charge: trail of flames behind (phase 1: a cloud of dust instead)
        if (boss.IsDashing)
        {
            trailAccum += flameTrailPerSecond * dt;
            while (trailAccum >= 1f)
            {
                trailAccum -= 1f;
                if (Fiery)
                {
                    Vector2 pos = new Vector2(b.center.x + back * b.extents.x * Random.Range(0.2f, 0.9f),
                                              Random.Range(b.min.y + b.size.y * 0.15f, b.max.y - b.size.y * 0.2f));
                    EmitFire(pos, new Vector2(back * Random.Range(1.5f, 4f), Random.Range(0.3f, 1.5f)), Random.Range(0.25f, 0.55f), Random.Range(0.1f, 0.22f));
                }
                else if (Random.value < 0.35f) DustKick(back, 1, 1.5f, 4f);
            }
        }
        else trailAccum = 0f;

        // Footsteps: a puff of dust (and sparks once it burns) while it runs on the ground
        bool running = !boss.IsAirborneNow && Mathf.Abs(vx) > 1f;
        if (running)
        {
            stepTimer -= dt * Mathf.Clamp(Mathf.Abs(vx) / 5f, 0.6f, 2.5f);
            if (stepTimer <= 0f)
            {
                stepTimer = stepInterval;
                DustKick(back, Random.Range(4, 7), 1f, 3f);
                if (Fiery) Sparks(Random.Range(2, 5));
            }
        }
        else stepTimer = 0f;
    }

    // Dust from the feet. dir: -1/1 = mostly to that side, 0 = both sides
    private void DustKick(float dir, int count, float minSpeed, float maxSpeed)
    {
        if (dust == null) return;
        Bounds b = visual.bounds;
        for (int i = 0; i < count; i++)
        {
            float side = dir != 0f ? (Random.value < 0.8f ? dir : -dir) : (Random.value < 0.5f ? -1f : 1f);
            var e = new ParticleSystem.EmitParams
            {
                position = new Vector3(b.center.x + Random.Range(-b.extents.x, b.extents.x) * 0.5f, b.min.y + 0.12f, 0f),
                velocity = new Vector2(side * Random.Range(minSpeed, maxSpeed), Random.Range(0.6f, 2.5f)),
                startSize = Random.Range(0.1f, 0.24f),
                startLifetime = Random.Range(0.35f, 0.7f),
                startColor = dustColors[Random.Range(0, dustColors.Length)],
            };
            dust.Emit(e, 1);
        }
    }

    // Hot sparks jumping from the hooves
    private void Sparks(int count)
    {
        Bounds b = visual.bounds;
        for (int i = 0; i < count; i++)
        {
            Vector2 pos = new Vector2(b.center.x + Random.Range(-b.extents.x, b.extents.x) * 0.6f, b.min.y + 0.15f);
            EmitFire(pos, new Vector2(Random.Range(-3f, 3f), Random.Range(2.5f, 6f)), Random.Range(0.3f, 0.6f), Random.Range(0.06f, 0.11f));
        }
    }

    private void FireBurst(Vector2 center, int count, float minSpeed, float maxSpeed)
    {
        for (int i = 0; i < count; i++)
        {
            Vector2 dir = Random.insideUnitCircle.normalized;
            if (dir == Vector2.zero) dir = Vector2.up;
            EmitFire(center + Random.insideUnitCircle * 0.5f, dir * Random.Range(minSpeed, maxSpeed) + Vector2.up, Random.Range(0.4f, 0.9f), Random.Range(0.1f, 0.24f));
        }
    }

    private void EmitFire(Vector2 pos, Vector2 vel, float life, float size)
    {
        if (fire == null) return;
        var e = new ParticleSystem.EmitParams
        {
            position = pos,
            velocity = vel,
            startSize = size,
            startLifetime = life,
            startColor = fireColors[Random.Range(0, fireColors.Length)],
        };
        fire.Emit(e, 1);
    }

    private ParticleSystem CreateParticles(string name, float gravity, bool collide, Material mat, int order)
    {
        var go = new GameObject(name);
        go.SetActive(false);   // configure before it starts
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;   // don't follow the boss
        main.scalingMode = ParticleSystemScalingMode.Local;           // ignore the boss scale/flip
        main.gravityModifier = gravity;
        main.maxParticles = 600;
        main.startSpeed = 0f;

        var emission = ps.emission; emission.enabled = false;   // only Emit() by hand
        var shape = ps.shape; shape.enabled = false;

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.5f, 0.85f), new Keyframe(1f, 0.2f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        if (!collide)
        {
            // Flames flicker sideways as they rise
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.6f;
            noise.frequency = 1.5f;
            noise.scrollSpeed = 1f;
        }
        else
        {
            var c = ps.collision;
            c.enabled = true;
            c.type = ParticleSystemCollisionType.World;
            c.mode = ParticleSystemCollisionMode.Collision2D;
            c.collidesWith = LayerMask.GetMask("Ground");
            c.bounce = 0.15f;
            c.dampen = 0.7f;
            c.radiusScale = 0.5f;
        }

        var sheet = ps.textureSheetAnimation;
        sheet.enabled = true;
        sheet.mode = ParticleSystemAnimationMode.Sprites;
        sheet.AddSprite(SquareSprite());

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.sharedMaterial = mat;
        r.sortingLayerID = visual.sortingLayerID;
        r.sortingOrder = order;

        go.SetActive(true);
        return ps;
    }

    // Fire glows: unlit, so the dark arena lights don't dim it
    private Material UnlitMaterial()
    {
        Shader sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        return sh != null ? new Material(sh) { name = "BossFireUnlit" } : visual.sharedMaterial;
    }

    // ---------------- One-shot effects ----------------

    private void OnBurst()
    {
        ScreenShake.Shake(burstShake, 0.35f);
        FireBurst(transform.position, 110, boss.BurstRadius * 1.5f, boss.BurstRadius * 3.5f);
        // Expanding shock wave, same size as the hit area
        var go = new GameObject("Onda");
        go.transform.position = transform.position;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = RingSprite();
        sr.color = boss.BurstColor;
        sr.sortingLayerID = visual.sortingLayerID; sr.sortingOrder = visual.sortingOrder + 2;
        effects.Add(new Fx { t = go.transform, sr = sr, life = 0.4f, from = boss.BurstRadius * 0.6f, to = boss.BurstRadius * 2.4f, alpha = 1f });
    }

    private void Afterimage()
    {
        var go = new GameObject("Estela");
        go.transform.SetPositionAndRotation(visualT.position, visualT.rotation);
        go.transform.localScale = visualT.lossyScale;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = visual.sprite;
        sr.flipX = visual.flipX;
        sr.sharedMaterial = Silhouette();
        sr.color = afterimageColor;
        sr.sortingLayerID = visual.sortingLayerID; sr.sortingOrder = visual.sortingOrder - 1;
        effects.Add(new Fx { t = go.transform, sr = sr, life = afterimageLife, alpha = afterimageColor.a, keepScale = true });
    }

    private void Smoke(Vector3 at)
    {
        for (int i = 0; i < 16; i++)
        {
            var go = new GameObject("Humo");
            go.transform.position = at + (Vector3)(Random.insideUnitCircle * 1.2f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = DiscSprite();
            float g = Random.Range(0.12f, 0.25f);
            sr.color = new Color(g, g * 0.8f, g, 0.8f);
            sr.sortingLayerID = visual.sortingLayerID; sr.sortingOrder = visual.sortingOrder + 2;
            float size = Random.Range(0.5f, 1.2f);
            effects.Add(new Fx { t = go.transform, sr = sr, life = Random.Range(0.35f, 0.6f), from = size, to = size * 1.8f, alpha = 0.8f,
                                 velocity = Random.insideUnitCircle * 1.5f + Vector2.up * 0.8f });
        }
    }

    private void Dust(Vector3 at)
    {
        float halfWidth = visual.bounds.extents.x * 0.6f;
        float y = visual.bounds.min.y + 0.1f;
        for (int i = 0; i < 12; i++)
        {
            float side = i % 2 == 0 ? 1f : -1f;
            var go = new GameObject("Polvo");
            go.transform.position = new Vector3(at.x + side * Random.Range(0f, halfWidth), y, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SquareSprite();
            sr.color = new Color(0.78f, 0.7f, 0.6f, 0.9f);
            sr.sortingLayerID = visual.sortingLayerID; sr.sortingOrder = visual.sortingOrder + 1;
            float size = Random.Range(0.12f, 0.25f);
            effects.Add(new Fx { t = go.transform, sr = sr, life = Random.Range(0.3f, 0.5f), from = size, to = size * 0.4f, alpha = 0.9f,
                                 velocity = new Vector2(side * Random.Range(2f, 5f), Random.Range(0.5f, 2f)) });
        }
    }

    // ---------------- Tiny effect system (no prefabs needed) ----------------

    private class Fx
    {
        public Transform t; public SpriteRenderer sr;
        public float age, life, from, to, alpha;
        public Vector2 velocity; public bool keepScale;
    }
    private readonly List<Fx> effects = new List<Fx>();

    private void UpdateEffects()
    {
        for (int i = effects.Count - 1; i >= 0; i--)
        {
            var e = effects[i];
            e.age += Time.deltaTime;
            float k = Mathf.Clamp01(e.age / e.life);
            if (e.t == null || k >= 1f)
            {
                if (e.t != null) Destroy(e.t.gameObject);
                effects.RemoveAt(i);
                continue;
            }
            if (!e.keepScale) e.t.localScale = Vector3.one * Mathf.Lerp(e.from, e.to, k);
            e.t.position += (Vector3)(e.velocity * Time.deltaTime);
            var c = e.sr.color; c.a = e.alpha * (1f - k); e.sr.color = c;
        }
    }

    private void OnDestroy()
    {
        foreach (var e in effects) if (e.t != null) Destroy(e.t.gameObject);
        if (burstRing != null) Destroy(burstRing.gameObject);
    }

    private void EnsureBurstRing()
    {
        if (burstRing != null) return;
        var go = new GameObject("Aviso Burst");
        burstRing = go.transform;
        burstRingRenderer = go.AddComponent<SpriteRenderer>();
        burstRingRenderer.sprite = RingSprite();
        burstRingRenderer.sortingLayerID = visual.sortingLayerID;
        burstRingRenderer.sortingOrder = visual.sortingOrder + 2;
    }

    // ---------------- Generated sprites (1 unit wide) ----------------

    private static Sprite RingSprite()
    {
        if (ringSprite != null) return ringSprite;
        const int n = 64;
        var tex = new Texture2D(n, n) { filterMode = FilterMode.Point };
        for (int x = 0; x < n; x++)
            for (int y = 0; y < n; y++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f));
                tex.SetPixel(x, y, d <= n / 2f && d >= n / 2f - 3f ? Color.white : Color.clear);
            }
        tex.Apply();
        return ringSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
    }

    private static Sprite DiscSprite()
    {
        if (discSprite != null) return discSprite;
        const int n = 16;
        var tex = new Texture2D(n, n) { filterMode = FilterMode.Point };
        for (int x = 0; x < n; x++)
            for (int y = 0; y < n; y++)
                tex.SetPixel(x, y, Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) <= n / 2f ? Color.white : Color.clear);
        tex.Apply();
        return discSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
    }

    private static Sprite SquareSprite()
    {
        if (squareSprite != null) return squareSprite;
        var tex = new Texture2D(2, 2) { filterMode = FilterMode.Point };
        tex.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
        tex.Apply();
        return squareSprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 2);
    }

    private static Material Silhouette()
    {
        if (silhouette != null) return silhouette;
        Shader shader = Shader.Find("Custom/SpriteSilhouette");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        return silhouette = new Material(shader) { name = "BossSilhouette" };
    }
}
