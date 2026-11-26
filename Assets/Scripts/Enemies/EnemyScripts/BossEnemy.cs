using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Configuración de UNA fase del jefe. Todo es editable desde el Inspector.
/// El orden del array "phases" en BossEnemy define Fase 1, Fase 2, Fase 3...
/// </summary>
[System.Serializable]
public class BossPhaseSettings
{
    [Header("Visual (sprite por fase / estado)")]
    [Tooltip("Sprite base de esta fase (perseguir/estar quieto).")]
    public Sprite idleSprite;
    [Tooltip("Sprite mientras está en el aire (salto/caída). Vacío = idleSprite.")]
    public Sprite jumpSprite;
    [Tooltip("Sprite breve al recibir daño. Vacío = idleSprite.")]
    public Sprite hurtSprite;
    [Tooltip("Sprite durante el burst de energía. Vacío = idleSprite.")]
    public Sprite burstSprite;
    [Tooltip("Sprite durante el aviso (telegraph) de ataques. Vacío = idleSprite.")]
    public Sprite telegraphSprite;
    [Tooltip("Sprite durante la carga (dash). Vacío = idleSprite.")]
    public Sprite dashSprite;
    [Tooltip("Sprite al invocar enemigos. Vacío = idleSprite.")]
    public Sprite summonSprite;
    [Tooltip("Sprite al morir en esta fase. Vacío = el Death Sprite general.")]
    public Sprite deathSprite;
    [Tooltip("Tinte del sprite al perseguir en esta fase. Déjalo en blanco si cada fase ya tiene su propio arte.")]
    public Color tint = Color.white;

    [Header("Movimiento")]
    public float chaseSpeed = 4f;
    [Tooltip("Segundos mínimos entre saltos.")]
    public float jumpCooldown = 1f;

    [Header("Ataque - Carga (Dash)")]
    public float telegraphTime = 0.7f;
    public float dashSpeed = 12f;
    [Tooltip("Cuántas cargas seguidas hace por ataque (si la primera no conecta).")]
    public int dashesPerAttack = 1;
    public float chainedTelegraphTime = 0.3f;
    [Tooltip("Multiplicador del daño base (attackDamage y burstDamage) en esta fase.")]
    public float damageMultiplier = 1f;

    [Header("Ataque - Invocación")]
    public int minionsPerSummon = 2;
    [Tooltip("Máximo de súbditos vivos a la vez en esta fase.")]
    public int maxAliveMinions = 3;
    public float summonWindup = 1f;

    [Header("Selección de ataque (peso relativo, 0 = deshabilitado)")]
    public float dashWeight = 3f;
    public float summonWeight = 1f;
    [Tooltip("Se teletransporta detrás del jugador y carga inmediatamente.")]
    public float teleportStrikeWeight = 0f;

    [Header("Burst de energía")]
    [Tooltip("Golpes recibidos dentro de la ventana para disparar el burst en esta fase (0 = solo por % de daño).")]
    public int burstHitsThreshold = 6;

    [Header("Ritmo")]
    [Tooltip("Tiempo quieto/vulnerable después de atacar.")]
    public float recoveryTime = 1f;
    [Tooltip("Tiempo persiguiendo antes de poder lanzar el siguiente ataque.")]
    public float attackCooldown = 2f;
}

/// <summary>
/// Jefe final de fases configurables. Basado en EnemyDasher:
/// - Al ver al jugador no lo suelta jamás. Inmune a captura, poco knockback.
/// - Persigue con un planificador de saltos (simula trayectorias antes de saltar), no camina
///   al vacío, detecta atascos y se teletransporta si no puede llegar.
/// - Ataques: Carga (con encadenado), Invocación de súbditos, Teletransporte + Carga.
/// - Si le pegan muchas veces en poco tiempo: Burst de energía (daña al jugador) y queda
///   invencible unos segundos.
/// - Cada fase cambia sprites, velocidad, daño, ritmo y ataques.
/// </summary>
public class BossEnemy : MeleeChaserEnemy
{
    private enum AttackType { None, Dash, Summon, TeleportStrike, PhaseTransition, Burst }
    private enum AttackPhase { Telegraph, Reappear, Dashing, Summoning, Transition, BurstCharge, BurstRelease, Recovering }

    private struct HitRecord { public float time; public int damage; }

    // ───────────────────────────── Fases ─────────────────────────────
    [Header("Boss Phases")]
    [Tooltip("Una entrada por fase (Fase 1, Fase 2, Fase 3...).")]
    [SerializeField]
    private BossPhaseSettings[] phases = new BossPhaseSettings[]
    {
        new BossPhaseSettings
        {
            chaseSpeed = 3.5f, jumpCooldown = 1.2f, telegraphTime = 0.8f, dashSpeed = 11f, dashesPerAttack = 1,
            damageMultiplier = 1f, minionsPerSummon = 2, maxAliveMinions = 3, summonWindup = 1f,
            dashWeight = 3f, summonWeight = 1f, teleportStrikeWeight = 0f, recoveryTime = 1.2f, attackCooldown = 2.5f,
            burstHitsThreshold = 6, tint = Color.white
        },
        new BossPhaseSettings
        {
            chaseSpeed = 4.5f, jumpCooldown = 0.9f, telegraphTime = 0.6f, dashSpeed = 14f, dashesPerAttack = 1,
            damageMultiplier = 1.25f, minionsPerSummon = 3, maxAliveMinions = 5, summonWindup = 0.8f,
            dashWeight = 3f, summonWeight = 2f, teleportStrikeWeight = 1f, recoveryTime = 0.9f, attackCooldown = 1.8f,
            burstHitsThreshold = 5, tint = new Color(1f, 0.85f, 0.7f, 1f)
        },
        new BossPhaseSettings
        {
            chaseSpeed = 5.5f, jumpCooldown = 0.6f, telegraphTime = 0.4f, dashSpeed = 17f, dashesPerAttack = 2,
            chainedTelegraphTime = 0.25f, damageMultiplier = 1.5f, minionsPerSummon = 4, maxAliveMinions = 6,
            summonWindup = 0.6f, dashWeight = 3f, summonWeight = 2f, teleportStrikeWeight = 3f, recoveryTime = 0.6f,
            attackCooldown = 1.2f, burstHitsThreshold = 4, tint = new Color(1f, 0.65f, 0.65f, 1f)
        }
    };

    [Tooltip("% de vida (0-1) al que se pasa a la siguiente fase. (fases - 1) valores, de mayor a menor. Ej: 0.66 y 0.33.")]
    [SerializeField] private float[] phaseHealthThresholds = new float[] { 0.66f, 0.33f };

    [Header("Phase Transition")]
    [SerializeField] private float phaseTransitionDuration = 1.5f;
    [SerializeField] private Color transitionColor = new Color(1f, 1f, 0.4f, 1f);
    [Tooltip("Al terminar la transición invoca súbditos con los valores de la nueva fase.")]
    [SerializeField] private bool summonOnPhaseChange = true;
    [Tooltip("Invencible mientras dura la transición de fase.")]
    [SerializeField] private bool invulnerableDuringTransition = true;

    // ───────────────────────────── Resistencias ─────────────────────────────
    [Header("Resistances")]
    [Tooltip("Imposible de capturar (bloquea ICaptureable.CanBeCaptured).")]
    [SerializeField] private bool captureImmune = true;
    [Tooltip("Multiplicador del knockback recibido (0 = ninguno, 1 = normal). Además se ignora mientras ejecuta ataques.")]
    [SerializeField] private float knockbackMultiplier = 0.3f;
    [Tooltip("Fracción del stun que ignora (0 = normal, 1 = inmune al stun).")]
    [Range(0f, 1f)]
    [SerializeField] private float stunResistance = 0.75f;

    // ───────────────────────────── Movimiento ─────────────────────────────
    [Header("Chase Movement")]
    [SerializeField] private float stopDistance = 0.6f;
    [Tooltip("Aceleración horizontal en el aire (solo cuando cae sin salto planeado).")]
    [SerializeField] private float airAcceleration = 25f;
    [Tooltip("Crea un material sin fricción para que no se pegue a las paredes al saltar.")]
    [SerializeField] private bool useFrictionlessMaterial = true;

    [Header("Ground / Terrain Checks")]
    [Tooltip("Capa del suelo y plataformas. Si queda vacía usa Obstacle Layer.")]
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float groundCheckRadius = 0.15f;
    [SerializeField] private float wallCheckDistance = 0.5f;
    [SerializeField] private float wallCheckRadius = 0.2f;
    [SerializeField] private float wallCheckHeightOffset = 0f;
    [SerializeField] private float ledgeCheckForward = 0.4f;
    [SerializeField] private float ledgeCheckDepth = 0.4f;
    [SerializeField] private float ledgeCheckRadius = 0.2f;

