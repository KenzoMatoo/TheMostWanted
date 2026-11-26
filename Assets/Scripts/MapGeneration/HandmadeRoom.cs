using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

// Cuarto hecho a mano para el generador de mapas. Va en la raiz del prefab del cuarto (junto a Door).
// - El interior (pisos, repisas, decoracion, lamparas) esta pintado a mano en los tilemaps del prefab,
//   asi que el generador de cuevas no pone roca aqui.
// - Piezas por puerta: objetos que solo se quedan si una puerta esta abierta (o cerrada). Sus tilemaps
//   hijos se copian al tilemap del cuarto con el mismo nombre (Walls, Plataformas, Deco) para que los
//   RuleTiles se unan; lo que este dentro de su hijo "Props" se queda en el cuarto.
// - Las puertas cerradas se pintan dentro de Walls, asi la pared se ve continua.
// - Puede salir en espejo (izquierda <-> derecha) para tener mas variedad con el mismo diseno.
// - Enemigos y cajas solo aparecen cerca del piso (no flotando a media altura).
// Usa Tools > Cuartos > Validar cuartos a mano para revisar que todas las puertas se conectan sin gancho.
public class HandmadeRoom : MonoBehaviour
{
    [System.Serializable]
    public class DoorPiece
    {
        [Tooltip("Cuadrante de la puerta (1 = arriba izquierda, 2 = arriba derecha, 3 = abajo izquierda, 4 = abajo derecha). En 1x1 siempre es 1")]
        [Range(1, 4)] public int quadrant = 1;
        public EdgeDirection side;
        [Tooltip("Se queda solo si la puerta esta abierta")] public GameObject whenOpen;
        [Tooltip("Se queda solo si la puerta esta cerrada")] public GameObject whenClosed;
    }

    [SerializeField] private List<DoorPiece> doorPieces = new List<DoorPiece>();

    [Header("Espejo")]
    [SerializeField] private bool allowMirror = true;
    [Tooltip("En que cuadrante queda cada cuadrante al voltear (posicion 0 = Q1). 1x1 y 1x2: 1,2,3,4. 2x1 y 2x2: 2,1,4,3")]
    [SerializeField] private int[] mirroredQuadrant = { 1, 2, 3, 4 };

    [Header("Plataformeo")]
    [Tooltip("Cuarto de reto de movimiento: el validador acepta caminos que necesitan dash o las lamparas del gancho")]
    [SerializeField] private bool advancedMovement = false;

    [Header("Spawns")]
    [Tooltip("Enemigos y cajas solo aparecen si hay piso a esta distancia (en tiles) o menos debajo de ellos")]
    [SerializeField, Range(1, 10)] private int maxSpawnHeight = 4;

    public const string PropsName = "Props";
    public const string PlatformsName = "Plataformas";
    public const string WallsName = "Walls";
    public const string HazardsName = "Pinchos";

    public static readonly CaveSettings NoCaves = new CaveSettings { enabled = false, fillRock = false };

    public bool Mirrored { get; private set; }
    public IReadOnlyList<DoorPiece> DoorPieces => doorPieces;
    public bool AdvancedMovement => advancedMovement;

    public void Build(Door door, System.Random rng)
    {
        if (door == null) return;
        Mirrored = allowMirror && rng.NextDouble() < 0.5;
        var doors = new HashSet<GameObject>(door.AllDoors);

        if (Mirrored) Mirror(doors);
        ApplyDoorPieces(door, doors);
        SealClosedDoors(door);
    }

    /// <summary>Marks the spots where things may spawn: free tiles with ground (wall or platform) close below.</summary>
    public void PrepareSpawns(CaveResult cave)
    {
        if (cave == null) return;
        Tilemap platforms = FindRoomTilemap(PlatformsName, null);
        Tilemap hazards = FindRoomTilemap(HazardsName, null);
        int w = cave.width, h = cave.height;

        bool Solid(int x, int y)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return true;
            var cell = new Vector3Int(cave.bounds.xMin + x, cave.bounds.yMin + y, 0);
            if (cave.walls.HasTile(cell)) return true;
            return platforms != null && platforms.HasTile(platforms.WorldToCell(cave.walls.GetCellCenterWorld(cell)));
        }

