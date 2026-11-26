using UnityEngine;

/// Sniper tipo "Sniper Shell" de Enter the Gungeon, integrado con EnemyBase
/// (vida, stun, knockback, captura, daño por contacto).
/// Flujo: Patrol <-> Waiting -> Reposition -> Spotting -> Tracking -> Locked -> Cooldown -> Patrol
public class SniperEnemy : EnemyBase
{
    private enum State { Patrol, Waiting, Reposition, Spotting, Tracking, Locked, Cooldown }

    [Header("Referencias")]
    [SerializeField] private Transform player;
    [SerializeField] private Transform gun;                  // hijo gun_all_0
    [SerializeField] private Transform muzzle;               // punta del cañón (opcional)
    [SerializeField] private Animator bodyAnimator;          // ACSniper
    [SerializeField] private Animator gunAnimator;           // ACGun
    [SerializeField] private SniperBullet bulletPrefab;      // opcional

    [Header("Capas")]
    [SerializeField] private LayerMask obstacleLayer;
    [SerializeField] private LayerMask playerLayer;

    [Header("Patrulla")]
    [SerializeField] private Transform[] patrolPoints;
    [SerializeField] private bool loopPatrol = true;         // false = ping-pong
    [SerializeField] private float patrolSpeed = 1.5f;
    [SerializeField] private float patrolWaitTime = 2f;
    [SerializeField] private float waypointReachDistance = 0.3f;

    [Header("Detección y movimiento")]
    [SerializeField] private float detectionRange = 20f;
    [SerializeField] private float minDistance = 7f;
    [SerializeField] private float walkSpeed = 2.5f;
    [SerializeField] private bool spriteFacesLeft = true;
    [SerializeField] private float ledgeCheckDistance = 0.6f;

    [Header("Apuntado")]
    [SerializeField] private float spotDelay = 0.5f;
    [SerializeField] private float trackDuration = 4f;
    [SerializeField] private float lockDuration = 0.6f;
    [SerializeField] private float aimTurnSpeed = 220f;
    [SerializeField] private float lostSightGrace = 0.6f;
    [SerializeField] private float cooldown = 2.5f;

    [Header("Disparo")]
    [SerializeField] private int damage = 2;
    [SerializeField] private float bulletSpeed = 40f;

    [Header("Láser")]
    [SerializeField] private Color trackStartColor = new Color(1f, 0.9f, 0.1f, 0.5f);
    [SerializeField] private Color trackEndColor = new Color(1f, 0.35f, 0f, 0.9f);
    [SerializeField] private Color lockedColor = new Color(1f, 0f, 0f, 1f);
    [SerializeField] private float laserWidth = 0.04f;
    [SerializeField] private float lockedWidth = 0.09f;

    [Header("Animaciones (nombres de estado)")]
    [SerializeField] private string bodyIdle = "SniperIdle";
    [SerializeField] private string bodyWalk = "WalkSniper";
    [SerializeField] private string bodySpot = "SpotSniper";
    [SerializeField] private string bodyShoot = "ShootSniper";
    [SerializeField] private string gunIdle = "GunSniIdle";
    [SerializeField] private string gunWalk = "GunSniWalk";
    [SerializeField] private string gunSpot = "GunSniSpot";
    [SerializeField] private string gunShoot = "gunSniShoot";
    [SerializeField] private float shootAnimTime = 0.5f;

    [Header("Límite de velocidad (evita atravesar paredes)")]
    [Tooltip("Velocidad máxima que puede alcanzar el sniper (al ser lanzado, knockback, etc.). Si aún atraviesa paredes, bájala.")]
    [SerializeField] private float maxSpeed = 30f;

    [Header("Después de ser soltado (lanzado)")]
    [Tooltip("Tiempo mínimo en que la IA no toca la velocidad tras soltarlo.")]
    [SerializeField] private float releaseLockMinTime = 0.4f;
    [Tooltip("Tiempo máximo de bloqueo, por seguridad.")]
    [SerializeField] private float releaseLockMaxTime = 3f;
    [Tooltip("Cuando la velocidad baja de este valor (y pasó el tiempo mínimo), recupera el control.")]
    [SerializeField] private float releaseRecoverSpeed = 1.5f;

