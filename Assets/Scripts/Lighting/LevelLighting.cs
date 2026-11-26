using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

// Iluminacion y efectos de los niveles. Se crea sola en cualquier escena con jugador (niveles y tutorial).
// Los colores de cada nivel salen de Resources/IluminacionNiveles (LevelLightingProfile).
//  1. Luz base oscura con el tono del nivel.
//  2. Luces: halo del jugador, faroles en las lamparas del gancho, salida (naranja cerrada / verde abierta),
//     brillo rojo en los pinchos y la lava del toro (BossGlow).
//  3. Ambiente: polvo flotando y rayos de luz que bajan por las aberturas del techo de los cuartos.
//  4. Post-proceso: bloom, vineta, color; pulso de aberracion al recibir dano y vineta roja con poca vida.
[DefaultExecutionOrder(-50)]
public class LevelLighting : MonoBehaviour
{
    public static LevelLighting Instance { get; private set; }

    private LevelLightingProfile.Mood mood;
    private Light2D ambient;
    private Color ambientTint;
    private float ambientTintAmount, ambientTintCurrent;
    private Transform worldLights;
    private readonly List<(LevelExit exit, Light2D light, LightFlicker flicker)> exits = new List<(LevelExit, Light2D, LightFlicker)>();

    private PlayerStats player;
    private Light2D playerLight;
    private ParticleSystem dust;
    private Camera cam;

    private Volume volume;
    private Vignette vignette;
    private ChromaticAberration chromatic;
    private ColorAdjustments colorAdjust;
    private float hitPulse, lastHealth = -1, lowHealthBlend;
    private BossEnemy boss;
    private float bossSearchTimer;

