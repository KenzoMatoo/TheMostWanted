using UnityEngine;

// Estacion de "absorber" del tutorial: absorber solo funciona si te falta vida. Si el jugador
// entra a esta zona con la vida llena, recibe un golpe de una mascara (como si el muneco le pegara),
// asi siempre puede absorber. Pasa cada vez que entra con la vida llena (el tutorial obliga a absorber).
[RequireComponent(typeof(BoxCollider2D))]
public class AbsorbStation : MonoBehaviour
{
    [Tooltip("De donde 'viene' el golpe (para el empujon). Si esta vacio, desde este objeto.")]
    [SerializeField] private Transform hitFrom;
    [Tooltip("Segundos minimos entre dos golpes")]
    [SerializeField] private float cooldown = 1.5f;
    private float lastHit = -99f;

    void Awake() => GetComponent<BoxCollider2D>().isTrigger = true;

    void OnTriggerEnter2D(Collider2D other)
    {
        var stats = other.GetComponentInParent<PlayerStats>();
        if (stats == null || Time.time - lastHit < cooldown) return;
        if (stats.GetCurrentHealth() < stats.GetMaxHealth()) return; // ya le falta vida
        lastHit = Time.time;
        Vector2 from = hitFrom != null ? (Vector2)hitFrom.position : (Vector2)transform.position;
        stats.TakeDamage(1, from); // 1 de dano = 1 mascara
    }
}
