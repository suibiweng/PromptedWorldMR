using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using PromptedWorld;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
public class PromptedWorldManager : MonoBehaviour
{
    [Header("Create Shape Debounce")]
    [Tooltip("Minimum time between CreateShape calls (seconds) to ignore double-clicks / duplicate events.")]
    public float createCooldown = 0.25f;
    private float _lastCreateTime = -999f;

    [Header("User Anchors")]
    public Transform userHead;
    public Transform userLeftHand;
    public Transform userRightHand;

    public Transform spawnPoint;

    [Header("Spawning / Prefabs")]
    [Tooltip("Prefab that contains a ProgramableObject component.")]
    public GameObject ProgramableObjectPrefab;
    public GameObject ProgramableRealobjectPrefab;

    [Header("Selection (single + multi)")]
    [Tooltip("Last selected object (for legacy APIs).")]
    public GameObject selectedObject;

    [Tooltip("Dynamic list of all currently selected objects (via click / ProgramableObject).")]
    [SerializeField] private List<GameObject> selectedObjects = new List<GameObject>();
    public IReadOnlyList<GameObject> SelectedObjects => selectedObjects;

    [Header("TrackedObject Collector")]
    [Tooltip("Tag for real-world anchors to wrap with ProgramableObjectPrefab.")]
    public string trackedTag = "TrackedObject";
    [Tooltip("If true, the manager rescans for TrackedObject every frame.")]
    public bool keepUpdatedEachFrame = true;

    // Public views
    public IReadOnlyList<ProgramableObject> RealObjects => _realObjects;
    public IReadOnlyList<ProgramableObject> VirtualObjects => _virtualObjects;

    // Events
    public event Action<ProgramableObject> OnAdded;
    public event Action<ProgramableObject> OnRemoved;
    public event Action<ProgramableObject, bool> OnReclassified;

    // Internals
    public List<ProgramableObject> _realObjects = new();
    public List<ProgramableObject> _virtualObjects = new();
    public HashSet<ProgramableObject> _all = new();
    private readonly Dictionary<string, ProgramableObject> _byId = new();
    private readonly Dictionary<GameObject, ProgramableObject> _trackedMap = new();

    private void Awake()
    {
        Rebuild();
    }

    private void Update()
    {
        if (keepUpdatedEachFrame)
            RefreshTrackedObjects();
    }

    private void OnDestroy()
    {
        _realObjects.Clear();
        _virtualObjects.Clear();
        _all.Clear();
        _byId.Clear();
        _trackedMap.Clear();
        selectedObjects.Clear();
        selectedObject = null;
    }

    // ---------- Selection API ----------

    /// <summary>
    /// Legacy single-selection API: clears list and sets a single selected object.
    /// </summary>
    public void setSelectedObject(GameObject obj)
    {
        selectedObjects.Clear();
        if (obj != null)
        {
            selectedObjects.Add(obj);
        }
        selectedObject = obj;
    }

    /// <summary>
    /// Toggle-based selection:
    /// - If obj is already selected, remove it.
    /// - If not, add it.
    /// Keeps selectedObject synced to last selected item.
    //</summary>
    public void ToggleSelection(GameObject obj)
    {
        if (obj == null) return;

        int index = selectedObjects.IndexOf(obj);
        if (index >= 0)
        {
            // Unselect
            selectedObjects.RemoveAt(index);
            if (selectedObject == obj)
            {
                selectedObject = selectedObjects.Count > 0
                    ? selectedObjects[selectedObjects.Count - 1]
                    : null;
            }
        }
        else
        {
            // Select
            selectedObjects.Add(obj);
            selectedObject = obj;
        }
    }

    public void AddToSelection(GameObject obj)
    {
        if (obj == null) return;
        if (!selectedObjects.Contains(obj))
            selectedObjects.Add(obj);
        selectedObject = obj;
    }

    public void RemoveFromSelection(GameObject obj)
    {
        if (obj == null) return;
        int index = selectedObjects.IndexOf(obj);
        if (index < 0) return;
        selectedObjects.RemoveAt(index);
        if (selectedObject == obj)
        {
            selectedObject = selectedObjects.Count > 0
                ? selectedObjects[selectedObjects.Count - 1]
                : null;
        }
    }

    public void ClearSelection()
    {
        selectedObjects.Clear();
        selectedObject = null;
    }

    public bool IsSelected(GameObject obj)
    {
        if (obj == null) return false;
        return selectedObjects.Contains(obj);
    }

    public IReadOnlyList<GameObject> GetSelectedObjects()
    {
        return selectedObjects;
    }

    // ---------- Public Registry API ----------

