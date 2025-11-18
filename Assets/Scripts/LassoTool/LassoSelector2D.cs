using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Simple 2D lasso selection:
/// - Hold left mouse to draw a lasso on screen
/// - On release, all objects with SelectableTag whose screen position
///   lies inside the lasso polygon will be "selected"
/// - Current selection is exposed in the Inspector
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class LassoSelector2D : MonoBehaviour
{
    [Header("Setup")]
    public Camera cam;
    public LineRenderer lineRenderer;

    [Tooltip("Tag for objects that can be selected.")]
    public string selectableTag = "Selectable";

    [Header("Lasso Settings")]
    [Tooltip("Minimum pixel distance between lasso vertices.")]
    public float minVertexDistance = 5f;

    [Header("Debug / Runtime Info")]
    [Tooltip("Objects currently selected by the last lasso operation.")]
    [SerializeField] private List<GameObject> currentSelection = new List<GameObject>();

    // Internal state
    private readonly List<Vector2> _points = new List<Vector2>();
    private bool _isDrawing = false;

    private void Awake()
    {
        if (cam == null)
            cam = Camera.main;

        if (lineRenderer == null)
            lineRenderer = GetComponent<LineRenderer>();

        lineRenderer.positionCount = 0;
    }

    private void Update()
    {
        // Start drawing
        if (Input.GetMouseButtonDown(0))
        {
            BeginLasso();
        }

        // Continue drawing
        if (_isDrawing)
        {
            UpdateLasso();
        }

        // Finish and select
        if (Input.GetMouseButtonUp(0) && _isDrawing)
        {
            EndLasso();
        }
    }

    private void BeginLasso()
    {
        _isDrawing = true;
        _points.Clear();
        lineRenderer.positionCount = 0;

        AddPoint(Input.mousePosition);
    }

    private void UpdateLasso()
    {
        Vector2 mousePos = Input.mousePosition;

        if (_points.Count == 0)
        {
            AddPoint(mousePos);
            return;
        }

        if (Vector2.Distance(_points[_points.Count - 1], mousePos) >= minVertexDistance)
        {
            AddPoint(mousePos);
        }
    }

    private void EndLasso()
    {
        _isDrawing = false;

        if (_points.Count < 3)
        {
            // Too small to be a valid polygon
            ClearLasso();
            return;
        }

        // Close the polygon visually
        _points.Add(_points[0]);
        lineRenderer.positionCount = _points.Count;
        for (int i = 0; i < _points.Count; i++)
        {
            lineRenderer.SetPosition(i, ScreenToWorldOnNearPlane(_points[i]));
        }

        // Run selection
        SelectObjectsInsideLasso();

        // Clear visual after selection (optional)
        ClearLasso();
    }

    private void AddPoint(Vector2 screenPos)
    {
        _points.Add(screenPos);

        lineRenderer.positionCount = _points.Count;
        lineRenderer.SetPosition(_points.Count - 1, ScreenToWorldOnNearPlane(screenPos));
    }

    /// <summary>
    /// Convert screen position to a world point just in front of the camera
    /// so the lasso overlays the screen.
    /// </summary>
    private Vector3 ScreenToWorldOnNearPlane(Vector2 screenPos)
    {
        if (cam == null) return Vector3.zero;

        float z = cam.nearClipPlane + 0.01f;
        Vector3 sp = new Vector3(screenPos.x, screenPos.y, z);
        return cam.ScreenToWorldPoint(sp);
    }

    private void ClearLasso()
    {
        _points.Clear();
        lineRenderer.positionCount = 0;
    }

    private void SelectObjectsInsideLasso()
    {
        GameObject[] candidates = GameObject.FindGameObjectsWithTag(selectableTag);

        // Clear old selection list
        currentSelection.Clear();

        // Note: last _points entry == first, so we can safely use polygon for point-in-poly
        for (int i = 0; i < candidates.Length; i++)
        {
            GameObject go = candidates[i];
            if (go == null) continue;

            Vector3 screenPos3 = cam.WorldToScreenPoint(go.transform.position);
            if (screenPos3.z < 0f)
            {
                // Behind the camera, ignore
                Highlight(go, false);
                continue;
            }

            Vector2 screenPos = new Vector2(screenPos3.x, screenPos3.y);
            if (IsPointInPolygon(screenPos, _points))
            {
                currentSelection.Add(go);
                Highlight(go, true);
            }
            else
            {
                Highlight(go, false);
            }
        }

        Debug.Log($"Lasso selected {currentSelection.Count} objects.");
    }

    /// <summary>
    /// Simple highlighting: change renderer color.
    /// You can swap this out later for your own selection logic.
    /// </summary>
    private void Highlight(GameObject go, bool selected)
    {
        var rend = go.GetComponent<Renderer>();
        if (rend == null) return;

        rend.material.color = selected ? Color.yellow : Color.white;
    }

    /// <summary>
    /// Ray-casting "point in polygon" test.
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

    /// <summary>
    /// Optional public getter if you want to access this from other scripts.
    /// </summary>
    public IReadOnlyList<GameObject> GetCurrentSelection()
    {
        return currentSelection;
    }
}
