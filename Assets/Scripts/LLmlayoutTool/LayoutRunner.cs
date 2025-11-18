using UnityEngine;

public class LayoutRunner : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The LLMLayoutApplier that will spawn / edit objects.")]
    public LLMLayoutApplier layoutApplier;

    [Header("Overrides")]
    [Tooltip("Optional: Source prefab/object to use for spawned layouts. If set, overrides the applier's sourceObject.")]
    public GameObject sourceObjectOverride;

    [Tooltip("Optional: Parent transform for local layouts. If set, overrides the applier's layoutParent.")]
    public Transform parentOverride;

    [Tooltip("Optional: Existing objects that the LLM may relayout.\nIf set, overrides the applier's existingObjects list.")]
    public Transform[] existingObjectsOverride;

    [Tooltip("Optional: Anchor object (e.g., 'Object B') that can be mentioned in prompts.")]
    public Transform anchorOverride;

    [Header("Initial Layout Prompt")]
    [TextArea(3, 6)]
    public string initialLayoutPrompt =
        "Give me 12 of them and arrange them in a triangle on the XZ plane, " +
        "with about 0.5 meters between neighbors.";

    [Header("Edit Layout Prompt")]
    [TextArea(3, 6)]
    public string editLayoutPrompt =
        "Spread the objects out more evenly in a circle around the origin on the XZ plane.";

    [Header("Runtime Status")]
    [Tooltip("Name of the most recently applied layout (from the LLM JSON: layout_name).")]
    public string currentLayoutName = "(none)";

    [Tooltip("High-level description of the last operation / state.")]
    [TextArea(2, 5)]
    public string status = "Idle";

    [Tooltip("Last operation type: Generate or Edit.")]
    public string lastOperation = "(none)";

    [Tooltip("If true, logs detailed info to the Unity Console.")]
    public bool verboseLogging = true;

    // internal cache so we only re-parse when JSON changes
    private string _lastSeenLayoutJson;

    // tiny helper type just to grab layout_name from the LLM JSON
    [System.Serializable]
    private class LayoutNameOnly
    {
        public string layout_name;
    }

    private void Reset()
    {
        if (layoutApplier == null)
            layoutApplier = GetComponent<LLMLayoutApplier>();
    }

    /// <summary>
    /// Push override settings (source, parent, existing list, anchor) into the applier
    /// before we call the LLM.
    /// </summary>
    private void ApplyOverridesToApplier()
    {
        if (layoutApplier == null) return;

        if (sourceObjectOverride != null)
            layoutApplier.sourceObject = sourceObjectOverride;

        if (parentOverride != null)
            layoutApplier.layoutParent = parentOverride;

        if (existingObjectsOverride != null && existingObjectsOverride.Length > 0)
            layoutApplier.existingObjects = existingObjectsOverride;

        if (anchorOverride != null)
            layoutApplier.anchorObject = anchorOverride;
    }

    private void Update()
    {
        if (layoutApplier == null) return;

        string json = layoutApplier.LastLayoutJson;
        if (!string.IsNullOrEmpty(json) && json != _lastSeenLayoutJson)
        {
            _lastSeenLayoutJson = json;

            if (verboseLogging)
            {
                Debug.Log("[LayoutRunner] Detected new layout JSON from LLMLayoutApplier.");
            }

            TryUpdateLayoutNameFromJson(json);
        }
    }

    private void TryUpdateLayoutNameFromJson(string json)
    {
        try
        {
            var nameWrapper = JsonUtility.FromJson<LayoutNameOnly>(json);
            if (nameWrapper != null && !string.IsNullOrEmpty(nameWrapper.layout_name))
            {
                currentLayoutName = nameWrapper.layout_name;
                status = $"Applied layout: {currentLayoutName}";
                if (verboseLogging)
                {
                    Debug.Log($"[LayoutRunner] Applied layout '{currentLayoutName}'.");
                }
            }
            else
            {
                currentLayoutName = "(unnamed layout)";
                status = "Applied layout (no layout_name in JSON).";
                if (verboseLogging)
                {
                    Debug.LogWarning("[LayoutRunner] Applied layout, but JSON had no layout_name field.");
                }
            }
        }
        catch
        {
            currentLayoutName = "(parse error)";
            status = "Failed to parse layout_name from JSON.";
            if (verboseLogging)
            {
                Debug.LogError("[LayoutRunner] Failed to parse layout_name from LastLayoutJson.");
            }
        }
    }

    // --------- Public methods you can hook to UI buttons ---------

    /// <summary>
    /// Ask the LLM for a layout based on initialLayoutPrompt.
    /// - If existingObjects is set and LLM outputs matching ids/count,
    ///   the applier will RELAYOUT them.
    /// - Otherwise it will SPAWN copies of sourceObject.
    /// </summary>
    public void GenerateLayout()
    {
        if (layoutApplier == null)
        {
            Debug.LogError("[LayoutRunner] layoutApplier is not assigned.");
            status = "ERROR: layoutApplier not assigned.";
            return;
        }

        if (string.IsNullOrWhiteSpace(initialLayoutPrompt))
        {
            Debug.LogError("[LayoutRunner] initialLayoutPrompt is empty.");
            status = "ERROR: initial layout prompt is empty.";
            return;
        }

        ApplyOverridesToApplier();

        lastOperation = "Generate";
        status = "Requesting layout from LLM (Generate)...";

        if (verboseLogging)
        {
            Debug.Log("[LayoutRunner] GenerateLayout called with prompt:\n" + initialLayoutPrompt);
        }

        StartCoroutine(layoutApplier.RequestAndApplyLayout(initialLayoutPrompt));
    }

    /// <summary>
    /// Edit the currently applied layout using editLayoutPrompt.
    /// Falls back to GenerateLayout-like behavior if there's no previous layout.
    /// </summary>
    public void EditCurrentLayout()
    {
        if (layoutApplier == null)
        {
            Debug.LogError("[LayoutRunner] layoutApplier is not assigned.");
            status = "ERROR: layoutApplier not assigned.";
            return;
        }

        if (string.IsNullOrWhiteSpace(editLayoutPrompt))
        {
            Debug.LogError("[LayoutRunner] editLayoutPrompt is empty.");
            status = "ERROR: edit layout prompt is empty.";
            return;
        }

        ApplyOverridesToApplier();

        lastOperation = "Edit";
        status = "Requesting layout edit from LLM...";

        if (verboseLogging)
        {
            Debug.Log("[LayoutRunner] EditCurrentLayout called with prompt:\n" + editLayoutPrompt);
        }

        StartCoroutine(layoutApplier.RequestAndApplyEditedLayout(editLayoutPrompt));
    }

    // --------- Handy context menu shortcuts in Inspector ---------

    [ContextMenu("Generate Layout (Inspector)")]
    private void GenerateLayoutContextMenu()
    {
        GenerateLayout();
    }

    [ContextMenu("Edit Current Layout (Inspector)")]
    private void EditCurrentLayoutContextMenu()
    {
        EditCurrentLayout();
    }
}
