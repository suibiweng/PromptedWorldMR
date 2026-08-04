using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Events;
using Oculus.Interaction;   // IInteractableView / InteractableState
using PromptedWorld;        // PromptedWorldManager

// --------- Prompt Log DTO ---------
[Serializable]
public class PromptLogEntry
{
    public string id;
    public string timestampIso;
    public string objectName;
    [TextArea(2, 6)] public string prompt;
    public string mode;     // "Replace" | "EditInPlace" | etc.
    public string model;    // e.g., "gpt-4.1-mini", "o4-mini", etc.
    public bool succeeded;
    public float durationSec;
    public int inputTokens;
    public int outputTokens;
    public string luaHash;
    [TextArea(2, 6)] public string notes;
}

public class ProgramableObject : MonoBehaviour
{
    [Header("Core")]
    public PromptedWorldManager promptedWorldManager;
    public string id;
    public string promptlog;
    public bool isRealObject = false;

    [Header("UI/Visual")]
    public TMP_Text TextBox;
    public RawImage Objimage;
    public Renderer ShapeRenderer;
    public GameObject shape;
    public Transform shapeRoot;

    [Header("Lua State Indicator")]
    [Tooltip("Assign a UI Image or RawImage. Alpha 0 = no Lua assigned, red = stopped, green = playing.")]
    public Graphic LuaStateIndicator;
    public Color luaNoScriptColor = new Color(1f, 1f, 1f, 0f);
    public Color luaStoppedColor = new Color(1f, 0f, 0f, 1f);
    public Color luaPlayingColor = new Color(0f, 1f, 0f, 1f);

    [Header("Billboard Label")]
    public bool labelFacesCamera = true;
    [Tooltip("Enable if your label still appears backward after billboarding.")]
    public bool invertLabelFacing = false;
    [Tooltip("Optional override. If empty, uses the TextBox parent/2DDisplay transform.")]
    public Transform labelBillboardRoot;

    [Tooltip("Outline component used for selection highlight.")]
    public Outline selectOutline;

    [Header("Interaction Source")]
    [Tooltip("Assign your RayInteractable (or any IInteractableView). We'll mirror its state.")]
    [SerializeField, Interface(typeof(IInteractableView))]
    private UnityEngine.Object _interactableViewObj;
    private IInteractableView _view;

    [Header("Highlight behavior")]
    [Tooltip("If true, outline also shows while hovering (when not latched).")]
    public bool highlightOnHover = false;

    [Tooltip("If true, selection is sticky: click toggles it on/off (latched).")]
    public bool stickyHighlightEnabled = true;

    [Header("Outline Colors")]
    [SerializeField] private Color virtualOutlineColor = Color.red;
    [SerializeField] private Color realOutlineColor = Color.cyan;
    [HideInInspector] public bool alwaysShowRealObjectOutline = false;

    [Header("User Anchors & Proximity")]
    public Transform userRoot;
    public Transform userHead;
    public Transform userLeftHand;
    public Transform userRightHand;
    [Range(0.01f, 0.5f)] public float Touchingdistance = 0.1f;
    public bool isToching;

    [Header("Events (you can hook in the Inspector)")]
    public UnityEvent onHoverEnter;
    public UnityEvent onHoverExit;
    public UnityEvent onSelectEnter;
    public UnityEvent onSelectExit;
    public UnityEvent onProximityEnter;
    public UnityEvent onProximityExit;

    // --- internal state ---
    public bool _selected;
    public bool _hovering;
    public bool _prevTouching;

    [Tooltip("Sticky selection state. True means this object is logically selected.")]
    public bool highlightLatched;

    private InteractableState _lastState = InteractableState.Normal;

    // ========= PROMPT LOG (add-only) =========
    [Header("Prompt Log")]
    [SerializeField] private List<PromptLogEntry> _promptLog = new List<PromptLogEntry>();

    [Serializable] public class PromptLogUpdatedEvent : UnityEvent<PromptLogEntry> { }
    public PromptLogUpdatedEvent OnPromptLogUpdated;
    public IReadOnlyList<PromptLogEntry> PromptLog => _promptLog;

