using UnityEngine;

public class SimpleCollidable : MonoBehaviour
{
    [Tooltip("Optional. Auto-finds if null.")]
    public CollisionManager manager;

    Renderer[] _renderers;

    void Awake()
    {
        // Get renderers in children (including self)
        _renderers = GetComponentsInChildren<Renderer>();

        if (_renderers.Length == 0)
        {
            Debug.LogWarning($"[{name}] No Renderer found in children.");
            enabled = false;
            return;
        }

        // Auto-find manager
        if (manager == null)
            manager = FindFirstObjectByType<CollisionManager>();

        if (manager != null)
            manager.Register(this);
        else
            Debug.LogWarning($"[{name}] No CollisionManager in scene.");
    }

    void OnDestroy()
    {
        if (manager != null)
            manager.Unregister(this);
    }

    // =========================
    // Combined bounds
    // =========================
    public Bounds GetBounds()
    {
        Bounds b = _renderers[0].bounds;

        for (int i = 1; i < _renderers.Length; i++)
            b.Encapsulate(_renderers[i].bounds);

        return b;
    }

    // =========================
    // Custom callbacks
    // =========================
    void OnCustomCollisionEnter(GameObject other)
    {
        Debug.Log($"{name} ENTER {other.name}");
    }

    void OnCustomCollisionStay(GameObject other)
    {
        Debug.Log($"{name} STAY {other.name}");
    }

    void OnCustomCollisionExit(GameObject other)
    {
        Debug.Log($"{name} EXIT {other.name}");
    }
}
