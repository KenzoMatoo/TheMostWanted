using UnityEngine;

public class EnemyBandido : MeleeChaserEnemy
{
    [Header("Patrol Points")]
    [SerializeField] private Transform[] patrolPoints;
    [SerializeField] private bool loopPatrol = true; // true = loop, false = ping-pong

    [Header("Jump Settings")]
    [SerializeField] private bool canJump = true;
    [SerializeField] private float jumpForce = 10f;
    [SerializeField] private float jumpCooldown = 1f;
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.2f;
    [SerializeField] private Transform obstacleCheck;
    [SerializeField] private float obstacleCheckRadius = 0.2f;
    [SerializeField] private float obstacleCheckDistance = 0.5f;
    [SerializeField] private float playerAboveDetectionHeight = 1f;
    [SerializeField] private bool debugJump = true;

    [Header("Animaciones Cactus")]
    [SerializeField] private Animator cactusAnimator;
    [SerializeField] private float animCrossFade = 0.05f;
    [SerializeField] private float walkVelocityThreshold = 0.1f;

    [Header("Engaño (Spot)")]
    [Tooltip("Tiempo que el cactus se queda quieto 'fingiendo ser amigable' antes de atacar")]
    [SerializeField] private float spotDuration = 1.2f;
    [Tooltip("Segundos sin perseguir al jugador para que el engaño se reinicie")]
    [SerializeField] private float spotResetDelay = 3f;

    // Nombres de los estados del Animator
    private static readonly int AnimIdle = Animator.StringToHash("CactusIdle");
    private static readonly int AnimWalk = Animator.StringToHash("CactusWalk");
    private static readonly int AnimSpot = Animator.StringToHash("CactusSpot");
    private static readonly int AnimAttack = Animator.StringToHash("CactusAttack");
    private int currentAnimHash = 0;

    private int currentPatrolIndex = 0;
    private bool patrolForward = true;

    private float jumpTimer = 0f;
    public bool isGrounded = false;

    // Control del engaño
    private bool hasSpotted = false;   // ya hizo el engaño en esta persecución
    private bool isSpotting = false;   // está haciendo el engaño ahora mismo
    private float spotTimer = 0f;
    private float lastChaseTime = -999f;

    protected override void Start()
    {
        base.Start();

        if (cactusAnimator == null)
            cactusAnimator = GetComponentInChildren<Animator>();

        if (groundCheck == null)
        {
            GameObject groundCheckObj = new GameObject("GroundCheck");
            groundCheckObj.transform.parent = transform;
            groundCheckObj.transform.localPosition = new Vector3(0, -0.5f, 0);
            groundCheck = groundCheckObj.transform;
        }
        if (obstacleCheck == null)
        {
            GameObject obstacleCheckObj = new GameObject("ObstacleCheck");
            obstacleCheckObj.transform.parent = transform;
            obstacleCheckObj.transform.localPosition = new Vector3(0, 0.3f, 0);
            obstacleCheck = obstacleCheckObj.transform;
        }

        if (patrolPoints == null || patrolPoints.Length == 0)
        {
            Debug.LogWarning($"{gameObject.name} no tiene puntos de patrulla asignados. Creando patrulla simple.");
            CreateDefaultPatrolPoints();
        }

        PlayAnim(AnimIdle);

        if (logBehaviorDetails)
        {
            Debug.Log($"Bandido {gameObject.name} inicializado");
        }
    }

    protected override void OnActiveUpdate()
    {
        UpdateJumpTimer();
        CheckGrounded();

        // Marca la última vez que estuvo persiguiendo (o con el jugador en rango de ataque)
        if (currentState == ChaserState.Chase || IsPlayerInAttackRange())
        {
            lastChaseTime = Time.time;
        }

        // Reiniciar el engaño solo si perdió al jugador por un buen rato
        if (hasSpotted && Time.time - lastChaseTime > spotResetDelay)
        {
            hasSpotted = false;
            isSpotting = false;
        }

        // El temporizador del Spot corre aquí, sin depender del estado
        if (isSpotting)
        {
            spotTimer -= Time.deltaTime;
            if (spotTimer <= 0f)
            {
                isSpotting = false;
                hasSpotted = true; // el engaño ya se hizo, ahora ataca
            }
        }

        // No saltar mientras hace el engaño
        if (canJump && isGrounded && jumpTimer <= 0 && !isSpotting && !IsFullyStunned())
        {
            if (currentState == ChaserState.Chase || currentState == ChaserState.Patrol)
            {
                if (ShouldJump())
                {
                    PerformJump();
                }
            }
        }

        UpdateAnimation();
    }

    #region Animation

    private void PlayAnim(int hash)
    {
        if (cactusAnimator == null || currentAnimHash == hash) return;

        currentAnimHash = hash;
        cactusAnimator.CrossFade(hash, animCrossFade, 0, 0f);
    }

