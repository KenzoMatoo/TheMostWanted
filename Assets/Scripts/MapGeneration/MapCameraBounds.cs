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

        // Celdas ocupadas: se agrupan los tiles de todos los cuartos en cuadros de cellSize
        var rooms = GetComponent<RoomManager>();
        Transform root = rooms != null ? rooms.transform : transform;
        var chunks = new HashSet<Vector2Int>();
        float half = cellSize * 0.5f;
        foreach (var room in root.GetComponentsInChildren<Room>())
        {
            Vector2 origin = room.transform.position; // los cuartos estan centrados en su primera celda
            foreach (var tm in room.GetComponentsInChildren<Tilemap>())
            {
                tm.CompressBounds();
                foreach (var c in tm.cellBounds.allPositionsWithin)
                {
                    if (!tm.HasTile(c)) continue;
                    Vector2 world = tm.GetCellCenterWorld(c);
                    chunks.Add(new Vector2Int(Mathf.RoundToInt((world.x - origin.x) / cellSize), Mathf.RoundToInt((world.y - origin.y) / cellSize))
                               + RoomKey(origin));
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
        foreach (var k in chunks)
        {
            var box = boundsObject.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.compositeOperation = Collider2D.CompositeOperation.Merge;
            box.offset = new Vector2(k.x * cellSize, k.y * cellSize) + chunkOffset;
            box.size = new Vector2(cellSize, cellSize);
        }
        composite.GenerateGeometry();

        confiner.BoundingShape2D = composite;
        confiner.InvalidateBoundingShapeCache();
        Debug.Log($"[MapCameraBounds] Limites de camara: {chunks.Count} celdas.");
    }

    // Las llaves de celda se guardan en coordenadas globales: (posicion del cuarto / cellSize) redondeada,
    // y el centro real de la celda (0,0) queda en chunkOffset (la posicion del primer cuarto puede no ser multiplo de cellSize).
    private Vector2 chunkOffset;
    private bool offsetSet;

    Vector2Int RoomKey(Vector2 origin)
    {
        if (!offsetSet)
        {
            chunkOffset = new Vector2(origin.x - Mathf.Round(origin.x / cellSize) * cellSize, origin.y - Mathf.Round(origin.y / cellSize) * cellSize);
            offsetSet = true;
        }
        return new Vector2Int(Mathf.RoundToInt((origin.x - chunkOffset.x) / cellSize), Mathf.RoundToInt((origin.y - chunkOffset.y) / cellSize));
    }
}
