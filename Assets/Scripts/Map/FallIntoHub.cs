using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// Llegada al HUB despues del tutorial: el jugador aparece mas arriba de su lugar normal y cae
// mientras la pantalla vuelve de negro, como si hubiera caido por el hueco del tutorial.
// TutorialFinale pone Pending = true justo antes de cargar el HUB.
public class FallIntoHub : MonoBehaviour
{
    public static bool Pending;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        SceneManager.sceneLoaded += (scene, mode) =>
        {
            if (!Pending) return;
            Pending = false;
            var player = FindAnyObjectByType<PlayerController>();
            if (player == null) return;
            player.gameObject.AddComponent<FallIntoHub>();
        };
    }

    [SerializeField] private float dropHeight = 14f;
    [SerializeField] private float fadeInTime = 0.7f;

    private IEnumerator Start()
    {
        // Black first, so the jump up is never seen
        var fader = CameraRoom.GetFader();
        fader.alpha = 1f;

        // As high as possible (up to dropHeight) without going into the ceiling
        var controller = GetComponent<PlayerController>();
        LayerMask ground = controller != null && controller.stats != null ? controller.stats.GroundLayer : (LayerMask)~0;
        Vector2 start = transform.position;
        RaycastHit2D hit = Physics2D.Raycast(start + Vector2.up * 0.5f, Vector2.up, dropHeight, ground);
        float up = hit.collider != null ? Mathf.Max(0f, hit.distance - 1.5f) : dropHeight;
        Vector3 pos = transform.position + Vector3.up * up;
        transform.position = pos;
        var rb = GetComponent<Rigidbody2D>();
        if (rb != null) { rb.position = pos; rb.linearVelocity = new Vector2(0f, -6f); }
        Physics2D.SyncTransforms();

        yield return null;   // let the camera snap to the new position
        yield return CameraRoom.Fade(fader, 0f, fadeInTime);
        Destroy(this);
    }
}
