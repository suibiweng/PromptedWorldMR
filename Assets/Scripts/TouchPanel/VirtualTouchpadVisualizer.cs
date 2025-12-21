using System.Collections.Generic;
using Oculus.Interaction;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

public class VirtualTouchpadVisualizer : MonoBehaviour
{
    [Header("Touchpad UI (optional for cursor)")]
    public RectTransform touchpadArea;
    public RectTransform cursor;
    public Image cursorImage;

    [Header("Log Label")]
    public TMP_Text logLabel;

    [Header("Which canvas? (optional for cursor)")]
    public Canvas canvas;

    [Header("Behavior")]
    public bool singlePointerOnly = true;
    public int maxLogLines = 0; // 0 = only latest line

    private readonly Dictionary<int, PointableCanvasModule.Pointer> _activePointers =
        new Dictionary<int, PointableCanvasModule.Pointer>();

    private int? _currentPointerId = null;
    private readonly List<string> _logLines = new List<string>();

    private void OnEnable()
    {
        PointableCanvasModule.WhenPointerStarted += HandlePointerStarted;
    }

    private void OnDisable()
    {
        PointableCanvasModule.WhenPointerStarted -= HandlePointerStarted;

        foreach (var kvp in _activePointers)
        {
            kvp.Value.WhenUpdated -= HandlePointerUpdated;
            kvp.Value.WhenDisposed -= HandlePointerDisposed;
        }

        _activePointers.Clear();
        _currentPointerId = null;
    }

    private void HandlePointerStarted(PointableCanvasModule.Pointer pointer)
    {
        Debug.Log($"[VirtualTouchpad] Pointer started: id={pointer.Identifier}");

        if (singlePointerOnly && _currentPointerId.HasValue)
        {
            return;
        }

        if (!_activePointers.ContainsKey(pointer.Identifier))
        {
            _activePointers.Add(pointer.Identifier, pointer);
            pointer.WhenUpdated += HandlePointerUpdated;
            pointer.WhenDisposed += HandlePointerDisposed;

            if (!_currentPointerId.HasValue)
            {
                _currentPointerId = pointer.Identifier;
            }

            // Optional: show that we got a pointer
            if (logLabel != null && maxLogLines == 0)
            {
                logLabel.text = $"Pointer started: {pointer.Identifier}";
            }
        }
    }

    private void HandlePointerDisposed()
    {
        // no id here; cleanup can be done via NotifyPointerDisposed(int id) if needed
    }

    private void HandlePointerUpdated(PointerEventData eventData)
    {
        int id = eventData.pointerId;

        if (singlePointerOnly && _currentPointerId.HasValue && id != _currentPointerId.Value)
        {
            return;
        }

        bool isPressed = eventData.pointerPress != null || eventData.dragging;
        bool isDragging = eventData.dragging;

        // Build log line and ALWAYS write it if we have a label
        string line =
            $"id:{id} | x:{eventData.position.x:F1}, y:{eventData.position.y:F1} | " +
            $"pressed:{isPressed} | dragging:{isDragging}";

        if (logLabel != null)
        {
            if (maxLogLines > 0)
            {
                _logLines.Add(line);
                while (_logLines.Count > maxLogLines)
                {
                    _logLines.RemoveAt(0);
                }
                logLabel.text = string.Join("\n", _logLines);
            }
            else
            {
                logLabel.text = line;
            }
        }

        // Optional cursor move
        if (canvas != null && touchpadArea != null && cursor != null)
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                touchpadArea,
                eventData.position,
                canvas.worldCamera,
                out Vector2 localPoint))
            {
                cursor.anchoredPosition = localPoint;
            }
        }

        // Optional cursor color
        if (cursorImage != null)
        {
            if (isDragging)
            {
                cursorImage.color = Color.red;
            }
            else if (isPressed)
            {
                cursorImage.color = Color.yellow;
            }
            else
            {
                cursorImage.color = Color.white;
            }
        }

        // Extra debug if you want to see it in the console too
        // Debug.Log("[VirtualTouchpad] " + line);
    }

    public void NotifyPointerDisposed(int id)
    {
        if (_activePointers.TryGetValue(id, out var pointer))
        {
            pointer.WhenUpdated -= HandlePointerUpdated;
            pointer.WhenDisposed -= HandlePointerDisposed;
            _activePointers.Remove(id);
        }

        if (_currentPointerId == id)
        {
            _currentPointerId = null;
            if (_activePointers.Count > 0)
            {
                foreach (var kvp in _activePointers)
                {
                    _currentPointerId = kvp.Key;
                    break;
                }
            }
        }
    }
}
