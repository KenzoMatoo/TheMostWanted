using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum LevelType { Normal, Platformer, Elite, Boss }

/// <summary>
/// Defines a run (session): an ordered list of steps. Each step has a pool of level types the player can pick from,
/// and optionally only shows a random subset of that pool. Create the asset with
/// Assets > Create > Game > Run Progression, then edit the steps and the lore bank in the Inspector.
///
/// The CONFIG lives in the asset. The RUN STATE (current step, rolled options, lore) is static, so it survives scene
/// changes and is never saved into the asset.
///
/// Flow: the hub calls StartRun() once, reads CurrentOptions / CurrentLore, and loads the chosen scene.
/// When a level is finished, LevelExit calls RunProgression.NotifyLevelCompleted(), which moves to the next step.
/// </summary>
[CreateAssetMenu(fileName = "RunProgression", menuName = "Game/Run Progression")]
public class RunProgression : ScriptableObject
{
    [Serializable]
    public class LevelEntry
    {
        public LevelType type;
        public string displayName;
        [Tooltip("Exact scene name (it must be in Build Settings)")]
        public string sceneName;
    }

    [Serializable]
    public class Step
    {
        public string name = "Step";
        [Tooltip("Level types that can appear in this step")]
        public List<LevelType> pool = new List<LevelType>();
        [Tooltip("How many random options of the pool are offered. 0 = the whole pool")]
        [Min(0)] public int optionsShown = 0;
        [Tooltip("Lore lines for this step. Leave empty to use the Default Lore bank.\n" +
                 "Placeholders: {step} {total} {options}")]
        [TextArea(1, 3)] public List<string> lore = new List<string>();
    }

    [Header("Levels (type -> scene)")]
    public List<LevelEntry> levels = new List<LevelEntry>
    {
        new LevelEntry { type = LevelType.Normal,     displayName = "Normal",     sceneName = "NormalLevel" },
        new LevelEntry { type = LevelType.Platformer, displayName = "Platformer", sceneName = "PlatformerLevel" },
        new LevelEntry { type = LevelType.Elite,      displayName = "Elite",      sceneName = "EliteLevel" },
        new LevelEntry { type = LevelType.Boss,       displayName = "Boss",       sceneName = "BossLevel" },
    };

    [Header("Run order (edit freely: add, remove or reorder steps)")]
    public List<Step> steps = new List<Step>
    {
        new Step { name = "1 - Normal",            pool = { LevelType.Normal } },
        new Step { name = "2 - Normal / Platformer", pool = { LevelType.Normal, LevelType.Platformer } },
        new Step { name = "3 - Elite",             pool = { LevelType.Elite } },
        new Step { name = "4 - Two of three",      pool = { LevelType.Normal, LevelType.Platformer, LevelType.Elite }, optionsShown = 2 },
        new Step { name = "5 - All open",          pool = { LevelType.Normal, LevelType.Platformer, LevelType.Elite } },
        new Step { name = "6 - Boss",              pool = { LevelType.Boss } },
    };

    [Header("Lore bank (used by steps that have no lore of their own)")]
    [Tooltip("Placeholders: {step} {total} {options}")]
    [TextArea(1, 3)]
    public List<string> defaultLore = new List<string>
    {
        "The corridors shift behind you. Whatever hunts here has noticed your footsteps.",
        "A faint signal crackles from deeper inside. Someone, or something, is calling you onward.",
        "The air grows heavy. Old machinery hums in the walls, still guarding what it was built to protect.",
        "You find scratches on the wall, a tally of those who came before. The count is not encouraging.",
        "A distant echo answers your every step. You are not alone down here.",
        "The path forks ahead. Each way smells of rust and ozone: {options}.",
        "Stage {step} of {total}. The deeper you go, the less the maps can be trusted.",
        "Dust swirls in the dark. Somewhere ahead, a door waits for those who survive.",
    };

    [Tooltip("Shown by the hub when every step has been completed")]
    [TextArea(1, 3)] public string runCompleteText = "The way is clear. You made it through.";

    // ------------------------------------------------------------------ Run state (static, survives scene loads)

    public static RunProgression Active { get; private set; }
    public static bool IsRunActive => Active != null;

