using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class LayoutRunnerUI : MonoBehaviour
{
    [Header("Core Reference")]
    [Tooltip("The LayoutRunner that controls the LLM layout workflow.")]
    public LayoutRunner layoutRunner;

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

    // internal
    private string _logBuffer = "";
    private string _lastSeenLayoutName = null;

    [System.Serializable]
    private class LayoutNameOnly
    {
        public string layout_name;
    }

    private void Start()
    {
        if (layoutRunner == null)
        {
            Debug.LogError("[LayoutRunnerUI] layoutRunner is not assigned.");
            return;
        }

        // Hook up button in code (so you don't forget in Inspector)
        if (submitButton != null)
        {
            submitButton.onClick.AddListener(OnApplyPromptClicked);
        }
        else
        {
            Debug.LogWarning("[LayoutRunnerUI] submitButton is not assigned. Assign a Button in the Inspector.");
        }

        // Initialize prompt field from runner (if any text is already there)
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

    // ------------- PUBLIC UI HOOK (Submit Button) -------------

    /// <summary>
    /// Called by the Submit button: applies the current prompt.
    /// If there is no existing layout, calls GenerateLayout().
    /// If there is an existing layout, calls EditCurrentLayout().
    /// </summary>
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

        // Push this prompt into both initial and edit fields on the runner
        layoutRunner.initialLayoutPrompt = prompt;
        layoutRunner.editLayoutPrompt    = prompt;

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

    // ------------- LABEL UPDATERS -------------

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
    if (layoutRunner == null || selectedObjectsLabel == null) return;

    string text = "Selected: (none)";

    // Prefer overrides (what you set from LassoToLayoutRunnerBridge, etc.)
    Transform[] arr = layoutRunner.existingObjectsOverride;

    // If no override, fall back to what the applier is actually using
    if ((arr == null || arr.Length == 0) && layoutRunner.layoutApplier != null)
    {
        arr = layoutRunner.layoutApplier.existingObjects;
    }

    if (arr != null && arr.Length > 0)
    {
        string firstName = arr[0] != null ? arr[0].name : "(null)";
        if (arr.Length == 1)
        {
            text = $"Selected: {firstName}";
        }
        else
        {
            text = $"Selected: {firstName} (+{arr.Length - 1} more)";
        }

        if (verboseLogging)
        {
            Debug.Log($"[LayoutRunnerUI] Selected objects count = {arr.Length}, first = {firstName}");
        }
    }

    selectedObjectsLabel.text = text;
}


    private void RefreshStatusLabel()
    {
        if (layoutRunner == null || statusLabel == null) return;
        statusLabel.text = $"Status: {layoutRunner.status}";
    }

    private void RefreshLogLabel()
    {
        if (logLabel == null) return;
        logLabel.text = _logBuffer;
    }

    // ------------- LOGGING -------------

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
