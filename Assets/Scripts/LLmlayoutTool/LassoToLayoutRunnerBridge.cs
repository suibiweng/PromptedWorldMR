using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bridge between MR 3D lasso selection / click selection and the LLM layout system.
/// 
/// Now supports:
/// - LassoSelectorMR3D selection
/// - PromptedWorldManager multi-selection (ProgramableObject click)
/// Combined selection (union) is sent to LayoutRunner.
/// </summary>
public class LassoToLayoutRunnerBridge : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The MR 3D lasso selector that holds the current lasso selection (optional).")]
    public LassoSelectorMR3D lassoSelector;

    [Tooltip("PromptedWorldManager that tracks click-based selections from ProgramableObject (optional).")]
    public PromptedWorldManager promptedWorldManager;

    [Tooltip("The LayoutRunner that talks to the LLM and applies layouts.")]
    public LayoutRunner layoutRunner;

    [Header("Defaults for Auto Prompts")]
    [Tooltip("Default prompt when simply wiring the selection but not changing the text.")]
    [TextArea(2, 4)]
    public string defaultRelayoutPrompt =
        "Relayout these selected objects into a clean arrangement on the XZ plane.";

    [Tooltip("Template used for the circle relayout prompt.")]
    [TextArea(2, 4)]
    public string circlePromptTemplate =
        "Take these {0} selected objects and arrange them in a circle around the origin on the XZ plane.";

    [Tooltip("Template used for the line relayout prompt.")]
    [TextArea(2, 4)]
    public string linePromptTemplate =
        "Take these {0} selected objects and arrange them in a straight line along the X axis with 0.5 meters spacing.";

    [Tooltip("Template used for the grid relayout prompt.")]
    [TextArea(2, 4)]
    public string gridPromptTemplate =
        "Take these {0} selected objects and arrange them in a grid on the XZ plane.";

    [Header("Debug")]
    [Tooltip("If true, logs bridge steps to the console.")]
    public bool verboseLogging = true;

    /// <summary>
    /// Reads the current selection from:
    /// - lassoSelector (if assigned)
    /// - promptedWorldManager.SelectedObjects (if assigned)
    /// and pushes the UNION into layoutRunner.existingObjectsOverride.
    /// Does NOT change the prompt; you can type whatever you want in LayoutRunner.
    /// </summary>
    public void UseCurrentLassoSelection()
    {
        if (!EnsureRunner()) return;

        var selection = GetCombinedSelection();
        if (!PrepareSelection(selection)) return;

        if (string.IsNullOrWhiteSpace(layoutRunner.initialLayoutPrompt))
        {
            layoutRunner.initialLayoutPrompt = defaultRelayoutPrompt;
        }

        layoutRunner.status = $"Using selection ({selection.Count} objs) as existingObjects.";
        if (verboseLogging)
        {
            Debug.Log($"[LassoToLayoutRunnerBridge] Wired {selection.Count} selected objects (lasso + click) into LayoutRunner.existingObjectsOverride.");
        }
    }

    /// <summary>
    /// Convenience: use the current (lasso + click) selection and immediately ask the LLM
    /// to arrange them in a circle.
    /// </summary>
    public void RelayoutSelectionInCircle()
    {
        if (!EnsureRunner()) return;

        var selection = GetCombinedSelection();
        if (!PrepareSelection(selection)) return;

        int count = selection.Count;
        layoutRunner.initialLayoutPrompt = string.Format(circlePromptTemplate, count);

        if (verboseLogging)
        {
            Debug.Log($"[LassoToLayoutRunnerBridge] RelayoutSelectionInCircle with {count} objects.");
        }

        layoutRunner.GenerateLayout();
    }

    /// <summary>
    /// Convenience: use the current (lasso + click) selection and ask the LLM
    /// to arrange them in a line.
    /// </summary>
    public void RelayoutSelectionInLine()
    {
        if (!EnsureRunner()) return;

        var selection = GetCombinedSelection();
        if (!PrepareSelection(selection)) return;

        int count = selection.Count;
        layoutRunner.initialLayoutPrompt = string.Format(linePromptTemplate, count);

        if (verboseLogging)
        {
            Debug.Log($"[LassoToLayoutRunnerBridge] RelayoutSelectionInLine with {count} objects.");
        }

        layoutRunner.GenerateLayout();
    }

    /// <summary>
    /// Convenience: use the current (lasso + click) selection and ask the LLM
    /// to arrange them in a grid.
    /// </summary>
    public void RelayoutSelectionInGrid()
    {
        if (!EnsureRunner()) return;

        var selection = GetCombinedSelection();
        if (!PrepareSelection(selection)) return;

        int count = selection.Count;
        layoutRunner.initialLayoutPrompt = string.Format(gridPromptTemplate, count);

        if (verboseLogging)
        {
            Debug.Log($"[LassoToLayoutRunnerBridge] RelayoutSelectionInGrid with {count} objects.");
        }

        layoutRunner.GenerateLayout();
    }

    // ---------- Helpers ----------

    /// <summary>
    /// Combine lasso selection and PromptedWorldManager selection into one list (no duplicates).
    /// </summary>
    private List<GameObject> GetCombinedSelection()
    {
        var combined = new List<GameObject>();
        var seen = new HashSet<GameObject>();

        // Lasso selection
        if (lassoSelector != null)
        {
            var lassoSel = lassoSelector.GetCurrentSelection();
            if (lassoSel != null)
            {
                for (int i = 0; i < lassoSel.Count; i++)
                {
                    var go = lassoSel[i];
                    if (go != null && seen.Add(go))
                        combined.Add(go);
                }
            }
        }

        // Click selection from PromptedWorldManager
        if (promptedWorldManager != null)
        {
            var clickSel = promptedWorldManager.GetSelectedObjects();
            if (clickSel != null)
            {
                for (int i = 0; i < clickSel.Count; i++)
                {
                    var go = clickSel[i];
                    if (go != null && seen.Add(go))
                        combined.Add(go);
                }
            }
        }

        if (verboseLogging)
        {
            Debug.Log($"[LassoToLayoutRunnerBridge] GetCombinedSelection: {combined.Count} total objects (lasso + click).");
        }

        return combined;
    }

    private bool EnsureRunner()
    {
        if (layoutRunner == null)
        {
            Debug.LogError("[LassoToLayoutRunnerBridge] layoutRunner is not assigned.");
            return false;
        }
        return true;
    }

    private bool PrepareSelection(IReadOnlyList<GameObject> selection)
    {
        if (selection == null || selection.Count == 0)
        {
            if (verboseLogging)
                Debug.LogWarning("[LassoToLayoutRunnerBridge] Selection is empty (lasso + click).");
            layoutRunner.status = "No objects selected (lasso or click).";
            return false;
        }

        // Push transforms into the runner so LLM can choose relayout vs spawn mode.
        Transform[] arr = new Transform[selection.Count];
        for (int i = 0; i < selection.Count; i++)
        {
            arr[i] = selection[i] != null ? selection[i].transform : null;
        }

        layoutRunner.existingObjectsOverride = arr;
        layoutRunner.status = $"Prepared {selection.Count} selected objects for LLM relayout.";

        if (verboseLogging)
        {
            Debug.Log($"[LassoToLayoutRunnerBridge] Prepared {selection.Count} objects for layout relayout.");
        }

        return true;
    }

    // Optional: context menu hooks for quick testing in Editor

    [ContextMenu("Use Current Selection (Lasso + Click, no auto prompt)")]
    private void UseSelectionContextMenu()
    {
        UseCurrentLassoSelection();
    }

    [ContextMenu("Relayout Selection: Circle")]
    private void RelayoutCircleContextMenu()
    {
        RelayoutSelectionInCircle();
    }

    [ContextMenu("Relayout Selection: Line")]
    private void RelayoutLineContextMenu()
    {
        RelayoutSelectionInLine();
    }

    [ContextMenu("Relayout Selection: Grid")]
    private void RelayoutGridContextMenu()
    {
        RelayoutSelectionInGrid();
    }
}