    [Header("Jump (planificador de saltos)")]
    [SerializeField] private bool useJumping = true;
    [Tooltip("Velocidad vertical de un salto normal (depende de la gravedad del Rigidbody2D).")]
    [SerializeField] private float jumpVelocity = 12f;
    [Tooltip("Velocidad vertical del salto alto. El planificador prueba ambos y elige el mejor.")]
    [SerializeField] private float highJumpVelocity = 17f;
    [Tooltip("Si el jugador está más arriba que esto (en Y), considera saltar hacia él.")]
    [SerializeField] private float jumpHeightThreshold = 1.5f;
    [Tooltip("Altura máxima que puede alcanzar saltando. Más alto que esto => teletransporte.")]
    [SerializeField] private float maxJumpableHeight = 6f;
    [Tooltip("Distancia horizontal máxima al jugador para intentar saltar a su plataforma.")]
    [SerializeField] private float jumpTriggerHorizontalDistance = 5f;
    [Tooltip("Si el jugador está más abajo que esto, no salta los huecos: se deja caer.")]
    [SerializeField] private float dropDownThreshold = 1.5f;
    [Tooltip("Marca si tus plataformas son one-way (se puede subir a través de ellas desde abajo).")]
    [SerializeField] private bool oneWayPlatforms = true;
    [Tooltip("Cada cuánto (s) recalcula el plan de salto.")]
    [SerializeField] private float jumpPlanInterval = 0.2f;
    [Tooltip("Paso de tiempo de la simulación del salto (menor = más preciso y más costoso).")]
    [SerializeField] private float jumpPlanStep = 0.06f;
    [SerializeField] private float jumpPlanMaxTime = 1.8f;
    [Tooltip("Caída máxima aceptada al planear (más que esto = cae al vacío = salto descartado).")]
    [SerializeField] private float jumpPlanMaxDrop = 4f;
    [Tooltip("Cuánto debe acercarlo al jugador el aterrizaje para que valga la pena saltar.")]
    [SerializeField] private float jumpPlanMinImprovement = 0.5f;
    [Range(0.5f, 1f)]
    [SerializeField] private float jumpPlanBodyShrink = 0.85f;
    [Tooltip("Fuerza del salto de 'desatasco' (fracción de jumpVelocity).")]
    [SerializeField] private float unstickHopFactor = 0.8f;

    [Header("Anti-atasco")]
    [SerializeField] private float stuckTime = 0.5f;
    [SerializeField] private float stuckSpeedThreshold = 0.1f;
    [Tooltip("Cada cuántos segundos revisa si avanzó algo. Si se movió menos de 'No Progress Min Move', se considera atascado.")]
    [SerializeField] private float noProgressTime = 1.5f;
    [SerializeField] private float noProgressMinMove = 0.6f;

    [Header("Teleport (si no puede llegar)")]
    [SerializeField] private bool useTeleportFallback = true;
    [SerializeField] private float teleportDistance = 14f;
    [SerializeField] private float teleportVerticalDifference = 7f;
    [Tooltip("Tiempo siendo inalcanzable/atascado antes de teletransportarse.")]
    [SerializeField] private float teleportAfterUnreachableTime = 0.9f;
    [SerializeField] private float teleportCooldown = 3f;
    [SerializeField] private float teleportVanishTime = 0.25f;
    [SerializeField] private float teleportOffsetX = 2.5f;
    [SerializeField] private float teleportRayHeight = 4f;
    [Tooltip("Pausa tras reaparecer detrás del jugador, antes de cargar (Teleport Strike).")]
    [SerializeField] private float postTeleportDelay = 0.35f;
    [Tooltip("Opcional: límites de la arena. Si se asigna, solo se teletransporta dentro de ellos.")]
    [SerializeField] private Collider2D arenaBounds;
    [SerializeField] private GameObject teleportVfxPrefab;
    [Tooltip("Si el cuerpo queda dentro de una pared mas de estos segundos, se teletransporta a un lugar libre (seguridad).")]
    [SerializeField] private float insideWallTeleportTime = 0.4f;

    // ───────────────────────────── Carga (Dash) ─────────────────────────────
    [Header("Dash Settings")]
    [SerializeField] private float dashOvershootBuffer = 2f;

    [Header("Obstacle Check (dash)")]
    [SerializeField] private Transform obstacleCheck;
    [SerializeField] private float obstacleCheckRadius = 0.3f;
    [SerializeField] private float obstacleCheckDistance = 0.6f;

    [Header("Dash Hitbox")]
    [Tooltip("Collider2D (trigger) que detecta el golpe durante la carga. Si está vacío se autogenera.")]
    [SerializeField] private Collider2D dashHitboxCollider;
    [SerializeField] private Vector2 dashHitboxSize = new Vector2(1.5f, 1.5f);

    [Header("Impact Feedback")]
    [SerializeField] private float recoilSpeed = 6f;
    [SerializeField] private float recoilDuration = 0.15f;

    [Header("Action Colors")]
    [SerializeField] private Color telegraphColor = new Color(1f, 0.55f, 0.55f, 1f);
    [SerializeField] private Color dashColor = new Color(0.75f, 0f, 0f, 1f);
    [SerializeField] private Color summonColor = new Color(0.6f, 0.4f, 1f, 1f);

    [Header("Sprite")]
    [Tooltip("Marca esto si el arte original mira hacia la DERECHA. (EnemyDasher asume que mira a la izquierda.)")]
    [SerializeField] private bool spriteFacesRight = false;
    [Tooltip("Cuánto dura el sprite de 'recibir daño'.")]
    [SerializeField] private float hurtSpriteDuration = 0.2f;

    // ───────────────────────────── Burst de energía ─────────────────────────────
    [Header("Energy Burst (reacción a golpes rápidos)")]
    [SerializeField] private bool enableEnergyBurst = true;
    [Tooltip("Ventana de tiempo (s) en la que se cuentan los golpes recibidos.")]
    [SerializeField] private float burstHitWindow = 3f;
    [Tooltip("También dispara el burst si recibe este % de su vida máxima (0-1) dentro de la ventana. 0 = desactivado.")]
    [SerializeField] private float burstDamagePercentThreshold = 0.12f;
    [Tooltip("Tiempo de carga antes de que explote.")]
    [SerializeField] private float burstWindup = 0.5f;
    [Tooltip("Tiempo quieto después de la explosión.")]
    [SerializeField] private float burstRecoveryTime = 0.4f;
    [SerializeField] private float burstRadius = 4f;
    [SerializeField] private int burstDamage = 15;
    [Tooltip("Segundos que el jefe es invencible, contados desde que empieza la carga del burst (1-2 s).")]
    [SerializeField] private float burstInvulnerableDuration = 1.5f;
    [Tooltip("Tiempo mínimo entre un burst y el siguiente.")]
    [SerializeField] private float burstCooldown = 8f;
    [SerializeField] private Color burstColor = new Color(0.4f, 0.9f, 1f, 1f);
    [SerializeField] private GameObject burstVfxPrefab;

    // ───────────────────────────── Súbditos ─────────────────────────────
    [Header("Minion Spawning")]
    [Tooltip("Prefabs de enemigos que puede invocar (elige uno al azar). Repite un prefab para que salga más seguido.")]
    [SerializeField] private GameObject[] minionPrefabs;
    [Tooltip("Opcional: puntos fijos de spawn. Si está vacío aparecen alrededor del jefe.")]
    [SerializeField] private Transform[] spawnPoints;
    [SerializeField] private float minSpawnDistance = 1.5f;
    [SerializeField] private float spawnRadius = 4f;
    [SerializeField] private float minionSpawnStagger = 0.25f;
    [SerializeField] private bool killMinionsOnDeath = true;

    // ───────────────────────────── Muerte / Eventos ─────────────────────────────
    [Header("Death")]
    [SerializeField] private Sprite deathSprite;

    [Header("Events")]
    [Tooltip("Primera vez que detecta al jugador (cerrar puertas, música, barra de vida...).")]
    public UnityEvent onBossEngaged;
    [Tooltip("Al empezar una nueva fase. Recibe el número de fase (2, 3...).")]
    public UnityEvent<int> onPhaseChanged;
    [Tooltip("Cuando empieza a cargar el burst de energía.")]
    public UnityEvent onBurstStarted;
    [Tooltip("En el instante de la explosión del burst (screen shake, sonido...).")]
    public UnityEvent onEnergyBurst;
    [Tooltip("Cuando un golpe del jugador es bloqueado por invulnerabilidad (sonido de 'clink').")]
    public UnityEvent onDamageBlocked;

    // ───────────────────────────── Estado interno ─────────────────────────────
    private int currentPhaseIndex = 0;
    private int targetPhaseIndex = 0;

    private AttackType currentAttack = AttackType.None;
    private AttackType lastAttack = AttackType.None;
    private AttackPhase attackPhase = AttackPhase.Telegraph;
    private float phaseTimer;

    private Vector2 dashDirection;
    private Vector2 dashStartPosition;
    private float dashTargetDistance;
    private int dashesRemaining;
    private float recoilTimer;

    private bool hasEngaged;
    private bool isTeleporting;
    private bool isSpawning;

    // Movimiento / pathing
    private float jumpCooldownTimer;
    private float jumpPlanTimer;
    private bool lastPlanFailed;
    private bool jumpCommitted;
    private float committedVx;
    private float jumpLaunchTime;
    private float teleportCooldownTimer;
    private float unreachableTimer;
    private float stuckTimer;
    private float progressTimer;
    private Vector2 progressCheckPos;
    private bool isAirborne;
    private float insideWallTimer;
    private float insidePlatformTimer;
    private float dashElapsed;
    private float dashBlockedTimer;
    private Vector2 dashLastPos;
    private static readonly Collider2D[] solidBuffer = new Collider2D[16];
    private static readonly RaycastHit2D[] castBuffer = new RaycastHit2D[16];

