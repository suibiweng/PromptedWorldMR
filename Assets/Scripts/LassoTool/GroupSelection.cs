using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class GroupSelection : MonoBehaviour
{
    [Header("Metadata")]
    [Tooltip("Optional label or description for this grouped selection.")]
    public string text;

    [Header("Debug / Inspector")]
    [Tooltip("Current direct children in this group (auto-populated).")]
    [SerializeField] private List<GameObject> children = new List<GameObject>();
    public IReadOnlyList<GameObject> Children => children;

    [Header("Bounds Settings")]
    [Tooltip("Recalculate every frame (true) or only when you call RecalculateNow() (false).")]
    public bool updateEveryFrame = true;

    [Tooltip("Include inactive children in the bounds for Renderers/Colliders.")]
    public bool includeInactive = false;

    [Tooltip("Use child Renderers (mesh/mesh+material) to calculate bounds. If false, use child Colliders.")]
    public bool useRenderers = true;

    [Header("Transform Settings")]
    [Tooltip("If true, move this group object's transform to the center of the children bounds, and keep the BoxCollider centered at (0,0,0).")]
    public bool recenterOnRecalculate = true;

    [Header("Collider Rules")]
    [Tooltip("If true, all colliders on children will be disabled while they are in this group.")]
    public bool disableChildColliders = true;

    private BoxCollider boxCol;

    // Track which Colliders we turned off so we can restore them
    private readonly Dictionary<GameObject, List<Collider>> disabledChildColliderMap
        = new Dictionary<GameObject, List<Collider>>();

    void Awake()
    {
        boxCol = GetComponent<BoxCollider>();
        RefreshChildrenList();

        if (disableChildColliders)
        {
            ApplyColliderRules();
        }
    }

    void Update()
    {
        if (updateEveryFrame)
        {
            RecalculateNow();
        }
    }

    void OnDestroy()
    {
        // When the group is destroyed, restore all Colliders we disabled
        RestoreChildColliders();
    }

    /// <summary>
    /// Call this if you set updateEveryFrame = false and want to recalc manually.
    /// Also refreshes the children list and collider rules.
    /// </summary>
    public void RecalculateNow()
    {
        RefreshChildrenList();

        if (disableChildColliders)
        {
            ApplyColliderRules();
        }

        if (useRenderers)
            FitToChildRenderers();
        else
            FitToChildColliders();
    }

    /// <summary>
    /// Update the serialized children list so you can see all grouped objects in the inspector.
    /// Only direct children are listed.
    /// </summary>
    public void RefreshChildrenList()
    {
        children.Clear();
        int count = transform.childCount;
        for (int i = 0; i < count; i++)
        {
            Transform child = transform.GetChild(i);
            if (child != null)
            {
                children.Add(child.gameObject);
            }
        }
    }

    /// <summary>
    /// Disable all colliders on children (but NOT this group's own BoxCollider).
    /// </summary>
    private void ApplyColliderRules()
    {
        foreach (var child in children)
        {
            if (child == null) continue;

            // Get or create the list for this child
            if (!disabledChildColliderMap.TryGetValue(child, out var list))
            {
                list = new List<Collider>();
                disabledChildColliderMap[child] = list;
            }

            var colliders = child.GetComponentsInChildren<Collider>(includeInactive);
            foreach (var col in colliders)
            {
                if (col == null) continue;

                // Don't touch this group's own BoxCollider
                if (col.gameObject == this.gameObject)
                    continue;

                if (col.enabled && !list.Contains(col))
                {
                    col.enabled = false;
                    list.Add(col);
                }
            }
        }
    }

    /// <summary>
    /// Restore all child colliders we disabled.
    /// </summary>
    private void RestoreChildColliders()
    {
        foreach (var kvp in disabledChildColliderMap)
        {
            var child = kvp.Key;
            if (child == null) continue;

            var list = kvp.Value;
            foreach (var col in list)
            {
                if (col != null)
                {
                    col.enabled = true;
                }
            }
        }

        disabledChildColliderMap.Clear();
    }

    private void FitToChildRenderers()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(includeInactive);

        if (renderers.Length == 0)
        {
            boxCol.center = Vector3.zero;
            boxCol.size   = Vector3.zero;
            return;
        }

        Bounds combined = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            combined.Encapsulate(renderers[i].bounds);
        }

        ApplyBounds(combined);
    }

    private void FitToChildColliders()
    {
        // NOTE: If you use this mode, it will only use currently ENABLED colliders.
        Collider[] colliders = GetComponentsInChildren<Collider>(includeInactive);

        // Exclude this group's own BoxCollider from bounds
        List<Collider> filtered = new List<Collider>();
        foreach (var c in colliders)
        {
            if (c == null) continue;
            if (c == boxCol) continue;
            filtered.Add(c);
        }

        if (filtered.Count == 0)
        {
            boxCol.center = Vector3.zero;
            boxCol.size   = Vector3.zero;
            return;
        }

        Bounds combined = filtered[0].bounds;
        for (int i = 1; i < filtered.Count; i++)
        {
            combined.Encapsulate(filtered[i].bounds);
        }

        ApplyBounds(combined);
    }

    private void ApplyBounds(Bounds worldBounds)
    {
        if (recenterOnRecalculate)
        {
            // Move the group object to the bounds center
            transform.position = worldBounds.center;

            // Collider centered at local origin
            boxCol.center = Vector3.zero;

            Vector3 worldExtents = worldBounds.extents;

            Vector3 localExtentsX = transform.InverseTransformVector(new Vector3(worldExtents.x, 0, 0));
            Vector3 localExtentsY = transform.InverseTransformVector(new Vector3(0, worldExtents.y, 0));
            Vector3 localExtentsZ = transform.InverseTransformVector(new Vector3(0, 0, worldExtents.z));

            Vector3 localSize = new Vector3(
                Mathf.Abs(localExtentsX.x) * 2f,
                Mathf.Abs(localExtentsY.y) * 2f,
                Mathf.Abs(localExtentsZ.z) * 2f
            );

            boxCol.size = localSize;
        }
        else
        {
            // Only adjust collider, keep group transform as-is
            Vector3 localCenter = transform.InverseTransformPoint(worldBounds.center);

            Vector3 worldExtents = worldBounds.extents;

            Vector3 localExtentsX = transform.InverseTransformVector(new Vector3(worldExtents.x, 0, 0));
            Vector3 localExtentsY = transform.InverseTransformVector(new Vector3(0, worldExtents.y, 0));
            Vector3 localExtentsZ = transform.InverseTransformVector(new Vector3(0, 0, worldExtents.z));

            Vector3 localSize = new Vector3(
                Mathf.Abs(localExtentsX.x) * 2f,
                Mathf.Abs(localExtentsY.y) * 2f,
                Mathf.Abs(localExtentsZ.z) * 2f
            );

            boxCol.center = localCenter;
            boxCol.size   = localSize;
        }
    }
}
