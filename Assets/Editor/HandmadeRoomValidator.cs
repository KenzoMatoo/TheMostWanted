using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

// Revisa los prefabs de cuartos hechos a mano (HandmadeRoom) con los stats de salto reales del jugador:
// - Que desde cada puerta se pueda llegar a todas las demas SOLO saltando y caminando (sin gancho ni dash).
// - Que nada solido tape el frente de una puerta.
// Menu: Tools > Cuartos > Validar cuartos a mano (los prefabs seleccionados, o todos si no hay seleccion).
// Se revisa el caso con todas las puertas abiertas; las piezas "si esta abierta" cuentan, las "si esta cerrada" no.
public static class HandmadeRoomValidator
{
    const int ZoneDepth = 5;        // tiles libres frente a una puerta lateral / arriba
    const int FloorZoneDepth = 4;   // tiles libres sobre el hueco de una puerta de abajo
    const int FloorZoneSide = 5;    // y a sus lados (donde aterrizas al subir desde el cuarto de abajo)
    const string StatsPath = "Assets/Scripts/Player/PlayerStats.asset";
    const string PlayerPath = "Assets/Prefabs/Characters/KarinLeon.prefab";

    [MenuItem("Tools/Cuartos/Validar cuartos a mano")]
    static void ValidateMenu()
    {
        var paths = Selection.objects.Select(AssetDatabase.GetAssetPath).Where(p => p.EndsWith(".prefab")).ToList();
        if (paths.Count == 0)
            paths = AssetDatabase.FindAssets("t:Prefab").Select(AssetDatabase.GUIDToAssetPath).ToList();

        int checkedCount = 0, bad = 0;
        foreach (string path in paths)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null || prefab.GetComponent<HandmadeRoom>() == null) continue;
            checkedCount++;
            List<string> problems = Validate(prefab, out string map);
            if (problems.Count == 0) Debug.Log($"[Cuartos] OK  {path}\n{map}", prefab);
            else { bad++; Debug.LogWarning($"[Cuartos] {problems.Count} problema(s) en {path}:\n- " + string.Join("\n- ", problems) + "\n" + map, prefab); }
        }
        EditorUtility.DisplayDialog("Validar cuartos a mano",
            checkedCount == 0 ? "No hay prefabs con HandmadeRoom." : $"{checkedCount} cuarto(s) revisados, {bad} con problemas. Detalles en la consola.", "OK");
    }

    public struct JumpInfo { public float height, speed, gravity, jumpForce; public int maxRise; }

    public static JumpInfo GetJump()
    {
        float jumpForce = 18f, speed = 7.5f, gravityScale = 4f;
        var stats = AssetDatabase.LoadAssetAtPath<ScriptableStats>(StatsPath);
        if (stats != null) { jumpForce = stats.JumpForce; speed = stats.WalkSpeed; }
        var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
        var rb = player != null ? player.GetComponent<Rigidbody2D>() : null;
        if (rb != null) gravityScale = rb.gravityScale;
        float g = Physics2D.gravity.magnitude * gravityScale;
        float h = jumpForce * jumpForce / (2f * g);
        return new JumpInfo { height = h, speed = speed, gravity = g, jumpForce = jumpForce, maxRise = Mathf.FloorToInt(h - 0.35f) };
    }

    enum Side { N, S, E, W }

    class DoorInfo
    {
        public string name; public Side side; public int a, b, line; // a..b = span, line = x (E/W) or y (N/S)
        public List<Vector2Int> entries = new List<Vector2Int>();
        public System.Func<Vector2Int, bool> isExit;
    }

    /// <summary>Returns the problems found (empty = OK). map = ASCII drawing (top row first).</summary>
    public static List<string> Validate(GameObject room, out string map)
    {
        // A prefab asset has no live tilemap data: open a copy of its contents
        string assetPath = AssetDatabase.GetAssetPath(room);
        if (!string.IsNullOrEmpty(assetPath) && PrefabUtility.IsPartOfPrefabAsset(room))
        {
            var contents = PrefabUtility.LoadPrefabContents(assetPath);
            try { return ValidateLoaded(contents, out map); }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }
        return ValidateLoaded(room, out map);
    }

    static List<string> ValidateLoaded(GameObject room, out string map)
    {
        var problems = new List<string>();
        map = "";
        var hand = room.GetComponent<HandmadeRoom>();
        var door = room.GetComponent<Door>();
        if (hand == null || door == null) { problems.Add("Falta HandmadeRoom o Door en la raiz."); return problems; }

        var doors = new HashSet<GameObject>(door.AllDoors);
        Tilemap walls = Child(room, HandmadeRoom.WallsName);
        Tilemap platforms = Child(room, HandmadeRoom.PlatformsName);
        if (walls == null) { problems.Add("Falta el tilemap 'Walls'."); return problems; }

        walls.CompressBounds();
        BoundsInt b = walls.cellBounds;
        int w = b.size.x, h = b.size.y;
        var solid = new bool[w, h];
        var plat = new bool[w, h];

        void Mark(Tilemap tm, bool[,] grid, bool value)
        {
            if (tm == null) return;
            tm.CompressBounds();
            foreach (var p in tm.cellBounds.allPositionsWithin)
            {
                if (!tm.HasTile(p)) continue;
                Vector3Int c = walls.WorldToCell(tm.GetCellCenterWorld(p)) - b.min;
                if (c.x >= 0 && c.y >= 0 && c.x < w && c.y < h) grid[c.x, c.y] = value;
            }
        }

        Mark(walls, solid, true);
        Mark(platforms, plat, true);
        foreach (var piece in hand.DoorPieces)
        {
            if (piece?.whenOpen == null) continue;
            foreach (var tm in piece.whenOpen.GetComponentsInChildren<Tilemap>(true))
            {
                if (tm.gameObject.name == HandmadeRoom.WallsName) Mark(tm, solid, true);
                if (tm.gameObject.name == HandmadeRoom.PlatformsName) Mark(tm, plat, true);
            }
        }
        // Cells with no background are outside the room (the missing corner of an L): treat them as rock
        var inside = new bool[w, h];
        Mark(Child(room, "Background"), inside, true);
        Mark(walls, inside, true);
        for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) if (!inside[x, y]) solid[x, y] = true;
        bool Inside(int x, int y) => In(x, y, w, h) && inside[x, y];

        // Open doors are holes in the frame
        var doorCells = new List<(GameObject go, List<Vector2Int> cells)>();
        foreach (var d in doors)
        {
            var tm = d.GetComponent<Tilemap>();
            if (tm == null) continue;
            tm.CompressBounds();
            var cells = new List<Vector2Int>();
            foreach (var p in tm.cellBounds.allPositionsWithin)
                if (tm.HasTile(p)) { var c = walls.WorldToCell(tm.GetCellCenterWorld(p)) - b.min; cells.Add(new Vector2Int(c.x, c.y)); }
            if (cells.Count == 0) continue;
            foreach (var c in cells) if (In(c.x, c.y, w, h)) solid[c.x, c.y] = false;
            doorCells.Add((d, cells));
        }

        JumpInfo jump = GetJump();
        bool S(int x, int y) => !In(x, y, w, h) ? false : solid[x, y];
        bool P(int x, int y) => In(x, y, w, h) && plat[x, y];
        bool Stand(int x, int y) => In(x, y, w, h) && In(x, y + 1, w, h) && !solid[x, y] && !solid[x, y + 1] && !plat[x, y] && (S(x, y - 1) || P(x, y - 1));

        // ---------- Doors ----------
        var infos = new List<DoorInfo>();
        foreach (var (go, cells) in doorCells)
        {
            var info = new DoorInfo { name = go.transform.parent != null ? go.transform.parent.name + "/" + go.name : go.name };
            int minX = cells.Min(c => c.x), maxX = cells.Max(c => c.x), minY = cells.Min(c => c.y), maxY = cells.Max(c => c.y);
            if (minY == maxY)
            {
                // The room is on the side that has background: below = top door, above = bottom door
                int mid = (minX + maxX) / 2;
                info.side = Inside(mid, minY - 1) && !Inside(mid, minY + 1) ? Side.N : Side.S;
                info.a = minX; info.b = maxX; info.line = minY;
            }
            else
            {
                int mid = (minY + maxY) / 2;
                info.side = Inside(minX + 1, mid) && !Inside(minX - 1, mid) ? Side.W : Side.E;
                info.a = minY; info.b = maxY; info.line = minX;
            }
            infos.Add(info);
        }

        // ---------- Reserved zones ----------
        foreach (var d in infos)
        {
            IEnumerable<Vector2Int> zone;
            switch (d.side)
            {
                case Side.W: zone = Rect(d.line + 1, d.a, d.line + ZoneDepth, d.b); break;
                case Side.E: zone = Rect(d.line - ZoneDepth, d.a, d.line - 1, d.b); break;
                case Side.N: zone = Rect(d.a, d.line - ZoneDepth, d.b, d.line - 1); break;
                default: zone = Rect(d.a - FloorZoneSide, d.line + 1, d.b + FloorZoneSide, d.line + FloorZoneDepth); break;
            }
            var blocked = zone.Where(c => In(c.x, c.y, w, h) && solid[c.x, c.y]).ToList();
            if (blocked.Count > 0) problems.Add($"Puerta {d.side} ({d.name}): {blocked.Count} tile(s) solidos frente a la puerta, ej. ({blocked[0].x},{blocked[0].y}).");
        }

        // ---------- Entries and exits ----------
        foreach (var d in infos)
        {
            switch (d.side)
            {
                case Side.W:
                case Side.E:
                    {
                        int x = d.side == Side.W ? d.line + 1 : d.line - 1;
                        for (int y = d.a; y <= d.b; y++) if (Stand(x, y)) { d.entries.Add(new Vector2Int(x, y)); break; }
                        int dir = d.side == Side.W ? 1 : -1;
                        if (d.entries.Count == 0) problems.Add($"Puerta {d.side} ({d.name}): no hay donde pararse al entrar (falta piso en y={d.a - 1} junto a la puerta).");
                        int line = d.line, a = d.a, bb = d.b;
                        d.isExit = c => (c.x == line + dir || c.x == line + 2 * dir) && c.y >= a && c.y <= bb;
                        break;
                    }
                case Side.S:
                    {
                        foreach (int x in new[] { d.a - 1, d.a - 2, d.b + 1, d.b + 2 })
                            if (Stand(x, d.line + 1)) d.entries.Add(new Vector2Int(x, d.line + 1));
                        if (d.entries.Count == 0) problems.Add($"Puerta S ({d.name}): al subir desde el cuarto de abajo no hay piso junto al hueco.");
                        int a = d.a, bb = d.b, line = d.line;
                        d.isExit = c => c.y == line + 1 && (c.x == a - 1 || c.x == bb + 1);
                        break;
                    }
                case Side.N:
                    {
                        // Falling in: land where the player falls straight down in each column of the gap
                        bool fallsThrough = false;
                        for (int x = d.a; x <= d.b; x++)
                        {
                            int y = d.line - 1;
                            while (y > 0 && !Stand(x, y)) { if (S(x, y)) break; y--; }
                            if (Stand(x, y)) d.entries.Add(new Vector2Int(x, y)); else fallsThrough = true;
                        }
                        if (fallsThrough) problems.Add($"Puerta N ({d.name}): al caer por la puerta de arriba en algunas columnas no hay nada que te atrape (caes hasta el fondo).");
                        int a = d.a, bb = d.b, line = d.line, rise = jump.maxRise;
                        // Leaving: from a spot under the gap the jump must reach the floor of the room above (line + 2)
                        d.isExit = c =>
                        {
                            if (c.x < a - 1 || c.x > bb + 1) return false;
                            if (line + 2 - c.y > rise) return false;
                            for (int y = c.y; y <= line; y++) if (S(c.x, y)) return false;
                            return true;
                        };
                        break;
                    }
            }
        }

        // ---------- Movement graph ----------
        var spots = new List<Vector2Int>();
        for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) if (Stand(x, y)) spots.Add(new Vector2Int(x, y));
        var index = new Dictionary<Vector2Int, int>();
        for (int i = 0; i < spots.Count; i++) index[spots[i]] = i;

        int Reach(int dy)
        {
            // air time going up (and landing dy higher) * horizontal speed, minus the player's width
            float up = jump.jumpForce / jump.gravity;
            float fall = Mathf.Sqrt(2f * Mathf.Max(0f, jump.height - dy) / jump.gravity);
            return Mathf.FloorToInt(jump.speed * (up + fall) * 0.85f - 1f);
        }

        bool ClearColumn(int x, int y0, int y1) { for (int y = Mathf.Min(y0, y1); y <= Mathf.Max(y0, y1); y++) if (S(x, y)) return false; return true; }

        bool CanMove(Vector2Int from, Vector2Int to)
        {
            int dx = to.x - from.x, dy = to.y - from.y;
            if (dy > jump.maxRise) return false;
            int adx = Mathf.Abs(dx);
            if (dy == 0 && adx == 1) return true; // walk
            if (adx > Reach(dy)) return false;
            int top = Mathf.Max(from.y, to.y) + 1;
            if (dy > 0 && !ClearColumn(from.x, from.y, top)) return false;     // head room to jump up
            if (!ClearColumn(to.x, to.y, top)) return false;                  // come down onto the target from above
            int step = dx > 0 ? 1 : -1;
            if (dx != 0)
                for (int x = from.x + step; x != to.x; x += step) if (S(x, top) || S(x, top - 1) && dy >= 0) return false;
            return true;
        }

        var edges = new List<int>[spots.Count];
        for (int i = 0; i < spots.Count; i++)
        {
            edges[i] = new List<int>();
            for (int j = 0; j < spots.Count; j++) if (i != j && CanMove(spots[i], spots[j])) edges[i].Add(j);
        }

        bool[] Flood(IEnumerable<Vector2Int> start)
        {
            var seen = new bool[spots.Count];
            var q = new Queue<int>();
            foreach (var s in start) if (index.TryGetValue(s, out int i) && !seen[i]) { seen[i] = true; q.Enqueue(i); }
            while (q.Count > 0) { int i = q.Dequeue(); foreach (int j in edges[i]) if (!seen[j]) { seen[j] = true; q.Enqueue(j); } }
            return seen;
        }

        var anyReached = new bool[spots.Count];
        foreach (var from in infos)
        {
            if (from.entries.Count == 0) continue;
            bool[] seen = Flood(from.entries);
            for (int i = 0; i < seen.Length; i++) anyReached[i] |= seen[i];
            foreach (var to in infos)
            {
                if (to == from || to.isExit == null) continue;
                bool ok = false;
                for (int i = 0; i < spots.Count && !ok; i++) if (seen[i] && to.isExit(spots[i])) ok = true;
                if (!ok) problems.Add($"Sin gancho no se llega de la puerta {from.side} a la puerta {to.side}.");
            }
        }

        // ---------- Traps: places you can reach but never leave through a door ----------
        int traps = 0; Vector2Int firstTrap = default;
        for (int i = 0; i < spots.Count; i++)
        {
            if (!anyReached[i]) continue;
            bool[] seen = Flood(new[] { spots[i] });
            bool canLeave = false;
            for (int j = 0; j < spots.Count && !canLeave; j++)
                if (seen[j]) foreach (var d in infos) if (d.isExit != null && d.isExit(spots[j])) { canLeave = true; break; }
            if (!canLeave) { if (traps == 0) firstTrap = spots[i]; traps++; anyReached[i] = false; }
        }
        if (traps > 0) problems.Add($"Trampa: {traps} lugar(es) a los que puedes caer pero de donde no sales sin gancho, ej. ({firstTrap.x},{firstTrap.y}). Marcados con x.");

        // ---------- ASCII map ----------
        var sb = new StringBuilder();
        sb.AppendLine($"g={jump.gravity:0.00} fuerza={jump.jumpForce} vel={jump.speed}. Salto: {jump.height:0.0} tiles (sube hasta {jump.maxRise} tiles), alcance horizontal ~{Reach(jump.maxRise)}-{Reach(0)} tiles. # solido, - repisa, o alcanzable");
        for (int y = h - 1; y >= 0; y--)
        {
            for (int x = 0; x < w; x++)
            {
                char ch = solid[x, y] ? '#' : plat[x, y] ? '-' : '.';
                if (index.TryGetValue(new Vector2Int(x, y), out int i)) ch = anyReached[i] ? 'o' : 'x';
                sb.Append(ch);
            }
            sb.AppendLine();
        }
        map = sb.ToString();
        return problems;
    }

    static IEnumerable<Vector2Int> Rect(int x0, int y0, int x1, int y1)
    {
        for (int x = x0; x <= x1; x++) for (int y = y0; y <= y1; y++) yield return new Vector2Int(x, y);
    }

    static bool In(int x, int y, int w, int h) => x >= 0 && y >= 0 && x < w && y < h;

    static Tilemap Child(GameObject room, string n)
    {
        foreach (Transform c in room.transform) if (c.name == n) { var tm = c.GetComponent<Tilemap>(); if (tm != null) return tm; }
        return null;
    }
}
