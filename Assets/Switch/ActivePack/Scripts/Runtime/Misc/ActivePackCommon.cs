using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

using APKeyCode = UnityEngine.InputSystem.Key;
#else
using APKeyCode = UnityEngine.KeyCode;
#endif


namespace BigBlit.ActivePack
{ 
    public static class ActivePackCommon
    {

        public static bool GetKey(APKeyCode key) {
#if ENABLE_INPUT_SYSTEM
                return Keyboard.current[key].isPressed;
#else
            return Input.GetKey(key);
       
#endif
        }
         
        public static bool GetKeyDown(APKeyCode key) {

#if ENABLE_INPUT_SYSTEM

            return Keyboard.current[key].wasPressedThisFrame;
#else
            return Input.GetKeyDown(key);
       
#endif
        }

        public static bool GetKeyUp(APKeyCode key) {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current[key].wasReleasedThisFrame;
#else
            return Input.GetKeyDown(key);
       
#endif
        }


    }
}