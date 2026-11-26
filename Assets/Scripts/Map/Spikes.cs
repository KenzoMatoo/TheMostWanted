using UnityEngine;

public class Spikes : MonoBehaviour
{
    [Header("Damage Settings")]
    [SerializeField] private int damageAmount = 999;

    [Header("Optional Settings")]
    [SerializeField] private bool canDamageMultipleTimes = false;
    [SerializeField] private float damageCooldown = 1f;

    [Header("Plataformeo")]
    [Tooltip("Si esta activo, al jugador le quita el dano y lo regresa al ultimo lugar seguro donde estuvo parado (no lo mata de un golpe)")]
    [SerializeField] private bool respawnPlayer = false;

    private float lastDamageTime;

    private void OnTriggerEnter2D(Collider2D collision)
    {
        // Intentar obtener el componente IDamageable
        IDamageable damageable = collision.GetComponent<IDamageable>();

        if (damageable != null)
        {
            // Verificar si ya está muerto
            if (damageable.IsDead())
            {
                Debug.Log($"Spikes: {collision.name} ya está muerto, no se aplica daño.");
                return;
            }

            // Verificar cooldown si está habilitado el daño múltiple
            if (canDamageMultipleTimes)
            {
                if (Time.time - lastDamageTime < damageCooldown)
                {
                    return;
                }
                lastDamageTime = Time.time;
            }

            // Aplicar daño
            if (respawnPlayer && TryRespawnHit(collision.gameObject, damageable)) return;
            damageable.TakeDamage(damageAmount);
            Debug.Log($"Spikes causaron {damageAmount} de daño a {collision.name}");
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        // Intentar obtener el componente IDamageable
        IDamageable damageable = collision.gameObject.GetComponent<IDamageable>();

        if (damageable != null)
        {
            // Verificar si ya está muerto
            if (damageable.IsDead())
            {
                Debug.Log($"Spikes: {collision.gameObject.name} ya está muerto, no se aplica daño.");
                return;
            }

            // Verificar cooldown si está habilitado el daño múltiple
            if (canDamageMultipleTimes)
            {
                if (Time.time - lastDamageTime < damageCooldown)
                {
                    return;
                }
                lastDamageTime = Time.time;
            }

            // Aplicar daño
            if (respawnPlayer && TryRespawnHit(collision.gameObject, damageable)) return;
            damageable.TakeDamage(damageAmount);
            Debug.Log($"Spikes causaron {damageAmount} de daño a {collision.gameObject.name}");
        }
    }

    // Jugador: quitar el dano (si no es invencible) y regresarlo al ultimo lugar seguro
    private bool TryRespawnHit(GameObject target, IDamageable damageable)
    {
        var tracker = target.GetComponentInParent<SafeGroundTracker>();
        if (tracker == null) return false;
        if (tracker.IsRespawning) return true;
        damageable.TakeDamage(damageAmount, transform.position);
        if (!damageable.IsDead()) tracker.RespawnAtSafeGround();
        return true;
    }
}
