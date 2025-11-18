using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Stacks ParagraphPanels vertically inside a right-side root.
/// Prevents overlap and auto-reflows when panels change/close.
/// Works with the updated ParagraphPanel (uses BuildIfNeeded and optional prompt prefab).
/// </summary>
[DisallowMultipleComponent]
public class PanelLayoutFlow : MonoBehaviour
{
    [Header("Root (assign a RectTransform anchored top-left)")]
    public RectTransform rightRoot;

    [Header("Sizing")]
    public float panelWidth = 520f;
    public float topPadding = 8f;
    public float vGutter = 8f;
    public float defaultPanelHeight = 320f;

    [Header("Defaults passed into each ParagraphPanel")]
    [Tooltip("If provided, this InputField or TMP_InputField prefab will be used by spawned panels.")]
    public Object defaultPromptPrefab; // GameObject or Component with InputField/TMP_InputField

    // Track which owner button spawned which panel
    private readonly Dictionary<Button, ParagraphPanel> _ownerToPanel = new();

    /// <summary>
    /// Create (or fetch existing) panel for an owner button, set content, and place it in the flow.
    /// </summary>
    public ParagraphPanel AddOrGet(Button owner, string title, string body, Vector2? size = null)
    {
        if (rightRoot == null)
        {
            rightRoot = CreateFallbackRoot();
        }

        // Reuse if already exists
        if (owner != null && _ownerToPanel.TryGetValue(owner, out var existing) && existing != null)
        {
            existing.SetContent(title, body);
            Relayout();
            return existing;
        }

        // Spawn new
        var go = new GameObject("ParagraphPanel", typeof(RectTransform), typeof(Image), typeof(ParagraphPanel));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(rightRoot, false);
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot     = new Vector2(0f, 1f);

        Vector2 sz = size ?? new Vector2(panelWidth, defaultPanelHeight);
        rt.sizeDelta = new Vector2(panelWidth, Mathf.Max(120f, sz.y));

        var panel = go.GetComponent<ParagraphPanel>();
        panel.ownerButton = owner;

        // Pass the optional prompt prefab through BEFORE building
        var promptField = typeof(ParagraphPanel).GetField("promptPrefab",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (promptField != null && defaultPromptPrefab != null)
        {
            promptField.SetValue(panel, defaultPromptPrefab);
        }

        // Build visuals (ensures EventSystem + correct input module)
        panel.BuildIfNeeded(rt.sizeDelta);
        panel.SetContent(title, body);

        // Hook reflow events
     // Hook reflow events
panel.onAnyChanged += _ => Relayout();                     // was: panel.onAnyChanged += Relayout;
panel.onClose      += p => { RemovePanel(p); Relayout(); };
panel.onSubmit     += (p, prompt, bodyTxt) => { Relayout(); };
// Action<ParagraphPanel,string,string>

        // Track mapping
        if (owner != null) _ownerToPanel[owner] = panel;

        Relayout();
        return panel;
    }

    /// <summary>
    /// Remove and destroy the panel associated with an owner.
    /// </summary>
    public void Close(Button owner)
    {
        if (owner != null && _ownerToPanel.TryGetValue(owner, out var panel) && panel != null)
        {
            _ownerToPanel.Remove(owner);
            if (panel) Destroy(panel.gameObject);
            Relayout();
        }
    }

    /// <summary>
    /// Remove from dictionary without destroying (used by onClose to allow panel to handle its own lifecycle).
    /// </summary>
    private void RemovePanel(ParagraphPanel panel)
    {
        Button keyToRemove = null;
        foreach (var kv in _ownerToPanel)
        {
            if (kv.Value == panel) { keyToRemove = kv.Key; break; }
        }
        if (keyToRemove != null) _ownerToPanel.Remove(keyToRemove);
    }

    /// <summary>
    /// Lay out all ParagraphPanel children of rightRoot vertically.
    /// </summary>
    public void Relayout()
    {
        if (rightRoot == null) return;

        float y = topPadding;
        var panels = rightRoot.GetComponentsInChildren<ParagraphPanel>(true);
        foreach (var p in panels)
        {
            if (p == null) continue;
            var rt = p.GetComponent<RectTransform>();
            if (rt == null) continue;

            // Clamp width
            rt.sizeDelta = new Vector2(panelWidth, rt.sizeDelta.y);
            rt.anchoredPosition = new Vector2(0f, -y);

            // Advance
            y += rt.sizeDelta.y + vGutter;
        }
    }

    /// <summary>
    /// Find the panel for an owner (returns null if none).
    /// </summary>
    public ParagraphPanel GetPanel(Button owner)
    {
        if (owner != null && _ownerToPanel.TryGetValue(owner, out var p)) return p;
        return null;
    }

    /// <summary>
    /// Ensure there is a Canvas + container if rightRoot wasn't assigned.
    /// </summary>
    private RectTransform CreateFallbackRoot()
    {
        // Create a simple overlay canvas
        var canvasGO = new GameObject("PanelFlowCanvas",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        // Create the right-side root under the canvas
        var right = new GameObject("RightRoot", typeof(RectTransform));
        var rightRT = right.GetComponent<RectTransform>();
        rightRT.SetParent(canvasGO.transform, false);
        rightRT.anchorMin = new Vector2(1f, 1f);
        rightRT.anchorMax = new Vector2(1f, 1f);
        rightRT.pivot     = new Vector2(1f, 1f);
        rightRT.sizeDelta = new Vector2(panelWidth, 0f);
        rightRT.anchoredPosition = new Vector2(-16f, -16f);

        return rightRT;
    }
}
