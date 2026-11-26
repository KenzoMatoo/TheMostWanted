using UnityEngine;

/// Tank: torreta (sentry) que no se mueve. Detecta al jugador, gira el arma suavemente
/// siguiéndolo (máx. 45° arriba/abajo respecto a la horizontal) y dispara ráfagas de 2 balas
/// usando SniperBullet. Hereda de EnemyBase (vida, stun, captura, daño por contacto).
/// Flujo: Idle -> Spotting -> Aiming -> Shooting -> Cooldown -> Aiming ...
public class TankEnemy : EnemyBase
{
    private enum State { Idle, Spotting, Aiming, Shooting, Cooldown }

    [Header("Referencias")]
    [SerializeField] private Transform player;               // si está vacío busca el tag "Player"
    [SerializeField] private Transform gun;                  // objeto que ROTA (pivote del arma)
    [SerializeField] private Transform muzzle;               // hijo con el sprite del fogonazo
    [SerializeField] private Transform firePoint;            // punta del cañón (opcional; si no, usa muzzle)
    [SerializeField] private Animator bodyAnimator;          // ACTank (en Tank_Slime_0)
    [SerializeField] private Animator gunAnimator;           // Tank_Gun_64_0 controller
    [SerializeField] private Animator muzzleAnimator;        // Tank_Muzzle_0 controller
    [SerializeField] private SniperBullet bulletPrefab;      // el mismo prefab de bala del sniper

    [Header("Capas")]
    [SerializeField] private LayerMask obstacleLayer;        // suelo / paredes (NO la capa del tank)
    [SerializeField] private LayerMask playerLayer;

    [Header("Detección")]
    [SerializeField] private float detectionRange = 12f;     // área mediana
    [Tooltip("Una vez enganchado, tolera que el jugador se aleje hasta detectionRange x este valor.")]
    [SerializeField] private float loseRangeMultiplier = 1.15f;
    [SerializeField] private float lostSightGrace = 0.8f;    // tiempo sin verte antes de rendirse
    [Tooltip("Diferencia mínima en X para voltear al tank (evita parpadeo al estar justo encima).")]
    [SerializeField] private float flipDeadZone = 0.5f;

    [Header("Giro del arma")]
    [SerializeField] private float maxAimUp = 45f;           // máximo hacia arriba (grados)
    [SerializeField] private float maxAimDown = 45f;         // máximo hacia abajo (grados)
    [SerializeField] private float aimTurnSpeed = 120f;      // grados por segundo
    [Tooltip("Qué tan alineado debe estar el arma para empezar a contar el tiempo de apuntado.")]
    [SerializeField] private float aimTolerance = 4f;
    [Tooltip("Margen extra para considerar al jugador dentro del arco de tiro.")]
    [SerializeField] private float arcTolerance = 3f;

    [Header("Apuntado y cadencia")]
    [SerializeField] private float spotDelay = 0.6f;         // animación de alerta
    [SerializeField] private float aimTime = 0.8f;           // tiempo alineado antes de disparar
    [SerializeField] private float cooldown = 2.2f;          // espera entre ráfagas

    [Header("Disparo")]
    [SerializeField] private int shotsPerBurst = 2;
    [SerializeField] private float burstInterval = 0.3f;
    [SerializeField] private int damage = 1;
    [SerializeField] private float bulletSpeed = 20f;

    [Header("Animaciones (nombres de estado)")]
    [SerializeField] private string bodyIdle = "IdleTank";
    [SerializeField] private string bodySpot = "SpotTank";
    [SerializeField] private string bodyAim = "AimTank";
    [SerializeField] private string bodyShoot = "ShootTank";
    [SerializeField] private string gunIdle = "GunTankIdle";
    [SerializeField] private string gunSpot = "GunTankSpot";
    [SerializeField] private string gunAim = "GunTankAim";
    [SerializeField] private string gunShoot = "GunTankShoot";
    [SerializeField] private string muzzleShoot = "MuzzleShoot";
    [SerializeField] private float muzzleFlashTime = 0.25f;

