// Assets/Scripts/PromptLogAndCodePanel.cs
using System.Text;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using PromptedWorld;
public class PromptLogAndCodePanel : MonoBehaviour
{
    [Header("Scene References")]
    [Tooltip("Manager that holds the currently selected object")]
    [SerializeField] private PromptedWorldManager promptedWorldManager;

    [Tooltip("Generator used for the Regenerate action")]
    [SerializeField] private OpenAILuaGenerator generator;

    [Tooltip("(Optional) Your prompt input field; used by 'Load Prompt → Input'")]
    [SerializeField] private TMP_InputField promptInput;

    [Header("Auto-follow & Behavior")]
    [SerializeField] private bool autoFollowSelection = true;
    [SerializeField] private bool autoScrollLogToBottomOnUpdate = true;
    [SerializeField] private bool showDetailsInLog = true; // duration, tokens, notes

    [Header("LOG PANEL (assign your Scroll View + its Text)")]
    [Tooltip("ScrollRect of the Scroll View that contains the log text")]
    [SerializeField] private ScrollRect logScrollRect;

    [Tooltip("TMP_Text inside the Scroll View's Content for the log")]
    [SerializeField] private TextMeshProUGUI logContentText;

    [Header("LOG Formatting")]
    [SerializeField] private string logHeaderPrefix = "";
    [SerializeField] private bool showHeaderObjectName = true;
    [SerializeField] private string successMark = "✓";
    [SerializeField] private string failMark = "×";

    [Header("CODE PANEL (assign a TMP_Text or TMP_InputField->Text)")]
    [Tooltip("Target text to display the currently applied Lua")]
    [SerializeField] private TMP_Text currentLuaText;


    [Header("Navigator Behavior")]
[SerializeField] private bool useLatestWhenNoIndex = true;


    [Header("Navigator (Index + Buttons)")]
    [Tooltip("Type an index you see in the log (#0..#N)")]
    [SerializeField] private TMP_InputField indexField;

    [SerializeField] private Button loadPromptButton;   // copies a past prompt → promptInput
    [SerializeField] private Button reapplyLuaButton;   // reapplies stored Lua of that entry
    [SerializeField] private Button regenerateButton;   // runs generator with that entry's prompt
    [SerializeField] private Button copyLuaButton;      // copies current Lua to clipboard
    [SerializeField] private Button refreshLuaButton;   // refresh current Lua view
    [SerializeField] private Button clearViewButton;    // clear both views

    // --- runtime selection/cache ---
    private ProgramableObject _po;
    private LuaBehaviour _lua;
    private GameObject _lastSelected;

    // -------- Unity lifecycle --------
    void OnEnable()
    {
        WireButtons(true); // <-- now exists
        if (autoFollowSelection && promptedWorldManager != null)
            RebindToManagerSelection();
        else
            RefreshAll();
    }

    void OnDisable()
    {
        WireButtons(false); // <-- now exists
        UnsubscribeFromPO();
    }

    void Update()
    {
        if (!autoFollowSelection || promptedWorldManager == null) return;
        if (_lastSelected != promptedWorldManager.selectedObject)
            RebindToManagerSelection();
    }

    // -------- Public bind (optional if not auto-following) --------
    public void Bind(GameObject go) => Bind(go ? go.GetComponent<ProgramableObject>() : null);

    public void Bind(ProgramableObject po)
    {
        if (po == _po) { RefreshAll(); return; }
        UnsubscribeFromPO();

        _po  = po;
        _lua = (_po != null) ? _po.GetComponent<LuaBehaviour>() : null;

        SubscribeToPO();
        RefreshAll();
    }

    // -------- internal: selection & events --------
    private void RebindToManagerSelection()
    {
        _lastSelected = promptedWorldManager.selectedObject;
        Bind(_lastSelected);
    }

    private void SubscribeToPO()
    {
        if (_po != null)
            _po.OnPromptLogUpdated.AddListener(OnPoLogUpdated);
    }

    private void UnsubscribeFromPO()
    {
        if (_po != null)
            _po.OnPromptLogUpdated.RemoveListener(OnPoLogUpdated);
        _po = null;
        _lua = null;
    }

    private void OnPoLogUpdated(PromptLogEntry _)
    {
        // Refresh both when a prompt starts/completes
        RefreshAll();
    }

