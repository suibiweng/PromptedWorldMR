// ActivePack Library
// Copyright (C) BigBlit Assets Michal Kalinowski
// http://bigblit.fun
//

using UnityEngine;

namespace BigBlit.ActivePack.Buttons
{
    /// <summary> 
    /// Toggleable Button. Implements toggle on/toggle of behaviour.
    /// </summary>
    [BehaviourInfo("Toggleable Button. Implements toggle on/toggle of behaviour.\n" +
        "Use it if you want more advanced button that can be toggled on/off. Works with Press controller and ButtonSwitch behaviour.")]
    public class ToggleButton : ClickButton, IToggleable
    {
        #region FIELDS AND PROPERTIES
        public enum ToggleTrigger { Off, Click, LongClick, Both }

        /// <summary>The event that causes the button to be toggled on.</summary>
        [Tooltip("The event that causes the button to be toggled on.")]
        [SerializeField] ToggleTrigger toggleOnTrigger = ToggleTrigger.Both;

        /// <summary>The event causes the button to be toggled off.</summary>
        [Tooltip("The event that causes the button to be toggled off.")]
        [SerializeField] ToggleTrigger toggleOffTrigger = ToggleTrigger.Both;

        /// <summary>The position of the button when it is toggled on.</summary>
        [Tooltip("The position of the button when it is toggled on.")]
        [SerializeField, Range(0.0f, 1.0f)] float toggleOnPos = 0.75f;

        [SerializeField] bool _defaultToggledOn;

        private bool _isToggledOn;
        public bool IsToggledOn => _isToggledOn;

        public event ToggleEvent toggleOnEvent;
        public event ToggleEvent toggleOffEvent;


        public override float Value {
            get {
                if (_isToggledOn) {
                    if (IsPressed)
                        return base.Value;
                    return toggleOnPos;
                }
                return base.Value;
            }

            set {
                if (value < 0.5f) {
                    if (_isToggledOn)
                        ToggleOff();
                }
                else if (!_isToggledOn)
                    ToggleOn();
            }

        }

        #endregion

        #region UNITY EVENTS 

        protected override void Awake() {
            base.Awake();

            if (_defaultToggledOn)
                ToggleOn();
        }

        #endregion

        #region PUBLIC METHODS
        public void ToggleOn() {
            if (_isToggledOn)
                return;
            _isToggledOn = true;
            OnToggleOn();
            RaiseValueChangeEvent();
        }

        public void ToggleOff() {
            if (!_isToggledOn)
                return;
            _isToggledOn = false;
            OnToggleOff();
            RaiseValueChangeEvent();
        }

        public void Toggle() {
            _isToggledOn = !_isToggledOn;
            if (_isToggledOn) {
                OnToggleOn();
            }
            else {
                OnToggleOff();
            }
            RaiseValueChangeEvent();
        }
        #endregion

        #region PROTECTED FUNCTIONS
        protected override void OnClick() {
            tryToggle(ToggleTrigger.Click);
            base.OnClick();
        }

        protected override void OnLongClick() {
            tryToggle(ToggleTrigger.LongClick);
            base.OnLongClick();
        }

        protected virtual void OnToggleOn() {
            toggleOnEvent?.Invoke(this);
        }

        protected virtual void OnToggleOff() {
            toggleOffEvent?.Invoke(this);
        }
        #endregion

        #region PRIVATE FUNCTIONS

        private void tryToggle(ToggleTrigger toggleTrigger) {
            if (_isToggledOn) {
                if (toggleOffTrigger == toggleTrigger || toggleOffTrigger == ToggleTrigger.Both) {
                    _isToggledOn = false;
                    OnToggleOff();
                }
            }
            else if (toggleOnTrigger == toggleTrigger || toggleOnTrigger == ToggleTrigger.Both) {
                _isToggledOn = true;
                OnToggleOn();
            }

        }

        #endregion
    }
}