    // Burst / invulnerabilidad
    private readonly List<HitRecord> recentHits = new List<HitRecord>();
    private bool burstQueued;
    private float nextBurstAllowedTime;
    private float invulnerableUntil;
    private bool burstSpriteActive;

    // Visual
    private Sprite actionSprite;
    private Color currentActionColor = Color.white;
    private float hurtTimer;
    private bool wasPulsing;

    // Debug
    private Vector2 lastPlanLanding;
    private float lastPlanTime = -999f;

    private Collider2D bodyCollider;
    private readonly List<GameObject> aliveMinions = new List<GameObject>();

    private BossPhaseSettings CurrentPhase => phases[Mathf.Clamp(currentPhaseIndex, 0, phases.Length - 1)];
    private bool PhaseTransitionQueued => currentPhaseIndex < targetPhaseIndex;
    private bool IsDashingNow => isAttacking && attackPhase == AttackPhase.Dashing;

    /// <summary>True mientras el jefe no puede recibir daño (burst o transición de fase).</summary>
    public bool IsInvulnerable =>
        Time.time < invulnerableUntil ||
        (invulnerableDuringTransition && isAttacking && attackPhase == AttackPhase.Transition);

    // ───────────────────────────── Para efectos (BossVisuals) y la arena (BossArena) ─────────────────────────────
    public bool IsTelegraphing => isAttacking && (attackPhase == AttackPhase.Telegraph || attackPhase == AttackPhase.Reappear);
    public bool IsDashing => IsDashingNow;
    public bool IsAirborneNow => isAirborne;
    public bool IsChargingBurst => isAttacking && attackPhase == AttackPhase.BurstCharge;
    public bool IsInPhaseTransition => isAttacking && attackPhase == AttackPhase.Transition;
    public float BurstRadius => burstRadius;
    public float BurstWindup => burstWindup;
    public Color BurstColor => burstColor;
    public int PhaseNumber => currentPhaseIndex + 1;
    public int PhaseCount => phases != null ? phases.Length : 1;
    /// <summary>Cada golpe que si le hace dano.</summary>
    public event System.Action Hurt;
    /// <summary>Al desaparecer (posicion de donde se fue) y al reaparecer (a donde llego).</summary>
    public event System.Action<Vector3> TeleportedFrom, TeleportedTo;

    /// <summary>Cambia el SpriteRenderer que usa el jefe (BossVisuals lo pasa a un objeto hijo para animarlo).</summary>
    public void SetVisualRenderer(SpriteRenderer renderer) { if (renderer != null) spriteRenderer = renderer; }

    /// <summary>Limites de la arena: el jefe solo se teletransporta dentro de ellos (BossArena lo asigna).</summary>
    public void SetArenaBounds(Collider2D bounds)
    {
        arenaBounds = bounds;
        if (!hasArenaHome) { arenaHome = transform.position; hasArenaHome = true; }
    }

    private Vector2 arenaHome;
    private bool hasArenaHome;

    private bool InsideArena(Vector2 p)
    {
        if (arenaBounds == null) return true;
        Bounds a = arenaBounds.bounds;
        return p.x > a.min.x && p.x < a.max.x && p.y > a.min.y && p.y < a.max.y;
    }

    // Never leaves the boss room: if it ends up outside (pushed through a wall, chased through a door...), it teleports back
    private void CheckOutsideArena()
    {
        if (arenaBounds == null || isTeleporting || isDead || rb == null || !hasArenaHome) return;
        if (InsideArena(transform.position)) return;
        jumpCommitted = false;
        float side = player != null ? -SignNonZero(player.position.x - arenaHome.x) : 1f;
        StartCoroutine(TeleportRoutine(side, arenaHome, null));
    }

    /// <summary>
    /// Deja al jefe como al principio: vida llena, fase 1, sin subditos, quieto en 'position'.
    /// La arena lo usa cuando el jugador muere y reaparece afuera.
    /// </summary>
    public void ResetBoss(Vector3 position)
    {
        if (isDead) return;
        StopAllCoroutines();
        isTeleporting = false;
        isSpawning = false;
        burstQueued = false;
        recentHits.Clear();
        invulnerableUntil = 0f;
        burstSpriteActive = false;
        currentAttack = AttackType.None;
        lastAttack = AttackType.None;
        isAttacking = false;
        attackPhase = AttackPhase.Telegraph;
        jumpCommitted = false;
        hasEngaged = false;
        if (dashHitboxCollider != null) dashHitboxCollider.enabled = false;

        foreach (GameObject m in aliveMinions.ToArray()) if (m != null) Destroy(m);
        aliveMinions.Clear();

        targetPhaseIndex = 0;
        ApplyPhase(0);
        Heal(GetMaxHealth());
        ClearStunned();
        CancelKnockback();

        transform.position = position;
        arenaHome = position; hasArenaHome = true;
        if (rb != null) { rb.simulated = true; rb.position = position; rb.linearVelocity = Vector2.zero; }
        if (spriteRenderer != null) spriteRenderer.enabled = true;
        attackTimer = CurrentPhase.attackCooldown;
        ResetPathingMemory();
        ChangeState(ChaserState.Patrol);
    }

    // ───────────────────────────── Inicialización ─────────────────────────────
    protected override void Start()
    {
        base.Start();

        if (phases == null || phases.Length == 0)
        {
            Debug.LogWarning($"{name}: no hay fases configuradas, se usa una por defecto.");
            phases = new BossPhaseSettings[] { new BossPhaseSettings() };
        }

        // El jefe NUNCA suelta al jugador una vez que lo detecta.
        loseTargetDistance = float.MaxValue;
        loseLineOfSightDelay = float.MaxValue;

        if (groundLayer.value == 0) groundLayer = obstacleLayer;

        foreach (Collider2D c in GetComponents<Collider2D>())
        {
            if (!c.isTrigger) { bodyCollider = c; break; }
        }
        if (bodyCollider == null)
        {
            Debug.LogWarning($"{name}: no se encontró un Collider2D sólido; los chequeos de suelo/pared no funcionarán.");
        }
        else if (useFrictionlessMaterial)
        {
            // La fricción contra paredes es la causa #1 de que un enemigo se "pegue" al saltar.
            PhysicsMaterial2D mat = new PhysicsMaterial2D("BossFrictionless") { friction = 0f, bounciness = 0f };
            bodyCollider.sharedMaterial = mat;
        }

        if (obstacleCheck == null)
        {
            GameObject obj = new GameObject("ObstacleCheck");
            obj.transform.parent = transform;
            obj.transform.localPosition = new Vector3(0, 0.3f, 0);
            obstacleCheck = obj.transform;
        }

        if (dashHitboxCollider == null)
        {
            BoxCollider2D box = gameObject.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.size = dashHitboxSize;
            dashHitboxCollider = box;
        }
        dashHitboxCollider.enabled = false;

        ApplyPhase(0);
        ResetPathingMemory();
    }

    protected override void InitializeEnemy()
    {
        base.InitializeEnemy();
        if (logBehaviorDetails)
        {
            Debug.Log($"Boss {gameObject.name} inicializado con {GetMaxHealth()} HP");
        }
    }

    /// <summary>Hace que el jefe empiece a perseguir (ej. al entrar a la arena, cutscene).</summary>
    public void ForceEngage()
    {
        if (player != null) ChangeState(ChaserState.Chase);
    }

    // ───────────────────────────── Overrides de EnemyBase ─────────────────────────────
    public override void TakeDamage(int amount, Vector2 damageSourcePosition = default)
    {
        if (IsInvulnerable && !isDead)
        {
            onDamageBlocked?.Invoke();
            return;
        }

        // No skipping phases with one big hit: the health stops at the threshold of the next phase,
        // and the phase change (invulnerable) has to happen before it can go lower.
        if (!isDead && phaseHealthThresholds != null && currentPhaseIndex < phaseHealthThresholds.Length && currentPhaseIndex < phases.Length - 1)
        {
            int floor = Mathf.FloorToInt(phaseHealthThresholds[currentPhaseIndex] * GetMaxHealth() + 0.001f); // at or below the threshold, so the phase change does trigger
            int allowed = GetCurrentHealth() - floor;
            if (allowed <= 0)
            {
                onDamageBlocked?.Invoke();   // already at the threshold, waiting for the phase change
                return;
            }
            amount = Mathf.Min(amount, allowed);
        }
        base.TakeDamage(amount, damageSourcePosition);
    }

    public override bool CanBeCaptured()
    {
        return !captureImmune && base.CanBeCaptured();
    }

    public override void AddStunned(float amount)
    {
        if (IsInvulnerable) return;
        base.AddStunned(amount * (1f - Mathf.Clamp01(stunResistance)));
    }

    protected override void ApplyKnockback(int damageAmount, Vector2 damageSource)
    {
        if (knockbackMultiplier <= 0f) return;

        // No interrumpir ataques en curso con un empujón
        if (isAttacking && (attackPhase == AttackPhase.Dashing || attackPhase == AttackPhase.Summoning ||
                            attackPhase == AttackPhase.Transition || attackPhase == AttackPhase.BurstCharge ||
                            attackPhase == AttackPhase.BurstRelease))
            return;

        // La distancia de knockback es proporcional al daño: escalar el daño escala el empujón.
        int scaled = Mathf.RoundToInt(damageAmount * knockbackMultiplier);
        if (scaled <= 0) return; // sin empuje = sin "congelar" la IA durante el knockback

        base.ApplyKnockback(scaled, damageSource);
    }