        bool Hazard(int x, int y)
        {
            if (hazards == null || x < 0 || y < 0 || x >= w || y >= h) return false;
            var cell = new Vector3Int(cave.bounds.xMin + x, cave.bounds.yMin + y, 0);
            return hazards.HasTile(hazards.WorldToCell(cave.walls.GetCellCenterWorld(cell)));
        }

        var doorCells = new HashSet<Vector2Int>(cave.doorCells);
        cave.spawnAllowed = new bool[w, h];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                if (!cave.freeFloor[x, y]) continue;
                if (Solid(x, y) || Hazard(x, y)) { cave.freeFloor[x, y] = false; continue; }   // inside a platform or spikes

                for (int d = 1; d <= maxSpawnHeight; d++)
                {
                    int yy = y - d;
                    if (doorCells.Contains(new Vector2Int(x, yy))) break;      // over a hole in the floor
                    if (Hazard(x, yy)) break;                                   // over spikes
                    if (Solid(x, yy)) { cave.spawnAllowed[x, y] = true; break; }
                }
            }
    }

    // ---------------- Mirror ----------------

    private void Mirror(HashSet<GameObject> doors)
    {
        Tilemap walls = FindRoomTilemap(WallsName, doors);
        if (walls == null) return;
        walls.CompressBounds();
        float cx = walls.transform.TransformPoint(walls.localBounds.center).x;

        foreach (Tilemap tm in GetComponentsInChildren<Tilemap>(true))
            if (!IsUnderDoor(tm.transform, doors)) MirrorTilemap(tm, cx);

        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name != PropsName) continue;
            foreach (Transform prop in t)
            {
                Vector3 p = prop.position;
                p.x = 2f * cx - p.x;
                prop.position = p;
            }
        }
    }

    private static void MirrorTilemap(Tilemap tm, float cx)
    {
        tm.CompressBounds();
        var cells = new List<(Vector3Int pos, TileBase tile, Matrix4x4 m, Color c)>();
        foreach (Vector3Int p in tm.cellBounds.allPositionsWithin)
        {
            TileBase t = tm.GetTile(p);
            if (t != null) cells.Add((p, t, tm.GetTransformMatrix(p), tm.GetColor(p)));
        }
        if (cells.Count == 0) return;
        tm.ClearAllTiles();

        Matrix4x4 flip = Matrix4x4.Scale(new Vector3(-1f, 1f, 1f));
        foreach (var c in cells)
        {
            Vector3 world = tm.GetCellCenterWorld(c.pos);
            world.x = 2f * cx - world.x;
            Vector3Int np = tm.WorldToCell(world);
            tm.SetTile(np, c.tile);
            // RuleTiles pick their sprite again from the neighbours; plain tiles (shelf ends...) are flipped
            if (!IsRuleTile(c.tile))
            {
                tm.SetTileFlags(np, TileFlags.None);
                tm.SetTransformMatrix(np, flip * c.m);
                tm.SetColor(np, c.c);
            }
        }
    }

    private EdgeDirection MapSide(EdgeDirection side)
    {
        if (!Mirrored) return side;
        if (side == EdgeDirection.Left) return EdgeDirection.Right;
        if (side == EdgeDirection.Right) return EdgeDirection.Left;
        return side;
    }

    private int MapQuadrant(int q)
    {
        if (!Mirrored || mirroredQuadrant == null || q < 1 || q > mirroredQuadrant.Length) return q;
        return mirroredQuadrant[q - 1];
    }

    // ---------------- Door pieces ----------------

    private void ApplyDoorPieces(Door door, HashSet<GameObject> doors)
    {
        // Collect first: merging changes the hierarchy
        var keep = new List<GameObject>();
        var drop = new List<GameObject>();
        foreach (DoorPiece piece in doorPieces)
        {
            if (piece == null) continue;
            GameObject d = door.GetDoor(MapQuadrant(piece.quadrant), MapSide(piece.side));
            bool open = door.IsOpen(d);
            if (piece.whenOpen != null) (open ? keep : drop).Add(piece.whenOpen);
            if (piece.whenClosed != null) (open ? drop : keep).Add(piece.whenClosed);
        }

        foreach (GameObject g in drop) if (!keep.Contains(g)) Remove(g);
        foreach (GameObject g in keep) if (g != null) Merge(g, doors);
    }

    private void Merge(GameObject piece, HashSet<GameObject> doors)
    {
        foreach (Tilemap src in piece.GetComponentsInChildren<Tilemap>(true))
        {
            Tilemap dst = FindRoomTilemap(src.gameObject.name, doors, piece.transform);
            if (dst == null) { Debug.LogWarning($"HandmadeRoom: '{name}' has no tilemap '{src.gameObject.name}' for piece '{piece.name}'.", this); continue; }
            CopyTiles(src, dst);
        }

        Transform props = FindChild(piece.transform, PropsName);
        if (props != null)
        {
            Transform roomProps = FindChild(transform, PropsName);
            if (roomProps == null) { roomProps = new GameObject(PropsName).transform; roomProps.SetParent(transform, false); }
            var children = new List<Transform>();
            foreach (Transform c in props) children.Add(c);
            foreach (Transform c in children) c.SetParent(roomProps, true);
        }
        Remove(piece);
    }

    // Closed doors become part of the Walls tilemap so the RuleTile draws one continuous wall
    private void SealClosedDoors(Door door)
    {
        Tilemap walls = FindRoomTilemap(WallsName, new HashSet<GameObject>(door.AllDoors));
        if (walls == null) return;
        foreach (GameObject d in door.AllDoors)
        {
            if (door.IsOpen(d)) continue;
            Tilemap tm = d.GetComponent<Tilemap>();
            if (tm == null) continue;
            CopyTiles(tm, walls);
            tm.ClearAllTiles();
        }
    }

    private static void CopyTiles(Tilemap src, Tilemap dst)
    {
        src.CompressBounds();
        foreach (Vector3Int p in src.cellBounds.allPositionsWithin)
        {
            TileBase t = src.GetTile(p);
            if (t == null) continue;
            Vector3Int np = dst.WorldToCell(src.GetCellCenterWorld(p));
            dst.SetTile(np, t);
            if (!IsRuleTile(t))
            {
                dst.SetTileFlags(np, TileFlags.None);
                dst.SetTransformMatrix(np, src.GetTransformMatrix(p));
                dst.SetColor(np, src.GetColor(p));
            }
        }
    }

    // ---------------- Helpers ----------------

    // Detach first so the cave generator (which searches by name, also inactive objects) never finds it
    private static void Remove(GameObject g)
    {
        if (g == null) return;
        g.SetActive(false);
        g.transform.SetParent(null, false);
        if (Application.isPlaying) Destroy(g); else DestroyImmediate(g);
    }

    /// <summary>The room's own tilemap with this name (not one inside a door or a piece).</summary>
    public Tilemap FindRoomTilemap(string tilemapName, HashSet<GameObject> doors, Transform exclude = null)
    {
        foreach (Tilemap tm in GetComponentsInChildren<Tilemap>(true))
        {
            if (tm.gameObject.name != tilemapName) continue;
            if (exclude != null && tm.transform.IsChildOf(exclude)) continue;
            if (tm.transform.parent != transform) continue;   // room tilemaps are direct children
            if (doors != null && doors.Contains(tm.gameObject)) continue;
            return tm;
        }
        return null;
    }

    private static bool IsUnderDoor(Transform t, HashSet<GameObject> doors)
    {
        for (Transform p = t; p != null; p = p.parent)
            if (doors.Contains(p.gameObject)) return true;
        return false;
    }

    private static Transform FindChild(Transform parent, string childName)
    {
        foreach (Transform c in parent) if (c.name == childName) return c;
        return null;
    }

    public static bool IsRuleTile(TileBase t) => t != null && t.GetType().Name.Contains("RuleTile");
}