    [Header("Muerte")]
    [SerializeField] private float deathDestroyDelay = 2f;

    private Collider2D playerCol;
    private SpriteRenderer muzzleRenderer;

    private State state = State.Idle;
    private float timer, lostTimer, shotTimer, muzzleTimer;
    private int shotsFired;
    private int currentAnimHash;

    private float currentAimAngle;   // ángulo actual del arma (local, grados)
    private float targetAimAngle;    // ángulo objetivo ya limitado al arco
    private bool playerInArc;

    #region Ciclo de vida

    protected override void Start()
    {
        base.Start(); // rb, colliders, vida, flash

        if (bodyAnimator == null) bodyAnimator = GetComponent<Animator>();
        if (gunAnimator == null && gun != null) gunAnimator = gun.GetComponentInChildren<Animator>();
        if (muzzleAnimator == null && muzzle != null) muzzleAnimator = muzzle.GetComponent<Animator>();
        if (muzzleAnimator != null) muzzleRenderer = muzzleAnimator.GetComponent<SpriteRenderer>();
        HideMuzzle();

        // Torreta: no se desplaza en X ni gira. Sigue afectada por la gravedad.
        if (rb != null)
        {
            rb.constraints = RigidbodyConstraints2D.FreezePositionX | RigidbodyConstraints2D.FreezeRotation;
            rb.linearVelocity = Vector2.zero;
        }

        FindPlayer();
        SetState(State.Idle);
    }

    void FindPlayer()
    {
        if (player == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
        }
        if (player != null && playerCol == null) playerCol = player.GetComponent<Collider2D>();
    }

    protected override void Update()
    {
        TickMuzzleFlash();

        if (isDead || isCaptured) return;

        base.Update(); // stun decay + knockback

        if (player == null) { FindPlayer(); return; }

        // Aturdido por completo: no hace nada, el arma vuelve a su posición de reposo
        if (IsFullyStunned())
        {
            if (state != State.Idle) ReturnToIdle();
            RotateGun(0f);
            return;
        }

        switch (state)
        {
            case State.Idle: IdleBehavior(); break;
            case State.Spotting: SpottingBehavior(); break;
            case State.Aiming: AimingBehavior(); break;
            case State.Shooting: ShootingBehavior(); break;
            case State.Cooldown: CooldownBehavior(); break;
        }
    }

    #endregion

    #region Hooks de EnemyBase

    // Es una torreta: el knockback no la mueve
    protected override void ApplyKnockback(int damageAmount, Vector2 damageSource) { }

    protected override void OnCapturedCustom()
    {
        base.OnCapturedCustom();
        HideMuzzle();

        // Se puede capturar, pero no arrastrar: un cuerpo Static ignora el MovePosition
        // de CapturedEnemyController.
        if (rb != null) rb.bodyType = RigidbodyType2D.Static;
    }

    protected override void OnReleasedCustom()
    {
        base.OnReleasedCustom();

        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.linearVelocity = Vector2.zero;
        }

