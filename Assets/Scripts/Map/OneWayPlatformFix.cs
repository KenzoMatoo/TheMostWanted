using UnityEngine;
using UnityEngine.SceneManagement;

// Arregla las plataformas que se pueden traspasar (PlatformEffector2D one-way).
// 1. Sus CompositeCollider2D se generaban como CONTORNO hueco (Outlines): si el jugador quedaba dentro de la
//    plataforma se paraba sobre la linea de abajo del contorno y no podia salir. Aqui se pasan a POLIGONOS
//    solidos (lo correcto para one-way), al cargar la escena y cada vez que se genera un mapa.
// 2. Red de seguridad en el jugador: si se queda metido dentro de una plataforma sin moverse, sube encima.
// 3. Las paredes de los cuartos ("Walls") no tenian CompositeCollider2D: cada tile era un collider aparte y en las
//    uniones jugador y enemigos se enganchaban. Aqui se les agrega uno (contorno continuo, sin uniones).
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
        FixAll();
        foreach (var gen in FindObjectsByType<MapGenerationBase>(FindObjectsSortMode.None))
        {
            gen.MapGenerated -= FixAll;
            gen.MapGenerated += FixAll;
        }

        var player = FindAnyObjectByType<PlayerController>();
        if (player != null && player.GetComponent<OneWayPlatformFix>() == null) player.gameObject.AddComponent<OneWayPlatformFix>();
    }

    private static void FixAll()
    {
        FixAllPlatforms();
        FixWallColliders();
    }

    // Solid tilemaps without a composite get one: a single smooth outline instead of one box per tile
    public static void FixWallColliders()
    {
        int fixedCount = 0;
        foreach (var tc in FindObjectsByType<UnityEngine.Tilemaps.TilemapCollider2D>(FindObjectsSortMode.None))
        {
            if (tc.isTrigger || tc.usedByEffector || !tc.enabled) continue;
            if (tc.GetComponent<CompositeCollider2D>() != null) continue;
            var go = tc.gameObject;
            var body = go.GetComponent<Rigidbody2D>();
            if (body == null)
            {
                body = go.AddComponent<Rigidbody2D>();
                body.bodyType = RigidbodyType2D.Static;
            }
            else if (body.bodyType != RigidbodyType2D.Static) continue;   // a moving tilemap: leave it as it is
            var composite = go.AddComponent<CompositeCollider2D>();
            composite.geometryType = CompositeCollider2D.GeometryType.Outlines;
            composite.generationType = CompositeCollider2D.GenerationType.Synchronous;
            composite.sharedMaterial = tc.sharedMaterial;
            tc.compositeOperation = Collider2D.CompositeOperation.Merge;
            composite.GenerateGeometry();
            fixedCount++;
        }
        if (fixedCount > 0) Physics2D.SyncTransforms();
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
        // No room above: drop below the platform, but only if that space is free (never into a wall)
        Vector2 down = Vector2.down * (b.size.y * 0.6f);
        if (OnlyPlatforms((Vector2)b.center + down, b.size * 0.9f)) rb.position += down;
    }

    private static bool OnlyPlatforms(Vector2 center, Vector2 size)
    {
        int n = Physics2D.OverlapBoxNonAlloc(center, size, 0f, buffer, LayerMask.GetMask("Ground"));
        for (int i = 0; i < n; i++)
            if (buffer[i] != null && !buffer[i].isTrigger && !buffer[i].usedByEffector) return false;
        return true;
    }
}
