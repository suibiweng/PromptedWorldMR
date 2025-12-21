using UnityEngine;
using UnityEngine.EventSystems;

public enum TouchpadPhase
{
    Enter,
    Exit,
    Down,
    Up,
    Click,
    Move,
    Drag
}

public struct TouchpadEvent
{
    public TouchpadPhase Phase;
    public Vector2 ScreenPosition;      // eventData.position (pixels)
    public Vector2 LocalPosition;       // in RectTransform space (center = 0,0)
    public Vector2 NormalizedPosition;  // 0..1 inside rect
    public bool IsInside;
    public bool IsPressed;

    public PointerEventData RawEventData; // if listeners need full PointerEventData
}