    // -------- refreshers --------
    private void RefreshAll()
    {
        RefreshLog();
        RefreshCurrentLua();
        RefreshButtonsInteractable();
    }

    private void RefreshLog()
    {
        if (logContentText == null) return;

        if (_po == null || _po.PromptLog == null || _po.PromptLog.Count == 0)
        {
            logContentText.text = "No prompts yet.";
            AutoScrollLogBottom();
            return;
        }

        var sb = new StringBuilder(4096);
        if (showHeaderObjectName) sb.AppendLine($"{logHeaderPrefix}{_po.name}");

        // No max line cap: print entire history with indices
        for (int i = 0; i < _po.PromptLog.Count; i++)
        {
            var e = _po.PromptLog[i];

            // Index + summary line
            sb.Append('#').Append(i).Append(' ');
            sb.Append('[').Append(e.timestampIso).Append("] ");
            sb.Append(e.mode).Append(' ').Append(e.succeeded ? successMark : failMark).Append(' ');
            sb.AppendLine(e.objectName);

            // Prompt line
            sb.Append("  » ").AppendLine(e.prompt);

            if (showDetailsInLog)
            {
                bool any = false;
                if (!string.IsNullOrEmpty(e.luaHash))
                {
                    sb.Append("  luaHash:").Append(e.luaHash);
                    any = true;
                }
                if (e.durationSec > 0f)
                {
                    if (any) sb.Append(' ');
                    sb.Append("t=").Append(e.durationSec.ToString("0.00")).Append("s");
                    any = true;
                }
                if (e.inputTokens > 0 || e.outputTokens > 0)
                {
                    if (any) sb.Append(' ');
                    sb.Append("tok=").Append(e.inputTokens).Append('/').Append(e.outputTokens);
                    any = true;
                }
                if (!string.IsNullOrEmpty(e.notes))
                {
                    if (any) sb.AppendLine();
                    sb.Append("  notes: ").Append(e.notes);
                }
                sb.AppendLine();
            }

            sb.AppendLine(); // spacer
        }

        logContentText.text = sb.ToString();
        AutoScrollLogBottom();
    }

    private void RefreshCurrentLua()
    {
        if (currentLuaText == null) return;

        if (_lua == null)
        {
            currentLuaText.text = "(no LuaBehaviour on selection)";
            return;
        }

        string current = _lua.CurrentLua;
        currentLuaText.text = string.IsNullOrEmpty(current) ? "(no script applied yet)" : current;
    }

    private void RefreshButtonsInteractable()
    {
        bool hasSel = (_po != null);
        if (loadPromptButton) loadPromptButton.interactable = hasSel && promptInput != null;
        if (reapplyLuaButton) reapplyLuaButton.interactable = hasSel; // we’ll check appliedLua at click time
        if (regenerateButton) regenerateButton.interactable = hasSel && generator != null;
        if (copyLuaButton)    copyLuaButton.interactable = (_lua != null);
        if (refreshLuaButton) refreshLuaButton.interactable = true;
        if (clearViewButton)  clearViewButton.interactable = true;
    }

    private void AutoScrollLogBottom()
    {
        if (!autoScrollLogToBottomOnUpdate || logScrollRect == null) return;
        StartCoroutine(Co_ScrollBottom());
    }

    private IEnumerator Co_ScrollBottom()
    {
        yield return null; // wait one frame for layout
        if (logScrollRect != null)
            logScrollRect.normalizedPosition = new Vector2(0, 0); // bottom
    }

    // -------- actions (navigator & helpers) --------
 private int GetIndexFromField()
{
    // If there’s no index field (or it’s empty) and we’re allowed to default,
    // use the latest entry index.
    if (_po != null && useLatestWhenNoIndex &&
        (indexField == null || string.IsNullOrWhiteSpace(indexField.text)))
    {
        int count = _po.PromptLog != null ? _po.PromptLog.Count : 0;
        return count > 0 ? count - 1 : -1;
    }

    if (indexField == null) return -1;
    return int.TryParse(indexField.text, out int i) ? i : -1;
}


    private PromptLogEntry GetEntryByIndex()
    {
        if (_po == null || _po.PromptLog == null) return null;
        int idx = GetIndexFromField();
        if (idx < 0 || idx >= _po.PromptLog.Count) return null;   // <- no PromptCount/GetPrompt needed
        return _po.PromptLog[idx];
    }

