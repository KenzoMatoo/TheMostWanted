using UnityEngine;

// Zona de checkpoint del tutorial: cuando el jugador la toca, ese es su nuevo punto de respawn
// si muere (ver TutorialRespawn). Pon un BoxCollider2D trigger en la entrada de cada parte.
[RequireComponent(typeof(BoxCollider2D))]
public class TutorialCheckpoint : MonoBehaviour
{
    [Tooltip("Donde aparece el jugador. Si esta vacio, usa la posicion de este objeto.")]
    [SerializeField] private Transform spawnPoint;

    public static Vector3? Current { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Current = null;

    void Awake()
    {
        GetComponent<BoxCollider2D>().isTrigger = true;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponentInParent<PlayerController>() == null) return;
        Current = spawnPoint != null ? spawnPoint.position : transform.position;
    }

    void OnDrawGizmos()
    {
        var box = GetComponent<BoxCollider2D>();
        if (box == null) return;
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.25f);
        Gizmos.DrawCube(transform.TransformPoint(box.offset), Vector3.Scale(box.size, transform.lossyScale));
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(spawnPoint != null ? spawnPoint.position : transform.position, 0.5f);
    }
}
