using UnityEngine;

public class TouchpadInputState : MonoBehaviour
{
    [Header("Source")]
    [Tooltip("Touchpad detector that feeds this state.")]
    public MRTouchpadDetector detector;

    [Header("Debug (read-only at runtime)")]
    public Vector2 normalizedPosition; // 0..1 inside the pad
    public bool isInside;
    public bool isPressed;
    public bool isDragging;
    public TouchpadPhase phase;

    private void OnEnable()
    {
        if (detector != null)
        {
            detector.OnTouchpadEvent += HandleTouchpadEvent;
        }
    }

    private void OnDisable()
    {
        if (detector != null)
        {
            detector.OnTouchpadEvent -= HandleTouchpadEvent;
        }
    }

    private void HandleTouchpadEvent(TouchpadEvent ev)
    {
        normalizedPosition = ev.NormalizedPosition;
        isInside = ev.IsInside;
        isPressed = ev.IsPressed;
        isDragging = (ev.Phase == TouchpadPhase.Drag);
        phase = ev.Phase;
    }
}