    protected override void TryDealContactDamage(GameObject other)
    {
        if (IsDashingNow) return; // el hitbox de la carga ya se encarga del golpe
        base.TryDealContactDamage(other);
    }

    // ───────────────────────────── Timers activos ─────────────────────────────
    protected override void OnActiveUpdate()
    {
        if (jumpCooldownTimer > 0) jumpCooldownTimer -= Time.deltaTime;
        if (jumpPlanTimer > 0) jumpPlanTimer -= Time.deltaTime;
        if (teleportCooldownTimer > 0) teleportCooldownTimer -= Time.deltaTime;

        isAirborne = !IsGrounded();
        CheckInsideWall();
        CheckOutsideArena();

        // Parpadeo mientras es invencible por el burst
        bool burstInvulnerable = Time.time < invulnerableUntil;
        if (burstInvulnerable && spriteRenderer != null)
        {
            Color baseColor = isAttacking ? currentActionColor : CurrentPhase.tint;
            float k = 0.5f + 0.5f * Mathf.Sin(Time.time * 20f);
            spriteRenderer.color = Color.Lerp(baseColor, burstColor, k);
            wasPulsing = true;
        }
        else if (wasPulsing)
        {
            wasPulsing = false;
            UpdateVisualFeedback(isAttacking ? currentActionColor : CurrentPhase.tint);
        }
    }

    // ───────────────────────────── Patrulla / Espera ─────────────────────────────
    protected override void PatrolBehavior()
    {
        FreezeMovement();
    }

    protected override void WaitBehavior()
    {
        FreezeMovement();
        TickWaitTimer();
    }

    // ───────────────────────────── Perseguir ─────────────────────────────
    protected override void ChaseBehavior()
    {
        if (isTeleporting || player == null)
        {
            FreezeMovement();
            return;
        }

        // Doesn't follow the player out of its room (nor before the player walks in): it waits inside
        if (arenaBounds != null && !InsideArena(player.position))
        {
            FreezeMovement();
            return;
        }

        if (!hasEngaged)
        {
            hasEngaged = true;
            onBossEngaged?.Invoke();
        }

        // 1) Cambio de fase pendiente (prioridad máxima)
        if (PhaseTransitionQueued)
        {
            currentAttack = AttackType.PhaseTransition;
            ChangeState(ChaserState.Attack);
            return;
        }

        // 2) Reacción a golpes rápidos
        if (burstQueued)
        {
            currentAttack = AttackType.Burst;
            ChangeState(ChaserState.Attack);
            return;
        }

        // 3) ¿Toca atacar?
        if (attackTimer <= 0f)
        {
            if (currentAttack == AttackType.None) currentAttack = PickAttack();

            float dist = Vector2.Distance(transform.position, player.position);
            if (currentAttack != AttackType.Dash || dist <= attackRange)
            {
                ChangeState(ChaserState.Attack);
                return;
            }
        }

        // 4) Moverse
        HandleChaseMovement();
    }

    private void HandleChaseMovement()
    {
        Vector2 pos = transform.position;
        float dx = player.position.x - pos.x;
        float dy = player.position.y - pos.y;
        float dir = Mathf.Abs(dx) > stopDistance ? Mathf.Sign(dx) : 0f;

        if (dir != 0f) UpdateSpriteFlip(dir);

        float speed = CurrentPhase.chaseSpeed;
        bool grounded = IsGrounded();

        // Fin de un salto planeado
        if (jumpCommitted && grounded && Time.time > jumpLaunchTime + 0.12f && rb.linearVelocity.y <= 0.1f)
        {
            jumpCommitted = false;
            ResetPathingMemory();
        }

        // En pleno salto planeado: mantiene exactamente la trayectoria que simuló
        if (!grounded && jumpCommitted)
        {
            rb.linearVelocity = new Vector2(committedVx, rb.linearVelocity.y);
            return;
        }

        bool wallAhead = false, gapAhead = false;
        if (dir != 0f) ProbeTerrain(dir, out wallAhead, out gapAhead);

        bool playerAbove = dy > jumpHeightThreshold && Mathf.Abs(dx) <= jumpTriggerHorizontalDistance;
        bool dropAllowed = dy < -dropDownThreshold;
        // Hueco adelante y el jugador NO está abajo: no camina al vacío, se queda en el borde
        bool ledgeHold = grounded && dir != 0f && gapAhead && !dropAllowed;
        bool wantsJump = grounded && ((dir != 0f && (wallAhead || ledgeHold)) || playerAbove);

        bool jumped = false;
        bool jumpBlocked = false;

        if (wantsJump)
        {
            if (!useJumping || dy > maxJumpableHeight)
            {
                jumpBlocked = true;
            }
            else if (jumpCooldownTimer <= 0f)
            {
                if (jumpPlanTimer <= 0f)
                {
                    jumpPlanTimer = jumpPlanInterval;
                    lastPlanFailed = !TryPlanJump(dir, speed, out Vector2 launch);
                    if (!lastPlanFailed)
                    {
                        LaunchJump(launch);
                        jumped = true;
                    }
                }
                if (!jumped && lastPlanFailed) jumpBlocked = true;
            }
        }
        else
        {
            lastPlanFailed = false;
        }

        if (!jumped)
        {
            float moveDir = ledgeHold ? 0f : dir;
            Vector2 vel = rb.linearVelocity;
            float targetVx = moveDir * speed;
            vel.x = grounded ? targetVx : Mathf.MoveTowards(vel.x, targetVx, airAcceleration * Time.deltaTime);
            rb.linearVelocity = vel;
        }

        // ── Anti-atasco ──
        if (grounded && dir != 0f && !jumped && Mathf.Abs(rb.linearVelocity.x) < stuckSpeedThreshold)
            stuckTimer += Time.deltaTime;
        else
            stuckTimer = 0f;
        bool stuck = stuckTimer > stuckTime;

        // Atascado sin causa conocida: un saltito suele destrabarlo
        if (stuck && grounded && !jumpBlocked && !ledgeHold && useJumping && jumpCooldownTimer <= 0f)
        {
            rb.linearVelocity = new Vector2(dir * speed, jumpVelocity * unstickHopFactor);
            jumpCooldownTimer = CurrentPhase.jumpCooldown;
            stuckTimer = 0f;
        }

        // ¿Se movió algo en los últimos X segundos? (detecta rebotes en el sitio, saltos inútiles, etc.)
        progressTimer += Time.deltaTime;
        bool noProgress = false;
        if (progressTimer >= noProgressTime)
        {
            float moved = Vector2.Distance(pos, progressCheckPos);
            noProgress = dir != 0f && moved < noProgressMinMove;
            progressCheckPos = pos;
            progressTimer = 0f;
        }

        // ── Teletransporte si no puede llegar ──
        if (useTeleportFallback)
        {
            bool tooFar = Mathf.Abs(dx) > teleportDistance;
            bool tooVertical = Mathf.Abs(dy) > teleportVerticalDifference;

            if (tooFar || tooVertical || jumpBlocked || stuck)
                unreachableTimer += Time.deltaTime;
            else
                unreachableTimer = Mathf.Max(0f, unreachableTimer - Time.deltaTime * 2f);

            if (noProgress) unreachableTimer = teleportAfterUnreachableTime;

            if (unreachableTimer >= teleportAfterUnreachableTime && teleportCooldownTimer <= 0f)
            {
                unreachableTimer = 0f;
                stuckTimer = 0f;
                float side = -SignNonZero(dx); // lado del jugador donde ya está
                StartTeleport(side, null);
            }
        }
    }

    // Safety net: if the body ends up inside a wall (a dash into a corner, a bad landing...), teleport out of it
    private void CheckInsideWall()
    {
        if (bodyCollider == null || isTeleporting || isDead || obstacleLayer.value == 0) { insideWallTimer = 0f; insidePlatformTimer = 0f; return; }
        Bounds b = bodyCollider.bounds;

        // Solid wall: teleport to a free spot
        bool inside = OverlapSolidBox(b.center, b.size * 0.6f, obstacleLayer);
        insideWallTimer = inside ? insideWallTimer + Time.deltaTime : 0f;
        if (insideWallTimer >= insideWallTeleportTime)
        {
            insideWallTimer = 0f;
            float side = player != null ? -SignNonZero(player.position.x - transform.position.x) : 1f;
            StartTeleport(side, null);
            return;
        }

        // One-way platform: passing through is fine, but sitting inside one (no vertical motion) means it got wedged
        bool inPlatform = OverlapPlatformBox(b.center, new Vector2(b.size.x * 0.6f, b.size.y * 0.8f), groundLayer, out Collider2D platform);
        bool still = rb == null || Mathf.Abs(rb.linearVelocity.y) < 0.5f;
        insidePlatformTimer = inPlatform && still ? insidePlatformTimer + Time.deltaTime : 0f;
        if (insidePlatformTimer >= 0.3f)
        {
            insidePlatformTimer = 0f;
            PopOnTopOf(platform, b);
        }
    }

