using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Events;
using Oculus.Interaction;   // IInteractableView / InteractableState
using PromptedWorld;        // PromptedWorldManager

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
            // ---- Hover Enter hook ----
            // TODO: put your hover-enter code here
            // e.g., Debug.Log("Hover ENTER");
            onHoverEnter?.Invoke();
            OnHoverEnter(); // overridable block
        }
        else if (prevState == InteractableState.Hover && newState != InteractableState.Hover)
        {
            _hovering = false;
            // ---- Hover Exit hook ----
            // TODO: put your hover-exit code here
            // e.g., Debug.Log("Hover EXIT");
            onHoverExit?.Invoke();
            OnHoverExit(); // overridable block
        }

        // Select transitions
        if (prevState != InteractableState.Select && newState == InteractableState.Select)
        {
            _selected = true;
            // Latch/toggle sticky highlight on SELECT ENTER (if enabled)
            if (stickyHighlightEnabled)
                highlightLatched = !highlightLatched;

            // ---- Select Enter hook ----
            // TODO: put your select-enter code here
            // e.g., play sound, spawn UI, run Lua
            onSelectEnter?.Invoke();
            OnSelectEnter(); // overridable block
        }
        else if (prevState == InteractableState.Select && newState != InteractableState.Select)
        {
            _selected = false;
            // We do NOT auto-clear the latch here; it stays until next Select ENTER

            // ---- Select Exit hook ----
            // TODO: put your select-exit code here
            onSelectExit?.Invoke();
            OnSelectExit(); // overridable block
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
        // TODO: drop custom code here if you inherit this class
        // Example: Debug.Log("[ProgramableObject] Hover Enter");
    }

    protected virtual void OnHoverExit()
    {
        // TODO: drop custom code here
    }

    protected virtual void OnSelectEnter()
    {

        promptedWorldManager.selectedObject = this.gameObject;
        // TODO: drop custom code here
        // Example: run a Lua command:
        // if (luaBehaviour) luaBehaviour.Trigger();
    }

    protected virtual void OnSelectExit()
    {
        // TODO: drop custom code here
    }
}
