using System.Collections.Generic;
using UnityEngine;

// Hace que algo capturado o lanzado (enemigos, cajas) atraviese las plataformas traspasables (one-way).
// - Mientras lo sostienes con el lazo: siempre las atraviesa.
// - Al lanzarlo: las sigue atravesando en el vuelo; cuando ya va lento (o paso mucho tiempo) y no esta
//   dentro de ninguna, vuelve a chocar con ellas normal (para que pueda pararse encima otra vez).
// Lo agrega CharacterCombat al capturar (Hold) y al soltar/lanzar (Throw).
public class PlatformPassThrough : MonoBehaviour
{
    private const float MinFlightTime = 0.2f;
    private const float MaxFlightTime = 4f;
    private const float SlowSpeed = 2.5f;

    private readonly List<Collider2D> own = new List<Collider2D>();
    private readonly List<Collider2D> platforms = new List<Collider2D>();
    private Rigidbody2D rb;
    private bool held, ignoring;
    private float flightTime;

    /// <summary>While captured: always passes through one-way platforms.</summary>
    public static void Hold(GameObject go)
    {
        if (go == null) return;
        var p = go.GetComponent<PlatformPassThrough>();
        if (p == null) p = go.AddComponent<PlatformPassThrough>();
        p.held = true;
        p.SetIgnore(true);
    }

    /// <summary>Released or thrown: keeps passing through while it flies, then collides normally again.</summary>
    public static void Throw(GameObject go)
    {
        if (go == null) return;
        var p = go.GetComponent<PlatformPassThrough>();
        if (p == null) p = go.AddComponent<PlatformPassThrough>();
        p.held = false;
        p.flightTime = 0f;
        p.SetIgnore(true);
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    private void SetIgnore(bool ignore)
    {
        if (ignore)
        {
            // Refresh the lists every time (rooms can be generated after this object existed)
            own.Clear();
            foreach (var c in GetComponentsInChildren<Collider2D>()) if (!c.isTrigger) own.Add(c);
            platforms.Clear();
            foreach (var e in FindObjectsByType<PlatformEffector2D>(FindObjectsSortMode.None))
            {
                if (!e.useOneWay) continue;
                foreach (var c in e.GetComponents<Collider2D>()) if (c.usedByEffector) platforms.Add(c);
            }
        }
        foreach (var a in own)
            foreach (var b in platforms)
                if (a != null && b != null) Physics2D.IgnoreCollision(a, b, ignore);
        ignoring = ignore;
    }

    private void FixedUpdate()
    {
        if (!ignoring || held) return;
        flightTime += Time.fixedDeltaTime;
        if (flightTime < MinFlightTime) return;

        bool slow = rb == null || rb.linearVelocity.magnitude < SlowSpeed;
        if ((slow || flightTime > MaxFlightTime) && !InsideAPlatform())
        {
            SetIgnore(false);
            Destroy(this);
        }
    }

    // Turning collisions back on while it is inside a platform would get it stuck
    private bool InsideAPlatform()
    {
        foreach (var a in own)
        {
            if (a == null) continue;
            foreach (var b in platforms)
                if (b != null && a.Distance(b).isOverlapped) return true;
        }
        return false;
    }

    private void OnDestroy()
    {
        if (ignoring) SetIgnore(false);
    }
}
