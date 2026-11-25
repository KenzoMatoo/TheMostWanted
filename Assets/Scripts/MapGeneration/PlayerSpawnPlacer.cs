using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

// Pone al jugador en un lugar libre del cuarto inicial cada vez que se genera un mapa.
// El jugador empieza en el centro de la celda inicial, pero ahi puede haber piso, un puente o una isla
// (cuartos hechos a mano o roca de las cuevas). Este script busca el punto mas cercano al centro donde:
// - el cuerpo del jugador cabe sin tocar ningun tile con colision, y
// - hay piso o una repisa justo debajo de los pies.
// Se agrega solo al generador de mapas de cualquier escena (no hace falta ponerlo a mano).
public class PlayerSpawnPlacer : MonoBehaviour
{
    [Tooltip("Hasta cuantos tiles del centro de la celda inicial se busca")]
    [SerializeField] private int searchRadius = 16;
    [Tooltip("Tiles libres a cada lado del jugador (1 = un hueco de 3 tiles de ancho)")]
    [SerializeField] private int sideClearance = 1;
    [Tooltip("Tiles libres hacia arriba desde los pies")]
    [SerializeField] private int headClearance = 2;

    private MapGenerationBase generator;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAdd()
    {
        MapGenerationBase gen = MapGenerationBase.instance;
        if (gen != null && gen.GetComponent<PlayerSpawnPlacer>() == null) gen.gameObject.AddComponent<PlayerSpawnPlacer>();
    }

    private void Awake()
    {
        generator = GetComponent<MapGenerationBase>();
        if (generator == null) generator = MapGenerationBase.instance;
        if (generator != null) generator.MapGenerated += PlacePlayer;
    }

    private void Start()
    {
        // The map may already exist (generated before this component was added)
        if (generator != null && generator.getSpawnedCells != null && generator.getSpawnedCells.Count > 0) PlacePlayer();
    }

    private void OnDestroy()
    {
        if (generator != null) generator.MapGenerated -= PlacePlayer;
    }

    public void PlacePlayer()
    {
        var player = FindAnyObjectByType<PlayerController>();
        RoomManager rm = RoomManager.instance;
        if (player == null || rm == null || generator == null) return;

        // Center of the start cell: rooms sit at (gridX * offsetX, -gridY * offsetY) in the RoomManager's space
        int w = MapGenerationBase.GridWidth;
        int gx = generator.StartIndex % w, gy = generator.StartIndex / w;
        Vector3 center = rm.transform.TransformPoint(new Vector3(gx * rm.offsetX, -gy * rm.offsetY, 0f));

        // Tilemaps of the room around that point
        var solids = new List<Tilemap>();
        var platforms = new List<Tilemap>();
        Tilemap reference = null;
        foreach (Tilemap tm in rm.GetComponentsInChildren<Tilemap>(false))
        {
            var col = tm.GetComponent<TilemapCollider2D>();
            if (col == null || !col.enabled || col.isTrigger) continue;
            tm.CompressBounds();
            if (tm.GetUsedTilesCount() == 0) continue;
            Bounds b = tm.localBounds;
            Vector3 min = tm.transform.TransformPoint(b.min), max = tm.transform.TransformPoint(b.max);
            if (center.x < min.x - 1 || center.x > max.x + 1 || center.y < min.y - 1 || center.y > max.y + 1) continue;

            if (tm.GetComponent<PlatformEffector2D>() != null) platforms.Add(tm);
            else
            {
                solids.Add(tm);
                if (reference == null || tm.gameObject.name == HandmadeRoom.WallsName) reference = tm;
            }
        }
        if (reference == null) { Debug.LogWarning("PlayerSpawnPlacer: no tilemaps found in the start room."); return; }

        bool Has(List<Tilemap> maps, Vector3 world)
        {
            foreach (Tilemap tm in maps) if (tm.HasTile(tm.WorldToCell(world))) return true;
            return false;
        }

        Vector3Int c0 = reference.WorldToCell(center);
        float cell = reference.layoutGrid != null ? reference.layoutGrid.cellSize.y : 1f;
        bool found = false;
        Vector3 best = center;
        float bestDist = float.MaxValue;

        for (int dx = -searchRadius; dx <= searchRadius; dx++)
            for (int dy = -searchRadius; dy <= searchRadius; dy++)
            {
                float d = dx * dx + dy * dy;
                if (d >= bestDist) continue;
                Vector3Int c = c0 + new Vector3Int(dx, dy, 0);
                Vector3 feet = reference.GetCellCenterWorld(c);

                // Ground (wall or one-way platform) right below the feet
                Vector3 below = reference.GetCellCenterWorld(c + Vector3Int.down);
                if (!Has(solids, below) && !Has(platforms, below)) continue;

                bool free = true;
                for (int x = -sideClearance; x <= sideClearance && free; x++)
                    for (int y = 0; y < headClearance && free; y++)
                    {
                        Vector3 p = reference.GetCellCenterWorld(c + new Vector3Int(x, y, 0));
                        if (Has(solids, p) || Has(platforms, p)) free = false;
                    }
                if (!free) continue;

                found = true;
                bestDist = d;
                best = feet - new Vector3(0f, cell * 0.5f, 0f);   // bottom of the cell = top of the ground
            }

        if (!found) { Debug.LogWarning("PlayerSpawnPlacer: no free spot in the start room, the player stays where it is."); return; }

        // Put the bottom of the player's collider on the ground
        float feetOffset = 0f;
        var playerCol = player.GetComponent<Collider2D>();
        if (playerCol != null) feetOffset = player.transform.position.y - playerCol.bounds.min.y;
        Vector3 pos = new Vector3(best.x, best.y + feetOffset + 0.05f, player.transform.position.z);

        player.transform.position = pos;
        var rb = player.GetComponent<Rigidbody2D>();
        if (rb != null) { rb.position = pos; rb.linearVelocity = Vector2.zero; }
        Physics2D.SyncTransforms();
    }
}
