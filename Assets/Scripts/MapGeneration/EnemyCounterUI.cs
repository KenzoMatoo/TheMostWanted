using TMPro;
using UnityEngine;

/// <summary>
/// Shows how many enemies are left on maps that require killing them all (normal and elite).
/// On other maps the counter is hidden. Put it on an object that is always active (e.g. the Canvas)
/// and assign the TextMeshPro label.
/// </summary>
public class EnemyCounterUI : MonoBehaviour
{
    [SerializeField] private TMP_Text label;

    [Tooltip("{0} = enemies left, {1} = total enemies of the map")]
    [SerializeField] private string format = "Enemies left: {0}";

    [Tooltip("Optional text once every enemy is dead. Leave empty to keep showing the count")]
    [SerializeField] private string completedText = "All enemies defeated! The exit is open";

    [Tooltip("Optional object to show/hide with the counter (e.g. a panel). Defaults to the label itself")]
    [SerializeField] private GameObject container;


    [Tooltip("Shown on boss maps while the boss is alive. Leave empty to hide the label on boss maps")]
    [SerializeField] private string bossText = "Defeat the boss!";

    [Tooltip("Shown on boss maps once the boss is dead")]
    [SerializeField] private string bossDefeatedText = "Boss defeated! The exit is open";

    [Tooltip("Shown on maps without a condition (platformer), where the exit is open from the start. Leave empty to hide the label")]
    [SerializeField] private string reachExitText = "Reach the exit!";

    private LevelObjective objective;

    private void Start()
    {
        MapGenerationBase generator = MapGenerationBase.instance;
        if (generator == null)
        {
            Debug.LogWarning("EnemyCounterUI: no map generator in the scene.");
            enabled = false;
            return;
        }

        objective = generator.Objective;
        objective.Changed += Refresh;
        Refresh();
    }

    private void OnDestroy()
    {
        if (objective != null) objective.Changed -= Refresh;
    }

    private void Refresh()
    {
        if (label == null) return;

        string text = null;

        switch (objective.Type)
        {
            case ObjectiveType.KillAllEnemies:
                text = (objective.IsCompleted && !string.IsNullOrEmpty(completedText))
                    ? completedText
                    : string.Format(format, objective.EnemiesLeft, objective.TotalEnemies);
                break;

            case ObjectiveType.DefeatBoss:
                text = objective.IsCompleted ? bossDefeatedText : bossText;
                break;

            case ObjectiveType.None:
                text = reachExitText;
                break;
        }

        bool show = !string.IsNullOrEmpty(text);
        GameObject target = container != null ? container : label.gameObject;
        target.SetActive(show);
        if (show) label.text = text;
    }
}