    private static readonly Color voidColor = new Color(0.06f, 0.04f, 0.05f);
    private static Material litMaterial;
    private static Sprite pixel;

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
        if (FindAnyObjectByType<LevelLighting>() != null) return;
        if (FindAnyObjectByType<PlayerStats>() == null) return;   // menu and hub are UI screens
        new GameObject("Iluminacion").AddComponent<LevelLighting>();
    }

    // ---------------------------------------------------------------- Setup

    private void Awake()
    {
        Instance = this;
        var profile = Resources.Load<LevelLightingProfile>("IluminacionNiveles");
        string scene = SceneManager.GetActiveScene().name;
        mood = profile != null ? profile.Get(scene) : FindDefault(scene);

        player = FindAnyObjectByType<PlayerStats>();
        var playerRenderer = player != null ? player.GetComponent<SpriteRenderer>() : null;
        if (playerRenderer != null) litMaterial = playerRenderer.sharedMaterial;   // Sprite-Lit-Default

        CreateAmbient();
        CreatePlayerLight();
        CreateVolume();

        // Generated levels: lamps, exits, spikes and openings exist only after the map is built
        var generator = FindAnyObjectByType<MapGenerationBase>();
        if (generator != null) generator.MapGenerated += BuildWorld;
        else StartCoroutine(BuildNextFrame());
    }

    private void OnDestroy()
    {
        var generator = FindAnyObjectByType<MapGenerationBase>();
        if (generator != null) generator.MapGenerated -= BuildWorld;
        if (Instance == this) Instance = null;
    }

    private IEnumerator BuildNextFrame()
    {
        yield return null;
        BuildWorld();
    }

    private static LevelLightingProfile.Mood FindDefault(string scene)
    {
        var all = LevelLightingProfile.Defaults();
        foreach (var m in all) if (m.scene == scene) return m;
        return all[0];
    }

    /// <summary>Tints the base light (the boss uses it to turn the arena red). amount 0 = normal.</summary>
    public void SetAmbientTint(Color c, float amount)
    {
        ambientTint = c;
        ambientTintAmount = amount;
    }

    // ---------------------------------------------------------------- 1. Base light

    private void CreateAmbient()
    {
        var go = new GameObject("Luz base");
        go.transform.SetParent(transform, false);
        ambient = go.AddComponent<Light2D>();
        ambient.lightType = Light2D.LightType.Global;
        ambient.color = mood.ambientColor;
        ambient.intensity = mood.ambientIntensity;
    }

    // ---------------------------------------------------------------- 2. Lights

    public static Light2D MakePointLight(Transform parent, Vector3 position, Color color, float intensity, float radius, string name = "Luz")
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, true);
        go.transform.position = position;
        var l = go.AddComponent<Light2D>();
        l.lightType = Light2D.LightType.Point;
        l.color = color;
        l.intensity = intensity;
        l.pointLightInnerRadius = 0f;
        l.pointLightOuterRadius = radius;
        l.falloffIntensity = 0.65f;
        l.shadowsEnabled = false;
        return l;
    }

    private void CreatePlayerLight()
    {
        if (player == null || mood.playerLightIntensity <= 0f) return;
        Collider2D col = player.GetComponent<Collider2D>();
        Vector3 pos = col != null ? col.bounds.center : player.transform.position;
        playerLight = MakePointLight(player.transform, pos, mood.playerLight, mood.playerLightIntensity, mood.playerLightRadius, "Halo jugador");
        playerLight.falloffIntensity = 0.75f;
    }

    private void BuildWorld()
    {
        if (worldLights != null) Destroy(worldLights.gameObject);
        worldLights = new GameObject("Luces del nivel").transform;
        worldLights.SetParent(transform, false);
        exits.Clear();

        // Lamps of the grapple hook: warm lanterns
        foreach (var lamp in FindObjectsByType<GrappleLamp>(FindObjectsSortMode.None))
        {
            var l = MakePointLight(lamp.transform, lamp.transform.position, mood.lampLight, mood.lampIntensity, mood.lampRadius, "Farol");
            var f = l.gameObject.AddComponent<LightFlicker>();
            f.baseIntensity = mood.lampIntensity; f.amount = 0.12f; f.speed = 4f;
        }

        // Exit door: orange while locked, green-turquoise when it opens
        foreach (var exit in FindObjectsByType<LevelExit>(FindObjectsSortMode.None))
        {
            Collider2D col = exit.GetComponent<Collider2D>();
            Vector3 pos = col != null ? col.bounds.center : exit.transform.position;
            var l = MakePointLight(worldLights, pos, exit.IsLocked ? mood.exitLockedLight : mood.exitOpenLight, 0.8f, 6f, "Luz salida");
            var f = l.gameObject.AddComponent<LightFlicker>();
            f.baseIntensity = 0.8f; f.amount = 0.08f; f.speed = 2f;
            exits.Add((exit, l, f));
            exit.onUnlocked.AddListener(() => f.Boost(3f));
        }

        BuildWallLanterns();
        BuildHazardLights();
        if (mood.lightShafts) BuildLightShafts();
        CreateDust();
    }

    // Lanterns hung on the side walls of every room, at eye height, away from platforms, spikes and other lights
    private void BuildWallLanterns()
    {
        if (!mood.wallLanterns || mood.lanternsPerRoom <= 0) return;
        var placed = new List<Vector2>();
        foreach (var lamp in FindObjectsByType<GrappleLamp>(FindObjectsSortMode.None)) placed.Add(lamp.transform.position);
        var rng = new System.Random(SceneManager.GetActiveScene().name.GetHashCode());
        Vector3Int up = Vector3Int.up, down = Vector3Int.down;

        foreach (var tm in FindObjectsByType<Tilemap>(FindObjectsSortMode.None))
        {
            if (tm.name != HandmadeRoom.WallsName) continue;
            Tilemap bg = Sibling(tm, "Background"), plats = Sibling(tm, HandmadeRoom.PlatformsName), haz = Sibling(tm, HandmadeRoom.HazardsName);
            tm.CompressBounds();
            BoundsInt b = tm.cellBounds;
            if (b.size.x < 8 || b.size.y < 8) continue;
            int budget = Mathf.Max(1, Mathf.RoundToInt(mood.lanternsPerRoom * b.size.x * b.size.y / (33f * 33f)));

            var candidates = new List<(Vector3Int cell, int side)>();
            for (int y = b.yMin + 3; y < b.yMax - 3; y++)
                for (int x = b.xMin + 1; x < b.xMax - 1; x++)
                {
                    var c = new Vector3Int(x, y, 0);
                    if (tm.HasTile(c) || tm.HasTile(c + up) || tm.HasTile(c + down) || tm.HasTile(c + up * 2)) continue;
                    int side = tm.HasTile(c + Vector3Int.left) ? -1 : tm.HasTile(c + Vector3Int.right) ? 1 : 0;
                    if (side == 0) continue;
                    var wall = c + new Vector3Int(side, 0, 0);
                    if (!tm.HasTile(wall + up) || !tm.HasTile(wall + down)) continue;     // a real wall, not the tip of a ledge
                    if (tm.HasTile(c - new Vector3Int(side, 0, 0))) continue;              // not in a 1-tile gap
                    if (bg != null && !bg.HasTile(c)) continue;                            // inside the room
                    if (Near(plats, c, 1) || Near(haz, c, 2)) continue;
                    candidates.Add((c, side));
                }

            // Random order (fixed per scene), then keep the ones far enough from every other light
            for (int i = candidates.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (candidates[i], candidates[j]) = (candidates[j], candidates[i]); }
            int n = 0;
            foreach (var (cell, side) in candidates)
            {
                Vector3 center = tm.GetCellCenterWorld(cell);
                bool free = true;
                foreach (var p in placed) if (Vector2.Distance(p, center) < 9f) { free = false; break; }
                if (!free) continue;
                MakeWallLantern(center, side, tm.cellSize.x);
                placed.Add(center);
                if (++n >= budget) break;
            }
        }
    }

    private static Tilemap Sibling(Tilemap tm, string name)
    {
        if (tm.transform.parent == null) return null;
        Transform t = tm.transform.parent.Find(name);
        return t != null ? t.GetComponent<Tilemap>() : null;
    }

    private static bool Near(Tilemap tm, Vector3Int c, int r)
    {
        if (tm == null) return false;
        for (int dx = -r; dx <= r; dx++)
            for (int dy = -r; dy <= r; dy++)
                if (tm.HasTile(c + new Vector3Int(dx, dy, 0))) return true;
        return false;
    }

    private void MakeWallLantern(Vector3 cellCenter, int side, float cell)
    {
        var go = new GameObject("Farol de pared");
        go.transform.SetParent(worldLights, false);
        Sprite s = LanternSprite();
        float halfW = s.bounds.extents.x;
        go.transform.position = new Vector3(cellCenter.x + side * (cell * 0.5f - halfW), cellCenter.y + 0.15f, 0f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = s;
        sr.flipX = side > 0;          // drawn hanging from a wall on its left
        if (litMaterial != null) sr.sharedMaterial = litMaterial;
        sr.sortingOrder = -4;         // over the background and deco, under the walls

        // The flame is a little below the center of the sprite, on the lantern side
        Vector3 flame = go.transform.position + new Vector3(-side * 0.06f, -0.05f, 0f);
        var l = MakePointLight(go.transform, flame, mood.lampLight, mood.lanternIntensity, mood.lanternRadius, "Luz farol");
        var f = l.gameObject.AddComponent<LightFlicker>();
        f.baseIntensity = mood.lanternIntensity; f.amount = 0.14f; f.speed = 5f;
    }

    // 10x12 pixel lantern on an iron bracket (16 px = 1 tile)
    private static Sprite lanternSprite;
    private static Sprite LanternSprite()
    {
        if (lanternSprite != null) return lanternSprite;
        string[] rows =
        {
            "KKKKKKK...",
            "K.....K...",
            "K...MMMMM.",
            "....KGGGK.",
            "....KGFGK.",
            "....KFFFK.",
            "....KGFGK.",
            "....KGGGK.",
            "....MMMMM.",
            ".....KKK..",
            "......K...",
            "..........",
        };
        var colors = new Dictionary<char, Color32>
        {
            { 'K', new Color32(38, 28, 30, 255) }, { 'M', new Color32(96, 74, 62, 255) },
            { 'G', new Color32(255, 196, 96, 255) }, { 'F', new Color32(255, 246, 214, 255) },
        };
        int w = rows[0].Length, h = rows.Length;
        var tex = new Texture2D(w, h) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                char ch = rows[h - 1 - y][x];
                tex.SetPixel(x, y, colors.TryGetValue(ch, out var c) ? (Color)c : Color.clear);
            }
        tex.Apply();
        return lanternSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 16f);
    }

    // Faint red glow along each row of spikes (one light per run of tiles)
    private void BuildHazardLights()
    {
        if (mood.hazardIntensity <= 0f) return;
        int count = 0;
        foreach (var tm in FindObjectsByType<Tilemap>(FindObjectsSortMode.None))
        {
            if (tm.name != HandmadeRoom.HazardsName) continue;
            tm.CompressBounds();
            BoundsInt b = tm.cellBounds;
            for (int y = b.yMin; y < b.yMax; y++)
            {
                int runStart = int.MinValue;
                for (int x = b.xMin; x <= b.xMax; x++)
                {
                    bool has = x < b.xMax && tm.HasTile(new Vector3Int(x, y, 0));
                    if (has && runStart == int.MinValue) runStart = x;
                    if (!has && runStart != int.MinValue)
                    {
                        Vector3 a = tm.GetCellCenterWorld(new Vector3Int(runStart, y, 0));
                        Vector3 c = tm.GetCellCenterWorld(new Vector3Int(x - 1, y, 0));
                        float len = Vector3.Distance(a, c) + 1f;
                        MakePointLight(worldLights, (a + c) * 0.5f, mood.hazardLight, mood.hazardIntensity, Mathf.Clamp(len * 0.6f + 1f, 2f, 8f), "Brillo pinchos");
                        runStart = int.MinValue;
                        if (++count > 60) return;
                    }
                }
            }
        }
    }

    // ---------------------------------------------------------------- 3. Atmosphere

    // Slanted beams of light coming down through the openings in the top wall of each room
    private void BuildLightShafts()
    {
        int count = 0;
        float slant = 2.5f;
        foreach (var tm in FindObjectsByType<Tilemap>(FindObjectsSortMode.None))
        {
            if (tm.name != HandmadeRoom.WallsName) continue;
            tm.CompressBounds();
            BoundsInt b = tm.cellBounds;
            if (b.size.x < 8 || b.size.y < 8) continue;
            int top = b.yMax - 1;
            int runStart = int.MinValue;
            for (int x = b.xMin; x <= b.xMax; x++)
            {
                bool open = x < b.xMax && !tm.HasTile(new Vector3Int(x, top, 0)) && x > b.xMin && x < b.xMax - 1;
                if (open && runStart == int.MinValue) runStart = x;
                if (!open && runStart != int.MinValue)
                {
                    int width = x - runStart;
                    if (width >= 3)
                    {
                        Vector3 left = tm.CellToWorld(new Vector3Int(runStart, top + 1, 0));
                        Vector3 right = tm.CellToWorld(new Vector3Int(x, top + 1, 0));
                        // Length: until the first solid thing below the middle of the opening
                        Vector2 mid = (left + right) * 0.5f;
                        RaycastHit2D hit = Physics2D.Raycast(mid + Vector2.down * 1.5f, new Vector2(slant / 14f, -1f).normalized, 16f, LayerMask.GetMask("Ground"));
                        float length = hit.collider != null ? Mathf.Max(6f, hit.distance + 2.5f) : 14f;
                        MakeShaft(left, right, length, slant * length / 14f);
                        if (++count > 25) return;
                    }
                    runStart = int.MinValue;
                }
            }
        }
    }

    private void MakeShaft(Vector3 topLeft, Vector3 topRight, float length, float slant)
    {
        var go = new GameObject("Rayo de luz");
        go.transform.SetParent(worldLights, false);
        go.transform.position = (topLeft + topRight) * 0.5f;
        var l = go.AddComponent<Light2D>();
        l.lightType = Light2D.LightType.Freeform;
        float hw = Vector3.Distance(topLeft, topRight) * 0.5f;
        // Narrow at the top, wider at the bottom, leaning to one side
        l.SetShapePath(new[]
        {
            new Vector3(-hw * 0.8f, 0f, 0f),
            new Vector3(hw * 0.8f, 0f, 0f),
            new Vector3(hw * 1.5f + slant, -length, 0f),
            new Vector3(-hw * 1.5f + slant, -length, 0f),
        });
        l.shapeLightFalloffSize = 1.8f;
        l.color = mood.shaftColor;
        l.intensity = mood.shaftIntensity;
        l.shadowsEnabled = false;
        var f = go.AddComponent<LightFlicker>();
        f.baseIntensity = mood.shaftIntensity; f.amount = 0.18f; f.speed = 0.35f;   // slow breathing, like dust passing the light
    }

    // Specks of dust floating in the air: lit material, so they only show near the lights
    private void CreateDust()
    {
        if (dust != null || mood.dustPerSecond <= 0f) return;
        cam = Camera.main;
        if (cam == null) return;

        var go = new GameObject("Polvo flotante");
        go.SetActive(false);
        go.transform.SetParent(cam.transform, false);
        go.transform.localPosition = new Vector3(0f, 0f, 10f);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;   // the dust stays in the world, the camera moves through it
        main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 9f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
        main.startColor = mood.dustColor;
        main.maxParticles = 400;

        var emission = ps.emission;
        emission.rateOverTime = mood.dustPerSecond;

        float h = cam.orthographic ? cam.orthographicSize * 2.4f : 20f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(h * cam.aspect, h, 0.1f);

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.2f);
        vel.y = new ParticleSystem.MinMaxCurve(-0.08f, 0.18f);
        vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.25f;
        noise.frequency = 0.3f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        var sheet = ps.textureSheetAnimation;
        sheet.enabled = true;
        sheet.mode = ParticleSystemAnimationMode.Sprites;
        sheet.AddSprite(Pixel());

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        if (litMaterial != null) r.sharedMaterial = litMaterial;
        r.sortingOrder = 5;

        go.SetActive(true);
        ps.Play();
        dust = ps;
    }

    private static Sprite Pixel()
    {
        if (pixel != null) return pixel;
        var tex = new Texture2D(2, 2) { filterMode = FilterMode.Point };
        tex.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
        tex.Apply();
        return pixel = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 2);
    }

    // ---------------------------------------------------------------- 4. Post-processing

    private void CreateVolume()
    {
        var go = new GameObject("Post-proceso");
        go.transform.SetParent(transform, false);
        volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 10;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        volume.sharedProfile = profile;

        var bloom = profile.Add<Bloom>(true);
        bloom.intensity.value = mood.bloom;
        bloom.threshold.value = 0.85f;
        bloom.scatter.value = 0.65f;
        bloom.tint.value = Color.white;
        bloom.highQualityFiltering.value = false;

        vignette = profile.Add<Vignette>(true);
        vignette.intensity.value = mood.vignette;
        vignette.smoothness.value = 0.45f;
        vignette.color.value = Color.black;
        vignette.center.value = new Vector2(0.5f, 0.5f);
        vignette.rounded.value = false;

        colorAdjust = profile.Add<ColorAdjustments>(true);
        colorAdjust.postExposure.value = 0f;
        colorAdjust.contrast.value = mood.contrast;
        colorAdjust.saturation.value = mood.saturation;
        colorAdjust.colorFilter.value = mood.colorFilter;
        colorAdjust.hueShift.value = 0f;

        var split = profile.Add<SplitToning>(true);
        split.shadows.value = mood.shadowsTone;
        split.highlights.value = mood.highlightsTone;
        split.balance.value = 0f;

        chromatic = profile.Add<ChromaticAberration>(true);
        chromatic.intensity.value = 0f;
    }

    private void EnsureCameraPost()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;
        // Anything outside the rooms is dark instead of Unity's default blue
        if (cam.clearFlags != CameraClearFlags.SolidColor || cam.backgroundColor != voidColor)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = voidColor;
        }
        var data = cam.GetUniversalAdditionalCameraData();
        if (data != null && !data.renderPostProcessing) data.renderPostProcessing = true;
    }

    // ---------------------------------------------------------------- Per frame

    private void Update()
    {
        EnsureCameraPost();
        float dt = Time.unscaledDeltaTime;

        // Base light (+ boss tint)
        ambientTintCurrent = Mathf.MoveTowards(ambientTintCurrent, ambientTintAmount, dt * 0.5f);
        ambient.color = Color.Lerp(mood.ambientColor, ambientTint, ambientTintCurrent);

        // Exit light follows the lock state
        foreach (var (exit, light, _) in exits)
        {
            if (exit == null || light == null) continue;
            Color target = exit.IsLocked ? mood.exitLockedLight : mood.exitOpenLight;
            light.color = Color.Lerp(light.color, target, dt * 3f);
        }

        // Boss: give it its lava glow as soon as it exists
        if (boss == null)
        {
            bossSearchTimer -= dt;
            if (bossSearchTimer <= 0f)
            {
                bossSearchTimer = 1f;
                boss = FindAnyObjectByType<BossEnemy>();
                if (boss != null && boss.GetComponent<BossGlow>() == null) boss.gameObject.AddComponent<BossGlow>();
            }
        }

        UpdateDamageFeedback(dt);
    }

    // Hit: a quick color split. Low health: the screen loses color and the edges pulse red.
    private void UpdateDamageFeedback(float dt)
    {
        if (player == null || vignette == null) return;
        float health = player.GetCurrentHealth();
        if (lastHealth >= 0 && health < lastHealth) hitPulse = 1f;
        lastHealth = health;

        hitPulse = Mathf.MoveTowards(hitPulse, 0f, dt * 2.5f);
        chromatic.intensity.value = hitPulse * 0.9f;

        bool low = !player.IsDead() && health > 0 && health <= Mathf.Max(1f, player.GetMaxHealth() * 0.25f);
        lowHealthBlend = Mathf.MoveTowards(lowHealthBlend, low ? 1f : 0f, dt * 1.5f);
        float beat = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f);
        vignette.color.value = Color.Lerp(Color.black, new Color(0.55f, 0.02f, 0.05f), lowHealthBlend);
        vignette.intensity.value = Mathf.Lerp(mood.vignette, 0.48f + 0.06f * beat, lowHealthBlend) + hitPulse * 0.12f;
        colorAdjust.saturation.value = Mathf.Lerp(mood.saturation, -35f, lowHealthBlend);
    }

    /// <summary>Moves a light to a new intensity over time (it keeps living).</summary>
    public void Dim(Light2D l, float to, float time)
    {
        if (l != null) StartCoroutine(DimRoutine(l, to, time));
    }

    private static IEnumerator DimRoutine(Light2D l, float to, float time)
    {
        float start = l.intensity;
        for (float t = 0f; t < time && l != null; t += Time.deltaTime)
        {
            l.intensity = Mathf.Lerp(start, to, t / time);
            yield return null;
        }
        if (l != null) l.intensity = to;
    }

    /// <summary>Fades a light out and destroys it (used by lights that outlive their owner).</summary>
    public void FadeOut(Light2D l, float time)
    {
        if (l != null) StartCoroutine(FadeRoutine(l, time));
    }

    private static IEnumerator FadeRoutine(Light2D l, float time)
    {
        var f = l.GetComponent<LightFlicker>();
        if (f != null) Destroy(f);
        float start = l.intensity;
        for (float t = 0f; t < time && l != null; t += Time.deltaTime)
        {
            l.intensity = Mathf.Lerp(start, 0f, t / time);
            yield return null;
        }
        if (l != null) Destroy(l.gameObject);
    }
}
