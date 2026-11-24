using System.Collections;
using UnityEngine;

public class PlayerStats : MonoBehaviour, IDamageable
{
    [Header("Stats Reference")]
    [SerializeField] private ScriptableStats playerStatsData;

    [Header("Health Bar Reference")]
    [SerializeField] private HealthBar healthBar;

    [Header("Damage Sprite")]
    [Tooltip("Sprite que se muestra brevemente cuando el jugador recibe daño.")]
    [SerializeField] private Sprite damageSprite;
    [SerializeField] private float damageSpriteDuration = 0.2f;

    private int currentHealth; // en MASCARAS (ver ScriptableStats.MaxMasks)

    // Mascaras agrietadas: las que perdiste con el ultimo golpe. Si golpeas a un enemigo antes de que
    // se acabe el tiempo, recuperas una por golpe. Si no, se rompen del todo.
    private int crackedMasks;
    private float crackTimer;

    /// <summary>Cambio en mascaras (vida, agrietadas). Lo escucha la UI.</summary>
    public event System.Action MasksChanged;
    public int CrackedMasks => crackedMasks;
    public float CrackTimeLeft01 => playerStatsData != null && playerStatsData.CrackedRecoverTime > 0f ? Mathf.Clamp01(crackTimer / playerStatsData.CrackedRecoverTime) : 0f;

    [Header("Events")]
    public UnityEngine.Events.UnityEvent OnDeath;
    public UnityEngine.Events.UnityEvent<int> OnHealthChanged;

    private bool isDead = false;

    [Header("Scripts to Disable on Death")]
    [SerializeField] private MonoBehaviour[] scriptsToDisable;
    [SerializeField] private bool disableAllScriptsExceptThis = false;

    private Rigidbody2D rb;
    private bool isKnockbackActive = false;
    private Vector2 knockbackDirection;
    private float knockbackTimer = 0f;
    private float knockbackStartDistance = 0f;

    private PlayerController playerController;
    private SpriteRenderer[] spriteRenderers;
    private SpriteRenderer mainSpriteRenderer;
    private Animator animator;
    private bool isInvincible = false;

    // La vida se inicializa en Awake (no en Start) para que la UI y otros scripts que leen la vida
    // en su Start ya vean las mascaras llenas.
    void Awake()
    {
        if (playerStatsData != null) currentHealth = playerStatsData.MaxMasks;
    }

    void Start()
    {
        // Validar que tenemos el ScriptableObject
        if (playerStatsData == null)
        {
            Debug.LogError("PlayerStats: No se asign� ScriptableStats! Asigna el ScriptableObject en el inspector.");
            return;
        }

        rb = GetComponent<Rigidbody2D>();
        if (rb == null && playerStatsData.CanBeKnockback)
        {
            Debug.LogWarning("PlayerStats: No se encontr� Rigidbody2D. El knockback no funcionar�.");
        }

        playerController = GetComponent<PlayerController>();
        spriteRenderers = GetComponentsInChildren<SpriteRenderer>();
        mainSpriteRenderer = GetComponent<SpriteRenderer>();
        animator = GetComponentInChildren<Animator>();

        // Inicializar la vida desde el ScriptableObject
        currentHealth = playerStatsData.MaxMasks;

        // Auto-encontrar la HealthBar si no est� asignada
        if (healthBar == null)
        {
            healthBar = FindObjectOfType<HealthBar>();
            if (healthBar == null)
            {
                Debug.LogWarning("PlayerStats: No se encontr� ninguna HealthBar en la escena.");
            }
        }

        // Actualizar la barra de vida al inicio
        UpdateHealthBar();
    }
    void Update()
    {
        UpdateKnockback();
        UpdateCrackedMasks();
    }

    private void UpdateCrackedMasks()
    {
        if (crackedMasks <= 0) return;
        crackTimer -= Time.deltaTime;
        if (crackTimer <= 0f)
        {
            crackedMasks = 0; // se rompen del todo
            MasksChanged?.Invoke();
        }
    }

