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
    /// Pointer controller for all pressable objects.
    /// NOTICE: The controller is based on Unity Event System. 
    /// For it to work please make sure that:
    /// - You have unity Event System configured.
    /// - Camera has PhysicsRaycaster component added.
    /// - PhysicsRaycaster EventMask layer and ActiveObjects layers are properly set.
    /// </summary>
    [BehaviourInfo("Pointer controller for all pressable objects. (Based on Unity Event System).\n" +
        "Use it if you want to work with simple pressed/not pressed gesture.")]
    public class PointerPressController : PressControllerBase, IPointerDownHandler, IPointerUpHandler
    {
        #region FIELDS AND PROPERTIES

        [SerializeField] bool _useMouse = true;

        /// <summary>The mouse button that will trigger the events.</summary>
        [Tooltip("The mouse button that will trigger the events.")]
        public InputButton _mouseButton;

        [SerializeField] bool _useTouch = true;

#pragma warning disable CS0414
        [SerializeField] bool _useTracked = true;
#pragma warning restore  CS0414

        /// <summary>Keyboard button that must be pressed in addition to the pointer.</summary>
        [Tooltip("Keyboard button that must be pressed in addition to the pointer.")]
        [SerializeField] APKeyCode _modifier = APKeyCode.None;

        private IPressable _target;

        #endregion 

        #region UNITY EVENTS
        protected override void Reset() {
            base.Reset();

            _modifier = APKeyCode.None;
        }
        #endregion

        #region ES INTERFACES IMPLEMENTATION
        public void OnPointerDown(PointerEventData eventData) {

            if (checkPointer(eventData)) {
                foreach (var target in Targets)
                    target.Press();

            }
        }

        public void OnPointerUp(PointerEventData eventData) {

            if (checkPointer(eventData)) {
                foreach (var target in Targets)
                    target.Normal();

            }
        }

        #endregion

        #region PRIVATE METHODS

        protected bool checkPointer(PointerEventData eventData) {
            bool modflag = (_modifier == APKeyCode.None || ActivePackCommon.GetKey(_modifier));

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

        #endregion
    }
}