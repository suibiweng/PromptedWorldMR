using System.Linq;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[DisallowMultipleComponent]
public class CentralPromptUI : MonoBehaviour
{
    [Header("Manager (hub)")]
    public PromptedMattersManager manager;

    [Header("Target Picker")]
    public TMP_Dropdown targetDropdown;
    public Button refreshTargetsButton; // optional

    [Header("Prompt")]
    public TMP_InputField inputField;
    public Button sendButton;
    public Button agreeButton;
    public Button cancelButton;

    [Header("Output (single line)")]
    public TMP_Text userLine;
    public TMP_Text systemLine;

    [Header("Options")]
    public bool hidePerObjectUIWhileSelected = true;

    private MatterDialogueAgent _current;

    private void Awake()
    {
        // --- AUTO-WIRE COMMON MISSING REFS (by child type / name contains) ---
        if (!manager) manager = FindObjectOfType<PromptedMattersManager>();
        if (!targetDropdown) targetDropdown = GetComponentInChildren<TMP_Dropdown>(true);
        if (!inputField) inputField = GetComponentInChildren<TMP_InputField>(true);

        if (!sendButton)  sendButton  = FindButtonContaining("Send")  ?? GetComponentInChildren<Button>(true);
        if (!agreeButton) agreeButton = FindButtonContaining("Agree");
        if (!cancelButton) cancelButton = FindButtonContaining("Cancel");
        if (!refreshTargetsButton) refreshTargetsButton = FindButtonContaining("Refresh");

        if (!userLine)   userLine   = FindTextContaining("You") ?? GetComponentsInChildren<TMP_Text>(true).FirstOrDefault();
        if (!systemLine) systemLine = FindTextContaining("System") ?? GetComponentsInChildren<TMP_Text>(true).LastOrDefault();

        // --- Hook events ---
        if (sendButton)  sendButton.onClick.AddListener(OnSend);
        if (agreeButton) agreeButton.onClick.AddListener(OnAgree);
        if (cancelButton) cancelButton.onClick.AddListener(OnCancel);
        if (refreshTargetsButton) refreshTargetsButton.onClick.AddListener(RefreshTargets);
        if (targetDropdown) targetDropdown.onValueChanged.AddListener(_ => OnTargetChanged());

        AgentsDirectory.OnChanged += HandleAgentsChanged;

        Debug.Log("[CentralPromptUI] Awake: wired."
            + $" manager={(manager?._GetTypeName() ?? "null")}"
            + $" send={(sendButton?._GetPath() ?? "null")}"
            + $" input={(inputField?._GetPath() ?? "null")}"
            + $" dropdown={(targetDropdown?._GetPath() ?? "null")}");
    }

    private void OnDestroy()
    {
        AgentsDirectory.OnChanged -= HandleAgentsChanged;
    }

    private void OnEnable()
    {
        RefreshTargets();
        StartCoroutine(RefreshNextFrame());
        if (inputField) inputField.Select();
    }

