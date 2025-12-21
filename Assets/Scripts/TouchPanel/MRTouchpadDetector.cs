using System;
using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

public class MRTouchpadDetector : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerDownHandler,
    IPointerUpHandler,
    IPointerClickHandler,
    IPointerMoveHandler,
    IDragHandler
{
    [Header("Optional UI output")]
    public TMP_Text statusLabel;

    [Header("Optional custom rect")]
    [Tooltip("If null, uses this RectTransform.")]
    public RectTransform touchpadRect;

    // Generic event: all phases
    public event Action<TouchpadEvent> OnTouchpadEvent;

    // Convenience events per phase
    public event Action<TouchpadEvent> OnTouchpadEnter;
    public event Action<TouchpadEvent> OnTouchpadExit;
    public event Action<TouchpadEvent> OnTouchpadDown;
    public event Action<TouchpadEvent> OnTouchpadUp;
    public event Action<TouchpadEvent> OnTouchpadClick;
    public event Action<TouchpadEvent> OnTouchpadMove;
    public event Action<TouchpadEvent> OnTouchpadDrag;

    private RectTransform _rect;
    private bool _isInside;
    private bool _isPressed;

    private void Awake()
    {
        _rect = touchpadRect != null ? touchpadRect : transform as RectTransform;

        if (_rect == null)
        {
            Debug.LogError("MRTouchpadDetector must be on a RectTransform or have touchpadRect assigned.");
        }
    }

    private void SetStatus(string text)
    {
        if (statusLabel != null)
        {
            statusLabel.text = text;
        }
        Debug.Log("[MRTouchpadDetector] " + text);
    }

    // Utility: build event data and fire events
    private void RaiseEvent(TouchpadPhase phase, PointerEventData eventData)
    {
        if (_rect == null)
        {
            return;
        }

        Camera cam = eventData.pressEventCamera ?? eventData.enterEventCamera ?? Camera.main;

        Vector2 localPoint;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _rect,
                eventData.position,
                cam,
                out localPoint))
        {
            return;
        }

        Vector2 size = _rect.rect.size;
        float normX = (localPoint.x / size.x) + 0.5f;
        float normY = (localPoint.y / size.y) + 0.5f;

        Vector2 normalized = new Vector2(
            Mathf.Clamp01(normX),
            Mathf.Clamp01(normY)
        );

        var ev = new TouchpadEvent
        {
            Phase = phase,
            ScreenPosition = eventData.position,
            LocalPosition = localPoint,
            NormalizedPosition = normalized,
            IsInside = _isInside,
            IsPressed = _isPressed,
            RawEventData = eventData
        };

        // Generic event
        OnTouchpadEvent?.Invoke(ev);

        // Phase-specific events
        switch (phase)
        {
            case TouchpadPhase.Enter: OnTouchpadEnter?.Invoke(ev); break;
            case TouchpadPhase.Exit: OnTouchpadExit?.Invoke(ev); break;
            case TouchpadPhase.Down: OnTouchpadDown?.Invoke(ev); break;
            case TouchpadPhase.Up: OnTouchpadUp?.Invoke(ev); break;
            case TouchpadPhase.Click: OnTouchpadClick?.Invoke(ev); break;
            case TouchpadPhase.Move: OnTouchpadMove?.Invoke(ev); break;
            case TouchpadPhase.Drag: OnTouchpadDrag?.Invoke(ev); break;
        }

        // Optional debug label
        if (statusLabel != null)
        {
            statusLabel.text =
                $"Phase: {phase}\n" +
                $"Screen: {ev.ScreenPosition.x:F0}, {ev.ScreenPosition.y:F0}\n" +
                $"Local: {ev.LocalPosition.x:F1}, {ev.LocalPosition.y:F1}\n" +
                $"Norm: {ev.NormalizedPosition.x:F2}, {ev.NormalizedPosition.y:F2}\n" +
                $"Inside: {ev.IsInside}  Pressed: {ev.IsPressed}";
        }

        // Optional console log
        // SetStatus(statusLabel != null ? statusLabel.text : $"Phase: {phase} at {ev.ScreenPosition}");
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _isInside = true;
        RaiseEvent(TouchpadPhase.Enter, eventData);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _isInside = false;
        _isPressed = false;
        RaiseEvent(TouchpadPhase.Exit, eventData);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        _isPressed = true;
        RaiseEvent(TouchpadPhase.Down, eventData);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        _isPressed = false;
        RaiseEvent(TouchpadPhase.Up, eventData);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        RaiseEvent(TouchpadPhase.Click, eventData);
    }

    public void OnPointerMove(PointerEventData eventData)
    {
        if (_isInside && !_isPressed)
        {
            RaiseEvent(TouchpadPhase.Move, eventData);
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        RaiseEvent(TouchpadPhase.Drag, eventData);
    }
}
