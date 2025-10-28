using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stacks ParagraphPanels vertically inside a right-side root.
/// Prevents overlap and auto-reflows when panels change/close.
/// </summary>
public class PanelLayoutFlow : MonoBehaviour
{
    [Header("Root (assign a RectTransform anchored top-left)")]
    public RectTransform rightRoot;

    [Header("Sizing")]
    public float panelWidth = 520f;
    public float topPadding = 8f;
    public float vGutter = 8f;

    [Header("Defaults")]
    public float defaultPanelHeight = 360f;

    // ownerButton -> panel
    private readonly Dictionary<UnityEngine.UI.Button, ParagraphPanel> _map = new();

    public ParagraphPanel AddOrGet(UnityEngine.UI.Button owner, Vector2? size = null)
    {
        if (owner != null && _map.TryGetValue(owner, out var existing) && existing != null)
            return existing;

        if (rightRoot == null)
        {
            Debug.LogError("[PanelLayoutFlow] rightRoot not assigned.");
            return null;
        }

        var go = new GameObject("ParagraphPanel", typeof(RectTransform), typeof(ParagraphPanel));
        var rt = go.GetComponent<RectTransform>();
        go.transform.SetParent(rightRoot, false);

        // top-left anchors
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);

        // size
        Vector2 sz = size ?? new Vector2(panelWidth, defaultPanelHeight);
        rt.sizeDelta = new Vector2(panelWidth, Mathf.Max(120f, sz.y));

        var panel = go.GetComponent<ParagraphPanel>();
        panel.ownerButton = owner;

        // Init visuals with size
        panel.InitVisual(rt.sizeDelta);

        // hook reflow events
        panel.onAnyChanged += Relayout;                 // Action<ParagraphPanel>
        panel.onClose += p => { Relayout(p); };         // Action<ParagraphPanel>
        panel.onSubmit += (p, prompt, body) => { Relayout(p); }; // Action<ParagraphPanel,string,string>

        if (owner != null) _map[owner] = panel;

        Relayout();
        return panel;
    }

    /// <summary>Reflow all panels (no-arg convenience).</summary>
    public void Relayout()
    {
        if (rightRoot == null) return;

        float y = topPadding;
        var panels = rightRoot.GetComponentsInChildren<ParagraphPanel>(true);
        foreach (var p in panels)
        {
            var rt = p.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(panelWidth, rt.sizeDelta.y); // clamp width
            rt.anchoredPosition = new Vector2(0f, -y);
            y += rt.sizeDelta.y + vGutter;
        }
    }

    /// <summary>Reflow with matching delegate signature.</summary>
    public void Relayout(ParagraphPanel _) => Relayout();
}
