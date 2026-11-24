using UnityEngine;

// Resistencia del jugador: la gastan el gancho (al engancharse y mientras cuelgas) y tener algo
// capturado. Se recupera sola despues de un momento sin usarla. Los valores estan en ScriptableStats.
// Se agrega sola al jugador la primera vez que alguien la pide con PlayerStamina.Get().
public class PlayerStamina : MonoBehaviour
{
    private ScriptableStats stats;
    private float current;
    private float lastUseTime = -999f;

    public float Max => stats != null ? stats.MaxStamina : 100f;
    public float Current => current;
    public float Current01 => Max > 0f ? current / Max : 0f;
    /// <summary>Ultima vez que se intento gastar sin tener suficiente (la UI parpadea en rojo).</summary>
    public float LastFailTime { get; private set; } = -999f;

    public static PlayerStamina Get(GameObject player)
    {
        var s = player.GetComponent<PlayerStamina>();
        return s != null ? s : player.AddComponent<PlayerStamina>();
    }

    void Awake()
    {
        var combat = GetComponent<CharacterCombat>();
        stats = combat != null ? combat.Stats : null;
        current = Max;
    }

    void Update()
    {
        if (stats == null || current >= Max) return;
        if (Time.time - lastUseTime < stats.StaminaRegenDelay) return;
        current = Mathf.Min(Max, current + stats.StaminaRegenPerSecond * Time.deltaTime);
    }

    /// <summary>Gasta una cantidad de golpe. Devuelve false (sin gastar) si no alcanza.</summary>
    public bool TrySpend(float amount)
    {
        if (amount <= 0f) return true;
        if (current < amount)
        {
            LastFailTime = Time.time;
            return false;
        }
        current -= amount;
        lastUseTime = Time.time;
        return true;
    }

    /// <summary>Gasto continuo. Devuelve false cuando ya no queda resistencia.</summary>
    public bool Drain(float perSecond, float deltaTime)
    {
        lastUseTime = Time.time;
        current -= perSecond * deltaTime;
        if (current > 0f) return true;
        current = 0f;
        LastFailTime = Time.time;
        return false;
    }

    public void Refill() => current = Max;
}
