using UnityEngine;
using UnityEngine.UI;

// Panel del minimapa en el HUD. El generador de mapas (MapGenerationBase) pone las celdas del mapa
// dentro de "content" (asignalo como overlayParent del generador). Cuando el mapa se genera, este
// script pinta los cuartos con los colores del HUD y escala/centra el mapa para que quepa en el panel.
// Si la escena no tiene generador de mapa (por ejemplo el tutorial), el panel se esconde.
public class MinimapPanel : MonoBehaviour
{
    [SerializeField] private RectTransform content;
    [SerializeField] private Vector2 padding = new Vector2(14f, 14f);
    [Tooltip("Color de los cuartos en el minimapa.")]
    [SerializeField] private Color roomColor = new Color(0.83f, 0.82f, 0.78f, 0.9f);
    [Tooltip("Nunca agrandar el mapa mas de esto (1 = tamano original de las celdas).")]
    [SerializeField] private float maxScale = 1f;

    private MapGenerationBase generator;

    void Start()
    {
        generator = MapGenerationBase.instance;
        if (generator == null) { gameObject.SetActive(false); return; }
        generator.MapGenerated += Fit;
        if (content != null && content.childCount > 0) Fit();
    }

    void OnDestroy()
    {
        if (generator != null) generator.MapGenerated -= Fit;
    }

    void Fit()
    {
        if (content == null) return;
        var panel = (RectTransform)transform;

        // Colores de los cuartos (los iconos especiales, como la salida, se quedan como estan)
        foreach (var cell in content.GetComponentsInChildren<Cell>(true))
            if (cell.spriteRenderer != null) cell.spriteRenderer.color = roomColor;

        // Limites del mapa dentro de "content" (sin escala)
        content.localScale = Vector3.one;
        bool any = false;
        Vector2 min = Vector2.zero, max = Vector2.zero;
        foreach (RectTransform child in content)
        {
            if (!child.gameObject.activeSelf || child.GetComponent<Cell>() == null) continue;
            Vector2 c = child.anchoredPosition, h = child.sizeDelta * 0.5f;
            if (!any) { min = c - h; max = c + h; any = true; }
            else { min = Vector2.Min(min, c - h); max = Vector2.Max(max, c + h); }
        }
        if (!any) return;

        // Escala para que quepa y centrado dentro del panel
        Vector2 size = max - min;
        Vector2 room = panel.rect.size - padding * 2f;
        float scale = Mathf.Min(maxScale, room.x / Mathf.Max(1f, size.x), room.y / Mathf.Max(1f, size.y));
        content.localScale = new Vector3(scale, scale, 1f);

        // content esta anclado arriba a la izquierda del panel; mover para centrar el mapa
        Vector2 mapCenter = (min + max) * 0.5f * scale;
        Vector2 panelCenter = new Vector2(panel.rect.width * 0.5f, -panel.rect.height * 0.5f);
        content.anchoredPosition = panelCenter - mapCenter;
    }
}
