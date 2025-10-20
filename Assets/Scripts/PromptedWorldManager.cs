using UnityEngine;
using PromptedWorld;
using System;
using System.Collections.Generic;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
public class PromptedWorldManager : MonoBehaviour
{
    [Header("User Anchors")]
    public Transform userHead;
    public Transform userLeftHand;
    public Transform userRightHand;

    [Header("Spawning / Prefabs")]
    [Tooltip("Prefab that contains a ProgramableObject component.")]
    public GameObject ProgramableObjectPrefab;

    [Header("Selection")]
    public GameObject selectedObject;

    [Header("TrackedObject Collector")]
    [Tooltip("Tag for real-world anchors to wrap with ProgramableObjectPrefab.")]
    public string trackedTag = "TrackedObject";
    [Tooltip("If true, the manager rescans for TrackedObject every frame.")]
    public bool keepUpdatedEachFrame = true;

    // --- Public views ---
    public IReadOnlyList<ProgramableObject> RealObjects => _realObjects;
    public IReadOnlyList<ProgramableObject> VirtualObjects => _virtualObjects;

    // --- Events ---
    public event Action<ProgramableObject> OnAdded;
    public event Action<ProgramableObject> OnRemoved;
    public event Action<ProgramableObject, bool> OnReclassified;

    // --- Internals ---
    private readonly List<ProgramableObject> _realObjects = new();
    private readonly List<ProgramableObject> _virtualObjects = new();
    private readonly HashSet<ProgramableObject> _all = new();
    private readonly Dictionary<string, ProgramableObject> _byId = new();
    private readonly Dictionary<GameObject, ProgramableObject> _trackedMap = new();

    // ---------- Lifecycle ----------
    private void Awake()
    {
        Rebuild();               // catch pre-existing ProgramableObjects
        RefreshTrackedObjects(); // wrap TrackedObject-tagged objects
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
    }

    // ---------- Public API ----------
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

    // ---------- Create virtual object ----------
    public void CreateShape(int shapeType)
    {
        if (ProgramableObjectPrefab == null)
        {
            Debug.LogWarning("[PromptedWorldManager] ProgramableObjectPrefab is not assigned.");
            return;
        }

        GameObject container = Instantiate(ProgramableObjectPrefab, userLeftHand.position, Quaternion.identity);
        container.name = $"{ProgramableObjectPrefab.name}_Virtual";
        container.transform.localPosition = Vector3.zero;
        container.transform.localRotation = Quaternion.identity;
        container.transform.localScale = Vector3.one * 0.2f;

        var prog = container.GetComponent<ProgramableObject>();
        if (prog == null)
        {
            Debug.LogError("[PromptedWorldManager] Prefab must contain ProgramableObject.");
            Destroy(container);
            return;
        }

        // Force VIRTUAL
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
        selectedObject = container;
    }

    public void setSelectedObject(GameObject obj) => selectedObject = obj;

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

            // Already mapped?
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

            // Look for ProgramableObject child
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

        // Cleanup for removed or inactive
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

    // === Add inside PromptedWorldManager ===

    // Run Lua on ALL tracked ProgramableObjects
    [ContextMenu("Lua • Run All")]
    public void RunAll()
    {
        foreach (var p in _all)
        {
            if (p == null) continue;
            var lb = p.GetComponent<LuaBehaviour>();
            if (lb == null) continue;

            // ensure a script is loaded; if you rely on generation only, this may be empty.
            // Start the run session (captures run-start pose + calls start()).
            lb.StartRun();
        }
    }

    // Stop Lua on ALL tracked ProgramableObjects
    // snapToStartPose: if true, each object snaps back to its 'run-start' position (the moment StartRun() was called)
    [ContextMenu("Lua • Stop All")]
    public void StopAll(bool snapToStartPose = true)
    {
        foreach (var p in _all)
        {
            if (p == null) continue;
            var lb = p.GetComponent<LuaBehaviour>();
            if (lb == null) continue;

            // Temporarily enforce snap behavior if requested
            bool prev = lb.resetPositionOnStop;
            lb.resetPositionOnStop = snapToStartPose;
            lb.StopRun();
            lb.resetPositionOnStop = prev;
        }
    }

    // Optional: only Real objects
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

    // Optional: only Virtual objects
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






}
