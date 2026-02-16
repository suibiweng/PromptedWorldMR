using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using static UnityEngine.EventSystems.PointerEventData;

#if ENABLE_INPUT_SYSTEM
    using UnityEngine.InputSystem.UI;

    using APKeyCode = UnityEngine.InputSystem.Key;
#else
using APKeyCode = UnityEngine.KeyCode;
#endif

namespace BigBlit.ActivePack
{
    /// <summary>
    /// Pointer controller for all draggable objects.
    /// NOTICE: The controller is based on Unity Event System. 
    /// For it to work please make sure that:
    /// - You have unity Event System configured.
    /// - Camera has PhysicsRaycaster component added.
    /// - PhysicsRaycaster EventMask layer and ActiveObjects layers are properly set.
    /// </summary>
    ///     ///
    [BehaviourInfo("Basic Pointer controller for all draggable objects (Based on Unity Event System).\n" +
        "Use it if you want to work with mouse dragging gestures (like dragging a lever).")]
    public class PointerDragController : DragControllerBase,
        IInitializePotentialDragHandler, IBeginDragHandler, IEndDragHandler, IDragHandler
    {
        #region FIELDS AND PROPERTIES

        [SerializeField] bool _useMouse;

        /// <summary>The mouse button that will trigger the events.</summary>
        [Tooltip("The mouse button that will trigger the events.")]
        public InputButton _mouseButton;

        [SerializeField] bool _useTouch;

#pragma warning disable CS0414
        [SerializeField] bool _useTracked = true;
#pragma warning restore  CS0414

        /// <summary>Keyboard button that must be pressed in addition to the pointer.</summary>
        [Tooltip("Keyboard button that must be pressed in addition to the pointer.")]
        [SerializeField] APKeyCode _modifier = APKeyCode.None;


        /// <summary>Use drag threshold.</summary>
        [Tooltip("Use drag threshold.")]
        [SerializeField] bool _useDragThreshold = true;

        /// <summary>Pointer sensitivity</summary>
        [Tooltip("Pointer sensitivity.")]
        [SerializeField] float _sensitivity = 1.0f;

        /// <summary>Hide cursor while dragging.</summary>
        [Tooltip("Hide cursor on drag.")]
        [SerializeField] bool _hideCursor = false;

        public enum DragPlane { Right, Up, Forward };

        /// <summary>Local game object plane that should be used for drag delta calculation.</summary>
        [SerializeField] DragPlane _dragPlane = DragPlane.Forward;

        public enum DragAxis { Off, On, Inverted };

        /// <summary>Pointer delta on local X axis.</summary>
        [Tooltip("Pointer delta on local X axis.")]
        [SerializeField] DragAxis _xAxis = DragAxis.Off;

        /// <summary>Pointer delta on local Y axis.</summary>
        [Tooltip("Pointer delta on local Y axis.")]
        [SerializeField] DragAxis _yAxis = DragAxis.On;

        /// <summary>Pointer delta on local Z axis.</summary>
        [Tooltip("Pointer delta on local Z axis.")]
        [SerializeField] DragAxis _zAxis = DragAxis.Off;


        private Camera _eventCamera = null;

        private Camera EventCamera => _eventCamera != null ? _eventCamera : Camera.main;

        private bool _prevCursorVisible = false;

        private bool _isDragging;

        private Vector3 _dragCenter = Vector3.zero;

        private Plane _rayDirPlane = new Plane();

        private Vector3 _prevLocalPoint = Vector3.zero;

        private Vector3 _localDrag = Vector3.zero;

#endregion

#region UNITY EVENTS



        void OnDrawGizmosSelected() {
            if (!_isDragging)
                return;

            Vector3 _dragPoint = transform.TransformPoint(_localDrag);

            float dist = Vector3.Distance(_dragCenter, _dragPoint);

            if (_xAxis != DragAxis.Off) {
                Gizmos.color = Color.red;
                Gizmos.DrawSphere(_dragCenter + transform.TransformDirection(new Vector3(getValue(_xAxis, _localDrag.x), 0.0f, 0.0f)), 0.005f);
            }

            if (_yAxis != DragAxis.Off) {
                Gizmos.color = Color.green;
                Gizmos.DrawSphere(_dragCenter + transform.TransformDirection(new Vector3(0.0f, getValue(_yAxis, _localDrag.y), 0.0f)), 0.005f);
            }

            if (_zAxis != DragAxis.Off) {
                Gizmos.color = Color.blue;
                Gizmos.DrawSphere(_dragCenter + transform.TransformDirection(new Vector3(0.0f, 0.0f, getValue(_zAxis, _localDrag.z))), 0.005f);
            }
            Gizmos.color = Color.white;

            if (_dragPlane == DragPlane.Forward) {
                Vector3 a = _dragCenter + (-transform.right - transform.up) * dist;
                Vector3 b = _dragCenter + (-transform.right + transform.up) * dist;
                Vector3 c = _dragCenter + (transform.right + transform.up) * dist;
                Vector3 d = _dragCenter + (transform.right - transform.up) * dist;
                Gizmos.DrawLine(a, b);
                Gizmos.DrawLine(b, c);
                Gizmos.DrawLine(c, d);
                Gizmos.DrawLine(d, a);
            }
            else if (_dragPlane == DragPlane.Right) {
                Vector3 a = _dragCenter + (-transform.forward - transform.up) * dist;
                Vector3 b = _dragCenter + (-transform.forward + transform.up) * dist;
                Vector3 c = _dragCenter + (transform.forward + transform.up) * dist;
                Vector3 d = _dragCenter + (transform.forward - transform.up) * dist;
                Gizmos.DrawLine(a, b);
                Gizmos.DrawLine(b, c);
                Gizmos.DrawLine(c, d);
                Gizmos.DrawLine(d, a);
            }
            else if (_dragPlane == DragPlane.Up) {
                Vector3 a = _dragCenter + (-transform.forward - transform.forward) * dist;
                Vector3 b = _dragCenter + (-transform.right + transform.forward) * dist;
                Vector3 c = _dragCenter + (transform.right + transform.forward) * dist;
                Vector3 d = _dragCenter + (transform.right - transform.forward) * dist;
                Gizmos.DrawLine(a, b);
                Gizmos.DrawLine(b, c);
                Gizmos.DrawLine(c, d);
                Gizmos.DrawLine(d, a);
            }
        }

#endregion

#region EVENT SYSTEM EVENTS
        public void OnInitializePotentialDrag(PointerEventData eventData) {

            if (!checkPointer(eventData, true))
                return;

            eventData.useDragThreshold = _useDragThreshold;
            _eventCamera = eventData.pressEventCamera;
            _dragCenter = eventData.pointerCurrentRaycast.worldPosition;
        }

        public void OnBeginDrag(PointerEventData eventData) {

            if (!checkPointer(eventData, true))
                return;
           
            _isDragging = true;

            Ray ray = EventCamera.ScreenPointToRay(eventData.position);
            _rayDirPlane = new Plane(-ray.direction, _dragCenter);
            _prevLocalPoint = transform.InverseTransformPoint(_dragCenter);
            _localDrag = Vector3.zero;

            _prevCursorVisible = Cursor.visible;
            if (_hideCursor)
                Cursor.visible = false;

            foreach (var target in Targets)
                target.DragStart();

        }

        public void OnDrag(PointerEventData eventData) {

            if (!_isDragging || !checkPointer(eventData, false))
                return;

            Ray ray = EventCamera.ScreenPointToRay(eventData.position);
            _rayDirPlane.Raycast(ray, out var dist);
            Vector3 worldPoint = ray.GetPoint(dist);

            Vector3 planeNormal = transform.forward;
            if (_dragPlane == DragPlane.Right)
                planeNormal = transform.right;
            else if (_dragPlane == DragPlane.Up)
                planeNormal = transform.up;

            Plane dragPlane = new Plane(planeNormal, _dragCenter);
            worldPoint = dragPlane.ClosestPointOnPlane(worldPoint);

            Vector3 localPoint = transform.InverseTransformPoint(worldPoint);
            Vector3 localDelta = localPoint - _prevLocalPoint;
            _prevLocalPoint = localPoint;
            _localDrag += localDelta;

            foreach (var target in Targets) {
                target.Drag(new Vector3(getValue(_xAxis, -localDelta.x * _sensitivity),
                getValue(_yAxis, localDelta.y) * _sensitivity,
                getValue(_zAxis, -localDelta.z) * _sensitivity));
            }
        }

        public void OnEndDrag(PointerEventData eventData) {

            if (!_isDragging || !checkPointer(eventData, false))
                return;

            foreach (var target in Targets) {
                target.DragEnd();
            }

            _isDragging = false;
            _eventCamera = null;
            Cursor.visible = _prevCursorVisible;
        }

#endregion

#region PRIVATE METHODS

        private float getValue(DragAxis axisMode, float val) {
            if (axisMode == DragAxis.Off)
                return 0.0f;
            if (axisMode == DragAxis.On)
                return val;
            return -val;
        }

        protected bool checkPointer(PointerEventData eventData, bool checkModifier) {
            bool modflag = checkModifier || (_modifier == APKeyCode.None || ActivePackCommon.GetKey(_modifier));

#if ENABLE_INPUT_SYSTEM

            ExtendedPointerEventData e = eventData as ExtendedPointerEventData;
            if (e == null)
                return false;

            return (modflag && ((e.pointerType == UIPointerType.MouseOrPen && _useMouse && e.button == _mouseButton)
                || (e.pointerType == UIPointerType.Touch && _useTouch)
                || (e.pointerType == UIPointerType.Tracked && _useTracked)));

#else
            return modflag && ((eventData.pointerId < 0 && _useMouse && eventData.button == _mouseButton)
                || (eventData.pointerId >= 0 && _useTouch));

#endif

        }

    }

#endregion

}