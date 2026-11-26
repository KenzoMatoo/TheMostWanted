using UnityEngine;
using UnityEngine.Rendering.Universal;

// Luz del toro: sus grietas de lava iluminan la arena, mas fuerte en cada fase.
// Destellos en el burst y en el cambio de fase; en la fase 3 la luz de toda la arena se enrojece.
// La agrega LevelLighting al encontrar al jefe.
[RequireComponent(typeof(BossEnemy))]
public class BossGlow : MonoBehaviour
{
    public Color glowColor = new Color(1f, 0.5f, 0.2f);
    public float[] radiusPerPhase = { 5f, 8f, 11f };
    public float[] intensityPerPhase = { 0.3f, 0.95f, 1.45f };
    [Tooltip("Cuanto se enrojece la luz base del nivel en la ultima fase")]
    [Range(0f, 1f)] public float lastPhaseAmbientTint = 0.45f;
    public Color lastPhaseAmbient = new Color(1f, 0.35f, 0.25f);

    [Header("Luces rojas de la arena")]
    public Color arenaLightColor = new Color(1f, 0.16f, 0.1f);
    [Tooltip("Antes de que empiece la pelea")]
    public float arenaIdleIntensity = 0.5f;
    public float[] arenaIntensityPerPhase = { 0.9f, 1.6f, 2.6f };
    public float[] arenaRadiusPerPhase = { 7f, 8.5f, 10.5f };
    [Tooltip("Velocidad del latido de las luces en cada fase")]
    public float[] arenaPulseSpeed = { 1.2f, 2.2f, 3.4f };
    // Room tiles (0..32) where the red lights go: floor corners, over the ledges, upper walls and ceiling
    private static readonly Vector2Int[] arenaSpots =
    {
        new Vector2Int(3, 3), new Vector2Int(29, 3), new Vector2Int(9, 2), new Vector2Int(23, 2),
        new Vector2Int(2, 15), new Vector2Int(30, 15), new Vector2Int(2, 25), new Vector2Int(30, 25),
        new Vector2Int(8, 30), new Vector2Int(24, 30), new Vector2Int(16, 21),
    };
    private readonly System.Collections.Generic.List<Light2D> arenaLights = new System.Collections.Generic.List<Light2D>();
    private bool engaged;

    private BossEnemy boss;
    private Light2D glow, flash;
    private LightFlicker flicker;
    private float flashLevel, flashRadius;
    private bool dead;

    private void Start()
    {
        boss = GetComponent<BossEnemy>();
        Collider2D col = GetComponent<Collider2D>();
        Vector3 center = col != null ? col.bounds.center - transform.position : Vector3.zero;

        glow = LevelLighting.MakePointLight(transform, transform.position + center, glowColor, 0f, radiusPerPhase[0], "Luz lava");
        flicker = glow.gameObject.AddComponent<LightFlicker>();
        flicker.amount = 0.22f; flicker.speed = 5f;

        flash = LevelLighting.MakePointLight(transform, transform.position + center, new Color(1f, 0.7f, 0.4f), 0f, 10f, "Destello");

        boss.onEnergyBurst.AddListener(() => Flash(4f, boss.BurstRadius * 2.2f));
        boss.onPhaseChanged.AddListener(_ => Flash(3f, 14f));
        boss.OnDeath.AddListener(OnDeath);
        boss.onBossEngaged.AddListener(() => engaged = true);
        boss.onPhaseChanged.AddListener(_ => { foreach (var l in arenaLights) if (l != null) l.intensity += 2.5f; });   // all flare up together
        CreateArenaLights();
    }

    private void CreateArenaLights()
    {
        BossArena arena = GetComponentInParent<BossArena>();
        if (arena == null) return;
        UnityEngine.Tilemaps.Tilemap walls = null;
        foreach (Transform c in arena.transform)
            if (c.name == HandmadeRoom.WallsName) walls = c.GetComponent<UnityEngine.Tilemaps.Tilemap>();
        if (walls == null) return;
        walls.CompressBounds();
        Vector3Int min = walls.cellBounds.min;
        foreach (var spot in arenaSpots)
        {
            Vector3 pos = walls.GetCellCenterWorld(min + new Vector3Int(spot.x, spot.y, 0));
            var l = LevelLighting.MakePointLight(arena.transform, pos, arenaLightColor, arenaIdleIntensity, arenaRadiusPerPhase[0], "Luz roja arena");
            l.falloffIntensity = 0.55f;
            arenaLights.Add(l);
        }
    }

