using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Groups the currently click-selected objects (via PromptedWorldManager)
/// into a single parent group when a controller button is pressed.
/// Uses the same group prefab setup as the Lasso tool (BoxCollider + GroupSelection).
/// Also auto-ungroups when the click selection changes (select new object, deselect, etc).
/// </summary>
public class ClickSelectionGrouper : MonoBehaviour
{
    [Header("Selection Source")]
    [Tooltip("PromptedWorldManager that keeps track of click-based selection.")]
    public PromptedWorldManager promptedWorldManager;

    [Header("Grouping")]
    [Tooltip("Prefab with a BoxCollider + GroupSelection component. Same as used in LassoSelectorMR3D.")]
    public GameObject groupPrefab;

    [Tooltip("Minimum number of selected objects required to form a group.")]
    public int minGroupSize = 2;

    [Tooltip("Optional parent to reattach children to when the group is destroyed. If null, uses world root.")]
    public Transform ungroupParent;

    [Header("Input Settings (OVR)")]
    [Tooltip("OVR button used to trigger grouping.")]
    public OVRInput.Button groupButton = OVRInput.Button.PrimaryHandTrigger;

    [Tooltip("Which controller to listen to (e.g., RTouch, LTouch).")]
    public OVRInput.Controller controller = OVRInput.Controller.RTouch;

    [Tooltip("Also allow keyboard key (G) for testing in the Editor.")]
    public bool useKeyboardInEditor = true;

    // Active group instance created from last grouping
    private GameObject activeGroup;

    // Remember original parents so we can restore when destroying the group
    private readonly Dictionary<GameObject, Transform> originalParents = new Dictionary<GameObject, Transform>();

    // Snapshot of selection at the moment we created the group
    private readonly HashSet<GameObject> selectionAtGroupTime = new HashSet<GameObject>();

    private void Awake()
    {
        if (promptedWorldManager == null)
            promptedWorldManager = FindObjectOfType<PromptedWorldManager>();
    }

    private void Update()
    {
        // 1) Check for "group" button
        if (WasGroupButtonPressed())
        {
            GroupFromCurrentSelection();
        }

        // 2) Auto-ungroup if selection has changed
        AutoUngroupOnSelectionChange();
    }

    private bool WasGroupButtonPressed()
    {
#if UNITY_EDITOR
        if (useKeyboardInEditor && Input.GetKeyDown(KeyCode.G))
            return true;
#endif
        return OVRInput.GetDown(groupButton, controller);
    }

    /// <summary>
    /// Public method you can call from other scripts or UI to group the current selection.
    /// </summary>
    public void GroupFromCurrentSelection()
    {
        if (promptedWorldManager == null)
        {
            Debug.LogWarning("[ClickSelectionGrouper] No PromptedWorldManager assigned or found.");
            return;
        }

        var sel = promptedWorldManager.GetSelectedObjects();
        if (sel == null || sel.Count < minGroupSize)
        {
            Debug.Log("[ClickSelectionGrouper] Not enough objects selected to form a group. Count = "
                      + (sel == null ? 0 : sel.Count));
            return;
        }

        // Always remove the old group first (but keep its children alive)
        ClearExistingGroup();

        CreateGroupFromSelection(sel);

        // Record the selection snapshot at group time
        selectionAtGroupTime.Clear();
        foreach (var go in sel)
        {
            if (go != null)
                selectionAtGroupTime.Add(go);
        }
    }

    /// <summary>
    /// Destroy the current group GameObject but keep its children and restore them to their original parents if known.
    /// </summary>
    public void BreakCurrentGroup()
    {
        ClearExistingGroup();
    }

    private void ClearExistingGroup()
    {
        if (activeGroup == null) return;

        // Copy children first (can't iterate while reparenting)
        List<Transform> children = new List<Transform>();
        for (int i = 0; i < activeGroup.transform.childCount; i++)
        {
            children.Add(activeGroup.transform.GetChild(i));
        }

        foreach (Transform child in children)
        {
            if (child == null) continue;

            Transform originalParent;
            if (originalParents.TryGetValue(child.gameObject, out originalParent) && originalParent != null)
            {
                child.SetParent(originalParent, true);
            }
            else if (ungroupParent != null)
            {
                child.SetParent(ungroupParent, true);
            }
            else
            {
                // World root
                child.SetParent(null, true);
            }
        }

        originalParents.Clear();
        selectionAtGroupTime.Clear();

        // This removes the group object itself
        Destroy(activeGroup);
        activeGroup = null;

        Debug.Log("[ClickSelectionGrouper] Group broken (group GameObject destroyed).");
    }

    private void CreateGroupFromSelection(IReadOnlyList<GameObject> selection)
    {
        if (groupPrefab == null)
        {
            Debug.LogWarning("[ClickSelectionGrouper] groupPrefab is not assigned.");
            return;
        }

        // Compute average world position of the selected objects
        Vector3 sum = Vector3.zero;
        int count = 0;
        foreach (GameObject go in selection)
        {
            if (go == null) continue;
            sum += go.transform.position;
            count++;
        }

        if (count == 0)
        {
            Debug.LogWarning("[ClickSelectionGrouper] Selection was empty after null filter.");
            return;
        }

        Vector3 center = sum / count;

        // Instantiate group at the center
        activeGroup = Instantiate(groupPrefab, center, Quaternion.identity);

        // Reparent selected objects into the new group
        foreach (GameObject go in selection)
        {
            if (go == null) continue;

            if (!originalParents.ContainsKey(go))
            {
                originalParents[go] = go.transform.parent;
            }

            go.transform.SetParent(activeGroup.transform, true);
        }

        // Ask the auto-collider to fit
        var autoCol = activeGroup.GetComponent<GroupSelection>();
        if (autoCol != null)
        {
            autoCol.RecalculateNow();
        }

        Debug.Log("[ClickSelectionGrouper] Group created with " + count + " objects.");
    }

    /// <summary>
    /// If we have an active group and the click selection changes
    /// (select new object, deselect, click again, etc), break the group.
    /// </summary>
    private void AutoUngroupOnSelectionChange()
    {
        if (activeGroup == null) return;
        if (promptedWorldManager == null) return;

        var sel = promptedWorldManager.GetSelectedObjects();

        // Build current selection set
        HashSet<GameObject> currentSet = new HashSet<GameObject>();
        if (sel != null)
        {
            foreach (var go in sel)
            {
                if (go != null)
                    currentSet.Add(go);
            }
        }

        // If selection is completely empty, break group
        if (currentSet.Count == 0 && selectionAtGroupTime.Count > 0)
        {
            BreakCurrentGroup();
            return;
        }

        // If the sets are different (count or membership), break group
        if (currentSet.Count != selectionAtGroupTime.Count)
        {
            BreakCurrentGroup();
            return;
        }

        foreach (var go in currentSet)
        {
            if (!selectionAtGroupTime.Contains(go))
            {
                BreakCurrentGroup();
                return;
            }
        }
    }
}
