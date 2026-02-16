// ActivePack Library
// Copyright (C) BigBlit Assets Michal Kalinowski
// http://bigblit.fun
//

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
    ///  Pointer Drag Controller for circular gesture.
    /// NOTICE: The controller is based on Unity Event System. 
    /// For it to work please make sure that:
    /// - You have unity Event System configured.
    /// - Camera has PhysicsRaycaster component added.
    /// - PhysicsRaycaster EventMask layer and ActiveObjects layers are properly set.
    /// </summary>
    [BehaviourInfo("Pointer Drag Controller for a circular gesture (Based on Unity Event System).\n" +
        "Use it if you want to work with more advanced circular-motion gestures.")]
    public class PointerCircularDragController : DragControllerBase,
        IInitializePotentialDragHandler, IBeginDragHandler, IEndDragHandler, IDragHandler
    {

        [SerializeField] bool _useMouse = true;

        /// <summary>The mouse button that will trigger the events.</summary>
        [Tooltip("The mouse button that will trigger the events.")]
        public InputButton _mouseButton;

        [SerializeField] bool _useTouch = true;
#pragma warning disable CS0414
        [SerializeField] bool _useTracked = true;
#pragma warning restore  CS0414

        #region FIELDS AND PROPERTIES

        /// <summary>Keyboard button that must be pressed in addition to the pointer.</summary>
        [Tooltip("Keyboard button that must be pressed in addition to the pointer.")]
        [SerializeField] APKeyCode _modifier = APKeyCode.None;

        /// <summary>Use drag threshold.</summary>
        [Tooltip("Use drag threshold.")]
        [SerializeField] bool _useDragThreshold = true;

        /// <summary>Pointer sensitivity in turns.More turns = less sensitivity</summary>
        [Tooltip("Pointer sensitivity expressed in turns. More turns = less sensitivity.")]
        [SerializeField] float _turns = 1.0f;

        /// <summary>Invert input.</summary>
        [Tooltip("Invert input.")]
        [SerializeField] bool _invert = false;

        /// <summary>Hide cursor while dragging.</summary>
        [Tooltip("Hide cursor on drag.")]
        [SerializeField] bool _hideCursor = false;

        /// <summary>Drag Center offset on game object local Z axis.</summary>
        [Tooltip(">Drag Center offset on game object local Z axis.")]
        [SerializeField] float _dragCenterZOffset = 0.0f;

        private Vector3 DragCenter => transform.TransformPoint(new Vector3(0.0f, 0.0f, _dragCenterZOffset));

        private Camera EventCamera => _eventCamera != null ? _eventCamera : Camera.main;

        private Camera _eventCamera = null;

        private bool _prevCursorVisible = false;

        private float _prevAngle = 0.0f;

        private bool _isDragging = false;

        #endregion
         
        #region UNITY EVENTS
        protected override void OnValidate() {
            base.OnValidate();

            _turns = Mathf.Max(0.01f, _turns);
        }

        void OnDrawGizmosSelected() {
            Gizmos.DrawSphere(DragCenter, 0.01f);
        }

        #endregion

        #region EVENT SYSTEM EVENTS
        public void OnInitializePotentialDrag(PointerEventData eventData) {
            eventData.useDragThreshold = _useDragThreshold;
        }

        public void OnBeginDrag(PointerEventData eventData) {
            if (!checkPointer(eventData, true))
                return;

            _isDragging = true;
            _eventCamera = eventData.pressEventCamera;
            _prevAngle = getMousePosAngle(eventData.position);

            _prevCursorVisible = Cursor.visible;
            if (_hideCursor)
                Cursor.visible = false;
            foreach (var target in Targets) {
                target.DragStart();
            }
        }

        public void OnDrag(PointerEventData eventData) {
            if (!_isDragging || !checkPointer(eventData, false))
                return;

            float angle = getMousePosAngle(eventData.position);
            float angleDelta = Mathf.DeltaAngle(angle, _prevAngle);


            _prevAngle = angle;

            Vector2 r = new Vector2(angleDelta / (360.0f * _turns), 0.0f);
            if (_invert)
                r.x = -r.x;

            foreach (var target in Targets) {
                target.Drag(r);
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

        private float getMousePosAngle(Vector2 mousePos) {

            Vector2 pivotPosition = EventCamera.WorldToScreenPoint(DragCenter);
            Vector2 vec = (mousePos - pivotPosition).normalized;
            return Mathf.Atan2(vec.y, vec.x) * Mathf.Rad2Deg;

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

            #endregion
        }
    }
}