    [Header("Muerte")]
    [SerializeField] private float deathDestroyDelay = 3f;

    private Collider2D col;
    private Collider2D playerCol;
    private LineRenderer laser;

    private State state = State.Patrol;
    private float timer, trackTimer, lostTimer;
    private Vector2 aimDir = Vector2.left;
    private Vector2 lockedDir;
    private int currentAnimHash;
    private bool hasLOS;

    private int patrolIndex = 0;
    private bool patrolForward = true;
    private float waitTimer;

    private bool releaseLocked;
    private float releaseLockElapsed;

    #region Ciclo de vida

    protected override void Start()
    {
        base.Start(); // obtiene rb, colliders, vida, flash

        col = GetComponent<Collider2D>();
        if (bodyAnimator == null) bodyAnimator = GetComponent<Animator>();
        if (gunAnimator == null && gun != null) gunAnimator = gun.GetComponent<Animator>();

        // Colisión continua: no se salta paredes a alta velocidad
        if (rb != null)
        {
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        }

        FindPlayer();
        CreateLaser();

        if (patrolPoints == null || patrolPoints.Length == 0) CreateDefaultPatrolPoints();
        SetState(State.Patrol);
    }

    void FindPlayer()
    {
        // A reference to the player PREFAB (asset) is not the real player: look for the one in the scene
        if (player != null && !player.gameObject.scene.IsValid()) player = null;
        if (player == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
        }
        if (player != null && playerCol == null) playerCol = player.GetComponent<Collider2D>();
    }

    void CreateLaser()
    {
        // Sin padre: así la escala (x3) y el flip del slime no deforman el grosor
        GameObject go = new GameObject("SniperLaser_" + name);
        laser = go.AddComponent<LineRenderer>();
        laser.useWorldSpace = true;
        laser.positionCount = 2;
        laser.material = new Material(Shader.Find("Sprites/Default"));
        laser.sortingOrder = 10;
        laser.numCapVertices = 2;
        laser.enabled = false;
    }

    void FixedUpdate()
    {
        // Límite de velocidad: se aplica en cada paso de física
        if (rb == null || isDead || isCaptured) return;

        Vector2 v = rb.linearVelocity;
        if (v.sqrMagnitude > maxSpeed * maxSpeed)
        {
            rb.linearVelocity = v.normalized * maxSpeed;
        }
    }

    protected override void Update()
    {
        if (isDead || isCaptured)
        {
            HideLaser();
            return;
        }

        base.Update(); // stun decay + knockback

        if (player == null) { FindPlayer(); return; }

        // En knockback no tocamos la velocidad
        if (IsInKnockback()) return;

        // Recién soltado: sigue volando, la IA no toca la velocidad
        if (releaseLocked)
        {
            UpdateReleaseLock();
            if (releaseLocked) return;
        }

        // Aturdido por completo: se detiene y cancela el apuntado
        if (IsFullyStunned())
        {
            StopMoving();
            if (IsAiming()) CancelAim();
            return;
        }

        hasLOS = HasLineOfSight();

        switch (state)
        {
            case State.Patrol: PatrolBehavior(); break;
            case State.Waiting: WaitBehavior(); break;
            case State.Reposition: RepositionBehavior(); break;
            case State.Spotting: SpottingBehavior(); break;
            case State.Tracking: TrackingBehavior(); break;
            case State.Locked: LockedBehavior(); break;
            case State.Cooldown: CooldownBehavior(); break;
        }
    }

    void OnDisable() { HideLaser(); }
    void OnDestroy() { if (laser != null) Destroy(laser.gameObject); }

    #endregion

    #region Hooks de EnemyBase

    // Recibir daño interrumpe el apuntado (como en Gungeon)
    protected override void OnDamageTakenCustom(int damageAmount)
    {
        base.OnDamageTakenCustom(damageAmount);
        if (!isDead && IsAiming()) CancelAim();
    }

    protected override void OnCapturedCustom()
    {
        base.OnCapturedCustom();
        HideLaser();
    }

