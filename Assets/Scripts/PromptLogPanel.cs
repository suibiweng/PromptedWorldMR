using System.Text;
using TMPro;
using UnityEngine;

public class PromptLogPanel : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _logText;
    [SerializeField] private int _maxLines = 5;

    private ProgramableObject _po;

    public void Bind(ProgramableObject po)
    {
        if (_po != null) _po.OnPromptLogUpdated.RemoveListener(OnLogUpdated);
        _po = po;
        if (_po != null) _po.OnPromptLogUpdated.AddListener(OnLogUpdated);
        Refresh();
    }

    private void OnDestroy()
    {
        if (_po != null) _po.OnPromptLogUpdated.RemoveListener(OnLogUpdated);
    }

    private void OnLogUpdated(PromptLogEntry _)
    {
        Refresh();
    }

    private void Refresh()
    {
        if (_logText == null)
            return;

        if (_po == null || _po.PromptLog == null || _po.PromptLog.Count == 0)
        {
            _logText.text = "No prompts yet.";
            return;
        }

        int start = Mathf.Max(0, _po.PromptLog.Count - _maxLines);
        var sb = new StringBuilder();
        for (int i = start; i < _po.PromptLog.Count; i++)
        {
            var e = _po.PromptLog[i];
            sb.Append('[').Append(e.timestampIso).Append("] ");
            sb.Append(e.mode).Append(' ').Append(e.succeeded ? "✓" : "×").Append(' ');
            sb.AppendLine(e.objectName);
            sb.Append("  » ").AppendLine(e.prompt);
            if (!string.IsNullOrEmpty(e.luaHash))
                sb.Append("  luaHash:").Append(e.luaHash);
            if (e.durationSec > 0f)
                sb.Append("  t=").Append(e.durationSec.ToString("0.00")).Append("s");
            if (e.inputTokens > 0 || e.outputTokens > 0)
                sb.Append("  tok(in/out)=").Append(e.inputTokens).Append('/').Append(e.outputTokens);
            if (!string.IsNullOrEmpty(e.notes))
                sb.Append("\n  notes: ").Append(e.notes);
            sb.AppendLine("\n");
        }
        _logText.text = sb.ToString();
    }
}
