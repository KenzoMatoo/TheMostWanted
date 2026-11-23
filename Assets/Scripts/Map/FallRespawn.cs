using System.Collections;
using UnityEngine;

// Zona de caida: si el jugador entra (se cae por un hueco), la pantalla se va a negro,
// el jugador aparece en el punto de respawn y la pantalla vuelve. No hace dano.
// Pon el BoxCollider2D (trigger) debajo de los huecos y arrastra un Transform a "Respawn Point".
[RequireComponent(typeof(BoxCollider2D))]
public class FallRespawn : MonoBehaviour
{
    [SerializeField] private Transform respawnPoint;
    [SerializeField] private float fadeOutTime = 0.25f;
    [SerializeField] private float fadeInTime = 0.3f;

    private bool busy;

    void Awake()
    {
        GetComponent<BoxCollider2D>().isTrigger = true;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (busy || respawnPoint == null) return;
        var player = other.GetComponentInParent<PlayerController>();
        if (player != null) StartCoroutine(Respawn(player));
    }

    IEnumerator Respawn(PlayerController player)
    {
        busy = true;
        var fader = CameraRoom.GetFader();
        yield return CameraRoom.Fade(fader, 1f, fadeOutTime);

        // Soltar el gancho por si iba colgado
        var hook = player.GetComponent<GrapplingHook>();
        if (hook != null) hook.ReleaseHook();

        player.transform.position = respawnPoint.position;
        var rb = player.GetComponent<Rigidbody2D>();
        if (rb != null) rb.linearVelocity = Vector2.zero;
        player.canMove = true;

        yield return new WaitForSecondsRealtime(0.1f); // que la camara alcance al jugador antes de mostrar
        yield return CameraRoom.Fade(fader, 0f, fadeInTime);
        busy = false;
    }

    void OnDrawGizmos()
    {
        var box = GetComponent<BoxCollider2D>();
        if (box == null) return;
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.35f);
        Gizmos.DrawCube(transform.TransformPoint(box.offset), Vector3.Scale(box.size, transform.lossyScale));
        if (respawnPoint != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(respawnPoint.position, 0.5f);
        }
    }
}