    void Awake()
    {
        if (promptedWorldManager == null)
            promptedWorldManager = FindAnyObjectByType<PromptedWorldManager>();

        if (string.IsNullOrEmpty(id))
            id = IDGenerator.GenerateID();

        _view = _interactableViewObj as IInteractableView;

        if (userHead == null && promptedWorldManager != null)
            userHead = promptedWorldManager.userHead;
        if (userRoot == null)
            userRoot = userHead;
        if (userLeftHand == null && promptedWorldManager != null)
            userLeftHand = promptedWorldManager.userLeftHand;
        if (userRightHand == null && promptedWorldManager != null)
            userRightHand = promptedWorldManager.userRightHand;

        if (shape != null && selectOutline == null)
            selectOutline = shape.GetComponentInChildren<Outline>(includeInactive: true);

        ApplyRealObjectInteractionPolicy();
        ApplyOutlineColor();
        SetOutline(false);
        UpdateLuaStateIndicator();
        highlightLatched = false;
    }

    void Start()
    {
        if (_view != null)
        {
            _lastState = _view.State;
            ApplyState(_view.State, _lastState);
        }
    }

    void OnEnable()
    {
        if (_view != null)
            _view.WhenStateChanged += OnViewStateChanged;
    }

    void OnDisable()
    {
        if (_view != null)
            _view.WhenStateChanged -= OnViewStateChanged;
    }

    void Update()
    {
        ProximityTouchingDetection();
        UpdateLuaStateIndicator();
        UpdateLabelBillboard();
    }

    void OnValidate()
    {
        UpdateLuaStateIndicator();
    }

    // ---------- Public helpers ----------

    public bool hasLuaScript() => GetComponent<LuaBehaviour>() != null;

    private void UpdateLabelBillboard()
    {
        if (!labelFacesCamera)
            return;

        Transform labelTransform = GetLabelBillboardTransform();
        if (labelTransform == null)
            return;

        Transform cameraTransform = GetBillboardCameraTransform();
        if (cameraTransform == null)
            return;

        Vector3 direction = labelTransform.position - cameraTransform.position;
        if (direction.sqrMagnitude < 0.0001f)
            return;

        if (invertLabelFacing)
            direction = -direction;

        labelTransform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
    }