    /// <summary>
    /// Llamado cuando el jugador golpea a un enemigo: recupera una mascara agrietada (estilo "rally").
    /// </summary>
    public void OnPlayerHitEnemy()
    {
        if (isDead || crackedMasks <= 0) return;
        crackedMasks--;
        currentHealth = Mathf.Min(currentHealth + 1, playerStatsData.MaxMasks);
        UpdateHealthBar();
        OnHealthChanged?.Invoke(currentHealth);
        MasksChanged?.Invoke();
    }

    #region Knockback

    private void UpdateKnockback()
    {
        if (!isKnockbackActive || rb == null) return;

        knockbackTimer += Time.deltaTime;
        float progress = knockbackTimer / playerStatsData.KnockbackDuration;

        if (progress >= 1f)
        {
            // Knockback completado
            isKnockbackActive = false;
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y); // Mantener velocidad Y

            if (playerController != null)
            {
                playerController.canMove = true;
            }
            return;
        }

        // Aplicar knockback usando la curva de animaci�n
        float curveValue = playerStatsData.KnockbackCurve.Evaluate(progress);
        float currentSpeed = (knockbackStartDistance / playerStatsData.KnockbackDuration) * curveValue;

        // Solo afectar el eje X para el jugador, mantener la velocidad Y (gravedad/salto)
        Vector2 knockbackVelocity = new Vector2(knockbackDirection.x * currentSpeed, rb.linearVelocity.y);
        rb.linearVelocity = knockbackVelocity;
    }

    private void ApplyKnockback(int damageAmount, Vector2 damageSource)
    {
        if (!playerStatsData.CanBeKnockback || rb == null || isDead) return;

        // Calcular el porcentaje de da�o respecto a la vida m�xima
        float damagePercentage = Mathf.Clamp01((float)damageAmount / playerStatsData.maxHealth);

        // Calcular la distancia de knockback basada en el porcentaje de da�o
        // Aplicar reducci�n para el jugador
        knockbackStartDistance = damagePercentage * playerStatsData.MaxKnockbackDistance * playerStatsData.PlayerKnockbackReduction;

        // Calcular la direcci�n del knockback (desde la fuente del da�o hacia el jugador)
        Vector2 playerPosition = transform.position;
        knockbackDirection = (playerPosition - damageSource).normalized;

        // Iniciar el knockback
        isKnockbackActive = true;
        knockbackTimer = 0f;

        // Impulso vertical instant�neo: hace que el knockback se sienta como un peque�o
        // salto hacia el lado contrario del golpe, en vez de solo un empuj�n horizontal.
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, playerStatsData.KnockbackUpForce);

        // Suspender el control del jugador mientras dura el knockback, para que
        // PlayerController.FixedUpdate() no pise la velocidad que aplicamos aqu�.
        if (playerController != null)
        {
            playerController.canMove = false;
        }

        Debug.Log($"Player - Knockback aplicado: {knockbackStartDistance:F2} unidades. Da�o: {damagePercentage * 100:F1}%");
    }

    public void CancelKnockback()
    {
        if (isKnockbackActive && rb != null)
        {
            isKnockbackActive = false;
            // No resetear la velocidad completamente para no interferir con el movimiento del jugador

            if (playerController != null)
            {
                playerController.canMove = true;
            }
        }
    }

    public bool IsKnockbackActive()
    {
        return isKnockbackActive;
    }

    #endregion

    #region Damage Sprite

    private void ShowDamageSprite()
    {
        if (damageSprite == null || mainSpriteRenderer == null) return;

        StopCoroutine(nameof(DamageSpriteCoroutine));
        StartCoroutine(DamageSpriteCoroutine());
    }

    private IEnumerator DamageSpriteCoroutine()
    {
        // El Animator reescribe el sprite todos los frames mientras est� activo
        // (incluso con un solo estado sin transiciones), as� que hay que apagarlo
        // durante la ventana del sprite de da�o o la asignaci�n de abajo no se ver�a.
        if (animator != null) animator.enabled = false;
        mainSpriteRenderer.sprite = damageSprite;

        yield return new WaitForSeconds(damageSpriteDuration);

        if (animator != null) animator.enabled = true;
    }

    #endregion

    #region Invincibility

    public bool IsInvincible()
    {
        return isInvincible;
    }

    private void StartInvincibility()
    {
        if (playerStatsData.InvincibilityDuration <= 0f) return;

        StopInvincibilityRoutine();
        invincibilityRoutine = StartCoroutine(InvincibilityCoroutine());
    }

    // Pares (collider del jugador, collider de enemigo) que ignoramos durante la invencibilidad,
    // para restaurar solo esos (el enemigo capturado ya ignora al jugador por su cuenta).
    private readonly System.Collections.Generic.List<(Collider2D, Collider2D)> ignoredEnemyPairs =
        new System.Collections.Generic.List<(Collider2D, Collider2D)>();

    /// <summary>
    /// Durante los i-frames el jugador atraviesa a los enemigos (no lo empujan ni lo bloquean),
    /// asi puede salir de encima de ellos despues de recibir un golpe.
    /// </summary>
    private void SetPassThroughEnemies(bool passThrough)
    {
        if (passThrough)
        {
            var playerColliders = GetComponentsInChildren<Collider2D>();
            foreach (var enemy in FindObjectsByType<EnemyBase>(FindObjectsSortMode.None))
            {
                foreach (var enemyCol in enemy.GetComponentsInChildren<Collider2D>())
                {
                    if (enemyCol.isTrigger) continue;
                    foreach (var playerCol in playerColliders)
                    {
                        if (playerCol.isTrigger || Physics2D.GetIgnoreCollision(playerCol, enemyCol)) continue;
                        Physics2D.IgnoreCollision(playerCol, enemyCol, true);
                        ignoredEnemyPairs.Add((playerCol, enemyCol));
                    }
                }
            }
        }
        else
        {
            foreach (var (playerCol, enemyCol) in ignoredEnemyPairs)
            {
                if (playerCol != null && enemyCol != null) Physics2D.IgnoreCollision(playerCol, enemyCol, false);
            }
            ignoredEnemyPairs.Clear();
        }
    }

    private Coroutine invincibilityRoutine;

    // StopCoroutine(nameof(...)) no detiene corrutinas iniciadas con StartCoroutine(IEnumerator),
    // asi que se guarda la referencia.
    private void StopInvincibilityRoutine()
    {
        if (invincibilityRoutine != null) StopCoroutine(invincibilityRoutine);
        invincibilityRoutine = null;
    }

    private IEnumerator InvincibilityCoroutine()
    {
        isInvincible = true;
        SetPassThroughEnemies(false); // por si venia de otra invencibilidad
        SetPassThroughEnemies(true);

        float elapsed = 0f;
        float interval = Mathf.Max(0.01f, playerStatsData.InvincibilityFlickerInterval);

        while (elapsed < playerStatsData.InvincibilityDuration)
        {
            SetSpritesVisible(false);
            yield return new WaitForSeconds(interval);
            SetSpritesVisible(true);
            yield return new WaitForSeconds(interval);
            elapsed += interval * 2f;
        }

        SetSpritesVisible(true);
        isInvincible = false;
        SetPassThroughEnemies(false);
    }

    private void SetSpritesVisible(bool visible)
    {
        if (spriteRenderers == null) return;

        foreach (SpriteRenderer sr in spriteRenderers)
        {
            if (sr != null) sr.enabled = visible;
        }
    }

    #endregion

    #region IDamageable Implementation

    public void TakeDamage(int amount, Vector2 damageSourcePosition = default)
    {
        if (isDead || isInvincible) return; // No recibir da�o si ya est� muerto o es invencible

        // El dano de los enemigos se convierte a mascaras (al menos 1 por golpe)
        int perMask = Mathf.Max(1, playerStatsData.DamagePerMask);
        int masksLost = Mathf.Max(1, Mathf.CeilToInt((float)amount / perMask));
        masksLost = Mathf.Min(masksLost, currentHealth);
        currentHealth -= masksLost;
        currentHealth = Mathf.Max(currentHealth, 0); // No bajar de 0

        // Las mascaras de este golpe quedan agrietadas (las agrietadas de antes ya se rompen)
        crackedMasks = currentHealth > 0 ? masksLost : 0;
        crackTimer = playerStatsData.CrackedRecoverTime;
        MasksChanged?.Invoke();

        Debug.Log($"Player recibi� {amount} de da�o. Vida actual: {currentHealth}/{playerStatsData.MaxMasks} mascaras");

        if (damageSourcePosition != default)
        {
            ApplyKnockback(amount, damageSourcePosition);
        }

        UpdateHealthBar();

        PlayerEffects effects = GetComponent<PlayerEffects>();
        if (effects != null) effects.PlayDamage(damageSourcePosition);

        // Invocar evento de cambio de vida
        OnHealthChanged?.Invoke(currentHealth);

        // Verificar si muri�
        if (currentHealth <= 0)
        {
            Die();
        }
        else
        {
            PlayerSounds sounds = GetComponent<PlayerSounds>();
            if (sounds != null) sounds.PlayDamage();

            ShowDamageSprite();
            StartInvincibility();
        }
    }
    public bool IsDead()
    {
        return isDead;
    }
    public int GetCurrentHealth()
    {
        return currentHealth;
    }
    public int GetMaxHealth()
    {
        return playerStatsData != null ? playerStatsData.MaxMasks : 0;
    }
    public float GetHealthPercentage()
    {
        if (playerStatsData == null || playerStatsData.MaxMasks <= 0)
            return 0f;

        return (float)currentHealth / playerStatsData.MaxMasks;
    }

    #endregion

    #region Additional Methods

    /// <summary>
    /// Cura al jugador
    /// </summary>
    public void Heal(int amount)
    {
        if (isDead || playerStatsData == null) return;

        currentHealth += amount;
        currentHealth = Mathf.Min(currentHealth, playerStatsData.MaxMasks);
        crackedMasks = Mathf.Min(crackedMasks, playerStatsData.MaxMasks - currentHealth); // no puede haber mas agrietadas que mascaras faltantes

        Debug.Log($"Player curado {amount}. Vida actual: {currentHealth}/{playerStatsData.MaxMasks}");

        // Actualizar la barra de vida
        UpdateHealthBar();

        OnHealthChanged?.Invoke(currentHealth);
        MasksChanged?.Invoke();
    }

    /// <summary>
    /// Restaura la vida al m�ximo
    /// </summary>
    public void FullHeal()
    {
        if (isDead || playerStatsData == null) return;

        currentHealth = playerStatsData.MaxMasks;
        crackedMasks = 0;
        MasksChanged?.Invoke();

        Debug.Log("Player completamente curado!");

        // Actualizar la barra de vida
        UpdateHealthBar();

        OnHealthChanged?.Invoke(currentHealth);
    }

    private void Die()
    {
        if (isDead) return;

        isDead = true;
        Debug.Log("Player ha muerto!");

        PlayerSounds sounds = GetComponent<PlayerSounds>();
        if (sounds != null) sounds.PlayDeath();

        CancelKnockback();

        StopInvincibilityRoutine();
        isInvincible = false;
        SetPassThroughEnemies(false);
        SetSpritesVisible(true);

        StopCoroutine(nameof(DamageSpriteCoroutine));
        if (animator != null) animator.enabled = true;

        UpdateHealthBar();

        // Si el jugador tenía un enemigo capturado, soltarlo ANTES de desactivar
        // CharacterCombat - si no, el enemigo queda siguiendo el mouse para siempre,
        // porque CharacterCombat (quien lo controla) se apaga y nadie más lo suelta.
        CharacterCombat combat = GetComponent<CharacterCombat>();
        if (combat != null)
        {
            combat.ReleaseEnemy();
        }

        DisablePlayerScripts();

        // Invocar evento de muerte
        OnDeath?.Invoke();

        // - Reproducir animaci�n de muerte
        // - Mostrar pantalla de Game Over
    }
    /// <summary>
    /// Desactiva los scripts del jugador al morir
    /// </summary>
    private void DisablePlayerScripts()
    {
        if (disableAllScriptsExceptThis)
        {
            // Desactiva TODOS los scripts excepto PlayerStats
            MonoBehaviour[] allScripts = GetComponents<MonoBehaviour>();
            foreach (MonoBehaviour script in allScripts)
            {
                if (script != this && script.enabled)
                {
                    script.enabled = false;
                    Debug.Log($"Script desactivado: {script.GetType().Name}");
                }
            }
        }
        else if (scriptsToDisable != null && scriptsToDisable.Length > 0)
        {
            // Desactiva solo los scripts especificados en el inspector
            foreach (MonoBehaviour script in scriptsToDisable)
            {
                if (script != null && script.enabled)
                {
                    script.enabled = false;
                    Debug.Log($"Script desactivado: {script.GetType().Name}");
                }
            }
        }
    }

    /// <summary>
    /// Reactiva los scripts al revivir
    /// </summary>
    private void EnablePlayerScripts()
    {
        if (disableAllScriptsExceptThis)
        {
            MonoBehaviour[] allScripts = GetComponents<MonoBehaviour>();
            foreach (MonoBehaviour script in allScripts)
            {
                if (script != this && !script.enabled)
                {
                    script.enabled = true;
                    Debug.Log($"Script reactivado: {script.GetType().Name}");
                }
            }
        }
        else if (scriptsToDisable != null && scriptsToDisable.Length > 0)
        {
            foreach (MonoBehaviour script in scriptsToDisable)
            {
                if (script != null && !script.enabled)
                {
                    script.enabled = true;
                    Debug.Log($"Script reactivado: {script.GetType().Name}");
                }
            }
        }
    }

    /// <summary>
    /// Revive al jugador (�til para respawn)
    /// </summary>
    public void Revive()
    {
        if (playerStatsData == null) return;

        isDead = false;
        currentHealth = playerStatsData.MaxMasks;
        crackedMasks = 0;
        MasksChanged?.Invoke();

        CancelKnockback();

        StopInvincibilityRoutine();
        isInvincible = false;
        SetPassThroughEnemies(false);
        SetSpritesVisible(true);

        StopCoroutine(nameof(DamageSpriteCoroutine));
        if (animator != null) animator.enabled = true;

        // NUEVO: Reactivar los scripts del jugador
        EnablePlayerScripts();

        if (animator != null && animator.runtimeAnimatorController != null)
        {
            animator.SetBool("isDeath", false);
            animator.Play("Idle", 0, 0f); // Forzar la animaci�n Idle inmediatamente
        }

        Debug.Log("Player revivido!");

        // Actualizar la barra de vida
        UpdateHealthBar();

        OnHealthChanged?.Invoke(currentHealth);
    }


    /// <summary>
    /// Actualiza la HealthBar con los valores actuales
    /// </summary>
    private void UpdateHealthBar()
    {
        if (healthBar != null && playerStatsData != null)
        {
            healthBar.UpdateHealthBar(currentHealth, playerStatsData.MaxMasks);
        }
    }

    /// <summary>
    /// Permite cambiar la referencia de la HealthBar en runtime
    /// </summary>
    public void SetHealthBar(HealthBar newHealthBar)
    {
        healthBar = newHealthBar;
        UpdateHealthBar();
    }

    #endregion
}