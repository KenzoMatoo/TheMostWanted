using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Tilemaps;

// Limites de camara para un mapa generado: cuando el generador termina el mapa, arma una forma
// con un cuadro por cada celda (33x33) que tiene tiles y se la da al CinemachineConfiner2D.
// Asi la camara sigue al jugador pero nunca muestra el vacio fuera de los cuartos (sirve tambien
// para cuartos en L, porque solo cuenta las celdas que de verdad existen).
// Va en el mismo objeto que el generador (MapGenerationBase) y el RoomManager.
public class MapCameraBounds : MonoBehaviour
{
    [SerializeField] private CinemachineConfiner2D confiner;
    [Tooltip("Tamano de una celda del mapa en unidades (RoomManager.offsetX/Y).")]
    [SerializeField] private float cellSize = 33f;

    private MapGenerationBase generator;
    private GameObject boundsObject;

    void Awake()
    {
        generator = GetComponent<MapGenerationBase>();
        if (confiner == null) confiner = FindFirstObjectByType<CinemachineConfiner2D>();
        var rm = GetComponent<RoomManager>();
        if (rm != null && rm.offsetX > 0f) cellSize = rm.offsetX;
    }

    void OnEnable() { if (generator != null) generator.MapGenerated += Build; }
    void OnDisable() { if (generator != null) generator.MapGenerated -= Build; }

    public void Build()
    {
        if (confiner == null) { Debug.LogWarning("[MapCameraBounds] No hay CinemachineConfiner2D en la camara."); return; }
        if (boundsObject != null) Destroy(boundsObject);

        // Occupied cells: each room is cut into 33x33 squares starting at its real bottom-left corner
        // (taken from its tilemaps). Only squares that have tiles count, so the empty corner of an L is left out.
        // Using the corner and not the room position matters: big rooms (2x1, 2x2, L) are centered between cells.
        var rooms = GetComponent<RoomManager>();
        Transform root = rooms != null ? rooms.transform : transform;
        var chunks = new HashSet<Vector2Int>();   // keys = chunk center * 2, rounded (unique per square)
        var centers = new List<Vector2>();
        foreach (var room in root.GetComponentsInChildren<Room>())
        {
            var maps = room.GetComponentsInChildren<Tilemap>();
            bool any = false;
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            foreach (var tm in maps)
            {
                tm.CompressBounds();
                if (tm.cellBounds.size.x <= 0 || tm.cellBounds.size.y <= 0) continue;
                Vector3 corner = tm.CellToWorld(tm.cellBounds.min);
                min = Vector2.Min(min, corner);
                any = true;
            }
            if (!any) continue;

            foreach (var tm in maps)
            {
                foreach (var c in tm.cellBounds.allPositionsWithin)
                {
                    if (!tm.HasTile(c)) continue;
                    Vector2 world = tm.GetCellCenterWorld(c);
                    int cx = Mathf.FloorToInt((world.x - min.x) / cellSize);
                    int cy = Mathf.FloorToInt((world.y - min.y) / cellSize);
                    Vector2 center = min + new Vector2((cx + 0.5f) * cellSize, (cy + 0.5f) * cellSize);
                    var key = new Vector2Int(Mathf.RoundToInt(center.x * 2f), Mathf.RoundToInt(center.y * 2f));
                    if (chunks.Add(key)) centers.Add(center);
                }
            }
        }
        if (chunks.Count == 0) { Debug.LogWarning("[MapCameraBounds] El mapa no tiene tiles."); return; }

        boundsObject = new GameObject("Limites Camara") { layer = 2 }; // Ignore Raycast: no estorba al gancho ni a los raycasts
        boundsObject.transform.SetParent(transform, false);
        boundsObject.transform.position = Vector3.zero;
        var rb = boundsObject.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Static;
        var composite = boundsObject.AddComponent<CompositeCollider2D>();
        composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
        composite.isTrigger = true;
        composite.generationType = CompositeCollider2D.GenerationType.Manual;
        foreach (var center in centers)
        {
            var box = boundsObject.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.compositeOperation = Collider2D.CompositeOperation.Merge;
            box.offset = center;
            box.size = new Vector2(cellSize + 0.02f, cellSize + 0.02f);   // tiny overlap so neighbouring squares merge cleanly
        }
        composite.GenerateGeometry();

        confiner.BoundingShape2D = composite;
        confiner.InvalidateBoundingShapeCache();
        Debug.Log($"[MapCameraBounds] Limites de camara: {chunks.Count} celdas.");
    }
}
