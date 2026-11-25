using UnityEngine;

/// Bala rápida del sniper. Se mueve y detecta impactos con raycast (no usa físicas),
/// así que no atraviesa paredes aunque vaya muy rápido.
public class SniperBullet : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool logHits = false;   // actívalo para ver en la Console qué golpea la bala

    private Vector2 dir;
    private float speed;
    private int damage;
    private LayerMask hitMask;
    private Transform owner;                          // quien disparó (opcional)
    private float life = 4f;

    /// owner es opcional: si lo pasas, la bala ignora a quien la disparó.
    public void Init(Vector2 direction, float bulletSpeed, int bulletDamage, LayerMask mask, Transform owner = null)
    {
        dir = direction.normalized;
        speed = bulletSpeed;
        damage = bulletDamage;
        hitMask = mask;
        this.owner = owner;
        transform.right = dir;

        // Si el prefab no tiene visual, crea una estela simple
        if (GetComponentInChildren<Renderer>() == null)
        {
            LineRenderer lr = gameObject.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.positionCount = 2;
            lr.SetPosition(0, Vector3.left * 0.6f);
            lr.SetPosition(1, Vector3.zero);
            lr.startWidth = lr.endWidth = 0.08f;
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startColor = lr.endColor = new Color(1f, 0.85f, 0.3f);
            lr.sortingOrder = 11;
        }
    }

    void Update()
    {
        // Si no se inicializó (por ejemplo, una bala puesta a mano en la escena), no hace nada
        if (speed <= 0f) return;

        float step = speed * Time.deltaTime;
        RaycastHit2D[] hits = Physics2D.RaycastAll(transform.position, dir, step, hitMask);

        // RaycastAll devuelve los impactos ordenados por distancia
        foreach (RaycastHit2D hit in hits)
        {
            Collider2D c = hit.collider;
            if (c == null) continue;

            // Ignorar triggers (zonas, sensores, etc.)
            if (c.isTrigger) continue;

            // Ignorar a quien disparó y a cualquier sniper (sin fuego amigo)
            if (owner != null && c.transform.IsChildOf(owner)) continue;
            if (c.GetComponentInParent<SniperEnemy>() != null) continue;

            if (logHits)
                Debug.Log($"[SniperBullet] Impacto en '{c.name}' (capa {LayerMask.LayerToName(c.gameObject.layer)})", c);

            IDamageable target = c.GetComponentInParent<IDamageable>();
            if (target != null)
            {
                // La "fuente" va detrás del impacto para que el knockback empuje en la dirección de la bala
                target.TakeDamage(damage, hit.point - dir * 0.5f);
            }

            Destroy(gameObject);
            return;
        }

        transform.position += (Vector3)(dir * step);

        life -= Time.deltaTime;
        if (life <= 0f) Destroy(gameObject);
    }
}
