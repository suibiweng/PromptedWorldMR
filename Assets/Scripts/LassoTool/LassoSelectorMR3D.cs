using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// MR 3D lasso selection:
/// - Hold the configured button on the configured controller to draw a lasso in 3D (using drawTip transform).
/// - On release, selects all objects with a given tag whose
///   screen position lies inside the polygon and are visible
///   from the camera.
/// - If more than one object is selected, they are grouped
///   under a prefab parent with an auto-sizing BoxCollider.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class LassoSelectorMR3D : MonoBehaviour
{
    [Header("Setup")]
    public Camera cam;

    [Tooltip("Transform of your drawing tip (e.g., right controller or fingertip).")]
    public Transform drawTip;

    [Tooltip("Tag for objects that can be selected.")]
    public string selectableTag = "Selectable";

    [Header("Input Settings (OVR)")]
    [Tooltip("OVR button used to draw the lasso.")]
    public OVRInput.Button drawButton = OVRInput.Button.PrimaryIndexTrigger;

    [Tooltip("Which controller to listen to (e.g., RTouch, LTouch).")]
    public OVRInput.Controller controller = OVRInput.Controller.RTouch;

    [Tooltip("Also allow mouse left button for testing in the Editor.")]
    public bool useMouseInEditor = true;

    [Header("Lasso Settings")]
    [Tooltip("Minimum screen-space distance between recorded lasso vertices (pixels).")]
    public float minScreenDistance = 5f;

    [Tooltip("Max distance for the visibility ray from camera to object.")]
    public float maxRayDistance = 50f;

    [Header("Layout Bridge (optional)")]
    [Tooltip("If assigned, this bridge will receive the current selection after each lasso.")]
    public LassoToLayoutRunnerBridge layoutBridge;

    [Tooltip("If true, automatically pushes selection to LayoutRunner via the bridge after each lasso.")]
    public bool autoPushSelectionToLayout = true;

    [Header("Grouping")]
    [Tooltip("Prefab with a BoxCollider + GroupSelection. Selected objects will become its children.")]
    public GameObject groupPrefab;

    [Tooltip("Minimum number of selected objects required to form a group.")]
    public int minGroupSize = 2;

    [Tooltip("Optional parent to reattach children to when the group is destroyed. If null, uses world root.")]
    public Transform ungroupParent;

    [Header("Optional Click Group Grouper")]
    [Tooltip("If assigned, any existing click-based group will be broken when a new lasso starts.")]
    public ClickSelectionGrouper clickSelectionGrouper;

    [Header("Debug / Runtime Info")]
    [Tooltip("Objects currently selected by the last lasso operation.")]
    [SerializeField] private List<GameObject> currentSelection = new List<GameObject>();

    private LineRenderer lineRenderer;

    // World-space line points (for rendering)
    private readonly List<Vector3> _worldPoints = new List<Vector3>();

    // Screen-space polygon points (for selection)
    private readonly List<Vector2> _screenPoints = new List<Vector2>();

    private bool _isDrawing = false;

    // Active group instance created from last selection
    private GameObject activeGroup;

    // Remember original parents so we can restore when destroying the group
    private readonly Dictionary<GameObject, Transform> originalParents = new Dictionary<GameObject, Transform>();

    private void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();

        if (cam == null)
            cam = Camera.main;

        if (lineRenderer != null)
        {
            lineRenderer.positionCount = 0;
            lineRenderer.useWorldSpace = true;
        }

        if (drawTip == null)
        {
            Debug.LogWarning("LassoSelectorMR3D: drawTip is not assigned.");
        }

        // Auto-find ClickSelectionGrouper if not wired
        if (clickSelectionGrouper == null)
        {
            clickSelectionGrouper = FindObjectOfType<ClickSelectionGrouper>();
        }
    }

    private void Update()
    {
        bool triggerDown, triggerHeld, triggerUp;
        GetInput(out triggerDown, out triggerHeld, out triggerUp);

        if (triggerDown)
        {
            BeginLasso();
        }

        if (_isDrawing && triggerHeld)
        {
            UpdateLasso();
        }

        if (_isDrawing && triggerUp)
        {
            EndLasso();
        }
    }

    /// <summary>
    /// Centralized input handling so you can switch button/controller easily.
    /// </summary>
    private void GetInput(out bool down, out bool held, out bool up)
    {
        down = held = up = false;

#if UNITY_EDITOR
        if (useMouseInEditor)
        {
            down = Input.GetMouseButtonDown(0);
            held = Input.GetMouseButton(0);
            up   = Input.GetMouseButtonUp(0);
            return;
        }
#endif

        // OVR input
        down = OVRInput.GetDown(drawButton, controller);
        held = OVRInput.Get(drawButton, controller);
        up   = OVRInput.GetUp(drawButton, controller);
    }

    private void BeginLasso()
    {
        // Break any existing lasso group
        BreakCurrentGroup();

        // Also break any click-selection-based group
        if (clickSelectionGrouper != null)
        {
            clickSelectionGrouper.BreakCurrentGroup();
        }

        if (drawTip == null || cam == null) return;

        _isDrawing = true;

        _worldPoints.Clear();
        _screenPoints.Clear();

        if (lineRenderer != null)
        {
            lineRenderer.positionCount = 0;
        }

        AddPoint(drawTip.position);
    }

    private void UpdateLasso()
    {
        if (drawTip == null || cam == null) return;

        Vector3 worldPos = drawTip.position;
        Vector3 screenPos3 = cam.WorldToScreenPoint(worldPos);

        if (_screenPoints.Count == 0)
        {
            AddPoint(worldPos);
            return;
        }

        if (Vector2.Distance(
                _screenPoints[_screenPoints.Count - 1],
                new Vector2(screenPos3.x, screenPos3.y)
            ) >= minScreenDistance)
        {
            AddPoint(worldPos);
        }
    }

    private void EndLasso()
    {
        _isDrawing = false;

        if (_screenPoints.Count < 3)
        {
            // Too small for a valid polygon
            ClearLasso();
            return;
        }

        // Close polygon: repeat the first screen point
        _screenPoints.Add(_screenPoints[0]);

        // Close visual line: repeat first world point
        if (_worldPoints.Count > 0)
        {
            _worldPoints.Add(_worldPoints[0]);
        }

        // Update line renderer
        if (lineRenderer != null)
        {
            lineRenderer.positionCount = _worldPoints.Count;
            for (int i = 0; i < _worldPoints.Count; i++)
            {
                lineRenderer.SetPosition(i, _worldPoints[i]);
            }
        }

        // Run selection
        SelectObjectsInsideLasso();

        // Handle grouping (destroy old group, create new one if needed)
        HandleGroupingAfterSelection();

        // After we know the selection, optionally push it into the layout system
        if (autoPushSelectionToLayout && layoutBridge != null)
        {
            layoutBridge.UseCurrentLassoSelection();
        }

        // Optionally clear visual after selection
        ClearLasso();
    }

    private void AddPoint(Vector3 worldPos)
    {
        if (cam == null) return;

        Vector3 screenPos3 = cam.WorldToScreenPoint(worldPos);
        Vector2 screenPos2 = new Vector2(screenPos3.x, screenPos3.y);

        _worldPoints.Add(worldPos);
        _screenPoints.Add(screenPos2);

        if (lineRenderer != null)
        {
            lineRenderer.positionCount = _worldPoints.Count;
            lineRenderer.SetPosition(_worldPoints.Count - 1, worldPos);
        }
    }

    private void ClearLasso()
    {
        _worldPoints.Clear();
        _screenPoints.Clear();

        if (lineRenderer != null)
        {
            lineRenderer.positionCount = 0;
        }
    }

    private void SelectObjectsInsideLasso()
    {
        if (cam == null) return;

        GameObject[] candidates = GameObject.FindGameObjectsWithTag(selectableTag);
        currentSelection.Clear();

        int polyCount = _screenPoints.Count;
        if (polyCount < 3)
        {
            Debug.LogWarning("LassoSelectorMR3D: Polygon has fewer than 3 points.");
            return;
        }

        foreach (GameObject go in candidates)
        {
            if (go == null) continue;

            Vector3 objectWorldPos = go.transform.position;
            Vector3 screenPos3 = cam.WorldToScreenPoint(objectWorldPos);

            if (screenPos3.z < 0f)
            {
                // Behind camera
                Highlight(go, false);
                continue;
            }

            Vector2 screenPos = new Vector2(screenPos3.x, screenPos3.y);

            if (IsPointInPolygon(screenPos, _screenPoints))
            {
                // Optional: visibility check via raycast from camera to object
                if (IsVisibleFromCamera(go, objectWorldPos))
                {
                    currentSelection.Add(go);
                    Highlight(go, true);
                }
                else
                {
                    Highlight(go, false);
                }
            }
            else
            {
                Highlight(go, false);
            }
        }

        Debug.Log($"MR lasso selected {currentSelection.Count} objects.");
    }

    private bool IsVisibleFromCamera(GameObject go, Vector3 targetWorldPos)
    {
        Vector3 origin = cam.transform.position;
        Vector3 dir = (targetWorldPos - origin).normalized;

        if (Physics.Raycast(origin, dir, out RaycastHit hit, maxRayDistance))
        {
            return hit.collider != null && hit.collider.gameObject == go;
        }

        return false;
    }

    /// <summary>
    /// Standard ray-casting point-in-polygon test.
    /// Assumes the last vertex equals the first (closed polygon).
    /// </summary>
    private bool IsPointInPolygon(Vector2 p, List<Vector2> poly)
    {
        bool inside = false;
        int count = poly.Count;
        if (count < 3) return false;

        for (int i = 0, j = count - 1; i < count; j = i++)
        {
            Vector2 pi = poly[i];
            Vector2 pj = poly[j];

            bool intersect =
                ((pi.y > p.y) != (pj.y > p.y)) &&
                (p.x < (pj.x - pi.x) * (p.y - pi.y) / ((pj.y - pi.y) + Mathf.Epsilon) + pi.x);

            if (intersect)
                inside = !inside;
        }

        return inside;
    }

    public IReadOnlyList<GameObject> GetCurrentSelection()
    {
        return currentSelection;
    }

    private void Highlight(GameObject go, bool selected)
    {
        // Prefer the ProgramableObject outline, if present
        var po = go.GetComponent<ProgramableObject>();
        if (po != null)
        {
            if (selected)
                po.SetLatchedHighlight(true);
            else
                po.ClearLatchedHighlight();
            return;
        }

        // Fallback: direct Outline on the object (or its children)
        var outline = go.GetComponentInChildren<Outline>(includeInactive: true);
        if (outline != null)
        {
            outline.enabled = selected;
            return;
        }

        // Last resort: color-based highlight for anything that doesn't use Outline
        var rend = go.GetComponent<Renderer>();
        if (rend == null) return;
        rend.material.color = selected ? Color.yellow : Color.white;
    }

    // ------------- GROUPING LOGIC -------------

    /// <summary>
    /// Destroy existing group (if any), then create a new one if we have enough selected objects.
    /// </summary>
    private void HandleGroupingAfterSelection()
    {
        // Always remove the old group first (but keep its children alive)
        ClearExistingGroup();

        if (groupPrefab == null)
        {
            return;
        }

        if (currentSelection.Count < minGroupSize)
        {
            // Nothing to group this time
            return;
        }

        CreateGroupFromSelection();
    }

    /// <summary>
    /// Destroy the current group GameObject but keep its children and restore them to their original parents if known.
    /// </summary>
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

        // Remove the lasso group object itself
        Destroy(activeGroup);
        activeGroup = null;

        Debug.Log("[LassoSelectorMR3D] Lasso group broken (group GameObject destroyed).");
    }

    /// <summary>
    /// Create a new group prefab instance at the center of the selected objects and reparent them into it.
    /// </summary>
    private void CreateGroupFromSelection()
    {
        // Compute average world position of the selected objects
        Vector3 sum = Vector3.zero;
        int count = 0;
        foreach (GameObject go in currentSelection)
        {
            if (go == null) continue;
            sum += go.transform.position;
            count++;
        }

        if (count == 0) return;

        Vector3 center = sum / count;

        // Instantiate group at the center
        activeGroup = Instantiate(groupPrefab, center, Quaternion.identity);

        // Reparent selected objects into the new group
        foreach (GameObject go in currentSelection)
        {
            if (go == null) continue;

            // Store original parent if we haven't seen this object before
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
    }

    // Public API so other systems can break the current lasso group
    public void BreakCurrentGroup()
    {
        ClearExistingGroup();
    }
}
