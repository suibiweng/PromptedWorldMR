// ActivePack Library
// Copyright (C) BigBlit Assets Michal Kalinowski
// http://bigblit.fun
//

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;

namespace BigBlit.ActivePack.Buttons
{
    /// <summary> 
    /// Implements a button switch behavior that toggles the state of the toggle buttons. 
    /// </summary>
    [BehaviourInfo("Implements a button switch behavior that toggles the state of the toggle buttons.\n +" +
        "Use it to drive/controlls toggle buttons states so they work as a group (ex. If one is on, others are off)")]
    public class ButtonSwitch : MonoBehaviour
    {
        #region FIELDS AND PROPERTIES
        /// <summary> Toggle buttons that are part of the switch.</summary>
        [SerializeField] List<ToggleButton> _buttons = new List<ToggleButton>();

        public ToggleButton ToggledButton => _toggledButton;

        private ToggleButton _toggledButton = null;

        #endregion

        #region UNITY EVENTS

        private void Start() {
            foreach (var button in _buttons) {
                button.toggleOnEvent += Button_toggleOnEvent;
                button.toggleOffEvent += Button_toggleOffEvent;
            }

            foreach (var button in _buttons) {
                if (button.IsToggledOn) {
                    if (_toggledButton == null) {
                        _toggledButton = button;
                    }
                    else {
                        button.ToggleOff();
                    }
                }         
            }
        }

        private void OnDestroy() {
            foreach (var button in _buttons) {
                button.toggleOnEvent -= Button_toggleOnEvent;
                button.toggleOffEvent -= Button_toggleOffEvent;
            }
        }

        #endregion

        #region IMPLEMENTATION
        private void Button_toggleOnEvent(IToggleable toggleable) {
            ToggleButton newToggled = (ToggleButton)toggleable;
            if (_toggledButton == newToggled)
                return;
            _toggledButton?.ToggleOff();
            _toggledButton = newToggled;
        }

        private void Button_toggleOffEvent(IToggleable toggleable) {
            if (_toggledButton == (ToggleButton)toggleable)
                _toggledButton = null;
        }

        #endregion
    }
}