    // Lifts the boss so its feet rest on the platform's top surface (teleports if there is no room up there)
    private void PopOnTopOf(Collider2D platform, Bounds b)
    {
        // Local top surface of the platform under the body (a tilemap's bounds cover ALL its platforms)
        RaycastHit2D hit = Physics2D.Raycast(new Vector2(b.center.x, b.max.y + 0.1f), Vector2.down, b.size.y + 0.2f, groundLayer);
        bool found = hit.collider == platform && hit.distance > 0f;
        float top = hit.point.y;

        Vector2 newCenter = new Vector2(b.center.x, top + b.extents.y + 0.03f);
        if (!found || OverlapSolidBox(newCenter, b.size * 0.9f, obstacleLayer) || OverlapPlatformBox(newCenter, b.size * 0.9f, groundLayer, out _))
        {
            float side = player != null ? -SignNonZero(player.position.x - transform.position.x) : 1f;
            StartTeleport(side, null);
            return;
        }

        Vector2 delta = newCenter - (Vector2)b.center;
        transform.position += (Vector3)delta;
        if (rb != null)
        {
            rb.position += delta;
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);
        }
        jumpCommitted = false;
    }

    // True if the feet start above the platform surface (a ray started inside a collider reports distance 0)
    private bool FeetAbovePlatform(Bounds b, Collider2D platform)
    {
        RaycastHit2D h = Physics2D.Raycast(new Vector2(b.center.x, b.min.y + 0.1f), Vector2.down, 0.3f, groundLayer);
        return h.collider == platform && h.distance > 0f;
    }

    // Physics helpers: one-way platforms share the Ground layer with walls, so they are told apart by their effector
    private bool IsOwnCollider(Collider2D c) => c.attachedRigidbody != null && c.attachedRigidbody == rb;

    private bool OverlapSolidBox(Vector2 center, Vector2 size, int mask)
    {
        int n = Physics2D.OverlapBoxNonAlloc(center, size, 0f, solidBuffer, mask);
        for (int i = 0; i < n; i++)
        {
            Collider2D c = solidBuffer[i];
            if (!c.isTrigger && !c.usedByEffector && !IsOwnCollider(c)) return true;
        }
        return false;
    }

    private bool OverlapSolidCircle(Vector2 center, float radius, int mask)
    {
        int n = Physics2D.OverlapCircleNonAlloc(center, radius, solidBuffer, mask);
        for (int i = 0; i < n; i++)
        {
            Collider2D c = solidBuffer[i];
            if (!c.isTrigger && !c.usedByEffector && !IsOwnCollider(c)) return true;
        }
        return false;
    }

    private bool OverlapPlatformBox(Vector2 center, Vector2 size, int mask, out Collider2D platform)
    {
        platform = null;
        int n = Physics2D.OverlapBoxNonAlloc(center, size, 0f, solidBuffer, mask);
        for (int i = 0; i < n; i++)
        {
            Collider2D c = solidBuffer[i];
            if (!c.isTrigger && c.usedByEffector && !IsOwnCollider(c)) { platform = c; return true; }
        }
        return false;
    }

    // BoxCast that skips one-way platforms unless allowed; a platform only counts if the box starts outside it (lands on top)
    private RaycastHit2D CastBody(Vector2 pos, Vector2 size, Vector2 dir, float dist, int mask, bool allowPlatforms)
    {
        int n = Physics2D.BoxCastNonAlloc(pos, size, 0f, dir, castBuffer, dist, mask);
        RaycastHit2D best = default;
        float bestDist = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            RaycastHit2D h = castBuffer[i];
            Collider2D c = h.collider;
            if (c == null || c.isTrigger || IsOwnCollider(c)) continue;
            if (c.usedByEffector && (!allowPlatforms || h.distance <= 0f)) continue;
            if (h.distance < bestDist) { bestDist = h.distance; best = h; }
        }
        return best;
    }

    private void ResetPathingMemory()
    {
        stuckTimer = 0f;
        unreachableTimer = 0f;
        progressTimer = 0f;
        progressCheckPos = transform.position;
    }

    private static float SignNonZero(float v) => v >= 0f ? 1f : -1f;

    private bool IsGrounded()
    {
        if (bodyCollider == null) return true;
        Bounds b = bodyCollider.bounds;
        Vector2 p = new Vector2(b.center.x, b.min.y - 0.02f);
        bool rising = rb != null && rb.linearVelocity.y > 0.1f;
        int n = Physics2D.OverlapCircleNonAlloc(p, groundCheckRadius, solidBuffer, groundLayer);
        for (int i = 0; i < n; i++)
        {
            Collider2D c = solidBuffer[i];
            if (c.isTrigger || IsOwnCollider(c)) continue;
            if (c.usedByEffector && (rising || !FeetAbovePlatform(b, c))) continue; // passing through it, not standing on it
            return true;
        }
        return false;
    }

    private void ProbeTerrain(float facing, out bool wallAhead, out bool gapAhead)
    {
        wallAhead = false;
        gapAhead = false;
        if (bodyCollider == null) return;

        Bounds b = bodyCollider.bounds;

        Vector2 wallPos = new Vector2(b.center.x + facing * (b.extents.x + wallCheckDistance), b.center.y + wallCheckHeightOffset);
        wallAhead = OverlapSolidCircle(wallPos, wallCheckRadius, obstacleLayer);

        Vector2 ledgePos = new Vector2(b.center.x + facing * (b.extents.x + ledgeCheckForward), b.min.y - ledgeCheckDepth);
        gapAhead = Physics2D.OverlapCircle(ledgePos, ledgeCheckRadius, groundLayer) == null;
    }

    // ───────────────────────────── Planificador de saltos ─────────────────────────────
    private void LaunchJump(Vector2 velocity)
    {
        rb.linearVelocity = velocity;
        committedVx = velocity.x;
        jumpCommitted = true;
        jumpLaunchTime = Time.time;
        jumpCooldownTimer = CurrentPhase.jumpCooldown;
        stuckTimer = 0f;
    }

    /// <summary>
    /// Simula varias trayectorias de salto (velocidad vertical x velocidad horizontal) contra el
    /// escenario y elige la que aterriza más cerca del jugador. Si ninguna mejora la posición
    /// actual lo suficiente, no salta (evita golpearse contra techos/paredes o caer al vacío).
    /// </summary>
    private bool TryPlanJump(float dir, float speed, out Vector2 launch)
    {
        launch = Vector2.zero;
        if (bodyCollider == null || rb == null || player == null) return false;

        float g = Mathf.Abs(Physics2D.gravity.y * rb.gravityScale);
        if (g < 0.01f) return false;

        Bounds b = bodyCollider.bounds;
        Vector2 start = b.center;
        Vector2 size = (Vector2)b.size * jumpPlanBodyShrink;
        Vector2 target = player.position;

        float bestScore = Vector2.Distance(start, target) - jumpPlanMinImprovement;
        bool found = false;

        int horizMask = obstacleLayer.value | groundLayer.value; // one-way platforms are filtered in CastBody
        int landMask = obstacleLayer.value | groundLayer.value;

        float[] velocities = { jumpVelocity, highJumpVelocity };
        float[] speedFractions = { 1f, 0.5f, 0f };
        float[] dirs = dir != 0f ? new float[] { dir } : new float[] { 1f, -1f };

        foreach (float d in dirs)
        {
            foreach (float v in velocities)
            {
                foreach (float f in speedFractions)
                {
                    Vector2 v0 = new Vector2(d * speed * f, v);
                    if (!SimulateJump(start, size, v0, g, horizMask, landMask, out Vector2 landing)) continue;

                    float score = Vector2.Distance(landing, target);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        launch = v0;
                        lastPlanLanding = landing;
                        found = true;
                    }
                }
            }
        }

        if (found) lastPlanTime = Time.time;
        return found;
    }

    private bool SimulateJump(Vector2 start, Vector2 size, Vector2 v0, float g, int horizMask, int landMask, out Vector2 landing)
    {
        landing = start;
        float dt = Mathf.Max(0.02f, jumpPlanStep);
        int maxSteps = Mathf.CeilToInt(jumpPlanMaxTime / dt);

        Vector2 pos = start;
        float vy = v0.y;

        for (int i = 0; i < maxSteps; i++)
        {
            vy -= g * dt;

            // Horizontal: si choca con una pared se queda pegado y sigue subiendo/cayendo (desliza)
            float mx = v0.x * dt;
            if (Mathf.Abs(mx) > 0.0001f)
            {
                Vector2 hdir = new Vector2(Mathf.Sign(mx), 0f);
                RaycastHit2D hh = CastBody(pos, size, hdir, Mathf.Abs(mx), horizMask, !oneWayPlatforms);
                if (hh.collider != null) pos.x += hdir.x * Mathf.Max(0f, hh.distance);
                else pos.x += mx;
            }

            // Vertical
            float my = vy * dt;
            if (Mathf.Abs(my) > 0.0001f)
            {
                Vector2 vdir = new Vector2(0f, Mathf.Sign(my));
                int mask = my > 0f ? horizMask : landMask;
                RaycastHit2D hv = CastBody(pos, size, vdir, Mathf.Abs(my), mask, !oneWayPlatforms || my < 0f);
                if (hv.collider != null)
                {
                    if (my > 0f) return false;                       // se golpea la cabeza
                    if (hv.normal.y >= 0.5f)                         // aterriza sobre una superficie
                    {
                        landing = pos + vdir * hv.distance;
                        return true;
                    }
                    return false;
                }
                pos.y += my;
            }

            if (pos.y < start.y - jumpPlanMaxDrop) return false;    // cae al vacío
        }

        return false; // no aterrizó a tiempo
    }

    // ───────────────────────────── Ataques ─────────────────────────────
    protected override void AttackBehavior()
    {
        if (isTeleporting)
        {
            FreezeMovement();
            return;
        }

        if (!isAttacking)
        {
            BeginAttack();
        }
        else if (burstQueued && CanBurstInterrupt())
        {
            // Si le pegan mientras avisa un ataque o se recupera, reacciona con el burst
            BeginBurst();
        }

        switch (attackPhase)
        {
            case AttackPhase.Telegraph: UpdateTelegraph(); break;
            case AttackPhase.Reappear: UpdateReappear(); break;
            case AttackPhase.Dashing: UpdateDash(); break;
            case AttackPhase.Summoning: UpdateSummon(); break;
            case AttackPhase.Transition: UpdateTransition(); break;
            case AttackPhase.BurstCharge: UpdateBurstCharge(); break;
            case AttackPhase.BurstRelease: UpdateBurstRelease(); break;
            case AttackPhase.Recovering: UpdateRecovery(); break;
        }
    }

    private bool CanBurstInterrupt()
    {
        return attackPhase == AttackPhase.Telegraph ||
               attackPhase == AttackPhase.Reappear ||
               attackPhase == AttackPhase.Recovering;
    }

    private AttackType PickAttack()
    {
        BossPhaseSettings p = CurrentPhase;
        bool canSummon = minionPrefabs != null && minionPrefabs.Length > 0 && CountAliveMinions() < p.maxAliveMinions;

        float wDash = p.dashWeight;
        float wSummon = canSummon ? p.summonWeight : 0f;
        float wStrike = p.teleportStrikeWeight;

        if (lastAttack == AttackType.Dash) wDash *= 0.35f;
        if (lastAttack == AttackType.Summon) wSummon *= 0.35f;
        if (lastAttack == AttackType.TeleportStrike) wStrike *= 0.35f;

        float total = wDash + wSummon + wStrike;
        if (total <= 0f) return AttackType.Dash;

        float roll = Random.value * total;
        if (roll < wDash) return AttackType.Dash;
        roll -= wDash;
        if (roll < wSummon) return AttackType.Summon;
        return AttackType.TeleportStrike;
    }

    private void BeginAttack()
    {
        isAttacking = true;
        jumpCommitted = false;
        FreezeMovement();

        if (PhaseTransitionQueued) currentAttack = AttackType.PhaseTransition;
        else if (burstQueued) currentAttack = AttackType.Burst;
        else if (currentAttack == AttackType.None) currentAttack = PickAttack();

        dashesRemaining = CurrentPhase.dashesPerAttack;
        FaceTarget();
        OnAttackStartedCustom();

        switch (currentAttack)
        {
            case AttackType.PhaseTransition:
                BeginTransition();
                break;
            case AttackType.Burst:
                BeginBurst();
                break;
            case AttackType.Summon:
                BeginTelegraph(CurrentPhase.summonWindup, SpriteOrIdle(CurrentPhase.summonSprite), summonColor);
                break;
            default: // Dash y TeleportStrike
                BeginTelegraph(CurrentPhase.telegraphTime, SpriteOrIdle(CurrentPhase.telegraphSprite), telegraphColor);
                break;
        }
    }

    private void BeginTelegraph(float duration, Sprite sprite, Color color)
    {
        attackPhase = AttackPhase.Telegraph;
        phaseTimer = duration;
        actionSprite = sprite;
        SetColor(color);
    }

    private void UpdateTelegraph()
    {
        FreezeMovement();
        FaceTarget();

        phaseTimer -= Time.deltaTime;
        if (phaseTimer > 0f) return;

        switch (currentAttack)
        {
            case AttackType.Dash:
                StartDash();
                break;

            case AttackType.Summon:
                StartSummon(CurrentPhase.minionsPerSummon);
                break;

            case AttackType.TeleportStrike:
                {
                    if (player == null) { StartDash(); break; }
                    float side = SignNonZero(player.position.x - transform.position.x);
                    StartTeleport(side, success =>
                    {
                        if (success) BeginReappear();
                        else StartDash();
                    });
                    break;
                }
        }
    }

    // ── Teleport Strike: pausa tras reaparecer ──
    private void BeginReappear()
    {
        attackPhase = AttackPhase.Reappear;
        phaseTimer = postTeleportDelay;
        actionSprite = SpriteOrIdle(CurrentPhase.telegraphSprite);
        SetColor(telegraphColor);
        FaceTarget();
    }

    private void UpdateReappear()
    {
        FreezeMovement();
        FaceTarget();
        phaseTimer -= Time.deltaTime;
        if (phaseTimer <= 0f) StartDash();
    }

    // ── Dash ──
    private void StartDash()
    {
        attackPhase = AttackPhase.Dashing;
        dashesRemaining--;

        actionSprite = SpriteOrIdle(CurrentPhase.dashSprite);
        SetColor(dashColor);

        if (dashHitboxCollider != null) dashHitboxCollider.enabled = true;

        Vector2 direction = player != null ? (Vector2)(player.position - transform.position) : lastMoveDirection;
        direction.y = 0;
        if (direction.sqrMagnitude < 0.0001f) direction = lastMoveDirection;
        dashDirection = direction.normalized;

        UpdateSpriteFlip(dashDirection.x);
        lastMoveDirection = dashDirection;

        dashStartPosition = rb != null ? rb.position : (Vector2)transform.position;
        float distanceToPlayer = player != null ? Mathf.Abs(player.position.x - transform.position.x) : 0f;
        dashTargetDistance = distanceToPlayer + dashOvershootBuffer;
        dashElapsed = 0f;
        dashBlockedTimer = 0f;
        dashLastPos = dashStartPosition;
    }

    private void UpdateDash()
    {
        float speed = CurrentPhase.dashSpeed;
        if (rb != null)
        {
            rb.linearVelocity = new Vector2(dashDirection.x * speed, rb.linearVelocity.y);
        }
        else
        {
            transform.position += (Vector3)dashDirection * speed * Time.deltaTime;
        }

        if (CheckObstacleAhead())
        {
            EndDash(false);
            return;
        }

        Vector2 currentPosition = rb != null ? rb.position : (Vector2)transform.position;

        // Watchdog: a charge that is blocked by anything (wall, player, corner) or runs too long always ends
        dashElapsed += Time.deltaTime;
        float stepMoved = Mathf.Abs(currentPosition.x - dashLastPos.x);
        dashLastPos = currentPosition;
        dashBlockedTimer = stepMoved < speed * Time.deltaTime * 0.25f ? dashBlockedTimer + Time.deltaTime : 0f;
        float maxDashTime = dashTargetDistance / Mathf.Max(1f, speed) + 0.5f;
        if (dashBlockedTimer > 0.15f || dashElapsed > maxDashTime)
        {
            EndDash(false);
            return;
        }

        if (Vector2.Distance(dashStartPosition, currentPosition) >= dashTargetDistance)
        {
            EndDash(false);
        }
    }

    private bool CheckObstacleAhead()
    {
        // Checks just in front of the body (the boss is big: a point near its center would be inside itself)
        Vector2 checkPosition;
        if (bodyCollider != null)
        {
            Bounds b = bodyCollider.bounds;
            checkPosition = new Vector2(b.center.x + dashDirection.x * (b.extents.x + obstacleCheckDistance * 0.5f), b.center.y);
        }
        else if (obstacleCheck != null)
            checkPosition = (Vector2)obstacleCheck.position + dashDirection * obstacleCheckDistance;
        else return false;
        return OverlapSolidCircle(checkPosition, obstacleCheckRadius, obstacleLayer);
    }

    private void OnTriggerEnter2D(Collider2D other) => TryDashHit(other);

    // Stay too: if the player is already touching the boss when the dash starts, Enter never fires
    private void OnTriggerStay2D(Collider2D other) => TryDashHit(other);

    private void TryDashHit(Collider2D other)
    {
        if (!IsDashingNow) return;
        // The boss has other triggers (detection area...): only count real contact with the dash hitbox
        if (dashHitboxCollider != null && !dashHitboxCollider.IsTouching(other)) return;
        if (currentAttack != AttackType.Dash && currentAttack != AttackType.TeleportStrike) return;
        if (((1 << other.gameObject.layer) & playerLayer) == 0) return;

        IDamageable damageable = other.GetComponent<IDamageable>();
        if (damageable != null)
        {
            int dmg = Mathf.Max(1, Mathf.RoundToInt(attackDamage * CurrentPhase.damageMultiplier));
            damageable.TakeDamage(dmg, transform.position);
        }

        OnAttackExecutedCustom();
        EndDash(true);
    }

    private void EndDash(bool hitPlayer)
    {
        if (dashHitboxCollider != null) dashHitboxCollider.enabled = false;

        if (rb != null) rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);

        // Encadenar otra carga si la anterior no conectó
        if (!hitPlayer && dashesRemaining > 0 && player != null)
        {
            BeginTelegraph(CurrentPhase.chainedTelegraphTime, SpriteOrIdle(CurrentPhase.telegraphSprite), telegraphColor);
            return;
        }

        recoilTimer = hitPlayer ? recoilDuration : 0f;
        BeginRecovery();
    }

    // ── Invocación ──
    private void StartSummon(int count)
    {
        attackPhase = AttackPhase.Summoning;
        actionSprite = SpriteOrIdle(CurrentPhase.summonSprite);
        SetColor(summonColor);
        StartCoroutine(SpawnMinionsRoutine(count));
    }

    private void UpdateSummon()
    {
        FreezeMovement();
        if (!isSpawning)
        {
            recoilTimer = 0f;
            BeginRecovery();
        }
    }

    private IEnumerator SpawnMinionsRoutine(int count)
    {
        isSpawning = true;

        CleanMinionList();
        int available = Mathf.Max(0, CurrentPhase.maxAliveMinions - aliveMinions.Count);
        count = Mathf.Min(count, available);

        for (int i = 0; i < count; i++)
        {
            SpawnOneMinion();
            if (minionSpawnStagger > 0f) yield return new WaitForSeconds(minionSpawnStagger);
        }

        isSpawning = false;
    }

    private void SpawnOneMinion()
    {
        if (minionPrefabs == null || minionPrefabs.Length == 0) return;
        GameObject prefab = minionPrefabs[Random.Range(0, minionPrefabs.Length)];
        if (prefab == null) return;

        Vector2 pos;
        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            Transform t = spawnPoints[Random.Range(0, spawnPoints.Length)];
            pos = t != null ? (Vector2)t.position : (Vector2)transform.position;
        }
        else
        {
            float side = Random.value < 0.5f ? -1f : 1f;
            float x = transform.position.x + side * Random.Range(minSpawnDistance, Mathf.Max(minSpawnDistance, spawnRadius));
            Vector2 origin = new Vector2(x, transform.position.y + 2f);
            RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, 6f, groundLayer);
            pos = hit.collider != null
                ? hit.point + Vector2.up * 0.5f
                : (Vector2)transform.position + new Vector2(side * minSpawnDistance, 0.5f);
        }

        GameObject go = Instantiate(prefab, pos, Quaternion.identity);
        aliveMinions.Add(go);

        EnemyBase eb = go.GetComponent<EnemyBase>();
        if (eb != null)
        {
            eb.OnDeath.AddListener(() => aliveMinions.Remove(go));
        }
    }

    private void CleanMinionList()
    {
        aliveMinions.RemoveAll(m => m == null);
    }

    private int CountAliveMinions()
    {
        CleanMinionList();
        return aliveMinions.Count;
    }

    // ── Burst de energía ──
    private void RegisterHitForBurst(int damageAmount)
    {
        if (!enableEnergyBurst || burstQueued || currentAttack == AttackType.Burst) return;

        float now = Time.time;
        recentHits.Add(new HitRecord { time = now, damage = damageAmount });
        recentHits.RemoveAll(h => now - h.time > burstHitWindow);

        int totalDamage = 0;
        foreach (HitRecord h in recentHits) totalDamage += h.damage;

        int threshold = CurrentPhase.burstHitsThreshold;
        bool byCount = threshold > 0 && recentHits.Count >= threshold;
        bool byDamage = burstDamagePercentThreshold > 0f && totalDamage >= burstDamagePercentThreshold * GetMaxHealth();

        if ((byCount || byDamage) && now >= nextBurstAllowedTime)
        {
            burstQueued = true;
            recentHits.Clear();
        }
    }

    private void BeginBurst()
    {
        burstQueued = false;
        recentHits.Clear();
        jumpCommitted = false;

        isAttacking = true;
        currentAttack = AttackType.Burst;
        attackPhase = AttackPhase.BurstCharge;
        phaseTimer = burstWindup;
        recoilTimer = 0f;

        if (dashHitboxCollider != null) dashHitboxCollider.enabled = false;
        FreezeMovement();
        FaceTarget();

        // Invencible desde que empieza a cargar
        invulnerableUntil = Time.time + burstInvulnerableDuration;

        burstSpriteActive = true;
        SetColor(burstColor);
        onBurstStarted?.Invoke();

        if (logBehaviorDetails) Debug.Log($"{name}: ¡Burst de energía!");
    }

    private void UpdateBurstCharge()
    {
        FreezeMovement();
        FaceTarget();

        phaseTimer -= Time.deltaTime;
        if (phaseTimer <= 0f)
        {
            ExecuteBurst();
            attackPhase = AttackPhase.BurstRelease;
            phaseTimer = burstRecoveryTime;
        }
    }

    private void ExecuteBurst()
    {
        int dmg = Mathf.Max(1, Mathf.RoundToInt(burstDamage * CurrentPhase.damageMultiplier));
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, burstRadius, playerLayer);
        HashSet<GameObject> damaged = new HashSet<GameObject>();

        foreach (Collider2D hit in hits)
        {
            GameObject root = hit.attachedRigidbody != null ? hit.attachedRigidbody.gameObject : hit.gameObject;
            if (!damaged.Add(root)) continue;

            IDamageable damageable = hit.GetComponent<IDamageable>();
            if (damageable == null && hit.attachedRigidbody != null)
                damageable = hit.attachedRigidbody.GetComponent<IDamageable>();

            if (damageable != null) damageable.TakeDamage(dmg, transform.position);
        }

        if (burstVfxPrefab != null) Instantiate(burstVfxPrefab, transform.position, Quaternion.identity);
        onEnergyBurst?.Invoke();
    }

    private void UpdateBurstRelease()
    {
        FreezeMovement();
        phaseTimer -= Time.deltaTime;
        if (phaseTimer <= 0f)
        {
            burstSpriteActive = false;
            nextBurstAllowedTime = Time.time + burstCooldown;
            lastAttack = AttackType.Burst;
            currentAttack = AttackType.None;
            isAttacking = false;
            attackTimer = CurrentPhase.attackCooldown;
            ApplyIdleVisual();
            ChangeState(ChaserState.Chase);
        }
    }

    // ── Transición de fase ──
    private void BeginTransition()
    {
        currentPhaseIndex++;
        ApplyPhase(currentPhaseIndex);
        onPhaseChanged?.Invoke(currentPhaseIndex + 1);

        burstQueued = false;
        recentHits.Clear();

        attackPhase = AttackPhase.Transition;
        phaseTimer = phaseTransitionDuration;
        SetColor(transitionColor);

        if (logBehaviorDetails)
        {
            Debug.Log($"{name}: ¡Fase {currentPhaseIndex + 1}!");
        }
    }

    private void UpdateTransition()
    {
        FreezeMovement();
        phaseTimer -= Time.deltaTime;
        if (phaseTimer > 0f) return;

        ApplyIdleVisual();

        bool canSummon = summonOnPhaseChange && minionPrefabs != null && minionPrefabs.Length > 0;
        if (canSummon)
        {
            StartSummon(CurrentPhase.minionsPerSummon);
        }
        else
        {
            recoilTimer = 0f;
            BeginRecovery();
        }
    }

    // ── Recuperación ──
    private void BeginRecovery()
    {
        attackPhase = AttackPhase.Recovering;
        phaseTimer = CurrentPhase.recoveryTime;
        ApplyIdleVisual();
    }

    private void UpdateRecovery()
    {
        if (recoilTimer > 0f)
        {
            recoilTimer -= Time.deltaTime;
            if (rb != null)
            {
                rb.linearVelocity = new Vector2(-dashDirection.x * recoilSpeed, rb.linearVelocity.y);
            }
        }
        else
        {
            FreezeMovement();
        }

        phaseTimer -= Time.deltaTime;
        if (phaseTimer <= 0f)
        {
            lastAttack = currentAttack;
            currentAttack = AttackType.None;
            isAttacking = false;
            attackTimer = CurrentPhase.attackCooldown;
            ResetPathingMemory();
            ChangeState(ChaserState.Chase); // nunca vuelve a Patrol
        }
    }

    // ───────────────────────────── Teletransporte ─────────────────────────────
    private void StartTeleport(float preferredSide, System.Action<bool> onFinished)
    {
        if (isTeleporting || player == null || rb == null)
        {
            onFinished?.Invoke(false);
            return;
        }

        if (!TryFindTeleportPoint(preferredSide, out Vector2 firstPoint))
        {
            teleportCooldownTimer = 1f; // reintenta pronto
            onFinished?.Invoke(false);
            return;
        }

        jumpCommitted = false;
        StartCoroutine(TeleportRoutine(preferredSide, firstPoint, onFinished));
    }

    private IEnumerator TeleportRoutine(float preferredSide, Vector2 firstPoint, System.Action<bool> onFinished)
    {
        isTeleporting = true;

        if (teleportVfxPrefab != null) Instantiate(teleportVfxPrefab, transform.position, Quaternion.identity);
        TeleportedFrom?.Invoke(transform.position);

        if (dashHitboxCollider != null) dashHitboxCollider.enabled = false;
        rb.linearVelocity = Vector2.zero;
        rb.simulated = false; // intangible mientras está invisible
        if (spriteRenderer != null) spriteRenderer.enabled = false;

        yield return new WaitForSeconds(teleportVanishTime);

        Vector2 dest = TryFindTeleportPoint(preferredSide, out Vector2 newPoint) ? newPoint : firstPoint;
        transform.position = dest;
        rb.position = dest;
        rb.linearVelocity = Vector2.zero;
        rb.simulated = true;

        if (spriteRenderer != null) spriteRenderer.enabled = true;
        if (teleportVfxPrefab != null) Instantiate(teleportVfxPrefab, dest, Quaternion.identity);
        TeleportedTo?.Invoke(dest);

        FaceTarget();
        ResetPathingMemory();
        teleportCooldownTimer = teleportCooldown;
        isTeleporting = false;

        onFinished?.Invoke(true);
    }

    private bool TryFindTeleportPoint(float preferredSide, out Vector2 point)
    {
        point = transform.position;
        if (player == null) return false;

        Vector2 size = bodyCollider != null ? (Vector2)bodyCollider.bounds.size : Vector2.one;
        float halfHeight = size.y * 0.5f;
        float[] sides = { preferredSide, -preferredSide };

        foreach (float side in sides)
        {
            float x = player.position.x + side * teleportOffsetX;
            Vector2 origin = new Vector2(x, player.position.y + teleportRayHeight);
            RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, teleportRayHeight * 2f, groundLayer);
            if (hit.collider == null) continue;

            Vector2 candidate = new Vector2(x, hit.point.y + halfHeight + 0.05f);

            if (arenaBounds != null && !arenaBounds.bounds.Contains(new Vector3(candidate.x, candidate.y, arenaBounds.bounds.center.z)))
                continue;

            if (OverlapSolidBox(candidate, size * 0.9f, obstacleLayer) || OverlapPlatformBox(candidate, size * 0.9f, groundLayer, out _))
                continue;

            point = candidate;
            return true;
        }

        return false;
    }

    // ───────────────────────────── Fases / Vida ─────────────────────────────
    protected override void OnDamageTakenCustom(int damageAmount)
    {
        base.OnDamageTakenCustom(damageAmount);
        if (isDead) return;

        hurtTimer = hurtSpriteDuration;
        Hurt?.Invoke();
        UpdateTargetPhase();
        RegisterHitForBurst(damageAmount);
    }

    private void UpdateTargetPhase()
    {
        float healthPercent = GetHealthPercentage();

        int idx = 0;
        if (phaseHealthThresholds != null)
        {
            for (int i = 0; i < phaseHealthThresholds.Length; i++)
            {
                if (healthPercent <= phaseHealthThresholds[i] + 0.0001f) idx = i + 1; // tolerance: 165/250 vs 0.66f float rounding
            }
        }

        targetPhaseIndex = Mathf.Max(targetPhaseIndex, Mathf.Clamp(idx, 0, phases.Length - 1));
    }

    private void ApplyPhase(int index)
    {
        currentPhaseIndex = Mathf.Clamp(index, 0, phases.Length - 1);
        chaseColor = CurrentPhase.tint; // el estado Chase de la base usa este color
        ApplyIdleVisual();
    }

    // ───────────────────────────── Visual ─────────────────────────────
    private void ApplyIdleVisual()
    {
        actionSprite = null;
        SetColor(CurrentPhase.tint);
    }

    private void SetColor(Color c)
    {
        currentActionColor = c;
        UpdateVisualFeedback(c);
    }

    private Sprite SpriteOrIdle(Sprite s)
    {
        return s != null ? s : CurrentPhase.idleSprite;
    }

    /// <summary>
    /// Resuelve qué sprite mostrar según prioridad:
    /// burst > recibir daño > acción (telegraph/dash/invocar) > en el aire (salto) > base de la fase.
    /// </summary>
    private void LateUpdate()
    {
        if (isDead || spriteRenderer == null) return;

        if (hurtTimer > 0f) hurtTimer -= Time.deltaTime;

        Sprite s;
        if (burstSpriteActive) s = SpriteOrIdle(CurrentPhase.burstSprite);
        else if (hurtTimer > 0f) s = SpriteOrIdle(CurrentPhase.hurtSprite);
        else if (actionSprite != null) s = actionSprite;
        else if (isAirborne && !isAttacking) s = SpriteOrIdle(CurrentPhase.jumpSprite);
        else s = CurrentPhase.idleSprite;

        if (s != null && spriteRenderer.sprite != s)
        {
            spriteRenderer.sprite = s;
        }
    }

    private void FaceTarget()
    {
        if (player != null) UpdateSpriteFlip(player.position.x - transform.position.x);
    }

    protected override void UpdateSpriteFlip(float directionX)
    {
        if (!autoFlipSprite || spriteRenderer == null) return;

        Vector3 localScale = transform.localScale;
        float facingRightSign = spriteFacesRight ? 1f : -1f;

        if (directionX > 0.01f)
            localScale.x = facingRightSign * Mathf.Abs(localScale.x);
        else if (directionX < -0.01f)
            localScale.x = -facingRightSign * Mathf.Abs(localScale.x);

        transform.localScale = localScale;
    }

    // ───────────────────────────── Muerte ─────────────────────────────
    protected override void OnDeathCustom()
    {
        StopAllCoroutines();
        isTeleporting = false;
        isSpawning = false;
        burstSpriteActive = false;
        invulnerableUntil = 0f;

        if (dashHitboxCollider != null) dashHitboxCollider.enabled = false;
        if (spriteRenderer != null) spriteRenderer.enabled = true;

        base.OnDeathCustom();

        if (killMinionsOnDeath)
        {
            foreach (GameObject m in aliveMinions.ToArray())
            {
                if (m == null) continue;
                EnemyBase eb = m.GetComponent<EnemyBase>();
                if (eb != null) eb.TakeDamage(9999);
                else Destroy(m);
            }
            aliveMinions.Clear();
        }
    }

    protected override void OnDeathVisuals()
    {
        Sprite death = CurrentPhase.deathSprite != null ? CurrentPhase.deathSprite : deathSprite;
        if (spriteRenderer != null && death != null)
        {
            spriteRenderer.sprite = death;
            spriteRenderer.color = Color.white;
        }
    }

    // ───────────────────────────── Gizmos ─────────────────────────────
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        Gizmos.color = new Color(1f, 0.5f, 0f, 1f);
        Gizmos.DrawWireSphere(transform.position, attackRange); // distancia a la que inicia la carga

        Gizmos.color = new Color(0.6f, 0.4f, 1f, 1f);
        Gizmos.DrawWireSphere(transform.position, teleportDistance);

        Gizmos.color = burstColor;
        Gizmos.DrawWireSphere(transform.position, burstRadius);

        if (dashHitboxCollider is BoxCollider2D box)
        {
            Gizmos.color = Color.red;
            Gizmos.matrix = box.transform.localToWorldMatrix;
            Gizmos.DrawWireCube(box.offset, box.size);
            Gizmos.matrix = Matrix4x4.identity;
        }

        if (obstacleCheck != null)
        {
            Gizmos.color = Color.magenta;
            Vector2 checkPos = Application.isPlaying
                ? (Vector2)obstacleCheck.position + dashDirection * obstacleCheckDistance
                : (Vector2)obstacleCheck.position;
            Gizmos.DrawWireSphere(checkPos, obstacleCheckRadius);
        }

        Collider2D col = bodyCollider != null ? bodyCollider : GetComponent<Collider2D>();
        if (col != null)
        {
            Bounds b = col.bounds;
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(new Vector2(b.center.x, b.min.y - 0.02f), groundCheckRadius);

            foreach (float f in new float[] { -1f, 1f })
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(new Vector2(b.center.x + f * (b.extents.x + wallCheckDistance), b.center.y + wallCheckHeightOffset), wallCheckRadius);
                Gizmos.color = Color.blue;
                Gizmos.DrawWireSphere(new Vector2(b.center.x + f * (b.extents.x + ledgeCheckForward), b.min.y - ledgeCheckDepth), ledgeCheckRadius);
            }
        }

        // Último aterrizaje planeado por el planificador de saltos (visible ~1.5 s)
        if (Application.isPlaying && Time.time - lastPlanTime < 1.5f)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(lastPlanLanding, 0.3f);
            Gizmos.DrawLine(transform.position, lastPlanLanding);
        }

        if (Application.isPlaying && attackPhase == AttackPhase.Dashing && isAttacking)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(dashStartPosition, dashStartPosition + dashDirection * dashTargetDistance);
        }
    }
}

