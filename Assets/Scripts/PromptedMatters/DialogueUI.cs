using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class DialogueUI : MonoBehaviour
{
    [Header("Wiring")]
    public PromptedMatterDialogue dialogue;
    public TMP_InputField inputField;
    public Button sendButton;
    public Button agreeButton;      // optional
    public Button cancelButton;     // optional

    [Header("Two-Pane (Single-Line)")]
    [Tooltip("Left (or top) pane: shows only USER's latest line.")]
    public TMP_Text userPane;
    [Tooltip("Right (or bottom) pane: shows only SYSTEM/OBJECT's latest line.")]
    public TMP_Text systemPane;

    [Header("Behavior")]
    [TextArea] public string seedIdea = "breathe softly when I touch it";
    public bool autoPrimeOnEnable = true;
    public bool autoFocusOnPrime = true;
    public bool selectAllOnPrime = true;

    // Optional: keep formatting tags, but squash to one visual line
    [Tooltip("If true, collapse newlines/tabs/spaces so it renders as one visual line.")]
    public bool forceOneVisualLine = true;
    [Tooltip("If true, prepend labels like 'You:' or 'Object:'")]
    public bool showLabels = false;

    private bool _started = false;

    private void Awake()
    {
        if (sendButton)  sendButton.onClick.AddListener(OnSendClicked);
        if (agreeButton) agreeButton.onClick.AddListener(OnAgreeClicked);
        if (cancelButton) cancelButton.onClick.AddListener(OnCancelClicked);

        if (dialogue)
        {
            dialogue.OnAssistantUtter += OnAssistantUtter;
            dialogue.OnStatus         += OnSystemStatus;
            dialogue.OnError          += OnSystemError;
        }
    }

    private void OnEnable()
    {
        if (autoPrimeOnEnable) Prime(seedIdea, autoFocusOnPrime, selectAllOnPrime);
    }

    private void OnDestroy()
    {
        if (dialogue)
        {
            dialogue.OnAssistantUtter -= OnAssistantUtter;
            dialogue.OnStatus         -= OnSystemStatus;
            dialogue.OnError          -= OnSystemError;
        }
    }

    /// Pre-fill the input; negotiation starts on first Send.
    public void Prime(string text, bool autoFocus = true, bool selectAll = true)
    {
        _started = false;
        SetTextOneLine(userPane, "");
        SetTextOneLine(systemPane, "");
        if (!inputField) return;

        inputField.SetTextWithoutNotify(text ?? "");
        if (autoFocus) inputField.Select();
        if (selectAll) inputField.stringPosition = inputField.text.Length;
    }

    private void Begin(string seed)
    {
        SetTextOneLine(userPane, "");
        SetTextOneLine(systemPane, "");
        SetSystemLine("Negotiation started.");
        if (dialogue) dialogue.BeginNegotiation(seed);
        SetUserLine(seed);
        _started = true;
    }

    private void OnSendClicked()
    {
        var t = inputField ? inputField.text.Trim() : "";
        if (string.IsNullOrEmpty(t)) return;

        if (!_started)
        {
            Begin(t);                         // first send starts negotiation
            inputField.SetTextWithoutNotify("");
            return;
        }

        SetUserLine(t);                        // subsequent sends
        if (dialogue) dialogue.ContinueWithUserReply(t);
        inputField.SetTextWithoutNotify("");
    }

    private void OnAgreeClicked()
    {
        var t = inputField ? inputField.text.Trim() : "";
        if (!_started)
        {
            if (string.IsNullOrEmpty(t)) t = "Yes, please proceed.";
            Begin(t);
            inputField.SetTextWithoutNotify("");
            return;
        }

        if (string.IsNullOrEmpty(t)) t = "Yes, please proceed.";
        SetUserLine(showLabels ? "[AGREE] " + t : t);
        if (dialogue) dialogue.ContinueWithUserReply("AGREE: " + t);
        inputField.SetTextWithoutNotify("");
    }

    private void OnCancelClicked()
    {
        SetSystemLine("Cancelled by user. No changes applied.");
        // optional: close/hide panel here
    }

    // ===== Event handlers from dialogue =====
    private void OnAssistantUtter(string s)
    {
        if (showLabels) SetSystemLine($"Object: {s}");
        else            SetSystemLine(s);
    }

    private void OnSystemStatus(string s)
    {
        if (showLabels) SetSystemLine($"[Status] {s}");
        else            SetSystemLine(s);
    }

    private void OnSystemError(string s)
    {
        var msg = $"Error: {s}";
        if (showLabels) SetSystemLine(msg);
        else            SetSystemLine(msg);
    }

    // ===== One-line setters =====
    private void SetUserLine(string s)
    {
        SetTextOneLine(userPane, showLabels ? $"You: {s}" : s);
    }

    private void SetSystemLine(string s)
    {
        SetTextOneLine(systemPane, s);
    }

    private void SetTextOneLine(TMP_Text pane, string value)
    {
        if (!pane) return;
        pane.enableWordWrapping = false;                    // force single line
        pane.overflowMode = TextOverflowModes.Ellipsis;     // ellipsize if long

        var text = value ?? "";
        if (forceOneVisualLine)
        {
            // collapse CR/LF/TAB to spaces and trim
            text = text.Replace("\r\n", " ").Replace("\n", " ").Replace("\t", " ");
            while (text.Contains("  ")) text = text.Replace("  ", " ");
            text = text.Trim();
        }

        pane.text = text;
        // If the TMP in Inspector still shows more than one line, make sure its RectTransform is tall enough
        // and check that "Rich Text" is enabled if you rely on formatting tags.
    }
}