    private Transform GetLabelBillboardTransform()
    {
        if (labelBillboardRoot != null)
            return labelBillboardRoot;

        if (TextBox == null)
            return null;

        Transform current = TextBox.transform;
        while (current.parent != null && current.parent != transform)
        {
            if (current.name.IndexOf("2DDisplay", StringComparison.OrdinalIgnoreCase) >= 0 ||
                current.name.IndexOf("Display", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return current;
            }

            current = current.parent;
        }

        return TextBox.transform.parent != null ? TextBox.transform.parent : TextBox.transform;
    }

    private Transform GetBillboardCameraTransform()
    {
        if (userHead != null)
            return userHead;

        if (promptedWorldManager != null && promptedWorldManager.userHead != null)
            return promptedWorldManager.userHead;

        Camera mainCamera = Camera.main;
        return mainCamera != null ? mainCamera.transform : null;
    }

    public void UpdateLuaStateIndicator()
    {
        if (LuaStateIndicator == null)
            return;

        var lua = GetComponent<LuaBehaviour>();
        if (lua == null || !LuaHasAssignedScript(lua))
        {
            LuaStateIndicator.color = luaNoScriptColor;
            return;
        }

        LuaStateIndicator.color = lua.runEnabled ? luaPlayingColor : luaStoppedColor;
    }

    private static bool LuaHasAssignedScript(LuaBehaviour lua)
    {
        if (lua == null)
            return false;

        return !string.IsNullOrWhiteSpace(lua.CurrentLua) ||
               !string.IsNullOrWhiteSpace(lua.inlineScript) ||
               lua.scriptAsset != null;
    }

    public GameObject GetLuaShapeObject()
    {
        if (ShapeRenderer != null)
            return ShapeRenderer.gameObject;

        Transform boundsCube = transform.Find("BoundsCube");
        if (boundsCube != null)
            return boundsCube.gameObject;

        if (shape != null)
        {
            var renderer = shape.GetComponentInChildren<Renderer>(includeInactive: true);
            if (renderer != null)
                return renderer.gameObject;

            return shape;
        }

        return gameObject;
    }

    public Transform GetLuaShapeTransform()
    {
        var shapeObject = GetLuaShapeObject();
        return shapeObject != null ? shapeObject.transform : transform;
    }

    public void SetOutlineVisible(bool on)
    {
        ApplyOutlineColor();
        SetOutline(on);
    }

    public void setShape(GameObject obj)
    {
        shape = obj;
        if (shape == null) return;

        shape.transform.SetParent(shapeRoot != null ? shapeRoot : transform);
        shape.transform.localPosition = Vector3.zero;
        shape.transform.localRotation = Quaternion.identity;

        ShapeRenderer = shape.GetComponent<Renderer>();

        if (selectOutline == null)
            selectOutline = shape.GetComponentInChildren<Outline>(includeInactive: true);

        ApplyRealObjectInteractionPolicy();
        ApplyOutlineColor();
        UpdateHighlightVisual();
    }

    public void ApplyRealObjectInteractionPolicy()
    {
        if (!isRealObject)
            return;

        DisableGrabComponents();

        var body = GetComponent<Rigidbody>();
        if (body != null)
        {
            body.isKinematic = true;
            body.useGravity = false;
        }
    }

    private void DisableGrabComponents()
    {
        foreach (var component in GetComponentsInChildren<MonoBehaviour>(includeInactive: true))
        {
            if (component == null || component == this)
                continue;

            string typeName = component.GetType().Name;
            string fullName = component.GetType().FullName ?? typeName;

            if (typeName.IndexOf("Grab", StringComparison.OrdinalIgnoreCase) >= 0 ||
                fullName.IndexOf(".Grab", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                component.enabled = false;
            }
        }

        foreach (Transform child in GetComponentsInChildren<Transform>(includeInactive: true))
        {
            if (child == null || child == transform)
                continue;

            if (child.name.IndexOf("HandGrab", StringComparison.OrdinalIgnoreCase) >= 0 ||
                child.name.IndexOf("DistanceHandGrab", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                child.gameObject.SetActive(false);
            }
        }
    }

    // These are used by LassoSelectorMR3D (and handy for code)
    public void SetLatchedHighlight(bool on)
    {
        highlightLatched = on;
        UpdateHighlightVisual();
    }

    public void ClearLatchedHighlight()
    {
        highlightLatched = false;
        UpdateHighlightVisual();
    }

    // ========== Interaction state wiring ==========

    private void OnViewStateChanged(InteractableStateChangeArgs args)
    {
        ApplyState(args.NewState, args.PreviousState);
        _lastState = args.NewState;
    }

    private void ApplyState(InteractableState newState, InteractableState prevState)
    {
        // Hover transitions
        if (prevState != InteractableState.Hover && newState == InteractableState.Hover)
        {
            _hovering = true;
            onHoverEnter?.Invoke();
            OnHoverEnter();
        }
        else if (prevState == InteractableState.Hover && newState != InteractableState.Hover)
        {
            _hovering = false;
            onHoverExit?.Invoke();
            OnHoverExit();
        }

        // Select transitions
        if (prevState != InteractableState.Select && newState == InteractableState.Select)
        {
            _selected = true;
            onSelectEnter?.Invoke();
            OnSelectEnter();
        }
        else if (prevState == InteractableState.Select && newState != InteractableState.Select)
        {
            _selected = false;
            onSelectExit?.Invoke();
            OnSelectExit();
        }

        UpdateHighlightVisual();
    }

    // Decide whether outline should be visible
    private void UpdateHighlightVisual()
    {
        ApplyOutlineColor();

        bool show =
            (stickyHighlightEnabled && highlightLatched) ||
            (!stickyHighlightEnabled && _selected) ||
            (!stickyHighlightEnabled && highlightOnHover && _hovering);

        SetOutline(show);
    }

    private void SetOutline(bool on)
    {
        bool shouldShow = on || (isRealObject && alwaysShowRealObjectOutline);

        if (isRealObject && shape != null)
        {
            foreach (var outline in shape.GetComponentsInChildren<Outline>(includeInactive: true))
            {
                if (outline == null)
                    continue;

                outline.OutlineColor = realOutlineColor;
                outline.enabled = shouldShow;
            }

            if (selectOutline != null)
            {
                selectOutline.OutlineColor = realOutlineColor;
                selectOutline.enabled = shouldShow;
            }

            ApplyBoundsWireOutline(shouldShow, realOutlineColor);
            return;
        }

        if (selectOutline != null)
            selectOutline.enabled = shouldShow;

        if (isRealObject)
            ApplyBoundsWireOutline(shouldShow, realOutlineColor);
    }

    private void ApplyOutlineColor()
    {
        if (selectOutline == null)
            return;

        selectOutline.OutlineColor = isRealObject ? realOutlineColor : virtualOutlineColor;
    }

    private void ApplyBoundsWireOutline(bool on, Color color)
    {
        Transform boundsCube = transform.Find("BoundsCube");
        if (boundsCube == null)
            return;

        var lines = boundsCube.GetComponentsInChildren<LineRenderer>(includeInactive: true);
        foreach (var line in lines)
        {
            if (line == null)
                continue;

            line.startColor = color;
            line.endColor = color;
            if (line.sharedMaterial != null && line.sharedMaterial.HasProperty("_Color"))
                line.sharedMaterial.SetColor("_Color", Color.white);
            line.enabled = on;
        }
    }

    // ========== OVERRIDABLE HOOKS ==========

    protected virtual void OnHoverEnter()
    {
        // custom hover enter
    }

    protected virtual void OnHoverExit()
    {
        // custom hover exit
    }

    protected virtual void OnSelectEnter()
    {
        // Click/ray selection is single-select. Lasso keeps its own multi-select path.
        if (promptedWorldManager != null)
        {
            highlightLatched = promptedWorldManager.TogglePrimarySelection(this.gameObject);
        }
        else
        {
            highlightLatched = !highlightLatched;
        }

        UpdateHighlightVisual();
    }

    protected virtual void OnSelectExit()
    {
        // Do not change selection here; click toggling is enough.
    }

    // ========= PROMPT LOG PUBLIC API =========

    public string BeginPromptLog(string prompt, string mode, string model)
    {
        var entry = new PromptLogEntry
        {
            id = Guid.NewGuid().ToString("N"),
            timestampIso = DateTime.UtcNow.ToString("o"),
            objectName = gameObject.name,
            prompt = prompt ?? "",
            mode = mode ?? "",
            model = model ?? "",
            succeeded = false,
            durationSec = 0f,
            inputTokens = 0,
            outputTokens = 0,
            luaHash = "",
            notes = ""
        };
        _promptLog.Add(entry);
        OnPromptLogUpdated?.Invoke(entry);
        return entry.id;
    }

    public void CompletePromptLogSuccess(string id, string luaAppliedText, float durationSec, int inputTokens = 0, int outputTokens = 0)
    {
        var e = FindEntry(id);
        if (e == null) return;

        e.succeeded = true;
        e.durationSec = durationSec;
        e.inputTokens = inputTokens;
        e.outputTokens = outputTokens;
        e.luaHash = ShortHash(luaAppliedText);
        OnPromptLogUpdated?.Invoke(e);
    }

    public void CompletePromptLogFailure(string id, string errorMessage, float durationSec)
    {
        var e = FindEntry(id);
        if (e == null) return;

        e.succeeded = false;
        e.durationSec = durationSec;
        e.notes = errorMessage ?? "Unknown error";
        OnPromptLogUpdated?.Invoke(e);
    }

    public void ClearPromptLog()
    {
        _promptLog.Clear();
        OnPromptLogUpdated?.Invoke(null);
    }

    private PromptLogEntry FindEntry(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        return _promptLog.Find(x => x.id == id);
    }

    private static string ShortHash(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        unchecked
        {
            int h = 23;
            for (int i = 0; i < text.Length; i++)
                h = h * 31 + text[i];
            return h.ToString("X8");
        }
    }

    // ========== Proximity / misc ==========

    private void ProximityTouchingDetection()
    {
        var selfPos = transform.position;

        bool leftClose = userLeftHand != null &&
                         Vector3.Distance(userLeftHand.position, selfPos) < Touchingdistance;
        bool rightClose = userRightHand != null &&
                          Vector3.Distance(userRightHand.position, selfPos) < Touchingdistance;

        isToching = leftClose || rightClose;

        if (isToching && !_prevTouching) onProximityEnter?.Invoke();
        if (!isToching && _prevTouching) onProximityExit?.Invoke();
        _prevTouching = isToching;
    }

    public float GetUserHeadDistance()
    {
        Transform anchor = userHead != null ? userHead : userRoot;
        return anchor != null
            ? Vector3.Distance(anchor.position, transform.position)
            : float.PositiveInfinity;
    }

    public bool IsUserClose(float distance)
    {
        return GetUserHeadDistance() <= Mathf.Max(0.001f, distance);
    }

    public void changeColor(Color color)
    {
        if (isRealObject)
        {
            realOutlineColor = color;
            ApplyOutlineColor();
            SetOutline(highlightLatched || _selected || _hovering);
            return;
        }

        if (ShapeRenderer != null)
        {
            ShapeRenderer.material.color = color;
        }
    }

    public void setLabel(string label)
    {
        if (TextBox != null) TextBox.text = label;
    }

    public void setImage(Texture texture)
    {
        if (Objimage == null) return;
        Objimage.gameObject.SetActive(true);
        Objimage.texture = texture;
        Objimage.color = Color.white;
    }
}
