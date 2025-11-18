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
    [Tooltip("Fire onClick when finger SELECTS (press) instead of on release.")]
    public bool clickOnPress = false;

    [Tooltip("Minimum time (s) the press must be held before a release counts as a click.")]
    public float minPressTime = 0f;

    [Tooltip("Ignore subsequent clicks within this time window (s).")]
    public float debounce = 0.12f;

    [Header("Optional Visual Press")]
    [Tooltip("Optional visual to move on press (e.g., the button face). Leave null to disable.")]
    public Transform visualTarget;

    [Tooltip("Local -Z offset applied while pressed (like a physical travel).")]
    public float pressedLocalOffset = 0.01f;

    [Tooltip("How quickly the visual eases to its target (higher is snappier).")]
    public float visualLerpSpeed = 18f;

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
            _visualPressedLocalPos = _visualRestLocalPos + new Vector3(0f, 0f, -Mathf.Abs(pressedLocalOffset));
        }
    }

    private void OnDestroy()
    {
        if (_poke != null) _poke.WhenPointerEventRaised -= OnPointerEvent;
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

    private void OnPointerEvent(PointerEvent e)
    {
        switch (e.Type) // e.Type is PointerEventType
        {
            case PointerEventType.Select:
                HandlePressed();
                break;

            case PointerEventType.Unselect:
                HandleReleased();
                break;

            // Optional: uncomment if you want hover feedback later
            // case PointerEventType.Hover: break;
            // case PointerEventType.Unhover: break;
            // case PointerEventType.Move: break;
        }
    }

    private void HandlePressed()
    {
        if (_isPressed) return;
        _isPressed = true;
        _pressStartTime = Time.time;
        onPressed?.Invoke();

        if (clickOnPress)
        {
            TryClick();
        }
    }

    private void HandleReleased()
    {
        if (!_isPressed) return;

        if (!clickOnPress)
        {
            var held = Time.time - _pressStartTime;
            if (held >= minPressTime)
            {
                TryClick();
            }
        }

        _isPressed = false;
        onReleased?.Invoke();
    }

    private void TryClick()
    {
        if (Time.time - _lastClickTime < debounce) return;
        _lastClickTime = Time.time;
        onClick?.Invoke();
    }
}