    // Load a past prompt back into the input field
    public void OnLoadPromptToInput()
    {
        var e = GetEntryByIndex();
        if (e == null || promptInput == null) return;
        promptInput.text = e.prompt ?? "";
        promptInput.caretPosition = promptInput.text.Length;
    }

    // Re-apply the exact stored Lua from a successful entry.
    // If your PromptLogEntry has a string field named "appliedLua" (as recommended), this uses it via reflection.
    // Otherwise, it falls back to reapplying the CURRENT script only when the chosen index is the latest entry.
    public void OnReapplyLua()
    {
        var e = GetEntryByIndex();
        if (e == null) return;
        if (_lua == null) return;

        string luaFromLog = TryGetAppliedLuaViaReflection(e);

        if (!string.IsNullOrEmpty(luaFromLog))
        {
            _lua.LoadScript(luaFromLog);
            RefreshCurrentLua();
            return;
        }

        // Fallback: if no stored Lua, allow "reapply" only for the latest entry using current script.
        int idx = GetIndexFromField();
        bool isLatest = (_po != null && idx == _po.PromptLog.Count - 1);
        if (isLatest && !string.IsNullOrEmpty(_lua.CurrentLua))
        {
            _lua.LoadScript(_lua.CurrentLua);
            RefreshCurrentLua();
        }
        else
        {
            Debug.LogWarning("[PromptLogPanel] This project doesn't store full Lua per entry. " +
                             "Add a 'string appliedLua' to PromptLogEntry and save it on success " +
                             "(see earlier instructions), or pick the latest index to reapply current script.");
        }
    }

    // Re-run the generator using the old prompt text
    public void OnRegenerateFromPrompt()
    {
        var e = GetEntryByIndex();
        if (e == null || generator == null) return;

        if (_lastSelected) generator.AssignTarget(_lastSelected);
        generator.SetIntent(e.prompt ?? "");
        generator.GenerateLuaNow();
    }

    // Copy current Lua text to clipboard
    public void OnCopyCurrentLua()
    {
        if (_lua == null) return;
        GUIUtility.systemCopyBuffer = _lua.CurrentLua ?? "";
    }

    // Manual refresh of just the current Lua
    public void OnRefreshLua()
    {
        RefreshCurrentLua();
    }

    // Clear both views (visual only)
    public void OnClearViews()
    {
        if (logContentText) logContentText.text = "";
        if (currentLuaText) currentLuaText.text = "";
        if (logScrollRect) logScrollRect.normalizedPosition = Vector2.up;
    }

    // -------- button wiring (this was missing in your build) --------
    private void WireButtons(bool on)
    {
        if (on)
        {
            if (loadPromptButton)  loadPromptButton.onClick.AddListener(OnLoadPromptToInput);
            if (reapplyLuaButton)  reapplyLuaButton.onClick.AddListener(OnReapplyLua);
            if (regenerateButton)  regenerateButton.onClick.AddListener(OnRegenerateFromPrompt);
            if (copyLuaButton)     copyLuaButton.onClick.AddListener(OnCopyCurrentLua);
            if (refreshLuaButton)  refreshLuaButton.onClick.AddListener(OnRefreshLua);
            if (clearViewButton)   clearViewButton.onClick.AddListener(OnClearViews);
        }
        else
        {
            if (loadPromptButton)  loadPromptButton.onClick.RemoveListener(OnLoadPromptToInput);
            if (reapplyLuaButton)  reapplyLuaButton.onClick.RemoveListener(OnReapplyLua);
            if (regenerateButton)  regenerateButton.onClick.RemoveListener(OnRegenerateFromPrompt);
            if (copyLuaButton)     copyLuaButton.onClick.RemoveListener(OnCopyCurrentLua);
            if (refreshLuaButton)  refreshLuaButton.onClick.RemoveListener(OnRefreshLua);
            if (clearViewButton)   clearViewButton.onClick.RemoveListener(OnClearViews);
        }
    }

    // -------- reflection helper: support both with/without appliedLua field --------
    private static string TryGetAppliedLuaViaReflection(PromptLogEntry e)
    {
        if (e == null) return null;
        // look for a public field named "appliedLua"
        FieldInfo f = typeof(PromptLogEntry).GetField("appliedLua",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (f == null) return null;
        object val = f.GetValue(e);
        return val as string;
    }
}
