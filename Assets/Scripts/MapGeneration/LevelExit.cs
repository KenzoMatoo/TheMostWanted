using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// The end door of the level. Put this on your door prefab (the prefab you assign as 'exit' in the RoomManager's spawn settings).
/// It starts locked and is unlocked by the map's objective. Needs a Collider2D with 'Is Trigger' on the same object,
/// so it can detect the player walking in.
/// If the prefab has no LevelExit, the spawner adds one (without visuals or events).
/// </summary>
public class LevelExit : MonoBehaviour
{
    [Header("Visuals (all optional)")]
    [Tooltip("Shown only while the door is locked (e.g. a closed door or a lock icon)")]
    [SerializeField] private GameObject lockedVisual;
    [Tooltip("Shown only when the door is unlocked")]
    [SerializeField] private GameObject unlockedVisual;
    [Tooltip("Solid colliders that block the way while the door is locked")]
    [SerializeField] private Collider2D[] blockersWhileLocked;
    [Tooltip("If no locked/unlocked visuals are assigned, darken the sprites while locked")]
    [SerializeField] private bool tintWhenLocked = true;
    [SerializeField] private Color lockedTint = new Color(0.4f, 0.4f, 0.4f, 1f);

    [Header("Player")]
    [SerializeField] private string playerTag = "Player";

    [Header("Events")]
    public UnityEvent onUnlocked;
    [Tooltip("Raised when the player enters the unlocked door: load the next level here")]
    public UnityEvent onPlayerEntered;

    public bool IsLocked { get; private set; } = true;


    private void Awake()
    {
        Apply();
        // Entering the open door finishes the level: advance the run and go back to the hub.
        // Uses the scene's SceneLoader if there is one (any name), otherwise loads the hub directly.
        onPlayerEntered.AddListener(() =>
        {
            RunProgression.NotifyLevelCompleted();   // advances the run (does nothing if no run is active)
            SceneLoader sceneLoader = FindAnyObjectByType<SceneLoader>();
            if (sceneLoader != null) sceneLoader.LoadScene("HUB");
            else UnityEngine.SceneManagement.SceneManager.LoadScene("HUB");
        });
    }

    public void SetLocked(bool locked)
    {
        bool wasLocked = IsLocked;
        IsLocked = locked;
        Apply();
        if (wasLocked && !locked) onUnlocked?.Invoke();
    }

    private void Apply()
    {
        if (lockedVisual != null) lockedVisual.SetActive(IsLocked);
        if (unlockedVisual != null) unlockedVisual.SetActive(!IsLocked);

        foreach (Collider2D blocker in blockersWhileLocked)
            if (blocker != null) blocker.enabled = IsLocked;

        if (tintWhenLocked && lockedVisual == null && unlockedVisual == null)
            foreach (SpriteRenderer sr in GetComponentsInChildren<SpriteRenderer>())
                sr.color = IsLocked ? lockedTint : Color.white;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (IsLocked || !other.CompareTag(playerTag)) return;
        onPlayerEntered?.Invoke();
    }
}