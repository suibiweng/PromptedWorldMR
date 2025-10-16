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

    // Your existing QuickOutline component. We only enable/disable it.
    public Outline selectOutline;

    [Header("Interaction Source")]
    [Tooltip("Assign your RayInteractable (or any IInteractableView). We'll mirror its state.")]
    [SerializeField, Interface(typeof(IInteractableView))]
    private UnityEngine.Object _interactableViewObj;
    private IInteractableView _view;

    [Header("Highlight behavior")]
    [Tooltip("If true, outline also shows while hovering (when not latched).")]
    public bool highlightOnHover = false;

    [Tooltip("Latch/sticky highlight: toggled on each Select ENTER, stays until next Select ENTER.")]
    public bool stickyHighlightEnabled = true;

    [Header("User Anchors & Proximity")]
    public Transform userRoot; // reserved for future use
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

    // --- private state ---
    public bool _selected;
    public bool _hovering;
    public bool _prevTouching;
    public bool highlightLatched;                    // sticky highlight state
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

        if (userLeftHand == null && promptedWorldManager != null)
            userLeftHand = promptedWorldManager.userLeftHand;
        if (userRightHand == null && promptedWorldManager != null)
            userRightHand = promptedWorldManager.userRightHand;

        if (shape != null && selectOutline == null)
            selectOutline = shape.GetComponentInChildren<Outline>(includeInactive: true);

        SetOutline(false);
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
    }

    // ---------- Public API (kept from your original) ----------

    public bool hasLuaScript() => GetComponent<LuaBehaviour>() != null;

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

        // sync visuals
        UpdateHighlightVisual();
    }

    // ========== STATE HANDLING ==========

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

            // Latch/toggle sticky highlight on SELECT ENTER (if enabled)
            if (stickyHighlightEnabled)
                highlightLatched = !highlightLatched;

            onSelectEnter?.Invoke();
            OnSelectEnter();
        }
        else if (prevState == InteractableState.Select && newState != InteractableState.Select)
        {
            _selected = false;
            // We do NOT auto-clear the latch here; it stays until next Select ENTER

            onSelectExit?.Invoke();
            OnSelectExit();
        }

        UpdateHighlightVisual();
    }

    // Centralized highlight decision
    private void UpdateHighlightVisual()
    {
        bool show =
            (stickyHighlightEnabled && highlightLatched) ||       // latched wins
            (!stickyHighlightEnabled && _selected) ||             // classic: show when selected
            (!stickyHighlightEnabled && highlightOnHover && _hovering); // optional hover when not sticky

        SetOutline(show);
    }

    private void SetOutline(bool on)
    {
        if (selectOutline != null)
            selectOutline.enabled = on;
    }

    // ========== PROXIMITY ==========

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

    // ========== PUBLIC HELPERS FOR STICKY CONTROL ==========

    // Manually toggle the latched highlight (e.g., call from a UI button or another script)
    public void ToggleLatchedHighlight()
    {
        highlightLatched = !highlightLatched;
        UpdateHighlightVisual();
    }

    // Force on/off
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

    // ========== OPTIONAL OVERRIDABLE HOOKS ==========

    protected virtual void OnHoverEnter()
    {
        // Custom code on hover enter
    }

    protected virtual void OnHoverExit()
    {
        // Custom code on hover exit
    }

    protected virtual void OnSelectEnter()
    {
        // Keep your existing selection behavior
        if (promptedWorldManager != null)
        {
            promptedWorldManager.selectedObject = this.gameObject;
         //   FindAnyObjectByType<OpenAILuaGenerator>().AssignTarget(promptedWorldManager.selectedObject);
        }

        // Custom code on select enter (e.g., lua trigger)
        // if (TryGetComponent<LuaBehaviour>(out var lua)) lua.Trigger();
    }

    protected virtual void OnSelectExit()
    {
        // Custom code on select exit
    }

    // ========= PROMPT LOG PUBLIC API =========

    /// <summary>
    /// Call when a prompt starts; returns log entry id (GUID).
    /// </summary>
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

    /// <summary>
    /// Call on success to finalize the entry.
    /// </summary>
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

    /// <summary>
    /// Call on failure to finalize the entry with an error note.
    /// </summary>
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

    // ========= PROMPT LOG INTERNALS =========

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
}
