using System.Collections.Generic;
using UnityEngine;

public class LevelEnemy : MonoBehaviour
{
    /// <summary>Enemies of the current map that are still alive. Used by the enemy compass.</summary>
    public static readonly List<LevelEnemy> Active = new List<LevelEnemy>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Active.Clear(); }

    private LevelObjective objective;
    private bool reported;

    public bool IsBoss { get; private set; }
    public int Generation { get; private set; }

    public void Bind(LevelObjective levelObjective, bool isBoss)
    {
        objective = levelObjective;
        IsBoss = isBoss;
        Generation = levelObjective.Generation;
        reported = false;
        if (!Active.Contains(this)) Active.Add(this);
    }

    public void NotifyDefeated()
    {
        if (reported || objective == null) return;
        reported = true;
        Active.Remove(this);
        objective.NotifyDefeated(this);
    }

    private void OnDestroy()
    {
        Active.Remove(this);
        NotifyDefeated();
    }
}