/* ═══════════════════════════════════════════════════════════════════════════
 * NOTAS DE CONFIGURACIÓN
 *
 * CAPTURA / KNOCKBACK / STUN: ya se bloquean por código (captureImmune, knockbackMultiplier,
 * stunResistance). Aun así, en el ScriptableObject "Enemy" del jefe conviene:
 *   Knockback.KnockbackDuration      = 0.05 - 0.1   (si hay empuje, que no "congele" la IA mucho)
 *   Stun.StunnedThresholdForFullStop = 1000         (que nunca quede paralizado del todo)
 *
 * DAÑO POR CONTACTO: lo maneja EnemyBase (Dealing Contact Damage / Contact Damage / Cooldown),
 * ya no hay campos duplicados en este script.
 *
 * PREFAB DEL JEFE:
 *   - Rigidbody2D (Dynamic, con gravedad, Freeze Rotation Z), Collider2D sólido, SpriteRenderer.
 *   - Player Layer, Obstacle Layer (paredes/techos) y Ground Layer (suelo y plataformas).
 *   - Si tus plataformas se pueden atravesar desde abajo (one-way) deja "One Way Platforms" marcado;
 *     si son sólidas, desmárcalo.
 *   - Un sprite por fase en "Phases > Idle Sprite"; opcionales: Jump, Hurt, Burst, Telegraph, Dash, Summon.
 *   - Si un Animator controla el sprite, pisará estos cambios.
 * ═══════════════════════════════════════════════════════════════════════════ */