    protected override void OnReleasedCustom()
    {
        base.OnReleasedCustom();
        // DisableAIComponents apagó este script; al soltarlo reiniciamos el estado
        currentAnimHash = 0;
        CancelInvoke();
        ResetGun();
        SetState(State.Patrol);

        // Bloquea la IA para que no frene el lanzamiento
        releaseLocked = true;
        releaseLockElapsed = 0f;

        // Limita la velocidad del lanzamiento de inmediato
        if (rb != null && rb.linearVelocity.sqrMagnitude > maxSpeed * maxSpeed)
        {
            rb.linearVelocity = rb.linearVelocity.normalized * maxSpeed;
        }
    }

    protected override void OnDeathCustom()
    {
        base.OnDeathCustom(); // ScoreManager
        CancelInvoke();
        HideLaser();
        ResetGun();
        releaseLocked = false;
        if (rb != null) rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        Destroy(gameObject, deathDestroyDelay);
    }

    #endregion

    #region Bloqueo tras ser soltado

    void UpdateReleaseLock()
    {
        releaseLockElapsed += Time.deltaTime;

        bool minTimePassed = releaseLockElapsed >= releaseLockMinTime;
        bool slowEnough = rb == null || rb.linearVelocity.magnitude <= releaseRecoverSpeed;

        if ((minTimePassed && slowEnough) || releaseLockElapsed >= releaseLockMaxTime)
        {
            releaseLocked = false;
        }
    }

    #endregion

    #region Patrulla y reposicionamiento

    bool PlayerDetected()
    {
        float dist = Vector2.Distance(transform.position, PlayerPos());
        return dist <= detectionRange && hasLOS;
    }

    void PatrolBehavior()
    {
        ResetGun();

        if (PlayerDetected())
        {
            StopMoving();
            SetState(State.Reposition);
            return;
        }

        if (patrolPoints == null || patrolPoints.Length == 0) return;
        Transform target = patrolPoints[patrolIndex];
        if (target == null) return;

        float dx = target.position.x - transform.position.x;

        if (Mathf.Abs(dx) <= waypointReachDistance)
        {
            NextPatrolPoint();
            StopMoving();
            waitTimer = patrolWaitTime;
            SetState(State.Waiting);
            return;
        }

        // Si hay pared o vacío, pasa al siguiente punto en vez de quedarse atorado
        if (!MoveHorizontal(Mathf.Sign(dx), patrolSpeed))
        {
            NextPatrolPoint();
            waitTimer = patrolWaitTime;
            SetState(State.Waiting);
        }
    }

    void WaitBehavior()
    {
        StopMoving();
        ResetGun();

        if (PlayerDetected())
        {
            SetState(State.Reposition);
            return;
        }

        waitTimer -= Time.deltaTime;
        if (waitTimer <= 0f) SetState(State.Patrol);
    }

    // Ya detectó al jugador: se aleja si está muy cerca, y si no, apunta
    void RepositionBehavior()
    {
        if (!PlayerDetected())
        {
            SetState(State.Patrol);
            return;
        }

        Vector2 toPlayer = PlayerPos() - (Vector2)transform.position;
        float dirX = Mathf.Sign(toPlayer.x);

        if (toPlayer.magnitude < minDistance && MoveHorizontal(-dirX, walkSpeed)) return;

        StopMoving();
        Face(dirX);
        timer = 0f;
        SetState(State.Spotting);
    }

    void NextPatrolPoint()
    {
        if (patrolPoints.Length <= 1) { patrolIndex = 0; return; }

        if (loopPatrol)
        {
            patrolIndex = (patrolIndex + 1) % patrolPoints.Length;
            return;
        }

        if (patrolForward)
        {
            patrolIndex++;
            if (patrolIndex >= patrolPoints.Length)
            {
                patrolIndex = patrolPoints.Length - 2;
                patrolForward = false;
            }
        }
        else
        {
            patrolIndex--;
            if (patrolIndex < 0)
            {
                patrolIndex = 1;
                patrolForward = true;
            }
        }
    }

    void CreateDefaultPatrolPoints()
    {
        GameObject container = new GameObject(name + "_PatrolPoints");
        Transform a = new GameObject("PatrolPoint1").transform;
        Transform b = new GameObject("PatrolPoint2").transform;
        a.SetParent(container.transform);
        b.SetParent(container.transform);
        a.position = transform.position + Vector3.left * 3f;
        b.position = transform.position + Vector3.right * 3f;
        patrolPoints = new[] { a, b };
    }

