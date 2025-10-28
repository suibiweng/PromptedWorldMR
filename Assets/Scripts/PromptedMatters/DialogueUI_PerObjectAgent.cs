using UnityEngine;
using UnityEngine.UI;
using TMPro;

[DisallowMultipleComponent]
public class DialogueUI_PerObjectAgent : MonoBehaviour
{
    [Header("Wiring")]
    public MatterDialogueAgent agent;   // local agent (on the same object or parent)
    public TMP_InputField inputField;
    public Button sendButton;
    public Button agreeButton;          // optional
    public Button cancelButton;         // optional

    [Header("One-line panes")]
    public TMP_Text userPane;           // latest USER line
    public TMP_Text systemPane;         // latest SYSTEM line

    [Header("Behavior")]
    [TextArea] public string seedIdea = "breathe softly when I touch it";
    public bool autoPrimeOnEnable = true;
    public bool autoFocusOnPrime = true;
    public bool selectAllOnPrime = true;
    public bool forceOneVisualLine = true;

    private bool _started;

    private void Awake()
    {
        if (!agent) agent = GetComponentInParent<MatterDialogueAgent>();

        if (sendButton)  sendButton.onClick.AddListener(OnSendClicked);
        if (agreeButton) agreeButton.onClick.AddListener(OnAgreeClicked);
        if (cancelButton) cancelButton.onClick.AddListener(OnCancelClicked);

        if (agent)
        {
            agent.OnAssistantUtter += s => SetSystemLine(s);
            agent.OnStatus         += s => SetSystemLine(s);
            agent.OnError          += s => SetSystemLine("Error: " + s);
            agent.OnFinished       += (ok, txt) => SetSystemLine(ok ? ("Applied: " + txt) : "Ended without changes.");
        }
    }

    private void OnEnable()
    {
        if (autoPrimeOnEnable) Prime(seedIdea, autoFocusOnPrime, selectAllOnPrime);
    }

    public void Prime(string text, bool autoFocus = true, bool selectAll = true)
    {
        _started = false;
        SetOne(userPane, "");
        SetOne(systemPane, "");
        if (!inputField) return;

        inputField.SetTextWithoutNotify(text ?? "");
        if (autoFocus) inputField.Select();
        if (selectAll) inputField.stringPosition = inputField.text.Length;
    }

    private void Begin(string seed)
    {
        SetOne(userPane, ""); SetOne(systemPane, "");
        SetSystemLine("Negotiation started.");
        agent?.Begin(seed);
        SetUserLine(seed);
        _started = true;
    }

    private void OnSendClicked()
    {
        var t = inputField ? inputField.text.Trim() : "";
        if (string.IsNullOrEmpty(t)) return;

        if (!_started)
        {
            Begin(t);
            inputField.SetTextWithoutNotify("");
            return;
        }

        SetUserLine(t);
        agent?.ContinueUser(t);
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
        SetUserLine(t);
        agent?.ContinueUser("AGREE: " + t);
        inputField.SetTextWithoutNotify("");
    }

    private void OnCancelClicked() => SetSystemLine("Cancelled. No changes applied.");

    private void SetUserLine(string s)   => SetOne(userPane, s);
    private void SetSystemLine(string s) => SetOne(systemPane, s);

    private void SetOne(TMP_Text pane, string value)
    {
        if (!pane) return;
        pane.enableWordWrapping = false;
        pane.overflowMode = TextOverflowModes.Ellipsis;

        var text = value ?? "";
        if (forceOneVisualLine)
        {
            text = text.Replace("\r\n", " ").Replace("\n", " ").Replace("\t", " ");
            while (text.Contains("  ")) text = text.Replace("  ", " ");
            text = text.Trim();
        }
        pane.text = text;
    }
}
