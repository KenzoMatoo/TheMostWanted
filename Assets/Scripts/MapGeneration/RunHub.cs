using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Put this in the hub scene. It enables only the buttons that are offered in the current step of the run,
/// shows the lore text, and loads the scene of the level the player picks.
/// </summary>
public class RunHub : MonoBehaviour
{
    [Serializable]
    public class LevelButton
    {
        public LevelType type;
        public Button button;
    }

    [Header("References")]
    [SerializeField] private RunProgression progression;
    [SerializeField] private LevelButton[] buttons;
    [SerializeField] private TMP_Text loreLabel;
    [Tooltip("Optional, e.g. 'Stage 2 / 6'")]
    [SerializeField] private TMP_Text stepLabel;
    [SerializeField] private string stepFormat = "Stage {0} / {1}";
    [Tooltip("Optional. Only shown when the run is complete, starts a new run")]
    [SerializeField] private Button newRunButton;
    [Tooltip("Optional. If empty, SceneManager is used")]
    [SerializeField] private SceneLoader sceneLoader;

    [Header("Behaviour")]
    [Tooltip("Hide the buttons that are not offered. If off, they stay visible but not clickable")]
    [SerializeField] private bool hideUnavailable = false;
    [Tooltip("Write the display names of the Run Progression asset on the buttons")]
    [SerializeField] private bool setButtonNames = false;

    [Header("Disabled look")]
    [Tooltip("Color of a button that is not available. The button background is tinted with it, and so are its texts and icons")]
    [SerializeField] private Color disabledColor = new Color(0.5f, 0.5f, 0.5f, 1f);

    // Original colors of the graphics inside the buttons (texts, icons), so they can be restored
    private readonly Dictionary<Graphic, Color> originalColors = new Dictionary<Graphic, Color>();

    private void Start()
    {
        if (progression == null)
        {
            Debug.LogError("RunHub: assign the Run Progression asset.");
            enabled = false;
            return;
        }

        // First time in the hub: begin the run. Coming back from a level keeps the progress.
        if (RunProgression.Active != progression) progression.StartRun();

        foreach (LevelButton lb in buttons)
        {
            if (lb.button == null) continue;
            LevelType type = lb.type;
            lb.button.onClick.AddListener(() => Choose(type));

            // The background is tinted by the button itself (disabled color)
            lb.button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = lb.button.colors;
            colors.disabledColor = disabledColor;
            colors.colorMultiplier = 1f;
            lb.button.colors = colors;

            // Texts and icons inside the button are not tinted by the button, so they are handled in Refresh
            foreach (Graphic g in lb.button.GetComponentsInChildren<Graphic>(true))
                if (g != lb.button.targetGraphic) originalColors[g] = g.color;

            if (setButtonNames)
            {
                TMP_Text text = lb.button.GetComponentInChildren<TMP_Text>();
                if (text != null) text.text = progression.GetDisplayName(type);
            }
        }

        if (newRunButton != null) newRunButton.onClick.AddListener(NewRun);
        Refresh();
    }

    private void Refresh()
    {
        bool done = progression.IsComplete;

        foreach (LevelButton lb in buttons)
        {
            if (lb.button == null) continue;
            bool available = !done && progression.IsAvailable(lb.type);
            lb.button.interactable = available;
            ApplyChildTint(lb.button, available);
            lb.button.gameObject.SetActive(!hideUnavailable || available);
        }

        if (loreLabel != null) loreLabel.text = progression.CurrentLore;
        if (stepLabel != null)
        {
            stepLabel.gameObject.SetActive(!done);
            stepLabel.text = string.Format(stepFormat, progression.StepNumber, progression.StepCount);
        }
        if (newRunButton != null) newRunButton.gameObject.SetActive(done);
    }

    private void ApplyChildTint(Button button, bool available)
    {
        foreach (Graphic g in button.GetComponentsInChildren<Graphic>(true))
        {
            if (!originalColors.TryGetValue(g, out Color original)) continue;
            g.color = available ? original : original * disabledColor;
        }
    }

    private void Choose(LevelType type)
    {
        if (!progression.IsAvailable(type)) return;

        string scene = progression.GetSceneName(type);
        if (string.IsNullOrEmpty(scene)) return;

        if (sceneLoader != null) sceneLoader.LoadScene(scene);
        else SceneManager.LoadScene(scene);
    }

    private void NewRun()
    {
        progression.StartRun();
        Refresh();
    }
}