    public void Rebuild()
    {
        _realObjects.Clear();
        _virtualObjects.Clear();
        _all.Clear();
        _byId.Clear();

        foreach (var p in FindObjectsOfType<ProgramableObject>(true))
        {
            if (p != null && p.isActiveAndEnabled)
            {
                _all.Add(p);
                if (!string.IsNullOrEmpty(p.id)) _byId[p.id] = p;
                (p.isRealObject ? _realObjects : _virtualObjects).Add(p);
            }
        }
    }

    public void Register(ProgramableObject p)
    {
        if (p == null || _all.Contains(p)) return;
        _all.Add(p);
        if (!string.IsNullOrEmpty(p.id)) _byId[p.id] = p;
        (p.isRealObject ? _realObjects : _virtualObjects).Add(p);
        OnAdded?.Invoke(p);
    }

    public void Unregister(ProgramableObject p)
    {
        if (p == null || !_all.Remove(p)) return;
        if (p.isRealObject) _realObjects.Remove(p);
        else _virtualObjects.Remove(p);
        if (!string.IsNullOrEmpty(p.id) && _byId.TryGetValue(p.id, out var cur) && cur == p)
            _byId.Remove(p.id);
        OnRemoved?.Invoke(p);

        // Remove from selection
        RemoveFromSelection(p.gameObject);
    }

    public void Reclassify(ProgramableObject p, bool nowIsReal)
    {
        if (p == null || !_all.Contains(p)) return;
        _realObjects.Remove(p);
        _virtualObjects.Remove(p);
        if (nowIsReal) _realObjects.Add(p);
        else _virtualObjects.Add(p);
        OnReclassified?.Invoke(p, nowIsReal);
    }

    public bool TryGetById(string id, out ProgramableObject obj)
    {
        if (!string.IsNullOrEmpty(id) && _byId.TryGetValue(id, out obj))
            return obj != null;
        obj = null;
        return false;
    }

    // ---------- Spawning / Shape Creation ----------

    bool shapeiscreating = false;

    public void CreateShapeUIButton(int shapeType)
    {
        if (Time.unscaledTime - _lastCreateTime < createCooldown) return;
        _lastCreateTime = Time.unscaledTime;
        CreateShape(shapeType);
    }

    public void CreateShape(int shapeType)
    {
        if (shapeiscreating) return;
        shapeiscreating = true;

        try
        {
            if (ProgramableObjectPrefab == null)
            {
                Debug.LogWarning("[PromptedWorldManager] ProgramableObjectPrefab is not assigned.");
                return;
            }

            GameObject container = Instantiate(ProgramableObjectPrefab, spawnPoint.position, Quaternion.identity);
            container.name = $"{ProgramableObjectPrefab.name}_Virtual";
            container.transform.localRotation = Quaternion.identity;
            container.transform.localScale = Vector3.one * 0.2f;

            var prog = container.GetComponent<ProgramableObject>();
            if (prog == null)
            {
                Debug.LogError("[PromptedWorldManager] Prefab must contain ProgramableObject.");
                Destroy(container);
                return;
            }

            if (prog.isRealObject)
            {
                prog.isRealObject = false;
                if (_all.Contains(prog)) Reclassify(prog, false);
            }

            prog.promptedWorldManager = this;

            GameObject shape = PrimitiveFactory.CreatePrimitive(shapeType, Vector3.zero, Quaternion.identity);
            if (shape == null)
            {
                Debug.LogError("[PromptedWorldManager] PrimitiveFactory returned null.");
                Destroy(container);
                return;
            }

            shape.transform.SetParent(container.transform, false);
            prog.setShape(shape);
            if (!_all.Contains(prog)) Register(prog);

            setSelectedObject(container);
        }
        finally
        {
            shapeiscreating = false;
        }
    }

    // ---------- Real-object assignment ----------

