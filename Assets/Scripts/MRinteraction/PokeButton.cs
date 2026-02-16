using Oculus.Interaction;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
[RequireComponent(typeof(PokeInteractable))]
public class PokeButton : MonoBehaviour
{
    [Header("Button Events")]
    public UnityEvent onClick;
    public UnityEvent onPressed;
    public UnityEvent onReleased;

    [Header("Click Logic")]
    public bool clickOnPress = false;
    public float minPressTime = 0f;
    public float debounce = 0.12f;

    [Header("Optional Visual Press")]
    public Transform visualTarget;
    public float pressedLocalOffset = 0.01f;
    public float visualLerpSpeed = 18f;

    // ✅ Lua-visible state
    public bool IsPressed => _isPressed;
    public bool ToggleState => _toggleState;

    public bool WasPressedThisFrame => _pressedThisFrame;
    public bool WasReleasedThisFrame => _releasedThisFrame;

    private bool _toggleState;
    private bool _pressedThisFrame;
    private bool _releasedThisFrame;

    private PokeInteractable _poke;
    private bool _isPressed;
    private float _pressStartTime;
    private float _lastClickTime;

    private Vector3 _visualRestLocalPos;
    private Vector3 _visualPressedLocalPos;

    private void Awake()
    {
        _poke = GetComponent<PokeInteractable>();
        _poke.WhenPointerEventRaised += OnPointerEvent;

        if (visualTarget != null)
        {
            _visualRestLocalPos = visualTarget.localPosition;
            _visualPressedLocalPos =
                _visualRestLocalPos + new Vector3(0, 0, -Mathf.Abs(pressedLocalOffset));
        }
    }

    private void LateUpdate()
    {
        _pressedThisFrame = false;
        _releasedThisFrame = false;
    }

    private void Update()
    {
        if (visualTarget != null)
        {
            var target = _isPressed ? _visualPressedLocalPos : _visualRestLocalPos;
            visualTarget.localPosition = Vector3.Lerp(
                visualTarget.localPosition,
                target,
                1f - Mathf.Exp(-visualLerpSpeed * Time.deltaTime)
            );
        }
    }

    private void OnDestroy()
    {
        if (_poke != null) _poke.WhenPointerEventRaised -= OnPointerEvent;
    }

    private void OnPointerEvent(PointerEvent e)
    {
        switch (e.Type)
        {
            case PointerEventType.Select:
                HandlePressed();
                break;

            case PointerEventType.Unselect:
                HandleReleased();
                break;
        }
    }

    private void HandlePressed()
    {
        if (_isPressed) return;

        _isPressed = true;
        _pressedThisFrame = true;
        _pressStartTime = Time.time;

        onPressed?.Invoke();

        if (clickOnPress)
            TryClick();
    }

    private void HandleReleased()
    {
        if (!_isPressed) return;

        _releasedThisFrame = true;

        if (!clickOnPress)
        {
            var held = Time.time - _pressStartTime;
            if (held >= minPressTime)
                TryClick();
        }

        _isPressed = false;
        onReleased?.Invoke();
    }

    private void TryClick()
    {
        if (Time.time - _lastClickTime < debounce) return;

        _lastClickTime = Time.time;

        // ✅ toggle mode built-in
        _toggleState = !_toggleState;

        onClick?.Invoke();
    }
}