        currentAnimHash = 0;
        ReturnToIdle();
    }

    protected override void OnDeathCustom()
    {
        base.OnDeathCustom(); // ScoreManager
        HideMuzzle();
        if (rb != null && rb.bodyType != RigidbodyType2D.Static) rb.linearVelocity = Vector2.zero;
        Destroy(gameObject, deathDestroyDelay);
    }

    #endregion

    #region Estados

    void IdleBehavior()
    {
        RotateGun(0f);

        if (PlayerDetected())
        {
            timer = 0f;
            lostTimer = 0f;
            SetState(State.Spotting);
        }
    }

    void SpottingBehavior()
    {
        if (!StillEngaged()) return;

        TrackPlayer();

        timer += Time.deltaTime;
        if (timer >= spotDelay)
        {
            timer = 0f;
            SetState(State.Aiming);
        }
    }

    void AimingBehavior()
    {
        if (!StillEngaged()) return;

        TrackPlayer();

        bool aligned = playerInArc && Mathf.Abs(Mathf.DeltaAngle(currentAimAngle, targetAimAngle)) <= aimTolerance;

        // Solo cuenta el tiempo si el arma ya está apuntando y el jugador está dentro del arco de tiro
        if (aligned) timer += Time.deltaTime;
        else timer = 0f;

        if (timer >= aimTime)
        {
            timer = 0f;
            shotsFired = 0;
            shotTimer = 0f;
            SetState(State.Shooting);
        }
    }

    void ShootingBehavior()
    {
        TrackPlayer();

        shotTimer -= Time.deltaTime;

        if (shotsFired < shotsPerBurst)
        {
            if (shotTimer <= 0f)
            {
                FireOne();
                shotsFired++;
                shotTimer = burstInterval;
            }
        }
        else if (shotTimer <= 0f)
        {
            timer = 0f;
            SetState(State.Cooldown);
        }
    }

    void CooldownBehavior()
    {
        if (!StillEngaged()) return;

        TrackPlayer();

        timer += Time.deltaTime;
        if (timer >= cooldown)
        {
            timer = 0f;
            SetState(State.Aiming);
        }
    }

    void ReturnToIdle()
    {
        timer = 0f;
        lostTimer = 0f;
        shotsFired = 0;
        SetState(State.Idle);
    }

    #endregion

    #region Detección

    bool PlayerDetected()
    {
        float dist = Vector2.Distance(AimOrigin(), PlayerPos());
        return dist <= detectionRange && ClearLine();
    }

    // Mientras está enganchado: tolera perder de vista al jugador un momento
    bool StillEngaged()
    {
        float dist = Vector2.Distance(AimOrigin(), PlayerPos());
        bool valid = dist <= detectionRange * loseRangeMultiplier && ClearLine();

        if (valid)
        {
            lostTimer = 0f;
            return true;
        }

        lostTimer += Time.deltaTime;
        if (lostTimer >= lostSightGrace)
        {
            ReturnToIdle();
            return false;
        }
        return true; // en gracia: sigue apuntando
    }

    bool ClearLine()
    {
        return !Physics2D.Linecast(AimOrigin(), PlayerPos(), obstacleLayer);
    }

    #endregion

    #region Apuntado del arma

    // Voltea al tank hacia el jugador y gira el arma hacia él, limitada al arco de tiro
    void TrackPlayer()
    {
        FacePlayer();

        float raw = RawAngleToPlayer();
        targetAimAngle = Mathf.Clamp(raw, -maxAimDown, maxAimUp);
        playerInArc = raw >= -maxAimDown - arcTolerance && raw <= maxAimUp + arcTolerance;

        RotateGun(targetAimAngle);
    }

    // Ángulo (local, respecto a la horizontal hacia donde mira el tank) hacia el jugador
    float RawAngleToPlayer()
    {
        Vector2 world = PlayerPos() - AimOrigin();
        Vector2 local = GunSpace().InverseTransformVector(world);
        return Mathf.Atan2(local.y, local.x) * Mathf.Rad2Deg;
    }

    void RotateGun(float targetAngle)
    {
        currentAimAngle = Mathf.MoveTowardsAngle(currentAimAngle, targetAngle, aimTurnSpeed * Time.deltaTime);
        if (gun != null) gun.localRotation = Quaternion.Euler(0f, 0f, currentAimAngle);
    }

    void FacePlayer()
    {
        float dx = PlayerPos().x - transform.position.x;
        if (Mathf.Abs(dx) < flipDeadZone) return;

        Vector3 s = transform.localScale;
        s.x = Mathf.Abs(s.x) * Mathf.Sign(dx);   // el sprite original mira a la derecha
        transform.localScale = s;
    }

    // Dirección real del cañón en el mundo (tiene en cuenta el flip del tank)
    Vector2 AimWorldDir()
    {
        float a = currentAimAngle * Mathf.Deg2Rad;
        Vector2 local = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        return ((Vector2)GunSpace().TransformVector(local)).normalized;
    }

    #endregion

    #region Disparo

    void FireOne()
    {
        Vector2 dir = AimWorldDir();
        Vector2 pos = FirePos();

        SniperBullet bullet = bulletPrefab != null
            ? Instantiate(bulletPrefab, pos, Quaternion.identity)
            : new GameObject("TankBullet").AddComponent<SniperBullet>();

        bullet.transform.position = pos;
        bullet.Init(dir, bulletSpeed, damage, obstacleLayer | playerLayer, transform);

        Play(bodyShoot, gunShoot, true);   // reinicia la animación en cada disparo
        ShowMuzzleFlash();
    }

    void ShowMuzzleFlash()
    {
        if (muzzleAnimator == null) return;
        if (muzzleRenderer != null) muzzleRenderer.enabled = true;
        muzzleAnimator.Play(Animator.StringToHash(muzzleShoot), 0, 0f);
        muzzleTimer = muzzleFlashTime;
    }

    void TickMuzzleFlash()
    {
        if (muzzleTimer <= 0f) return;
        muzzleTimer -= Time.deltaTime;
        if (muzzleTimer <= 0f) HideMuzzle();
    }

    void HideMuzzle()
    {
        muzzleTimer = 0f;
        if (muzzleRenderer != null) muzzleRenderer.enabled = false;
    }

    #endregion

    #region Utilidades

    Vector2 PlayerPos() => playerCol != null ? (Vector2)playerCol.bounds.center : (Vector2)player.position;

    Vector2 AimOrigin() => gun != null ? (Vector2)gun.position : (Vector2)transform.position;

    Vector2 FirePos()
    {
        if (firePoint != null) return firePoint.position;
        if (muzzle != null) return muzzle.position;
        return AimOrigin();
    }

    Transform GunSpace()
    {
        return (gun != null && gun.parent != null) ? gun.parent : transform;
    }

    void SetState(State newState)
    {
        if (state == newState && currentAnimHash != 0) return;
        state = newState;

        switch (state)
        {
            case State.Idle: Play(bodyIdle, gunIdle); break;
            case State.Spotting: Play(bodySpot, gunSpot); break;
            case State.Aiming: Play(bodyAim, gunAim); break;
            case State.Shooting: break;  // la animación se reproduce en cada disparo
            case State.Cooldown: Play(bodyAim, gunAim); break;
        }
    }

    void Play(string body, string gunState, bool force = false)
    {
        int hash = Animator.StringToHash(body);
        if (!force && hash == currentAnimHash) return;
        currentAnimHash = hash;
        if (bodyAnimator != null) bodyAnimator.Play(hash, 0, 0f);
        if (gunAnimator != null) gunAnimator.Play(Animator.StringToHash(gunState), 0, 0f);
    }

    void OnDrawGizmosSelected()
    {
        Vector3 origin = gun != null ? gun.position : transform.position;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(origin, detectionRange);

        // Arco de tiro (límites del giro del arma)
        float facing = transform.localScale.x >= 0f ? 1f : -1f;
        Gizmos.color = Color.red;
        float up = maxAimUp * Mathf.Deg2Rad;
        float down = -maxAimDown * Mathf.Deg2Rad;
        Gizmos.DrawLine(origin, origin + new Vector3(facing * Mathf.Cos(up), Mathf.Sin(up), 0f) * detectionRange);
        Gizmos.DrawLine(origin, origin + new Vector3(facing * Mathf.Cos(down), Mathf.Sin(down), 0f) * detectionRange);
    }

    #endregion
}