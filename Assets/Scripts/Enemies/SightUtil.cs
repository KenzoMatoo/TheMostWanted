using UnityEngine;

// Linea de vision de los enemigos: las plataformas que se pueden traspasar (one-way, con PlatformEffector2D)
// estan en la misma capa que las paredes, pero NO deben tapar la vista ni las balas.
// Estos raycasts ignoran triggers y plataformas one-way; solo las paredes de verdad bloquean.
public static class SightUtil
{
    private static readonly RaycastHit2D[] hits = new RaycastHit2D[16];

    /// <summary>A collider that really blocks sight: not a trigger and not a one-way platform.</summary>
    public static bool BlocksSight(Collider2D c) => c != null && !c.isTrigger && !c.usedByEffector;

    /// <summary>Like Physics2D.Raycast, but skips triggers and one-way platforms. Returns the closest blocking hit.</summary>
    public static RaycastHit2D Raycast(Vector2 origin, Vector2 direction, float distance, int mask)
    {
        var filter = new ContactFilter2D { useTriggers = false };
        filter.SetLayerMask(mask);
        int n = Physics2D.Raycast(origin, direction, filter, hits, distance);

        RaycastHit2D best = default;
        float bestDist = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            if (!BlocksSight(hits[i].collider)) continue;
            if (hits[i].distance < bestDist) { bestDist = hits[i].distance; best = hits[i]; }
        }
        return best;
    }

    /// <summary>True if a real wall is between the two points (one-way platforms don't count).</summary>
    public static bool Blocked(Vector2 from, Vector2 to, int mask)
    {
        Vector2 d = to - from;
        float dist = d.magnitude;
        if (dist < 0.0001f) return false;
        return Raycast(from, d / dist, dist, mask).collider != null;
    }
}
