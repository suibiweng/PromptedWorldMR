using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

public class TouchpadLogger : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerDownHandler,
    IPointerUpHandler,
    IDragHandler,
    IPointerMoveHandler
{
    [Header("Output")]
    [Tooltip("TMP label that will display pointer info.")]
    public TMP_Text logLabel;

    [Header("Optional")]
    [Tooltip("If true, also log to Console with Debug.Log.")]
    public bool logToConsole = false;

    private bool _isInside = false;
    private bool _isPressed = false;

    private void Awake()
    {
        if (logLabel != null)
        {
            logLabel.text = "Touchpad ready (no pointer yet)";
        }
    }

    // Utility to update the label with current state
    private void UpdateLabel(PointerEventData eventData, string phase)
    {
        if (logLabel == null) return;

        // eventData.position is screen space
        float x = eventData.position.x;
        float y = eventData.position.y;

        string text =
            $"phase: {phase}\n" +
            $"id: {eventData.pointerId}\n" +
            $"screen x: {x:F1}, y: {y:F1}\n" +
            $"inside: {_isInside}\n" +
            $"pressed: {_isPressed}\n" +
            $"dragging: {eventData.dragging}";

        logLabel.text = text;

        if (logToConsole)
        {
            Debug.Log("[TouchpadLogger] " + text);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _isInside = true;
        UpdateLabel(eventData, "Enter");
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _isInside = false;
        _isPressed = false;
        UpdateLabel(eventData, "Exit");
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        _isPressed = true;
        UpdateLabel(eventData, "Down");
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        _isPressed = false;
        UpdateLabel(eventData, "Up");
    }

    public void OnDrag(PointerEventData eventData)
    {
        // Called while pressed and moving
        UpdateLabel(eventData, "Drag");
    }

    public void OnPointerMove(PointerEventData eventData)
    {
        // Called while hovering and moving (not dragging)
        if (!_isPressed) // to avoid spamming two phases at once
        {
            UpdateLabel(eventData, "Move");
        }
    }
}