    #endregion

    #region Estados de apuntado

    void SpottingBehavior()
    {
        Face(Mathf.Sign(PlayerPos().x - transform.position.x));
        aimDir = (PlayerPos() - MuzzlePos()).normalized;
        AimGun(aimDir);

        timer += Time.deltaTime;
        if (timer >= spotDelay)
        {
            trackTimer = 0f;
            lostTimer = 0f;
            SetState(State.Tracking);
        }
    }

    void TrackingBehavior()
    {
        Vector2 targetDir = (PlayerPos() - MuzzlePos()).normalized;

        if (hasLOS)
        {
            lostTimer = 0f;
            trackTimer += Time.deltaTime;

            float cur = Mathf.Atan2(aimDir.y, aimDir.x) * Mathf.Rad2Deg;
            float tgt = Mathf.Atan2(targetDir.y, targetDir.x) * Mathf.Rad2Deg;
            float ang = Mathf.MoveTowardsAngle(cur, tgt, aimTurnSpeed * Time.deltaTime);
            aimDir = new Vector2(Mathf.Cos(ang * Mathf.Deg2Rad), Mathf.Sin(ang * Mathf.Deg2Rad));
        }
        else
        {
            lostTimer += Time.deltaTime;
            if (lostTimer > lostSightGrace ||
                Vector2.Distance(transform.position, player.position) > detectionRange * 1.2f)
            {
                CancelAim();
                return;
            }
        }

        Face(aimDir.x);
        AimGun(aimDir);

        float t = Mathf.Clamp01(trackTimer / trackDuration);
        DrawLaser(aimDir, Color.Lerp(trackStartColor, trackEndColor, t), laserWidth);

        if (trackTimer >= trackDuration)
        {
            lockedDir = aimDir;   // se guarda SOLO la última dirección
            timer = 0f;
            SetState(State.Locked);
        }
    }

    void LockedBehavior()
    {
        timer += Time.deltaTime;
        AimGun(lockedDir);

        bool blink = Mathf.FloorToInt(timer * 16f) % 2 == 0;
        DrawLaser(lockedDir, blink ? lockedColor : Color.white, lockedWidth);

        if (timer >= lockDuration)
        {
            Fire(lockedDir);
            HideLaser();
            timer = 0f;
            SetState(State.Cooldown);
        }
    }

    void CooldownBehavior()
    {
        timer += Time.deltaTime;
        // Si todavía ve al jugador, PlayerDetected() lo manda a apuntar de nuevo enseguida
        if (timer >= cooldown) SetState(State.Patrol);
    }

    void CancelAim()
    {
        HideLaser();
        CancelInvoke();
        ResetGun();
        SetState(State.Patrol);
    }

    bool IsAiming()
    {
        return state == State.Spotting || state == State.Tracking || state == State.Locked;
    }

    #endregion

    #region Disparo y láser

    void Fire(Vector2 dir)
    {
        Vector2 pos = MuzzlePos();
        SniperBullet bullet = bulletPrefab != null
            ? Instantiate(bulletPrefab, pos, Quaternion.identity)
            : new GameObject("SniperBullet").AddComponent<SniperBullet>();

        bullet.transform.position = pos;
        bullet.Init(dir, bulletSpeed, damage, obstacleLayer | playerLayer, transform);
    }

    void DrawLaser(Vector2 dir, Color color, float width)
    {
        if (laser == null) return;
        Vector2 origin = MuzzlePos();
        float length = detectionRange * 1.5f;
        RaycastHit2D hit = SightUtil.Raycast(origin, dir, length, obstacleLayer | playerLayer);
        Vector2 end = hit.collider != null ? hit.point : origin + dir * length;

        laser.enabled = true;
        laser.startColor = color;
        laser.endColor = color;
        laser.startWidth = width;
        laser.endWidth = width;
        laser.SetPosition(0, origin);
        laser.SetPosition(1, end);
    }

    void HideLaser()
    {
        if (laser != null) laser.enabled = false;
    }

    #endregion

    #region Movimiento

