using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Arrow that points the player to the closest enemy measured by the path the player can really walk
/// (room to room through the grid), not in a straight line.
/// - Enemy in the same room cell as the player: the arrow points straight at it.
/// - Enemy in another room: the arrow points to the next room on the shortest path (the door to take).
/// - Enemy with no walkable path (see CanPass): it is skipped and the next closest one is used.
/// - No reachable enemy: the arrow is hidden.
/// Put this script on the Canvas (or any object that is NOT the arrow, because the arrow gets disabled).
/// </summary>
public class EnemyCompass : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The UI arrow. Its sprite should point UP (otherwise use Sprite Angle Offset)")]
    [SerializeField] private RectTransform arrow;
    [Tooltip("Optional. If empty, the object with the player tag is searched")]
    [SerializeField] private Transform player;
    [SerializeField] private string playerTag = "Player";

    [Header("Behaviour")]
    [SerializeField] private bool includeBoss = true;
    [Tooltip("Seconds between path recalculations (the rotation is still smooth every frame)")]
    [SerializeField, Min(0.02f)] private float refreshInterval = 0.1f;
    [Tooltip("Degrees per second")]
    [SerializeField] private float rotationSpeed = 720f;
    [SerializeField] private float spriteAngleOffset = 0f;

    [Header("Orbit around the player (Screen Space - Overlay canvas only)")]
    [SerializeField] private bool orbitPlayer = false;
    [SerializeField] private float orbitRadius = 90f;   // pixels
    [SerializeField] private Camera cam;

    [Header("Grid mapping")]
    [Tooltip("Local offset (in world units) between a room's grid position and its real center. Only needed if the room prefabs are not centered on their origin")]
    [SerializeField] private Vector2 roomCenterOffset = Vector2.zero;

    private float timer;
    private bool hasTarget;
    private Transform aimTransform;
    private Vector3 aimPoint;
    private float currentAngle;
    private bool wasVisible;

    // BFS buffers, reused so there is no garbage every refresh
    private int[] dist;
    private int[] parent;
    private readonly Queue<int> queue = new Queue<int>();
    private static readonly int[] noSteps = { };

    private void Awake()
    {
        if (cam == null) cam = Camera.main;
    }

    private void Update()
    {
        if (arrow == null) return;

        if (player == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag(playerTag);
            if (p == null) { SetVisible(false); return; }
            player = p.transform;
        }

        timer -= Time.deltaTime;
        if (timer <= 0f)
        {
            timer = refreshInterval;
            Refresh();
        }

        SetVisible(hasTarget);
        if (!hasTarget) return;

        Vector3 aim = aimTransform != null ? aimTransform.position : aimPoint;
        Vector2 dir = aim - player.position;
        if (dir.sqrMagnitude < 0.0001f) return;

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f + spriteAngleOffset;
        if (!wasVisible) currentAngle = angle;   // snap the first frame
        currentAngle = Mathf.MoveTowardsAngle(currentAngle, angle, rotationSpeed * Time.deltaTime);
        arrow.localEulerAngles = new Vector3(0f, 0f, currentAngle);

        if (orbitPlayer && cam != null)
        {
            Vector3 screen = cam.WorldToScreenPoint(player.position);
            arrow.position = screen + (Vector3)(dir.normalized * orbitRadius);
        }
        wasVisible = true;
    }

    private void SetVisible(bool visible)
    {
        if (arrow.gameObject.activeSelf != visible) arrow.gameObject.SetActive(visible);
        if (!visible) wasVisible = false;
    }

    // ------------------------------------------------------------------ Target selection

    private void Refresh()
    {
        hasTarget = false;
        aimTransform = null;

        MapGenerationBase map = MapGenerationBase.instance;
        RoomManager rooms = RoomManager.instance;
        if (map == null || rooms == null || player == null) return;

        int[] grid = map.getMapGeneration;
        if (grid == null || grid.Length == 0) return;

        int start = WorldToGrid(player.position, grid);
        if (start < 0) return;

        ComputePaths(start, grid);

        LevelObjective objective = map.Objective;

        Transform target = null;
        int targetCell = -1, targetSteps = int.MaxValue;

        if (objective.IsCompleted)
        {
            // Platformer maps, or the condition is already met: guide the player to the exit
            LevelExit exit = objective.Exit;
            if (exit == null || !exit.gameObject.activeInHierarchy) return;

            int cell = WorldToGrid(exit.transform.position, grid);
            if (cell < 0 || dist[cell] < 0) return;

            target = exit.transform; targetCell = cell; targetSteps = dist[cell];
        }
        else
        {
            bool bossOnly = objective.Type == ObjectiveType.DefeatBoss;
            int generation = objective.Generation;
            float bestSqr = float.MaxValue;

            foreach (LevelEnemy enemy in LevelEnemy.Active)
            {
                if (enemy == null || !enemy.isActiveAndEnabled) continue;
                if (enemy.Generation != generation) continue;

                if (bossOnly) { if (!enemy.IsBoss) continue; }
                else if (enemy.IsBoss && !includeBoss) continue;

                int cell = WorldToGrid(enemy.transform.position, grid);
                if (cell < 0 || dist[cell] < 0) continue;

                int steps = dist[cell];
                float sqr = (enemy.transform.position - player.position).sqrMagnitude;
                if (steps < targetSteps || (steps == targetSteps && sqr < bestSqr))
                {
                    target = enemy.transform; targetCell = cell; targetSteps = steps; bestSqr = sqr;
                }
            }
        }

        if (target == null) return;

        if (targetSteps == 0)
        {
            aimTransform = target;   // same room cell: straight at it
        }
        else
        {
            // Walk the path backwards to find the first room after the player's
            int step = targetCell;
            while (parent[step] != start) step = parent[step];
            aimPoint = GridToWorld(step);
        }
        hasTarget = true;
    }

    /// <summary>
    /// Can the player walk from grid cell 'from' to the adjacent cell 'to'? By default every pair of adjacent room cells
    /// is connected (that is how the doors are opened). Replace this if you add locked doors or one-way rooms.
    /// </summary>
    protected virtual bool CanPass(int from, int to, int[] grid)
    {
        return true;
    }

    private void ComputePaths(int start, int[] grid)
    {
        if (dist == null || dist.Length != grid.Length)
        {
            dist = new int[grid.Length];
            parent = new int[grid.Length];
        }
        for (int i = 0; i < dist.Length; i++) { dist[i] = -1; parent[i] = -1; }

        int w = MapGenerationBase.GridWidth;
        int[] steps = { -1, 1, -w, w };

        queue.Clear();
        dist[start] = 0;
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            foreach (int s in steps)
            {
                int next = current + s;
                if (next < 0 || next >= grid.Length) continue;
                // Don't wrap around the grid edges when moving left/right
                if ((s == -1 || s == 1) && next / w != current / w) continue;
                if (grid[next] != 1 || dist[next] != -1) continue;
                if (!CanPass(current, next, grid)) continue;

                dist[next] = dist[current] + 1;
                parent[next] = current;
                queue.Enqueue(next);
            }
        }
    }

    // ------------------------------------------------------------------ World <-> grid

    // Rooms are spawned at (x * offsetX, -y * offsetY) as children of the RoomManager
    private int WorldToGrid(Vector3 world, int[] grid)
    {
        RoomManager rm = RoomManager.instance;
        if (Mathf.Approximately(rm.offsetX, 0f) || Mathf.Approximately(rm.offsetY, 0f)) return -1;

        Vector3 local = rm.transform.InverseTransformPoint(world) - (Vector3)roomCenterOffset;
        float fx = local.x / rm.offsetX;
        float fy = -local.y / rm.offsetY;

        int gx = Mathf.RoundToInt(fx);
        int gy = Mathf.RoundToInt(fy);
        int w = MapGenerationBase.GridWidth;
        int h = MapGenerationBase.GridHeight;

        // The rounded cell may be empty (doorways, cave edges): take the closest room cell around it
        int best = -1;
        float bestSqr = float.MaxValue;
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                int x = gx + dx, y = gy + dy;
                if (x < 0 || y < 0 || x >= w || y >= h) continue;
                int index = y * w + x;
                if (grid[index] != 1) continue;

                float sqr = (fx - x) * (fx - x) + (fy - y) * (fy - y);
                if (sqr < bestSqr) { bestSqr = sqr; best = index; }
            }
        return best;
    }

    private Vector3 GridToWorld(int index)
    {
        RoomManager rm = RoomManager.instance;
        int w = MapGenerationBase.GridWidth;
        Vector3 local = new Vector3((index % w) * rm.offsetX, -(index / w) * rm.offsetY, 0f) + (Vector3)roomCenterOffset;
        return rm.transform.TransformPoint(local);
    }
}