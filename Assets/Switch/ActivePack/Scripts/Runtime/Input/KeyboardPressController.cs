// ActivePack Library
// Copyright (C) BigBlit Assets Michal Kalinowski
// http://bigblit.fun
//

using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
using static UnityEngine.EventSystems.PointerEventData;
using APKeyCode = UnityEngine.InputSystem.Key;
#else
using APKeyCode = UnityEngine.KeyCode;
#endif

namespace BigBlit.ActivePack
{
    /// <summary>
    /// Keyboard controller for all pressable objects.
    /// </summary>
    [BehaviourInfo("Keyboard controller for all pressable objects\n" +
    "Use it if you want to press/unpress IPressable behaviour with a keyboard.")]
    public class KeyboardPressController : PressControllerBase
    {
        #region FIELDS AND PROPERTIES

        [SerializeField] APKeyCode keyCode = APKeyCode.None;

        #endregion


        #region UNITY EVENTS
        private void Update() {
            if (ActivePackCommon.GetKeyDown(keyCode)) {
                foreach (var target in Targets) {
                    target.Press();
                }
            }
            else if (ActivePackCommon.GetKeyUp(keyCode)) {
                foreach (var target in Targets) {
                    target.Normal();
                }
            }
        }

        #endregion


    }
}