    private static int stepIndex;
    private static List<LevelType> options;
    private static string lore;
    private static int lastCompletionFrame = -1;
    private static readonly HashSet<string> usedLore = new HashSet<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Active = null;
        stepIndex = 0;
        options = null;
        lore = null;
        lastCompletionFrame = -1;
        usedLore.Clear();
    }

    /// <summary>Ends the current run (the player died). The hub starts a new one next time.</summary>
    public static void EndRun() => ResetStatics();

    /// <summary>Begins a new run from the first step. Call it from the hub (or main menu) when the player starts.</summary>
    public void StartRun()
    {
        ResetStatics();
        Active = this;
    }

    public int StepNumber => Mathf.Min(stepIndex + 1, steps.Count);
    public int StepCount => steps.Count;
    public bool IsComplete => stepIndex >= steps.Count;
    public Step CurrentStep => IsComplete ? null : steps[stepIndex];

    /// <summary>The level types offered now. Rolled once per step, so returning to the hub shows the same choices.</summary>
    public IReadOnlyList<LevelType> CurrentOptions
    {
        get
        {
            if (IsComplete) return Array.Empty<LevelType>();
            if (options == null) options = RollOptions(CurrentStep);
            return options;
        }
    }

    /// <summary>The lore line of the current step. Picked once per step, avoiding repeats inside the same run.</summary>
    public string CurrentLore
    {
        get
        {
            if (IsComplete) return runCompleteText;
            if (lore == null) lore = BuildLore(CurrentStep);
            return lore;
        }
    }

    public bool IsAvailable(LevelType type) => CurrentOptions.Contains(type);

    public string GetSceneName(LevelType type)
    {
        LevelEntry entry = levels.FirstOrDefault(l => l.type == type);
        if (entry == null || string.IsNullOrEmpty(entry.sceneName))
        {
            Debug.LogError($"RunProgression: no scene name set for level type {type}.", this);
            return null;
        }
        return entry.sceneName;
    }

    public string GetDisplayName(LevelType type)
    {
        LevelEntry entry = levels.FirstOrDefault(l => l.type == type);
        return entry != null && !string.IsNullOrEmpty(entry.displayName) ? entry.displayName : type.ToString();
    }

    /// <summary>
    /// Moves the run to the next step. Called by LevelExit when the player enters the unlocked exit.
    /// Does nothing if no run is active (e.g. when testing a level scene directly).
    /// </summary>
    public static void NotifyLevelCompleted()
    {
        if (Active == null || Active.IsComplete) return;
        if (lastCompletionFrame == Time.frameCount) return;   // the trigger can fire twice in one frame
        lastCompletionFrame = Time.frameCount;

        stepIndex++;
        options = null;
        lore = null;
    }

    // ------------------------------------------------------------------ Internals

    private static List<LevelType> RollOptions(Step step)
    {
        List<LevelType> pool = step.pool.Distinct().ToList();

        if (step.optionsShown > 0 && step.optionsShown < pool.Count)
        {
            List<LevelType> picked = pool.OrderBy(_ => UnityEngine.Random.value).Take(step.optionsShown).ToList();
            pool = pool.Where(picked.Contains).ToList();   // keep the order of the Inspector
        }
        return pool;
    }

    private string BuildLore(Step step)
    {
        List<string> source = step.lore.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        if (source.Count == 0) source = defaultLore.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        if (source.Count == 0) return "";

        List<string> fresh = source.Where(s => !usedLore.Contains(s)).ToList();
        if (fresh.Count == 0)
        {
            foreach (string s in source) usedLore.Remove(s);   // everything was used: start over
            fresh = source;
        }

        string pick = fresh[UnityEngine.Random.Range(0, fresh.Count)];
        usedLore.Add(pick);

        string optionNames = string.Join(" / ", CurrentOptions.Select(GetDisplayName));
        return pick.Replace("{step}", StepNumber.ToString())
                   .Replace("{total}", StepCount.ToString())
                   .Replace("{options}", optionNames);
    }

    private void OnValidate()
    {
        foreach (Step step in steps)
        {
            if (step.pool.Count == 0)
                Debug.LogWarning($"RunProgression: step '{step.name}' has an empty pool.", this);

            foreach (LevelType t in step.pool)
                if (!levels.Any(l => l.type == t))
                    Debug.LogWarning($"RunProgression: step '{step.name}' uses {t} but it has no entry in Levels.", this);
        }
    }
}