    private bool IsPlayerInAttackRange()
    {
        Vector2 attackPos = attackPoint != null ? (Vector2)attackPoint.position : (Vector2)transform.position;
        return Physics2D.OverlapCircle(attackPos, attackRadius, playerLayer) != null;
    }

    private void UpdateAnimation()
    {
        // 1) Engaño: tiene prioridad
        if (isSpotting)
        {
            PlayAnim(AnimSpot);
            return;
        }

        // 2) Persiguiendo: ataque si está en rango, si no camina
        if (currentState == ChaserState.Chase)
        {
            PlayAnim(IsPlayerInAttackRange() ? AnimAttack : AnimWalk);
            return;
        }

        // 3) Patrulla / espera: se ve inofensivo
        float vx = rb != null ? Mathf.Abs(rb.linearVelocity.x) : 0f;
        PlayAnim(vx > walkVelocityThreshold ? AnimWalk : AnimIdle);
    }

    #endregion

    #region Patrol Behavior

    protected override void PatrolBehavior()
    {
        if (patrolPoints == null || patrolPoints.Length == 0) return;

        Transform targetPoint = patrolPoints[currentPatrolIndex];
        if (targetPoint == null) return;

        Vector2 direction = (targetPoint.position - transform.position);
        direction.y = 0;
        direction = direction.normalized;

        float speed = patrolSpeed * GetMovementSpeedMultiplier();

        if (rb != null)
        {
            rb.linearVelocity = new Vector2(direction.x * speed, rb.linearVelocity.y);
        }
        else
        {
            transform.position += (Vector3)direction * speed * Time.deltaTime;
        }

        lastMoveDirection = direction;
        UpdateSpriteFlip(direction.x);

        float distanceToPoint = Mathf.Abs(transform.position.x - targetPoint.position.x);
        if (distanceToPoint <= waypointReachDistance)
        {
            OnReachedPatrolPoint();
        }
    }

    private void OnReachedPatrolPoint()
    {
        if (loopPatrol)
        {
            currentPatrolIndex = (currentPatrolIndex + 1) % patrolPoints.Length;
        }
        else
        {
            if (patrolPoints.Length <= 1)
            {
                currentPatrolIndex = 0;
            }
            else if (patrolForward)
            {
                currentPatrolIndex++;
                if (currentPatrolIndex >= patrolPoints.Length)
                {
                    currentPatrolIndex = patrolPoints.Length - 2;
                    patrolForward = false;
                }
            }
            else
            {
                currentPatrolIndex--;
                if (currentPatrolIndex < 0)
                {
                    currentPatrolIndex = 1;
                    patrolForward = true;
                }
            }
        }

        ChangeState(ChaserState.Waiting);
    }

    protected override void WaitBehavior()
    {
        if (rb != null)
        {
            rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
        }

        TickWaitTimer();
    }

    private void CreateDefaultPatrolPoints()
    {
        GameObject patrolContainer = new GameObject($"{gameObject.name}_PatrolPoints");

        GameObject point1 = new GameObject("PatrolPoint1");
        point1.transform.parent = patrolContainer.transform;
        point1.transform.position = transform.position + Vector3.left * 3f;

        GameObject point2 = new GameObject("PatrolPoint2");
        point2.transform.parent = patrolContainer.transform;
        point2.transform.position = transform.position + Vector3.right * 3f;

        patrolPoints = new Transform[] { point1.transform, point2.transform };
    }

    #endregion

    #region Chase Behavior

    protected override void ChaseBehavior()
    {
        if (player == null)
        {
            ChangeState(ChaserState.Patrol);
            return;
        }

        Vector2 direction = (player.position - transform.position);
        direction.y = 0;
        direction = direction.normalized;

        // --- Engaño: solo la primera vez que ve al jugador ---
        if (!hasSpotted && !isSpotting)
        {
            isSpotting = true;
            spotTimer = spotDuration;
        }

        if (isSpotting)
        {
            // Quieto, mirando al jugador
            if (rb != null)
            {
                rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
            }
            UpdateSpriteFlip(direction.x);
            return;
        }

        // --- Persecución normal ---
        float speed = chaseSpeed * GetMovementSpeedMultiplier();

        if (rb != null)
        {
            rb.linearVelocity = new Vector2(direction.x * speed, rb.linearVelocity.y);
        }
        else
        {
            transform.position += new Vector3(direction.x * speed * Time.deltaTime, 0, 0);
        }

        lastMoveDirection = direction;
        UpdateSpriteFlip(direction.x);
    }

    #endregion

