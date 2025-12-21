using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

public class SimplePanelMouseDetector : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerDownHandler,
    IPointerUpHandler,
    IPointerClickHandler,
    IPointerMoveHandler
{
    [Header("Optional label to show the state")]
    public TMP_Text statusLabel;

    private bool _isInside = false;
    private bool _isPressed = false;

    private void SetStatus(string text)
    {
        if (statusLabel != null)
        {
            statusLabel.text = text;
        }

        Debug.Log("[SimplePanelMouseDetector] " + text);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _isInside = true;
        SetStatus($"Enter | pos: {eventData.position}");
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _isInside = false;
        _isPressed = false;
        SetStatus($"Exit | pos: {eventData.position}");
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        _isPressed = true;
        SetStatus($"Down | pos: {eventData.position}");
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        _isPressed = false;
        SetStatus($"Up | pos: {eventData.position}");
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        SetStatus($"Click | pos: {eventData.position}");
    }

    public void OnPointerMove(PointerEventData eventData)
    {
        if (_isInside)
        {
            SetStatus($"Move | pos: {eventData.position} | pressed: {_isPressed}");
        }
    }
}