    private void UpdateArenaLights()
    {
        if (arenaLights.Count == 0) return;
        int i = Mathf.Clamp(boss.PhaseNumber - 1, 0, arenaIntensityPerPhase.Length - 1);
        float target = dead ? 0.3f : engaged ? arenaIntensityPerPhase[i] : arenaIdleIntensity;
        float radius = arenaRadiusPerPhase[Mathf.Min(i, arenaRadiusPerPhase.Length - 1)];
        float speed = arenaPulseSpeed[Mathf.Min(i, arenaPulseSpeed.Length - 1)];
        for (int k = 0; k < arenaLights.Count; k++)
        {
            var l = arenaLights[k];
            if (l == null) continue;
            // Heartbeat: every light pulses, a little out of step with its neighbours
            float beat = 0.75f + 0.25f * Mathf.Sin(Time.time * speed * Mathf.PI + k * 0.9f);
            float want = target * beat;
            l.intensity = l.intensity > want ? Mathf.MoveTowards(l.intensity, want, Time.deltaTime * 4f) : Mathf.MoveTowards(l.intensity, want, Time.deltaTime * 2f);
            l.pointLightOuterRadius = Mathf.MoveTowards(l.pointLightOuterRadius, radius, Time.deltaTime * 3f);
        }
    }

    private void Flash(float intensity, float radius)
    {
        flashLevel = Mathf.Max(flashLevel, intensity);
        flashRadius = radius;
    }

    private void Update()
    {
        if (boss == null) return;
        UpdateArenaLights();
        if (glow == null) return;

        int i = Mathf.Clamp(boss.PhaseNumber - 1, 0, radiusPerPhase.Length - 1);
        float targetIntensity = dead ? 0f : intensityPerPhase[Mathf.Min(i, intensityPerPhase.Length - 1)];
        // While it charges the burst the glow swells
        if (!dead && boss.IsChargingBurst) targetIntensity *= 1.8f;
        flicker.baseIntensity = Mathf.MoveTowards(flicker.baseIntensity, targetIntensity, Time.deltaTime * 1.5f);
        glow.pointLightOuterRadius = Mathf.MoveTowards(glow.pointLightOuterRadius, radiusPerPhase[i], Time.deltaTime * 4f);

        flashLevel = Mathf.MoveTowards(flashLevel, 0f, Time.deltaTime * 6f);
        flash.intensity = flashLevel;
        flash.pointLightOuterRadius = flashRadius;

        // Last phase: the whole arena turns red
        bool lastPhase = !dead && boss.PhaseNumber >= boss.PhaseCount && boss.PhaseCount > 1;
        if (LevelLighting.Instance != null) LevelLighting.Instance.SetAmbientTint(lastPhaseAmbient, lastPhase ? lastPhaseAmbientTint : 0f);
    }

    private void OnDeath()
    {
        if (dead) return;
        dead = true;
        // The lights live on until they fade, even if the boss object is destroyed
        if (flash != null) { flash.intensity = 5f; flash.pointLightOuterRadius = 16f; }
        foreach (var l in new[] { glow, flash })
        {
            if (l == null) continue;
            l.transform.SetParent(null, true);
            if (LevelLighting.Instance != null) LevelLighting.Instance.FadeOut(l, l == flash ? 1.2f : 2.5f);
            else Destroy(l.gameObject, 2.5f);
        }
        glow = flash = null;
        // The arena calms down: the red lights stay as a dim glow (the boss object may be destroyed now)
        foreach (var l in arenaLights) if (l != null && LevelLighting.Instance != null) LevelLighting.Instance.Dim(l, 0.3f, 2f);
        if (LevelLighting.Instance != null) LevelLighting.Instance.SetAmbientTint(lastPhaseAmbient, 0f);
    }
}