    #region Jump Behavior
    private void CheckGrounded()
    {
        if (groundCheck == null) return;

        isGrounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);
    }

    private void UpdateJumpTimer()
    {
        if (jumpTimer > 0)
        {
            jumpTimer -= Time.deltaTime;
        }
    }

    private bool ShouldJump()
    {
        bool hasObstacle = CheckObstacleAhead();
        bool playerAbove = IsPlayerAbove();

        if (hasObstacle || playerAbove)
        {
            if (debugJump)
            {
                string reason = hasObstacle ? "obstáculo detectado" : "jugador encima";
                Debug.Log($"{gameObject.name} va a saltar: {reason}");
            }
            return true;
        }

        return false;
    }

    private bool CheckObstacleAhead()
    {
        if (obstacleCheck == null) return false;

        float direction = lastMoveDirection.x != 0 ? Mathf.Sign(lastMoveDirection.x) :
                         (transform.localScale.x > 0 ? -1f : 1f);

        Vector2 checkPosition = (Vector2)obstacleCheck.position + new Vector2(direction * obstacleCheckDistance, 0);
        bool hasObstacle = Physics2D.OverlapCircle(checkPosition, obstacleCheckRadius, groundLayer);

        if (debugJump && hasObstacle)
        {
            Debug.Log($"{gameObject.name} detectó obstáculo adelante");
        }

        return hasObstacle;
    }

    private bool IsPlayerAbove()
    {
        if (player == null) return false;

        Vector2 boxCenter = (Vector2)transform.position + Vector2.up * (playerAboveDetectionHeight * 0.5f);
        Vector2 boxSize = new Vector2(1f, playerAboveDetectionHeight);

        Collider2D hit = Physics2D.OverlapBox(boxCenter, boxSize, 0f, playerLayer);

        if (debugJump && hit != null)
        {
            Debug.DrawLine(transform.position, boxCenter + Vector2.up * (playerAboveDetectionHeight * 0.5f), Color.magenta);
        }

        return hit != null;
    }

    private void PerformJump()
    {
        if (rb == null) return;

        rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
        jumpTimer = jumpCooldown;

        if (debugJump)
        {
            Debug.Log($"{gameObject.name} realizó un salto con fuerza {jumpForce}");
        }

        OnJumpPerformed();
    }

    protected virtual void OnJumpPerformed()
    {
        // Override para efectos de sonido/visuales
    }

    #endregion

    #region Visual Feedback

    protected override void UpdateSpriteFlip(float directionX)
    {
        if (!autoFlipSprite || spriteRenderer == null) return;

        Vector3 localScale = transform.localScale;

        if (directionX > 0.01f)
        {
            localScale.x = -Mathf.Abs(localScale.x);
        }
        else if (directionX < -0.01f)
        {
            localScale.x = Mathf.Abs(localScale.x);
        }

        transform.localScale = localScale;
    }

    #endregion

    #region Custom Override Methods

    protected override void InitializeEnemy()
    {
        base.InitializeEnemy();
        if (logBehaviorDetails)
        {
            Debug.Log($"Bandido {gameObject.name} inicializado con {GetMaxHealth()} HP");
        }
    }

    #endregion

    #region Gizmos
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        Gizmos.color = Color.red;
        Vector3 attackPos = attackPoint != null ? attackPoint.position : transform.position;
        Gizmos.DrawWireSphere(attackPos, attackRadius);

        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(transform.position, loseTargetDistance);

        if (patrolPoints != null && patrolPoints.Length > 0)
        {
            Gizmos.color = Color.green;
            for (int i = 0; i < patrolPoints.Length; i++)
            {
                if (patrolPoints[i] != null)
                {
                    Gizmos.DrawWireSphere(patrolPoints[i].position, 0.3f);

                    if (i < patrolPoints.Length - 1 && patrolPoints[i + 1] != null)
                    {
                        Gizmos.DrawLine(patrolPoints[i].position, patrolPoints[i + 1].position);
                    }
                    else if (loopPatrol && i == patrolPoints.Length - 1 && patrolPoints[0] != null)
                    {
                        Gizmos.DrawLine(patrolPoints[i].position, patrolPoints[0].position);
                    }
                }
            }
        }

        if (Application.isPlaying && player != null && debugLineOfSight)
        {
            Vector3 visionPos = visionOrigin != null ? visionOrigin.position : transform.position;
            Gizmos.color = hasLineOfSight ? Color.green : Color.red;
            Gizmos.DrawLine(visionPos, player.position);
        }

        if (groundCheck != null)
        {
            Gizmos.color = Application.isPlaying && isGrounded ? Color.green : Color.red;
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
        }

        if (canJump)
        {
            Vector3 boxCenter = transform.position + Vector3.up * (playerAboveDetectionHeight * 0.5f);
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireCube(boxCenter, new Vector3(1f, playerAboveDetectionHeight, 0.1f));
        }
    }

    #endregion
}