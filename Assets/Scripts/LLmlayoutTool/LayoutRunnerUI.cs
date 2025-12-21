using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class LayoutRunnerUI : MonoBehaviour
{
    [Header("Core Reference")]
    [Tooltip("The LayoutRunner that controls the LLM layout workflow.")]
    public LayoutRunner layoutRunner;

    [Header("Selection Sources (optional)")]
    [Tooltip("PromptedWorldManager that tracks click-based selections.")]
    public PromptedWorldManager promptedWorldManager;

    [Tooltip("Lasso selector that tracks MR lasso selections.")]
    public LassoSelectorMR3D lassoSelector;

    [Header("TMPro UI References")]
    [Tooltip("Single prompt input field used for both initial and edit prompts.")]
    public TMP_InputField promptInput;

    [Tooltip("Button that submits the current prompt to the LayoutRunner (Generate or Edit).")]
    public Button submitButton;

    [Tooltip("Label that shows the current layout name.")]
    public TMP_Text layoutNameLabel;

    [Tooltip("Label that shows current selected object(s).")]
    public TMP_Text selectedObjectsLabel;

    [Tooltip("Label that shows status from LayoutRunner.")]
    public TMP_Text statusLabel;

    [Tooltip("Label that shows a rolling log of actions.")]
    public TMP_Text logLabel;

    [Header("Log Settings")]
    [Tooltip("Maximum number of lines to keep in the log label.")]
    public int maxLogLines = 10;

    [Tooltip("If true, also print UI events to the Unity Console.")]
    public bool verboseLogging = true;

    private string _logBuffer = "";
    private string _lastSeenLayoutName = null;

    // -------------------------------

    private void Start()
    {
        if (layoutRunner == null)
        {
            Debug.LogError("[LayoutRunnerUI] layoutRunner is not assigned.");
            return;
        }

        if (promptedWorldManager == null)
            promptedWorldManager = FindObjectOfType<PromptedWorldManager>();

        if (lassoSelector == null)
            lassoSelector = FindObjectOfType<LassoSelectorMR3D>();

        if (verboseLogging)
        {
            Debug.Log("[LayoutRunnerUI] pwm = " + (promptedWorldManager ? promptedWorldManager.gameObject.name : "null") +
                      ", lasso = " + (lassoSelector ? lassoSelector.gameObject.name : "null"));
        }

        if (submitButton != null)
            submitButton.onClick.AddListener(OnApplyPromptClicked);

        if (promptInput != null)
        {
            if (!string.IsNullOrWhiteSpace(layoutRunner.initialLayoutPrompt))
                promptInput.text = layoutRunner.initialLayoutPrompt;
            else if (!string.IsNullOrWhiteSpace(layoutRunner.editLayoutPrompt))
                promptInput.text = layoutRunner.editLayoutPrompt;
        }

        RefreshAllLabels();
    }

    private void Update()
    {
        if (layoutRunner == null) return;

        RefreshLayoutNameLabel();
        RefreshSelectedObjectsLabel();
        RefreshStatusLabel();
    }

    // -------------------------------
    // MAIN BUTTON
    // -------------------------------

    public void OnApplyPromptClicked()
    {
        if (layoutRunner == null)
        {
            Debug.LogError("[LayoutRunnerUI] layoutRunner is not assigned.");
            AppendLog("[UI] ERROR: layoutRunner not assigned.");
            return;
        }

        string prompt = promptInput != null ? promptInput.text : null;
        if (string.IsNullOrWhiteSpace(prompt))
        {
            AppendLog("[UI] ERROR: Prompt is empty.");
            if (verboseLogging)
                Debug.LogError("[LayoutRunnerUI] Prompt is empty.");
            return;
        }

        layoutRunner.initialLayoutPrompt = prompt;
        layoutRunner.editLayoutPrompt    = prompt;

        // Build union of current selection (lasso + click) and push into LayoutRunner
        PushCurrentSelectionIntoLayoutRunner();

        bool hasExistingLayout = HasExistingLayout();

        if (!hasExistingLayout)
        {
            AppendLog("[UI] GenerateLayout with prompt: " + prompt);
            if (verboseLogging)
                Debug.Log("[LayoutRunnerUI] Calling GenerateLayout with prompt:\n" + prompt);

            layoutRunner.GenerateLayout();
        }
        else
        {
            AppendLog("[UI] EditCurrentLayout with prompt: " + prompt);
            if (verboseLogging)
                Debug.Log("[LayoutRunnerUI] Calling EditCurrentLayout with prompt:\n" + prompt);

            layoutRunner.EditCurrentLayout();
        }

        RefreshStatusLabel();
    }

    /// <summary>
    /// Builds the union of:
    /// - Lasso selection (if any)
    /// - PromptedWorldManager click selection (if any)
    /// and writes it into layoutRunner.existingObjectsOverride.
    /// </summary>
    private void PushCurrentSelectionIntoLayoutRunner()
    {
        if (layoutRunner == null) return;

        var union = GetCurrentSelectionUnion(out int lassoCount, out int clickCount);

        if (union.Count == 0)
        {
            layoutRunner.existingObjectsOverride = null;
            if (verboseLogging)
            {
                Debug.Log("[LayoutRunnerUI] PushCurrentSelectionIntoLayoutRunner: selection empty (lasso="
                          + lassoCount + ", click=" + clickCount + ")");
            }
            return;
        }

        var list = new List<Transform>();
        foreach (var go in union)
        {
            if (go != null) list.Add(go.transform);
        }

        layoutRunner.existingObjectsOverride = list.ToArray();

        if (verboseLogging)
        {
            string firstName = list.Count > 0 && list[0] != null ? list[0].name : "(null)";
            Debug.Log("[LayoutRunnerUI] PushCurrentSelectionIntoLayoutRunner: total=" + list.Count +
                      " (lasso=" + lassoCount + ", click=" + clickCount + "), first=" + firstName);
        }
    }

    /// <summary>
    /// Returns a deduplicated list of currently selected GameObjects from:
    /// - LassoSelectorMR3D (if present)
    /// - PromptedWorldManager (if present)
    /// </summary>
    private List<GameObject> GetCurrentSelectionUnion(out int lassoCount, out int clickCount)
    {
        var result = new List<GameObject>();
        var seen = new HashSet<GameObject>();
        lassoCount = 0;
        clickCount = 0;

        // 1) Lasso selection
        if (lassoSelector != null)
        {
            var lassoSel = lassoSelector.GetCurrentSelection();
            if (lassoSel != null)
            {
                for (int i = 0; i < lassoSel.Count; i++)
                {
                    var go = lassoSel[i];
                    if (go != null && seen.Add(go))
                    {
                        result.Add(go);
                    }
                }
                lassoCount = lassoSel.Count;
            }
        }

        // 2) Click selection from PromptedWorldManager
        if (promptedWorldManager != null)
        {
            var clickSel = promptedWorldManager.GetSelectedObjects();
            if (clickSel != null)
            {
                for (int i = 0; i < clickSel.Count; i++)
                {
                    var go = clickSel[i];
                    if (go != null && seen.Add(go))
                    {
                        result.Add(go);
                    }
                }
                clickCount = clickSel.Count;
            }
        }

        return result;
    }

    // -------------------------------
    // LABELS
    // -------------------------------

    private void RefreshAllLabels()
    {
        RefreshLayoutNameLabel();
        RefreshSelectedObjectsLabel();
        RefreshStatusLabel();
        RefreshLogLabel();
    }

    private bool HasExistingLayout()
    {
        if (layoutRunner == null) return false;
        string name = layoutRunner.currentLayoutName;
        if (string.IsNullOrEmpty(name)) return false;
        if (name == "(none)") return false;
        return true;
    }

    private void RefreshLayoutNameLabel()
    {
        if (layoutRunner == null || layoutNameLabel == null) return;

        string name = layoutRunner.currentLayoutName;
        layoutNameLabel.text = $"Layout: {name}";

        if (name != _lastSeenLayoutName)
        {
            _lastSeenLayoutName = name;
            if (!string.IsNullOrEmpty(name) && name != "(none)")
            {
                AppendLog("[UI] Now on layout: " + name);
            }
        }
    }

    private void RefreshSelectedObjectsLabel()
    {
        if (selectedObjectsLabel == null) return;

        int lassoCount, clickCount;
        var union = GetCurrentSelectionUnion(out lassoCount, out clickCount);

        if (union.Count == 0)
        {
            selectedObjectsLabel.text = "Selected: (none)";
            if (verboseLogging)
            {
                Debug.Log("[LayoutRunnerUI] RefreshSelectedObjectsLabel: none selected (lasso="
                          + lassoCount + ", click=" + clickCount + ")");
            }
            return;
        }

        string firstName = union[0] != null ? union[0].name : "(null)";
        string text;
        if (union.Count == 1)
            text = $"Selected: {firstName}";
        else
            text = $"Selected: {firstName} (+{union.Count - 1} more)";

        selectedObjectsLabel.text = text;

        if (verboseLogging)
        {
            Debug.Log("[LayoutRunnerUI] RefreshSelectedObjectsLabel: total=" + union.Count +
                      " (lasso=" + lassoCount + ", click=" + clickCount + "), first=" + firstName);
        }
    }

    private void RefreshStatusLabel()
    {
        if (layoutRunner == null || statusLabel == null) return;
        statusLabel.text = $"Status: {layoutRunner.status}";
    }

    // -------------------------------
    // LOGGING
    // -------------------------------

    private void RefreshLogLabel()
    {
        if (logLabel == null) return;
        logLabel.text = _logBuffer;
    }

    private void AppendLog(string line)
    {
        if (string.IsNullOrEmpty(line)) return;

        if (string.IsNullOrEmpty(_logBuffer))
            _logBuffer = line;
        else
            _logBuffer += "\n" + line;

        var lines = _logBuffer.Split('\n');
        if (lines.Length > maxLogLines)
        {
            int start = lines.Length - maxLogLines;
            _logBuffer = string.Join("\n", lines, start, maxLogLines);
        }

        RefreshLogLabel();
    }
}
