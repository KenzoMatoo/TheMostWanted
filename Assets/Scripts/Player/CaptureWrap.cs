using UnityEngine;

// Cuerda de slime enrollada que se dibuja encima de lo que tienes capturado (se ve "atado").
// La crea CharacterCombat al completar una captura y la destruye al soltarlo.
// Se ajusta al tamano del enemigo y lo sigue; no es hija del enemigo para no heredar su escala o flip.
public class CaptureWrap : MonoBehaviour
{
    private Transform target;
    private Collider2D targetCollider;
    private Renderer targetRenderer;
    private SpriteRenderer sr;
    private float pop = 1f;

    [Tooltip("Que tan ancha es la atadura respecto al enemigo (1 = mismo ancho).")]
    public float widthFactor = 1.15f;
    [Tooltip("Que tan alta es respecto al enemigo.")]
    public float heightFactor = 0.7f;

    public static CaptureWrap Create(Sprite sprite, Transform target)
    {
        if (sprite == null || target == null) return null;
        var go = new GameObject("Atadura Slime");
        var wrap = go.AddComponent<CaptureWrap>();
        wrap.sr = go.AddComponent<SpriteRenderer>();
        wrap.sr.sprite = sprite;
        wrap.target = target;
        wrap.targetCollider = target.GetComponent<Collider2D>();
        wrap.targetRenderer = target.GetComponentInChildren<SpriteRenderer>();

        // Encima del sprite del enemigo
        if (wrap.targetRenderer is SpriteRenderer enemySr)
        {
            wrap.sr.sortingLayerID = enemySr.sortingLayerID;
            wrap.sr.sortingOrder = enemySr.sortingOrder + 5;
            wrap.sr.sharedMaterial = enemySr.sharedMaterial;
        }
        wrap.LateUpdate();
        return wrap;
    }

    void LateUpdate()
    {
        if (target == null || !target.gameObject.activeInHierarchy) { Destroy(gameObject); return; }

        Bounds b = targetCollider != null ? targetCollider.bounds
                 : targetRenderer != null ? targetRenderer.bounds
                 : new Bounds(target.position, Vector3.one);
        transform.position = new Vector3(b.center.x, b.center.y, target.position.z);

        // "Apretar" al atar: empieza grande y se cierra sobre el enemigo
        pop = Mathf.MoveTowards(pop, 0f, Time.deltaTime * 4f);
        float squeeze = 1f + 0.35f * pop * pop;

        Vector2 spriteSize = sr.sprite.bounds.size;
        float sx = b.size.x * widthFactor / Mathf.Max(0.01f, spriteSize.x);
        float sy = b.size.y * heightFactor / Mathf.Max(0.01f, spriteSize.y);
        transform.localScale = new Vector3(sx * squeeze, sy * squeeze, 1f);
    }
}
