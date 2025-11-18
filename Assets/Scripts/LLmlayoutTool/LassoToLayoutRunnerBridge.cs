using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bridge between MR 3D lasso selection and the LLM layout system.
/// 
/// Usage:
/// - Put this on any GameObject in your scene (e.g., "LayoutController").
/// - Assign:
///     - lassoSelector: your LassoSelectorMR3D component
///     - layoutRunner : your LayoutRunner component
/// - Optionally call the public methods from UI buttons or context menu:
///     - UseCurrentLassoSelection()
///     - RelayoutSelectionInCircle()
///     - RelayoutSelectionInLine()
///     - RelayoutSelectionInGrid()
/// </summary>
public class LassoToLayoutRunnerBridge : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The MR 3D lasso selector that holds the current selection.")]
    public LassoSelectorMR3D lassoSelector;

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
    /// Reads the current selection from the lasso, and pushes it into
    /// layoutRunner.existingObjectsOverride so the LLM can relayout them.
    /// Does NOT change the prompt; you can type whatever you want in LayoutRunner.
    /// </summary>
    public void UseCurrentLassoSelection()
    {
        if (!EnsureRefs()) return;

        var selection = lassoSelector.GetCurrentSelection();
        if (selection == null || selection.Count == 0)
        {
            if (verboseLogging)
                Debug.LogWarning("[LassoToLayoutRunnerBridge] Lasso selection is empty.");
            layoutRunner.status = "No objects selected via lasso.";
            return;
        }

        // Convert to Transform[]
        Transform[] arr = new Transform[selection.Count];
        for (int i = 0; i < selection.Count; i++)
        {
            arr[i] = selection[i] != null ? selection[i].transform : null;
        }

        layoutRunner.existingObjectsOverride = arr;

        if (string.IsNullOrWhiteSpace(layoutRunner.initialLayoutPrompt))
        {
            layoutRunner.initialLayoutPrompt = defaultRelayoutPrompt;
        }

        layoutRunner.status = $"Using lasso selection ({selection.Count} objs) as existingObjects.";
        if (verboseLogging)
        {
            Debug.Log($"[LassoToLayoutRunnerBridge] Wired {selection.Count} selected objects into LayoutRunner.existingObjectsOverride.");
        }
    }

    /// <summary>
    /// Convenience: use the current lasso selection and immediately ask the LLM
    /// to arrange them in a circle.
    /// </summary>
    public void RelayoutSelectionInCircle()
    {
        if (!EnsureRefs()) return;

        var selection = lassoSelector.GetCurrentSelection();
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
    /// Convenience: use the current lasso selection and ask the LLM
    /// to arrange them in a line.
    /// </summary>
    public void RelayoutSelectionInLine()
    {
        if (!EnsureRefs()) return;

        var selection = lassoSelector.GetCurrentSelection();
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
    /// Convenience: use the current lasso selection and ask the LLM
    /// to arrange them in a grid.
    /// </summary>
    public void RelayoutSelectionInGrid()
    {
        if (!EnsureRefs()) return;

        var selection = lassoSelector.GetCurrentSelection();
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

    private bool EnsureRefs()
    {
        if (lassoSelector == null)
        {
            Debug.LogError("[LassoToLayoutRunnerBridge] lassoSelector is not assigned.");
            return false;
        }

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
                Debug.LogWarning("[LassoToLayoutRunnerBridge] Lasso selection is empty.");
            layoutRunner.status = "No objects selected via lasso.";
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

        return true;
    }

    // Optional: context menu hooks for quick testing in Editor

    [ContextMenu("Use Current Lasso Selection (no auto prompt)")]
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