    public void AssignTheRealObject()
    {
        foreach (var f in FindObjectsByType<MRUKAnchor>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.InstanceID))
        {
            GameObject s = Instantiate(ProgramableRealobjectPrefab, f.transform.position, f.transform.rotation);
            f.transform.SetParent(s.transform);
            ProgramableObject programablerealobj = s.GetComponent<ProgramableObject>();

            programablerealobj.isRealObject = true;
            programablerealobj.shape = f.gameObject;

            if (programablerealobj.shape.GetComponentInChildren<MeshRenderer>())
                print(f.gameObject.name + " has Renderer");
            else
                print(f.gameObject.name + " has no Renderer");

            programablerealobj.ShapeRenderer = programablerealobj.shape.GetComponentInChildren<MeshRenderer>();
        }
    }

    public void DelayCreateRealobject()
    {
        StartCoroutine(DelayAssign());
    }

    public IEnumerator DelayAssign()
    {
        yield return new WaitForSeconds(0.1f);
        AssignTheRealObject();
    }

    // ---------- TrackedObject Collector ----------

    public void RefreshTrackedObjects()
    {
        if (ProgramableObjectPrefab == null) return;

        var sources = GameObject.FindGameObjectsWithTag(trackedTag);
        var seen = new HashSet<GameObject>();

        foreach (var src in sources)
        {
            if (src == null || !src.activeInHierarchy) continue;
            seen.Add(src);

            if (_trackedMap.TryGetValue(src, out var existing) && existing != null)
            {
                if (existing.transform.parent != src.transform)
                    existing.transform.SetParent(src.transform, false);

                if (!existing.isRealObject)
                {
                    existing.isRealObject = true;
                    Reclassify(existing, true);
                }
                continue;
            }

            ProgramableObject foundChild = null;
            foreach (Transform child in src.transform)
            {
                foundChild = child.GetComponent<ProgramableObject>();
                if (foundChild != null) break;
            }

            ProgramableObject prog;
            if (foundChild != null)
            {
                prog = foundChild;
            }
            else
            {
                var container = Instantiate(ProgramableObjectPrefab, src.transform, false);
                container.name = $"{ProgramableObjectPrefab.name}_Real_{src.name}";
                prog = container.GetComponent<ProgramableObject>();
                if (prog == null)
                {
                    Debug.LogError("[PromptedWorldManager] ProgramableObjectPrefab must include ProgramableObject.");
                    Destroy(container);
                    continue;
                }
            }

            if (!prog.isRealObject) prog.isRealObject = true;
            Register(prog);
            Reclassify(prog, true);

            _trackedMap[src] = prog;
        }

        var toRemove = new List<GameObject>();
        foreach (var kv in _trackedMap)
        {
            var src = kv.Key;
            var prog = kv.Value;

            if (src == null || !src.activeInHierarchy || !seen.Contains(src))
            {
                if (prog != null) Unregister(prog);
                if (src == null && prog != null)
                    Destroy(prog.gameObject);
                toRemove.Add(src);
            }
        }
        foreach (var r in toRemove) _trackedMap.Remove(r);
    }

    // ---------- Lua run-all helpers ----------

    [ContextMenu("Lua • Run All")]
    public void RunAll()
    {
        foreach (var p in _all)
        {
            if (p == null) continue;
            var lb = p.GetComponent<LuaBehaviour>();
            if (lb == null) continue;
            lb.StartRun();
        }
    }

    [ContextMenu("Lua • Stop All")]
    public void StopAll(bool snapToStartPose = true)
    {
        foreach (var p in _all)
        {
            if (p == null) continue;
            var lb = p.GetComponent<LuaBehaviour>();
            if (lb == null) continue;
            bool prev = lb.resetPositionOnStop;
            lb.resetPositionOnStop = snapToStartPose;
            lb.StopRun();
            lb.resetPositionOnStop = prev;
        }
    }

    [ContextMenu("Lua • Run All (Real)")]
    public void RunAllReal()
    {
        foreach (var p in _realObjects)
        {
            if (p == null) continue;
            var lb = p.GetComponent<LuaBehaviour>();
            if (lb != null) lb.StartRun();
        }
    }

    [ContextMenu("Lua • Stop All (Real)")]
    public void StopAllReal(bool snapToStartPose = true)
    {
        foreach (var p in _realObjects)
        {
            if (p == null) continue;
            var lb = p.GetComponent<LuaBehaviour>();
            if (lb == null) continue;
            bool prev = lb.resetPositionOnStop;
            lb.resetPositionOnStop = snapToStartPose;
            lb.StopRun();
            lb.resetPositionOnStop = prev;
        }
    }

    [ContextMenu("Lua • Run All (Virtual)")]
    public void RunAllVirtual()
    {
        foreach (var p in _virtualObjects)
        {
            if (p == null) continue;
            var lb = p.GetComponent<LuaBehaviour>();
            if (lb != null) lb.StartRun();
        }
    }

    [ContextMenu("Lua • Stop All (Virtual)")]
    public void StopAllVirtual(bool snapToStartPose = true)
    {
        foreach (var p in _virtualObjects)
        {
            if (p == null) continue;
            var lb = p.GetComponent<LuaBehaviour>();
            if (lb == null) continue;
            bool prev = lb.resetPositionOnStop;
            lb.resetPositionOnStop = snapToStartPose;
            lb.StopRun();
            lb.resetPositionOnStop = prev;
        }
    }



    // ---------- IoT Device Query ----------
[Header("IoT")]
public IOTManager iotManager;


public List<string> GetAllIoTDeviceIDs()
{
    if (iotManager == null)
        return new List<string>();

    return iotManager.GetAllDeviceIDs();
}



}
