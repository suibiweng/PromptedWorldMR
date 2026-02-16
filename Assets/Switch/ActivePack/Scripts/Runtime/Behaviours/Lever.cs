// ActivePack Library
// Copyright (C) BigBlit Assets Michal Kalinowski
// http://bigblit.fun
//

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BigBlit.ActivePack
{
    /// <summary> 
    /// Basic Lever. Implements draggable/pressable lever mechanism. 
    /// </summary>
    [BehaviourInfo("Basic Lever. Implements draggable/pressable lever mechanism. " +
   "Use it if you want lever-like behaviour. Works with Press and Drag controller.")]
    public class Lever : ActiveBehaviour, IDraggable, IValueable, IToggleable, IPressable, IDisable
    {

        #region FIELDS AND PROPERTIES

        public event DragEvent dragStartEvent;
        public event DragEvent dragEvent;
        public event DragEvent dragEndEvent;
        public event ValueChangeEvent valueChangeEvent;

        public event ToggleEvent toggleOnEvent;
        public event ToggleEvent toggleOffEvent;

        public event PressEvent pressedEvent;
        public event PressEvent normalEvent;

        public float Value {
            get => _value;
            set {
                _value = Mathf.Clamp(value, 0.0f, 1.0f);
                valueChangeEvent?.Invoke(this);
            }
        }

        public bool IsToggledOn => _isToggledOn;

        public bool IsPressed => _isPressed;
        public float PressTime => _pressTime;

       
        #region IDisable

        public event DisableEvent disableChangedEvent;

#if UNITY_EDITOR
        private bool _prevIsDisabled;
#endif

        public bool IsDisabled {
            get => _isDisabled;
            set {
                if (_isDisabled != value)
                    _isDisabled = false;
                disableChangedEvent?.Invoke(this);
#if UNITY_EDITOR
                _prevIsDisabled = _isDisabled;
#endif
            }
        }

        #endregion

        [SerializeField] private float _toggleInValue = 1.0f;
        [SerializeField] private bool _springed = true;
        [SerializeField] private float _value = 0.0f;
        [SerializeField] private bool _isDisabled = false;

        private bool _isToggledOn = false;

        private bool _isPressed = false;
        private float _pressTime = 0.0f;


        #endregion

        #region UNITY EVENTS

        protected override void OnValidate() {

            if (_springed)
                updateToggleValue();
#if UNITY_EDITOR
            if (_prevIsDisabled != _isDisabled) {
                _prevIsDisabled = _isDisabled;
                disableChangedEvent?.Invoke(this);
            }
#endif
        }

        #endregion

        #region IDraggable

        public void DragStart() {
            if (_isDisabled)
                return;

            dragStartEvent?.Invoke(this);
        }

        public void Drag(Vector3 delta) {
            if (_isDisabled)
                return;

            Value += (delta.x + delta.y + delta.z);

            dragEvent?.Invoke(this);

            if (!_isToggledOn && _value >= _toggleInValue)
                setToggle(true);
            else if (_isToggledOn && _value < _toggleInValue)
                setToggle(false);

        }

        public void DragEnd() {

            if (_springed)
                updateToggleValue();

            dragEndEvent?.Invoke(this);
        }

        #endregion

        #region IToggleable

        public void ToggleOn() {
            setToggle(true);
            updateToggleValue();
        }

        public void ToggleOff() {
            setToggle(false);
            updateToggleValue();
        }

        public void Toggle() {
            setToggle(!_isToggledOn);
            updateToggleValue();
        }

        #endregion

        #region IPressable

        public void Press() {

            if (_isDisabled)
                return;

            _pressTime = Time.time;
            _isPressed = true;

            pressedEvent?.Invoke(this);

            Toggle();
        }

        public void Normal() {
            _pressTime = 0.0f;
            _isPressed = false;

            normalEvent?.Invoke(this);
        }

        #endregion

        #region PRIVATE METHODS

        private void updateToggleValue() {
            if (_isToggledOn)
                Value = 1.0f;
            else
                Value = 0.0f;

            valueChangeEvent?.Invoke(this);
        }

        void setToggle(bool isToggleOn) {
            if (_isToggledOn == isToggleOn)
                return;

            _isToggledOn = isToggleOn;
            if (_isToggledOn)
                toggleOnEvent?.Invoke(this);
            else
                toggleOffEvent?.Invoke(this);
        }

        #endregion
    }
}
