using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// Recuerda el ultimo lugar seguro donde el jugador estuvo parado (piso firme, lejos de pinchos)
// y lo regresa ahi cuando toca un peligro (pinchos con "respawnPlayer"), como en Hollow Knight:
// la pantalla se va a negro, el jugador aparece en el lugar seguro y la pantalla vuelve.
// Se agrega solo al jugador en cada escena.
public class SafeGroundTracker : MonoBehaviour
{
    [Tooltip("No se guarda un lugar seguro si hay pinchos a esta distancia (tiles)")]
    [SerializeField] private float hazardMargin = 1.6f;
    [Tooltip("Segundos parado en el piso antes de contar como lugar seguro")]
    [SerializeField] private float settleTime = 0.12f;
    [SerializeField] private float fadeOutTime = 0.15f;
    [SerializeField] private float fadeInTime = 0.25f;

    private PlayerController controller;
    private PlayerStats stats;
    private GrapplingHook hook;
    private Rigidbody2D rb;
    private Collider2D body;
    private Vector3 safePosition;
    private bool hasSafePosition;
    private float groundedTime;
    private bool respawning;

    public bool IsRespawning => respawning;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        AddToPlayer();
        SceneManager.sceneLoaded += (s, m) => AddToPlayer();
    }

    private static void AddToPlayer()
    {
        var player = FindAnyObjectByType<PlayerController>();
        if (player != null && player.GetComponent<SafeGroundTracker>() == null) player.gameObject.AddComponent<SafeGroundTracker>();
    }

    private void Awake()
    {
        controller = GetComponent<PlayerController>();
        stats = GetComponent<PlayerStats>();
        hook = GetComponent<GrapplingHook>();
        rb = GetComponent<Rigidbody2D>();
        body = GetComponent<Collider2D>();
        safePosition = transform.position;
        hasSafePosition = true;
    }

    private void FixedUpdate()
    {
        if (respawning || controller == null) return;

        bool grounded = controller.IsGrounded() && !controller.IsDashing && (hook == null || !hook.IsHooked())
                        && rb != null && Mathf.Abs(rb.linearVelocity.y) < 0.5f;
        groundedTime = grounded ? groundedTime + Time.fixedDeltaTime : 0f;
        if (groundedTime < settleTime) return;
        if (!BothFeetOnGround() || NearHazard()) return;

        safePosition = transform.position;
        hasSafePosition = true;
    }

    // Both sides of the body over ground, so the safe spot is never on the very edge of a ledge
    private bool BothFeetOnGround()
    {
        if (body == null || controller.stats == null) return true;
        Bounds b = body.bounds;
        float y = b.min.y + 0.05f;
        LayerMask ground = controller.stats.GroundLayer;
        return Physics2D.Raycast(new Vector2(b.min.x + 0.1f, y), Vector2.down, 0.4f, ground)
            && Physics2D.Raycast(new Vector2(b.max.x - 0.1f, y), Vector2.down, 0.4f, ground);
    }

    private bool NearHazard()
    {
        Vector2 center = body != null ? (Vector2)body.bounds.center : (Vector2)transform.position;
        foreach (Collider2D c in Physics2D.OverlapCircleAll(center, hazardMargin))
            if (c.GetComponent<Spikes>() != null) return true;
        return false;
    }

    /// <summary>Fade out, put the player back on the last safe spot, fade in.</summary>
    public void RespawnAtSafeGround()
    {
        if (respawning || !hasSafePosition) return;
        StartCoroutine(Respawn());
    }

    private IEnumerator Respawn()
    {
        respawning = true;
        if (controller != null) controller.canMove = false;
        if (rb != null) rb.linearVelocity = Vector2.zero;

        var fader = CameraRoom.GetFader();
        yield return CameraRoom.Fade(fader, 1f, fadeOutTime);

        if (hook != null) hook.ReleaseHook();
        transform.position = safePosition;
        if (rb != null) { rb.position = safePosition; rb.linearVelocity = Vector2.zero; }
        Physics2D.SyncTransforms();

        yield return new WaitForSecondsRealtime(0.1f);   // let the camera catch up before showing
        if (controller != null && (stats == null || !stats.IsDead())) controller.canMove = true;
        yield return CameraRoom.Fade(fader, 0f, fadeInTime);
        groundedTime = 0f;
        respawning = false;
    }
}