    bool MoveHorizontal(float dirX, float baseSpeed)
    {
        if (!CanMove(dirX))
        {
            StopMoving();
            return false;
        }
        // Respeta el stun parcial del EnemyBase
        rb.linearVelocity = new Vector2(dirX * baseSpeed * GetMovementSpeedMultiplier(), rb.linearVelocity.y);
        Face(dirX);
        Play(bodyWalk, gunWalk);
        return true;
    }

    void StopMoving()
    {
        if (rb != null) rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
    }

    bool CanMove(float dirX)
    {
        Bounds b = col.bounds;
        // Pared
        if (Physics2D.Raycast(b.center, Vector2.right * dirX, b.extents.x + 0.1f, obstacleLayer)) return false;
        // Borde
        Vector2 front = new Vector2(b.center.x + dirX * (b.extents.x + 0.2f), b.min.y + 0.1f);
        return Physics2D.Raycast(front, Vector2.down, ledgeCheckDistance, obstacleLayer);
    }

    void Face(float dirX)
    {
        if (Mathf.Abs(dirX) < 0.01f) return;
        Vector3 s = transform.localScale;
        float sign = Mathf.Sign(dirX);
        s.x = Mathf.Abs(s.x) * (spriteFacesLeft ? -sign : sign);
        transform.localScale = s;
    }

    #endregion

    #region Utilidades

    Vector2 PlayerPos() => playerCol != null ? (Vector2)playerCol.bounds.center : (Vector2)player.position;

    Vector2 MuzzlePos() => muzzle != null ? (Vector2)muzzle.position
                         : (gun != null ? (Vector2)gun.position : (Vector2)transform.position);

    bool HasLineOfSight()
    {
        Vector2 from = MuzzlePos();
        Vector2 to = PlayerPos();
        if (Vector2.Distance(from, to) > detectionRange) return false;
        return !SightUtil.Blocked(from, to, obstacleLayer);   // one-way platforms don't block the view
    }

    // Rota el arma teniendo en cuenta el flip del padre (escala X negativa)
    void AimGun(Vector2 worldDir)
    {
        if (gun == null) return;
        Vector2 local = gun.parent.InverseTransformVector(worldDir);
        float ang = Mathf.Atan2(local.y, local.x) * Mathf.Rad2Deg + (spriteFacesLeft ? 180f : 0f);
        gun.localRotation = Quaternion.Euler(0f, 0f, ang);
    }

    void ResetGun()
    {
        if (gun != null) gun.localRotation = Quaternion.identity;
    }

    void SetState(State newState)
    {
        if (state == newState && currentAnimHash != 0) return;
        state = newState;

        switch (state)
        {
            case State.Patrol: Play(bodyWalk, gunWalk); break;
            case State.Waiting: Play(bodyIdle, gunIdle); break;
            case State.Reposition: break; // conserva la animación actual
            case State.Spotting:
            case State.Tracking:
            case State.Locked: Play(bodySpot, gunSpot); break;
            case State.Cooldown:
                Play(bodyShoot, gunShoot);
                Invoke(nameof(ReturnIdleAnim), shootAnimTime);
                break;
        }
    }

    void ReturnIdleAnim()
    {
        if (state == State.Cooldown && !isDead) Play(bodyIdle, gunIdle);
    }

    void Play(string body, string gunState)
    {
        int hash = Animator.StringToHash(body);
        if (hash == currentAnimHash) return;
        currentAnimHash = hash;
        if (bodyAnimator != null) bodyAnimator.Play(hash, 0, 0f);
        if (gunAnimator != null) gunAnimator.Play(Animator.StringToHash(gunState), 0, 0f);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, minDistance);

        if (patrolPoints != null)
        {
            Gizmos.color = Color.green;
            for (int i = 0; i < patrolPoints.Length; i++)
            {
                if (patrolPoints[i] == null) continue;
                Gizmos.DrawWireSphere(patrolPoints[i].position, 0.3f);
                if (i < patrolPoints.Length - 1 && patrolPoints[i + 1] != null)
                    Gizmos.DrawLine(patrolPoints[i].position, patrolPoints[i + 1].position);
                else if (loopPatrol && i == patrolPoints.Length - 1 && patrolPoints[0] != null)
                    Gizmos.DrawLine(patrolPoints[i].position, patrolPoints[0].position);
            }
        }
    }

    #endregion
}