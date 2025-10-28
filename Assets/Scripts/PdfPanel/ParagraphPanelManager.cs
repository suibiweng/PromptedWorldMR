using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ParagraphPanelManager : MonoBehaviour
{
    [Header("Panel Root (right side column)")]
    public RectTransform rightRoot;
    public Vector2 panelSize = new Vector2(520, 320);
    public float verticalSpacing = 8f;

    // owner button -> panel
    readonly Dictionary<Button, ParagraphPanel> _map = new();

    void Awake()
    {
        if (!rightRoot)
        {
            Debug.LogError("[ParagraphPanelManager] Assign rightRoot.");
        }
    }

    public ParagraphPanel GetOrCreate(Button owner)
    {
        if (!rightRoot || owner == null) return null;

        if (_map.TryGetValue(owner, out var existing) && existing)
            return existing;

        // Create new GameObject with ParagraphPanel
        var go = new GameObject($"Panel_{owner.name}", typeof(RectTransform), typeof(Image), typeof(ParagraphPanel));
        go.transform.SetParent(rightRoot, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.sizeDelta = panelSize;

        var panel = go.GetComponent<ParagraphPanel>();
        panel.ownerButton = owner;
        panel.BuildIfNeeded(panelSize);     // builds visual once

        // Hook events for layout and close
        panel.onAnyChanged += _ => Relayout();
        panel.onClose += (pp) =>
        {
            if (_map.ContainsKey(owner)) _map.Remove(owner);
            Destroy(pp.gameObject);
            Relayout();
        };
        panel.onSubmit += HandleSubmit;

        _map[owner] = panel;

        Relayout();
        return panel;
    }

    void HandleSubmit(ParagraphPanel pp, string prompt, string body)
    {
        // Placeholder for your LLM or processing pipeline.
        // Combine prompt + body and write to the processed field.
        var result = $"[processed]\nprompt: {prompt}\n---\n{body}";
        pp.SetProcessed(result);
    }

    public void Relayout()
    {
        if (!rightRoot) return;

        float y = 0f;
        // Keep a stable order: by y of owner button (top→down) then creation
        var list = new List<ParagraphPanel>(_map.Values);
        list.RemoveAll(p => p == null);
        list.Sort((a, b) =>
        {
            var ay = GetOwnerScreenY(a.ownerButton);
            var by = GetOwnerScreenY(b.ownerButton);
            return ay.CompareTo(by);
        });

        foreach (var p in list)
        {
            var rt = p.GetComponent<RectTransform>();
            rt.anchoredPosition = new Vector2(0, -y);
            y += p.reservedHeight + verticalSpacing;
        }

        // Resize the root to fit
        rightRoot.sizeDelta = new Vector2(rightRoot.sizeDelta.x, Mathf.Max(y, rightRoot.sizeDelta.y));
    }

    float GetOwnerScreenY(Button b)
    {
        if (!b) return 0f;
        var rt = b.GetComponent<RectTransform>();
        return rt ? -rt.anchoredPosition.y : 0f;
    }
}
