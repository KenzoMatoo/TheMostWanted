using UnityEngine;

// Al morir, el enemigo estalla en particulas de su color y desaparece al instante (sin la caida ni la espera de antes).
// El objeto se oculta y se apaga en el mismo momento; se borra un poco despues para que la captura/absorcion
// alcancen a soltarlo sin errores. No se usa en el jefe (tiene su propia muerte) ni en los muneco de practica.
// Lo agrega SoundHooks a cada enemigo de la escena.
[RequireComponent(typeof(EnemyBase))]
public class EnemyDeathExplosion : MonoBehaviour
{
    public Color[] palette = { Color.white, new Color(0.85f, 0.85f, 0.85f) };
    public int particleCount = 70;

    private static Sprite pixel;

    private void Start()
    {
        var enemy = GetComponent<EnemyBase>();
        palette = PaletteFor(enemy);
        enemy.OnDeath.AddListener(Explode);
    }

    private static Color[] PaletteFor(EnemyBase e)
    {
        switch (e)
        {
            case EnemyBandido _: return new[] { new Color(0.33f, 0.62f, 0.3f), new Color(0.55f, 0.8f, 0.4f), new Color(0.95f, 0.88f, 0.6f), new Color(0.85f, 0.35f, 0.45f) };
            case EnemyEvilBat _: return new[] { new Color(0.42f, 0.24f, 0.52f), new Color(0.7f, 0.42f, 0.78f), new Color(0.95f, 0.6f, 0.8f) };
            case EnemyDasher _: return new[] { new Color(0.92f, 0.9f, 0.84f), new Color(0.75f, 0.7f, 0.64f), new Color(0.6f, 0.43f, 0.28f) };
            case TankEnemy _:
            case SniperEnemy _: return new[] { new Color(0.9f, 0.95f, 0.9f), new Color(0.7f, 0.85f, 0.75f), new Color(0.45f, 0.45f, 0.5f) };
            default: return new[] { Color.white, new Color(0.85f, 0.85f, 0.85f) };
        }
    }

    private bool exploding;

    // Dies: stops being solid right away, but stays visible while an impact frame shows it in white; then explodes
    private void Explode()
    {
        if (exploding) return;
        exploding = true;
        foreach (var c in GetComponentsInChildren<Collider2D>()) c.enabled = false;
        var body = GetComponent<Rigidbody2D>();
        if (body != null) { body.linearVelocity = Vector2.zero; body.simulated = false; }
        StartCoroutine(ExplodeAfterImpact());
    }

    private System.Collections.IEnumerator ExplodeAfterImpact()
    {
        yield return null;   // the killing hit may start its impact frame right after this
        float limit = Time.realtimeSinceStartup + 1.5f;
        while (ImpactFrameManager.AnyActive && Time.realtimeSinceStartup < limit) yield return null;
        DoExplode();
    }

    private void DoExplode()
    {
        // Center and size of the body
        Bounds b = new Bounds(transform.position, Vector3.one);
        bool has = false;
        foreach (var r in GetComponentsInChildren<SpriteRenderer>())
        {
            if (!r.enabled || r.sprite == null) continue;
            if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
        }
        var sr = GetComponentInChildren<SpriteRenderer>();
        Burst(b, ParticleMaterial(), sr != null ? sr.sortingLayerID : 0, sr != null ? sr.sortingOrder + 2 : 10);
        ScreenShake.Shake(0.12f, 0.12f);

        // Gone right away: hide it, turn off its physics, then delete it a moment later
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
        foreach (var c in GetComponentsInChildren<Collider2D>()) c.enabled = false;
        var rb = GetComponent<Rigidbody2D>();
        if (rb != null) rb.simulated = false;
        foreach (var a in GetComponentsInChildren<Animator>()) a.enabled = false;
        Destroy(gameObject, 0.3f);
    }

    private void Burst(Bounds body, Material mat, int sortingLayer, int order)
    {
        var go = new GameObject("Explosion enemigo");
        go.SetActive(false);
        go.transform.position = body.center;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 2.2f;
        main.maxParticles = particleCount + 20;
        main.startSpeed = 0f;
        var emission = ps.emission; emission.enabled = false;
        var shape = ps.shape; shape.enabled = false;

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.7f, 0.9f), new Keyframe(1f, 0.2f)));
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        var collision = ps.collision;
        collision.enabled = true;
        collision.type = ParticleSystemCollisionType.World;
        collision.mode = ParticleSystemCollisionMode.Collision2D;
        collision.collidesWith = LayerMask.GetMask("Ground");
        collision.bounce = 0.25f;
        collision.dampen = 0.5f;
        collision.radiusScale = 0.5f;

        var sheet = ps.textureSheetAnimation;
        sheet.enabled = true;
        sheet.mode = ParticleSystemAnimationMode.Sprites;
        sheet.AddSprite(Pixel());

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        if (mat != null) r.sharedMaterial = mat;
        r.sortingLayerID = sortingLayer;
        r.sortingOrder = order;
        go.SetActive(true);

        float radius = Mathf.Max(body.extents.x, body.extents.y);
        for (int i = 0; i < particleCount; i++)
        {
            Vector2 dir = Random.insideUnitCircle.normalized;
            if (dir == Vector2.zero) dir = Vector2.up;
            dir = (dir + Vector2.up * 0.4f).normalized;
            bool big = i % 3 == 0;
            var e = new ParticleSystem.EmitParams
            {
                position = (Vector2)body.center + Random.insideUnitCircle * radius * 0.5f,
                velocity = dir * Random.Range(3f, big ? 9f : 13f),
                startSize = big ? Random.Range(0.12f, 0.2f) : Random.Range(0.06f, 0.11f),
                startLifetime = Random.Range(0.5f, 1.1f),
                startColor = palette[Random.Range(0, palette.Length)],
            };
            ps.Emit(e, 1);
        }
        // A few white sparks for the "pop"
        for (int i = 0; i < 12; i++)
        {
            var e = new ParticleSystem.EmitParams
            {
                position = body.center,
                velocity = Random.insideUnitCircle.normalized * Random.Range(8f, 15f),
                startSize = 0.08f,
                startLifetime = 0.25f,
                startColor = Color.white,
            };
            ps.Emit(e, 1);
        }
        Destroy(go, 2f);
    }

    // A sprite material that always works with particles in URP 2D (unlit: the colors show even in dark rooms)
    private static Material particleMaterial;
    private static Material ParticleMaterial()
    {
        if (particleMaterial != null) return particleMaterial;
        Shader s = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        if (s == null) s = Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default");
        if (s == null) s = Shader.Find("Sprites/Default");
        return particleMaterial = s != null ? new Material(s) { name = "EnemyBurst" } : null;
    }

    private static Sprite Pixel()
    {
        if (pixel != null) return pixel;
        var tex = new Texture2D(2, 2) { filterMode = FilterMode.Point };
        tex.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
        tex.Apply();
        return pixel = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 2);
    }
}