    private void Update()
    {
        // Press Enter/Return to send
        if (inputField && inputField.isFocused && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
        {
            OnSend();
        }
    }

    private IEnumerator RefreshNextFrame()
    {
        yield return null;
        RefreshTargets();
    }

    private void HandleAgentsChanged()
    {
        var all = AgentsDirectory.All.ToList();
        var had = _current;
        RefreshTargets();
        if (had && all.Contains(had)) SetCurrent(had);
    }

    public void RefreshTargets()
    {
        if (!targetDropdown) { Debug.LogWarning("[CentralPromptUI] No targetDropdown assigned."); return; }
        var all = AgentsDirectory.All.ToList();

        targetDropdown.ClearOptions();

        if (all.Count == 0)
        {
            targetDropdown.AddOptions(new System.Collections.Generic.List<string> { "(no objects found)" });
            SetCurrent(null);
            SetSystem("No objects found.");
            Debug.LogWarning("[CentralPromptUI] AgentsDirectory has 0 agents.");
            return;
        }

        var names = all.Select(a => a ? a.DisplayName : "(null)").ToList();
        targetDropdown.AddOptions(names);

        int idx = Mathf.Clamp(targetDropdown.value, 0, all.Count - 1);
        SetCurrent(all[idx]);

        Debug.Log($"[CentralPromptUI] Refreshed targets: {all.Count} found. Current={_current?.DisplayName}");
    }

    private void OnTargetChanged()
    {
        var all = AgentsDirectory.All.ToList();
        if (targetDropdown == null || all.Count == 0) { SetCurrent(null); return; }

        int idx = Mathf.Clamp(targetDropdown.value, 0, all.Count - 1);
        SetCurrent(all[idx]);
    }

    private void SetCurrent(MatterDialogueAgent agent)
    {
        if (_current == agent)
        {
            SetSystem(_current ? ("Ready: " + _current.DisplayName) : "No target.");
            return;
        }

        if (_current != null)
        {
            _current.OnAssistantUtter -= OnAssistant;
            _current.OnStatus         -= OnStatusMsg;
            _current.OnError          -= OnErrorMsg;
            _current.OnFinished       -= OnFinished;
            if (hidePerObjectUIWhileSelected) _current.SetObjectUIActive(true);
        }

        _current = agent;

        if (_current != null)
        {
            _current.OnAssistantUtter += OnAssistant;
            _current.OnStatus         += OnStatusMsg;
            _current.OnError          += OnErrorMsg;
            _current.OnFinished       += OnFinished;

            if (hidePerObjectUIWhileSelected) _current.SetObjectUIActive(false);
            _current.ResetSessionFlag(); // allow Agree-first to auto-begin
            SetSystem("Ready: " + _current.DisplayName);
        }
        else
        {
            SetSystem("No target.");
        }
    }

    // ---- Buttons ----
    private void OnSend()
    {
        Debug.Log("[CentralPromptUI] Send clicked.");
        if (_current == null) { SetSystem("Pick a target first."); Debug.LogWarning("[CentralPromptUI] No current agent."); return; }
        if (!inputField) { Debug.LogError("[CentralPromptUI] inputField is null."); return; }
        var t = inputField.text != null ? inputField.text.Trim() : "";
        if (string.IsNullOrEmpty(t)) { SetSystem("Type something first."); return; }

        SetUser(t);
        _current.Begin(t); // first turn always Begin
        inputField.SetTextWithoutNotify("");
    }

    private void OnAgree()
    {
        Debug.Log("[CentralPromptUI] Agree clicked.");
        if (_current == null) { SetSystem("Pick a target first."); return; }
        var t = inputField ? inputField.text.Trim() : "";
        if (string.IsNullOrEmpty(t)) t = "Yes, proceed.";
        SetUser(t);
        _current.ContinueUser("AGREE: " + t);
        if (inputField) inputField.SetTextWithoutNotify("");
    }

    private void OnCancel()
    {
        Debug.Log("[CentralPromptUI] Cancel clicked.");
        SetSystem("Cancelled. No changes applied.");
    }

    // ---- Agent callbacks ----
    private void OnAssistant(string s)  { SetSystem(s); Debug.Log("[CentralPromptUI] Assistant: " + s); }
    private void OnStatusMsg(string s)  { SetSystem(s); Debug.Log("[CentralPromptUI] Status: " + s); }
    private void OnErrorMsg(string s)   { SetSystem("Error: " + s); Debug.LogError("[CentralPromptUI] " + s); }
    private void OnFinished(bool ok, string txt)
    {
        SetSystem(ok ? "Applied: " + txt : "Ended without changes.");
        Debug.Log($"[CentralPromptUI] Finished ok={ok} txt={txt}");
    }

    // ---- One-line UI helpers ----
    private void SetUser(string s)   { if (userLine)   userLine.text   = OneLine(s); }
    private void SetSystem(string s) { if (systemLine) systemLine.text = OneLine(s); }

    private string OneLine(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Replace("\r\n"," ").Replace("\n"," ").Replace("\t"," ");
        while (s.Contains("  ")) s = s.Replace("  "," ");
        return s.Trim();
    }

    // ---- Find helpers ----
    private Button FindButtonContaining(string token)
    {
        var btns = GetComponentsInChildren<Button>(true);
        token = (token ?? "").ToLowerInvariant();
        foreach (var b in btns)
        {
            var t = b.GetComponentInChildren<TMP_Text>(true);
            if (t && (t.text ?? "").ToLowerInvariant().Contains(token)) return b;
            if (b.name.ToLowerInvariant().Contains(token)) return b;
        }
        return null;
    }
    private TMP_Text FindTextContaining(string token)
    {
        var ts = GetComponentsInChildren<TMP_Text>(true);
        token = (token ?? "").ToLowerInvariant();
        return ts.FirstOrDefault(t => (t.text ?? "").ToLowerInvariant().Contains(token) || t.name.ToLowerInvariant().Contains(token));
    }
}

// --- tiny debug extensions ---
static class _DbgExt
{
    public static string _GetTypeName(this object o) => o == null ? "null" : o.GetType().Name;
    public static string _GetPath(this Component c)
    {
        if (!c) return "null";
        var p = c.name;
        var t = c.transform;
        while (t.parent) { t = t.parent; p = t.name + "/" + p; }
        return p + " (" + c.GetType().Name + ")";
    }
}
