using UnityEngine;
using UnityEngine.SceneManagement;

// Arregla las plataformas que se pueden traspasar (PlatformEffector2D one-way).
// 1. Sus CompositeCollider2D se generaban como CONTORNO hueco (Outlines): si el jugador quedaba dentro de la
//    plataforma se paraba sobre la linea de abajo del contorno y no podia salir. Aqui se pasan a POLIGONOS
//    solidos (lo correcto para one-way), al cargar la escena y cada vez que se genera un mapa.
// 2. Red de seguridad en el jugador: si se queda metido dentro de una plataforma sin moverse, sube encima.
public class OneWayPlatformFix : MonoBehaviour
{
    [Tooltip("Segundos metido en una plataforma sin moverse antes de subirlo encima")]
    public float stuckTime = 0.2f;

    private Rigidbody2D rb;
    private Collider2D body;
    private float stuckTimer;
    private static readonly Collider2D[] buffer = new Collider2D[8];

    // ---------------------------------------------------------------- Auto setup

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Setup();
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m) => Setup();

    private static void Setup()
    {
        FixAllPlatforms();
        foreach (var gen in FindObjectsByType<MapGenerationBase>(FindObjectsSortMode.None))
        {
            gen.MapGenerated -= FixAllPlatforms;
            gen.MapGenerated += FixAllPlatforms;
        }

        var player = FindAnyObjectByType<PlayerController>();
        if (player != null && player.GetComponent<OneWayPlatformFix>() == null) player.gameObject.AddComponent<OneWayPlatformFix>();
    }

    public static void FixAllPlatforms()
    {
        foreach (var effector in FindObjectsByType<PlatformEffector2D>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!effector.useOneWay) continue;
            effector.useOneWayGrouping = true;   // all the tiles of the platform act as one
            var composite = effector.GetComponent<CompositeCollider2D>();
            if (composite != null && composite.geometryType != CompositeCollider2D.GeometryType.Polygons)
            {
                composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
                composite.GenerateGeometry();
            }
        }
    }

    // ---------------------------------------------------------------- Player safety net

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        foreach (var c in GetComponents<Collider2D>()) if (!c.isTrigger) { body = c; break; }
    }

    private void FixedUpdate()
    {
        if (rb == null || body == null || !body.enabled) return;

        Bounds b = body.bounds;
        Collider2D platform = null;
        int n = Physics2D.OverlapBoxNonAlloc(b.center, b.size * 0.7f, 0f, buffer, LayerMask.GetMask("Ground"));
        for (int i = 0; i < n; i++)
            if (buffer[i] != null && buffer[i].usedByEffector && !buffer[i].isTrigger) { platform = buffer[i]; break; }

        bool still = Mathf.Abs(rb.linearVelocity.y) < 0.5f;
        stuckTimer = platform != null && still ? stuckTimer + Time.fixedDeltaTime : 0f;
        if (stuckTimer < stuckTime) return;
        stuckTimer = 0f;
        PopOut(platform, b);
    }

    // Puts the feet on top of the platform; if there is no room up there, drops the player below it
    private void PopOut(Collider2D platform, Bounds b)
    {
        RaycastHit2D top = Physics2D.Raycast(new Vector2(b.center.x, b.max.y + 0.05f), Vector2.down, b.size.y + 0.1f, LayerMask.GetMask("Ground"));
        if (top.collider == platform && top.distance > 0f)
        {
            Vector2 up = new Vector2(0f, top.point.y - b.min.y + 0.02f);
            if (!Physics2D.OverlapBox((Vector2)b.center + up, b.size * 0.9f, 0f, LayerMask.GetMask("Ground")) || OnlyPlatforms((Vector2)b.center + up, b.size * 0.9f))
            {
                rb.position += up;
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);
                return;
            }
        }
        // No room above: let it fall through
        rb.position += Vector2.down * (b.size.y * 0.6f);
    }

    private static bool OnlyPlatforms(Vector2 center, Vector2 size)
    {
        int n = Physics2D.OverlapBoxNonAlloc(center, size, 0f, buffer, LayerMask.GetMask("Ground"));
        for (int i = 0; i < n; i++)
            if (buffer[i] != null && !buffer[i].isTrigger && !buffer[i].usedByEffector) return false;
        return true;
    }
}
