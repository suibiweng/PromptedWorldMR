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

    public GameObject ProgramableBtnPrefab;
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
    [Tooltip("If true, generated real-object BoundsCube outlines are visible immediately.")]
    public bool showRealObjectBoundsOutline = true;
    [Range(0f, 1f)]
    [Tooltip("Transparency of generated real-object BoundsCube fill.")]
    public float realObjectBoundsAlpha = 0f;
    [Range(0.001f, 0.05f)]
    [Tooltip("Width of generated real-object BoundsWire lines.")]
    public float realObjectBoundsWireWidth = 0.0015f;

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
    private Material _realObjectBoundsMaterial;
    private Material _realObjectBoundsWireMaterial;

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
        ClearSelectionHighlightsExcept(obj);
        selectedObjects.Clear();
        if (obj != null)
        {
            selectedObjects.Add(obj);
        }
        selectedObject = obj;
    }

    public void SetPrimarySelectedObject(GameObject obj)
    {
        setSelectedObject(obj);
    }

    public bool TogglePrimarySelection(GameObject obj)
    {
        if (obj == null)
        {
            ClearSelection();
            return false;
        }

        if (IsSelected(obj))
        {
            RemoveFromSelection(obj);
            var programableObject = obj.GetComponentInParent<ProgramableObject>();
            if (programableObject != null)
                programableObject.ClearLatchedHighlight();
            return false;
        }

        setSelectedObject(obj);
        return true;
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
        ClearSelectionHighlightsExcept(null);
        selectedObjects.Clear();
        selectedObject = null;
    }

    private void ClearSelectionHighlightsExcept(GameObject keep)
    {
        var snapshot = new List<GameObject>(selectedObjects);
        foreach (var selected in snapshot)
        {
            if (selected == null || selected == keep)
                continue;

            var programableObject = selected.GetComponentInParent<ProgramableObject>();
            if (programableObject != null)
                programableObject.ClearLatchedHighlight();
        }
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

    // ---------- Delete API ----------

    public bool DeleteVirtualObject(GameObject obj)
    {
        var programableObject = obj != null ? obj.GetComponentInParent<ProgramableObject>() : null;
        return DeleteVirtualObject(programableObject);
    }

    public bool DeleteVirtualObject(ProgramableObject programableObject)
    {
        if (programableObject == null || programableObject.isRealObject || IsProtectedFromDelete(programableObject))
            return false;

        var root = programableObject.gameObject;
        Unregister(programableObject);

        if (root != null)
            Destroy(root);

        return true;
    }

    private bool IsProtectedFromDelete(ProgramableObject programableObject)
    {
        if (programableObject == null)
            return true;

        var root = programableObject.gameObject;
        return root.GetComponentInParent<GlobalRuleTarget>() != null
            || root.GetComponentInChildren<GlobalRuleTarget>(true) != null;
    }

    public int DeleteSelectedVirtualObjects()
    {
        var snapshot = new List<GameObject>(selectedObjects);
        int deleted = 0;

        foreach (var obj in snapshot)
        {
            if (DeleteVirtualObject(obj))
                deleted++;
        }

        if (deleted > 0 && selectedObject != null)
        {
            var programableObject = selectedObject.GetComponentInParent<ProgramableObject>();
            if (programableObject == null || !programableObject.isRealObject)
                selectedObject = selectedObjects.Count > 0 ? selectedObjects[selectedObjects.Count - 1] : null;
        }

        return deleted;
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
                EnsureId(p);
                _all.Add(p);
                if (!string.IsNullOrEmpty(p.id)) _byId[p.id] = p;
                (p.isRealObject ? _realObjects : _virtualObjects).Add(p);
            }
        }
    }

    public void Register(ProgramableObject p)
    {
        if (p == null || _all.Contains(p)) return;
        EnsureId(p);
        _all.Add(p);
        if (!string.IsNullOrEmpty(p.id)) _byId[p.id] = p;
        (p.isRealObject ? _realObjects : _virtualObjects).Add(p);
        OnAdded?.Invoke(p);
    }

    private static void EnsureId(ProgramableObject p)
    {
        if (p != null && string.IsNullOrWhiteSpace(p.id))
            p.id = IDGenerator.GenerateID();
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

    public void CreateVirtualButton()
    {
        if (Time.unscaledTime - _lastCreateTime < createCooldown) return;
        _lastCreateTime = Time.unscaledTime;

        GameObject container = Instantiate(ProgramableBtnPrefab, spawnPoint.position, Quaternion.identity);
        container.name = $"{ProgramableBtnPrefab.name}_Virtual";
        container.transform.localRotation = Quaternion.identity;
        container.transform.localScale = Vector3.one * 0.2f;
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

            string friendlyName = BuildUniqueVirtualObjectName(shapeType);

            GameObject container = Instantiate(ProgramableObjectPrefab, spawnPoint.position, Quaternion.identity);
            container.name = friendlyName;
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

            GameObject shape = PrimitiveFactory.CreatePrimitive(shapeType, Vector3.zero, Quaternion.identity, name: friendlyName);
            if (shape == null)
            {
                Debug.LogError("[PromptedWorldManager] PrimitiveFactory returned null.");
                Destroy(container);
                return;
            }

            shape.transform.SetParent(container.transform, false);
            prog.setShape(shape);
            if (prog.TextBox != null)
                prog.TextBox.text = friendlyName;
            if (!_all.Contains(prog)) Register(prog);

            setSelectedObject(container);
        }
        finally
        {
            shapeiscreating = false;
        }
    }

    private string BuildUniqueVirtualObjectName(int shapeType)
    {
        string baseName = GetShapeTypeName(shapeType);
        int index = 1;
        string candidate;

        do
        {
            candidate = $"{baseName}_{index}";
            index++;
        }
        while (VirtualObjectNameExists(candidate));

        return candidate;
    }

    private bool VirtualObjectNameExists(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        foreach (var p in _virtualObjects)
        {
            if (p == null)
                continue;

            if (p.gameObject != null && string.Equals(p.gameObject.name, candidate, StringComparison.OrdinalIgnoreCase))
                return true;
            if (p.shape != null && string.Equals(p.shape.name, candidate, StringComparison.OrdinalIgnoreCase))
                return true;
            if (p.TextBox != null && string.Equals(p.TextBox.text, candidate, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private string GetShapeTypeName(int shapeType)
    {
        switch (shapeType)
        {
            case PrimitiveFactory.SHAPE_CUBE: return "Cube";
            case PrimitiveFactory.SHAPE_SPHERE: return "Sphere";
            case PrimitiveFactory.SHAPE_CAPSULE: return "Capsule";
            case PrimitiveFactory.SHAPE_CYLINDER: return "Cylinder";
            case PrimitiveFactory.SHAPE_PLANE: return "Plane";
            case PrimitiveFactory.SHAPE_QUAD: return "Quad";
            default: return "Object";
        }
    }

    // ---------- Real-object assignment ----------

    public void AssignTheRealObject()
    {
        foreach (var f in FindObjectsByType<MRUKAnchor>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.InstanceID))
        {
            if (f == null) continue;

            if (ShouldSkipRealObjectAnchor(f))
            {
                CleanupGeneratedRealObjectWrapper(f);
                continue;
            }

            ProgramableObject programablerealobj = f.GetComponentInParent<ProgramableObject>();
            if (programablerealobj == null)
            {
                if (ProgramableRealobjectPrefab == null)
                {
                    Debug.LogWarning("[PromptedWorldManager] ProgramableRealobjectPrefab is not assigned.");
                    return;
                }

                GameObject s = Instantiate(ProgramableRealobjectPrefab, f.transform.position, f.transform.rotation);
                s.name = $"{ProgramableRealobjectPrefab.name}_Real_{f.gameObject.name}";
                f.transform.SetParent(s.transform);
                programablerealobj = s.GetComponent<ProgramableObject>();
            }

            if (programablerealobj == null)
            {
                Debug.LogError("[PromptedWorldManager] ProgramableRealobjectPrefab must contain ProgramableObject.");
                continue;
            }

            ConfigureRealObject(programablerealobj, f);

            if (programablerealobj.ShapeRenderer != null)
                print(f.gameObject.name + " has Renderer");
            else
                print(f.gameObject.name + " has no Renderer");
        }
    }

    private bool ShouldSkipRealObjectAnchor(MRUKAnchor anchor)
    {
        if (anchor == null)
            return true;

        if ((anchor.Label & MRUKAnchor.SceneLabels.GLOBAL_MESH) != 0)
            return true;

        string name = anchor.gameObject != null ? anchor.gameObject.name : "";
        return name.IndexOf("GlobalMesh", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Global Mesh", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void CleanupGeneratedRealObjectWrapper(MRUKAnchor anchor)
    {
        if (anchor == null)
            return;

        var programableObject = anchor.GetComponentInParent<ProgramableObject>();
        if (programableObject == null || programableObject.transform == anchor.transform)
            return;

        if (!anchor.transform.IsChildOf(programableObject.transform))
            return;

        Transform wrapperParent = programableObject.transform.parent;
        anchor.transform.SetParent(wrapperParent, true);
        Unregister(programableObject);
        Destroy(programableObject.gameObject);
    }

    private void ConfigureRealObject(ProgramableObject programableObject, MRUKAnchor anchor)
    {
        if (programableObject == null || anchor == null)
            return;

        programableObject.promptedWorldManager = this;
        programableObject.isRealObject = true;
        programableObject.shape = anchor.gameObject;
        ConfigureRealObjectOutline(programableObject, anchor.gameObject);
        ConfigureRealObjectCollider(programableObject, anchor.gameObject);

        programableObject.ApplyRealObjectInteractionPolicy();

        if (programableObject.TextBox != null && string.IsNullOrWhiteSpace(programableObject.TextBox.text))
            programableObject.TextBox.text = anchor.gameObject.name;

        if (!_all.Contains(programableObject))
            Register(programableObject);
        Reclassify(programableObject, true);

        _trackedMap[anchor.gameObject] = programableObject;
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

            if (ShouldSkipTrackedRealObjectSource(src))
            {
                if (_trackedMap.TryGetValue(src, out var skippedProgramableObject) && skippedProgramableObject != null)
                {
                    Unregister(skippedProgramableObject);
                    if (skippedProgramableObject.gameObject != src)
                        Destroy(skippedProgramableObject.gameObject);
                }

                _trackedMap.Remove(src);
                continue;
            }

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

                existing.shape = src;
                ConfigureRealObjectOutline(existing, src);
                ConfigureRealObjectCollider(existing, src);
                existing.ApplyRealObjectInteractionPolicy();
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
            prog.shape = src;
            ConfigureRealObjectOutline(prog, src);
            ConfigureRealObjectCollider(prog, src);
            prog.ApplyRealObjectInteractionPolicy();
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

    private bool ShouldSkipTrackedRealObjectSource(GameObject source)
    {
        if (source == null)
            return true;

        var anchor = source.GetComponentInParent<MRUKAnchor>();
        if (anchor != null && ShouldSkipRealObjectAnchor(anchor))
            return true;

        string name = source.name;
        return name.IndexOf("GlobalMesh", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Global Mesh", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void ConfigureRealObjectOutline(ProgramableObject programableObject, GameObject physicalObject)
    {
        if (programableObject == null || physicalObject == null)
            return;

        var renderers = FindPhysicalMeshRenderers(programableObject, physicalObject);
        if (renderers.Count == 0)
            return;

        programableObject.ShapeRenderer = renderers[0];

        for (int i = 0; i < renderers.Count; i++)
        {
            var renderer = renderers[i];
            var outline = renderer.GetComponent<Outline>();
            if (outline == null)
                outline = renderer.gameObject.AddComponent<Outline>();

            outline.OutlineColor = Color.cyan;
            outline.enabled = false;

            if (i == 0)
                programableObject.selectOutline = outline;
        }
    }

    private void ConfigureRealObjectCollider(ProgramableObject programableObject, GameObject physicalObject)
    {
        if (programableObject == null || physicalObject == null)
            return;

        var renderers = FindPhysicalMeshRenderers(programableObject, physicalObject);
        Bounds localBounds;
        if (renderers.Count > 0)
        {
            Bounds worldBounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Count; i++)
                worldBounds.Encapsulate(renderers[i].bounds);

            localBounds = WorldBoundsToLocalBounds(programableObject.transform, worldBounds);
        }
        else if (!TryGetMRUKBoundsInLocalSpace(programableObject.transform, physicalObject, out localBounds))
        {
            return;
        }

        var boxCollider = programableObject.GetComponent<BoxCollider>();
        if (boxCollider == null)
            boxCollider = programableObject.gameObject.AddComponent<BoxCollider>();

        boxCollider.enabled = true;
        boxCollider.isTrigger = false;
        boxCollider.center = localBounds.center;
        boxCollider.size = new Vector3(
            Mathf.Max(localBounds.size.x, 0.01f),
            Mathf.Max(localBounds.size.y, 0.01f),
            Mathf.Max(localBounds.size.z, 0.01f)
        );

        boxCollider.includeLayers = new LayerMask { value = 0 };
        boxCollider.excludeLayers = new LayerMask { value = 0 };

        ConfigureRealObjectColliderChild(programableObject, localBounds);
        ConfigureRealObjectBoundsCube(programableObject, localBounds);
    }

    private void ConfigureRealObjectColliderChild(ProgramableObject programableObject, Bounds localBounds)
    {
        if (programableObject == null)
            return;

        Transform colliderChild = programableObject.transform.Find("Collider");
        if (colliderChild == null)
        {
            var colliderObject = new GameObject("Collider");
            colliderObject.transform.SetParent(programableObject.transform, false);
            colliderChild = colliderObject.transform;
        }

        colliderChild.gameObject.SetActive(true);
        colliderChild.localPosition = Vector3.zero;
        colliderChild.localRotation = Quaternion.identity;
        colliderChild.localScale = Vector3.one;

        var childBoxCollider = colliderChild.GetComponent<BoxCollider>();
        if (childBoxCollider == null)
            childBoxCollider = colliderChild.gameObject.AddComponent<BoxCollider>();

        childBoxCollider.enabled = true;
        childBoxCollider.isTrigger = false;
        childBoxCollider.center = localBounds.center;
        childBoxCollider.size = new Vector3(
            Mathf.Max(localBounds.size.x, 0.01f),
            Mathf.Max(localBounds.size.y, 0.01f),
            Mathf.Max(localBounds.size.z, 0.01f)
        );

        childBoxCollider.includeLayers = new LayerMask { value = 0 };
        childBoxCollider.excludeLayers = new LayerMask { value = 0 };
    }

    private void ConfigureRealObjectBoundsCube(ProgramableObject programableObject, Bounds localBounds)
    {
        if (programableObject == null)
            return;

        programableObject.alwaysShowRealObjectOutline = showRealObjectBoundsOutline;

        Transform cubeTransform = programableObject.transform.Find("BoundsCube");
        GameObject cube;
        if (cubeTransform == null)
        {
            cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "BoundsCube";
            cube.transform.SetParent(programableObject.transform, false);

            var generatedCollider = cube.GetComponent<Collider>();
            if (generatedCollider != null)
            {
                generatedCollider.enabled = false;
                Destroy(generatedCollider);
            }
        }
        else
        {
            cube = cubeTransform.gameObject;
        }

        cube.SetActive(true);
        cube.transform.localPosition = localBounds.center;
        cube.transform.localRotation = Quaternion.identity;
        cube.transform.localScale = new Vector3(
            Mathf.Max(localBounds.size.x, 0.01f),
            Mathf.Max(localBounds.size.y, 0.01f),
            Mathf.Max(localBounds.size.z, 0.01f)
        );

        var renderer = cube.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = GetOrCreateRealObjectBoundsMaterial();
            renderer.enabled = false;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

            var outline = cube.GetComponent<Outline>();
            if (outline != null)
                outline.enabled = false;
        }

        programableObject.ShapeRenderer = null;
        if (programableObject.selectOutline != null && programableObject.selectOutline.gameObject == cube)
            programableObject.selectOutline = null;

        ConfigureRealObjectBoundsWire(cube);
    }

    private void ConfigureRealObjectBoundsWire(GameObject cube)
    {
        if (cube == null)
            return;

        Transform wireTransform = cube.transform.Find("BoundsWire");
        GameObject wire;
        if (wireTransform == null)
        {
            wire = new GameObject("BoundsWire");
            wire.transform.SetParent(cube.transform, false);
            wireTransform = wire.transform;
        }
        else
        {
            wire = wireTransform.gameObject;
        }

        wire.SetActive(true);
        wireTransform.localPosition = Vector3.zero;
        wireTransform.localRotation = Quaternion.identity;
        wireTransform.localScale = Vector3.one;

        var line = wire.GetComponent<LineRenderer>();
        if (line == null)
            line = wire.AddComponent<LineRenderer>();

        line.useWorldSpace = false;
        line.loop = false;
        line.positionCount = 16;
        line.SetPositions(GetUnitCubeWirePoints());
        line.widthMultiplier = Mathf.Max(0.001f, realObjectBoundsWireWidth);
        line.numCornerVertices = 2;
        line.numCapVertices = 2;
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        line.sharedMaterial = GetOrCreateRealObjectBoundsWireMaterial();
        line.startColor = Color.cyan;
        line.endColor = Color.cyan;
        line.enabled = showRealObjectBoundsOutline;
    }

    private static Vector3[] GetUnitCubeWirePoints()
    {
        return new[]
        {
            new Vector3(-0.5f, -0.5f, -0.5f),
            new Vector3(0.5f, -0.5f, -0.5f),
            new Vector3(0.5f, -0.5f, 0.5f),
            new Vector3(-0.5f, -0.5f, 0.5f),
            new Vector3(-0.5f, -0.5f, -0.5f),
            new Vector3(-0.5f, 0.5f, -0.5f),
            new Vector3(0.5f, 0.5f, -0.5f),
            new Vector3(0.5f, -0.5f, -0.5f),
            new Vector3(0.5f, 0.5f, -0.5f),
            new Vector3(0.5f, 0.5f, 0.5f),
            new Vector3(0.5f, -0.5f, 0.5f),
            new Vector3(0.5f, 0.5f, 0.5f),
            new Vector3(-0.5f, 0.5f, 0.5f),
            new Vector3(-0.5f, -0.5f, 0.5f),
            new Vector3(-0.5f, 0.5f, 0.5f),
            new Vector3(-0.5f, 0.5f, -0.5f)
        };
    }

    private Material GetOrCreateRealObjectBoundsWireMaterial()
    {
        if (_realObjectBoundsWireMaterial != null)
            return _realObjectBoundsWireMaterial;

        Shader shader = Shader.Find("PromptedWorld/Always On Top Line");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        if (shader == null)
            return null;

        _realObjectBoundsWireMaterial = new Material(shader);
        _realObjectBoundsWireMaterial.name = "GeneratedRealObjectBoundsWireMat";
        _realObjectBoundsWireMaterial.color = Color.white;
        if (_realObjectBoundsWireMaterial.HasProperty("_Color"))
            _realObjectBoundsWireMaterial.SetColor("_Color", Color.white);
        _realObjectBoundsWireMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Overlay;

        return _realObjectBoundsWireMaterial;
    }

    private Material GetOrCreateRealObjectBoundsMaterial()
    {
        if (_realObjectBoundsMaterial != null)
        {
            ApplyRealObjectBoundsMaterialAlpha();
            return _realObjectBoundsMaterial;
        }

        Shader shader = Shader.Find("PromptedWorld/No Color No Depth");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        if (shader == null)
            return null;

        _realObjectBoundsMaterial = new Material(shader);
        _realObjectBoundsMaterial.name = "GeneratedRealObjectBoundsMat";
        _realObjectBoundsMaterial.color = new Color(0f, 0.8f, 1f, Mathf.Clamp01(realObjectBoundsAlpha));

        if (_realObjectBoundsMaterial.HasProperty("_Surface"))
            _realObjectBoundsMaterial.SetFloat("_Surface", 1f);
        if (_realObjectBoundsMaterial.HasProperty("_BaseColor"))
            _realObjectBoundsMaterial.SetColor("_BaseColor", _realObjectBoundsMaterial.color);
        _realObjectBoundsMaterial.SetFloat("_Mode", 3f);
        _realObjectBoundsMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        _realObjectBoundsMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        _realObjectBoundsMaterial.SetInt("_ZWrite", 0);
        _realObjectBoundsMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        _realObjectBoundsMaterial.DisableKeyword("_ALPHATEST_ON");
        _realObjectBoundsMaterial.EnableKeyword("_ALPHABLEND_ON");
        _realObjectBoundsMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        _realObjectBoundsMaterial.renderQueue = 3000;
        ApplyRealObjectBoundsMaterialAlpha();

        return _realObjectBoundsMaterial;
    }

    private void ApplyRealObjectBoundsMaterialAlpha()
    {
        if (_realObjectBoundsMaterial == null)
            return;

        Color color = new Color(0f, 0.8f, 1f, Mathf.Clamp01(realObjectBoundsAlpha));
        _realObjectBoundsMaterial.color = color;
        if (_realObjectBoundsMaterial.HasProperty("_BaseColor"))
            _realObjectBoundsMaterial.SetColor("_BaseColor", color);
    }

    private bool TryGetMRUKBoundsInLocalSpace(Transform target, GameObject physicalObject, out Bounds localBounds)
    {
        localBounds = default;

        var anchor = physicalObject != null ? physicalObject.GetComponentInParent<MRUKAnchor>() : null;
        if (anchor == null)
            return false;

        if (anchor.VolumeBounds.HasValue)
        {
            localBounds = LocalBoundsToTargetLocalBounds(anchor.transform, target, anchor.VolumeBounds.Value);
            return true;
        }

        if (anchor.PlaneRect.HasValue)
        {
            localBounds = PlaneRectToTargetLocalBounds(anchor.transform, target, anchor.PlaneRect.Value, 0.03f);
            return true;
        }

        return false;
    }

    private Bounds WorldBoundsToLocalBounds(Transform target, Bounds worldBounds)
    {
        Vector3 min = worldBounds.min;
        Vector3 max = worldBounds.max;

        var localBounds = new Bounds(
            target.InverseTransformPoint(new Vector3(min.x, min.y, min.z)),
            Vector3.zero
        );

        localBounds.Encapsulate(target.InverseTransformPoint(new Vector3(min.x, min.y, max.z)));
        localBounds.Encapsulate(target.InverseTransformPoint(new Vector3(min.x, max.y, min.z)));
        localBounds.Encapsulate(target.InverseTransformPoint(new Vector3(min.x, max.y, max.z)));
        localBounds.Encapsulate(target.InverseTransformPoint(new Vector3(max.x, min.y, min.z)));
        localBounds.Encapsulate(target.InverseTransformPoint(new Vector3(max.x, min.y, max.z)));
        localBounds.Encapsulate(target.InverseTransformPoint(new Vector3(max.x, max.y, min.z)));
        localBounds.Encapsulate(target.InverseTransformPoint(new Vector3(max.x, max.y, max.z)));

        return localBounds;
    }

    private Bounds LocalBoundsToTargetLocalBounds(Transform source, Transform target, Bounds sourceBounds)
    {
        Vector3 min = sourceBounds.min;
        Vector3 max = sourceBounds.max;

        var localBounds = new Bounds(
            target.InverseTransformPoint(source.TransformPoint(new Vector3(min.x, min.y, min.z))),
            Vector3.zero
        );

        localBounds.Encapsulate(target.InverseTransformPoint(source.TransformPoint(new Vector3(min.x, min.y, max.z))));
        localBounds.Encapsulate(target.InverseTransformPoint(source.TransformPoint(new Vector3(min.x, max.y, min.z))));
        localBounds.Encapsulate(target.InverseTransformPoint(source.TransformPoint(new Vector3(min.x, max.y, max.z))));
        localBounds.Encapsulate(target.InverseTransformPoint(source.TransformPoint(new Vector3(max.x, min.y, min.z))));
        localBounds.Encapsulate(target.InverseTransformPoint(source.TransformPoint(new Vector3(max.x, min.y, max.z))));
        localBounds.Encapsulate(target.InverseTransformPoint(source.TransformPoint(new Vector3(max.x, max.y, min.z))));
        localBounds.Encapsulate(target.InverseTransformPoint(source.TransformPoint(new Vector3(max.x, max.y, max.z))));

        return localBounds;
    }

    private Bounds PlaneRectToTargetLocalBounds(Transform source, Transform target, Rect planeRect, float thickness)
    {
        float halfThickness = Mathf.Max(thickness, 0.005f) * 0.5f;

        var localBounds = new Bounds(
            target.InverseTransformPoint(source.TransformPoint(new Vector3(planeRect.xMin, planeRect.yMin, -halfThickness))),
            Vector3.zero
        );

        localBounds.Encapsulate(target.InverseTransformPoint(source.TransformPoint(new Vector3(planeRect.xMin, planeRect.yMin, halfThickness))));
        localBounds.Encapsulate(target.InverseTransformPoint(source.TransformPoint(new Vector3(planeRect.xMin, planeRect.yMax, -halfThickness))));
        localBounds.Encapsulate(target.InverseTransformPoint(source.TransformPoint(new Vector3(planeRect.xMin, planeRect.yMax, halfThickness))));
        localBounds.Encapsulate(target.InverseTransformPoint(source.TransformPoint(new Vector3(planeRect.xMax, planeRect.yMin, -halfThickness))));
        localBounds.Encapsulate(target.InverseTransformPoint(source.TransformPoint(new Vector3(planeRect.xMax, planeRect.yMin, halfThickness))));
        localBounds.Encapsulate(target.InverseTransformPoint(source.TransformPoint(new Vector3(planeRect.xMax, planeRect.yMax, -halfThickness))));
        localBounds.Encapsulate(target.InverseTransformPoint(source.TransformPoint(new Vector3(planeRect.xMax, planeRect.yMax, halfThickness))));

        return localBounds;
    }

    private List<MeshRenderer> FindPhysicalMeshRenderers(ProgramableObject programableObject, GameObject physicalObject)
    {
        var physicalRenderers = new List<MeshRenderer>();
        var renderers = physicalObject.GetComponentsInChildren<MeshRenderer>(true);
        if (renderers == null || renderers.Length == 0)
            return physicalRenderers;

        Transform wrapper = programableObject != null ? programableObject.transform : null;
        foreach (var renderer in renderers)
        {
            if (renderer == null)
                continue;

            if (wrapper != null && (renderer.transform == wrapper || renderer.transform.IsChildOf(wrapper)))
                continue;

            physicalRenderers.Add(renderer);
        }

        if (physicalRenderers.Count == 0)
            physicalRenderers.AddRange(renderers);

        return physicalRenderers;
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
