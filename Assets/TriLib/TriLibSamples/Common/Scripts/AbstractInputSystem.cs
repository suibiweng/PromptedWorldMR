using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace TriLibCore.Samples
{
    /// <summary>
    /// Represents a base class to abstract input system actions.  
    /// This class automatically detects whether the old input system (UnityEngine.Input) 
    /// or the new input system (UnityEngine.InputSystem) is available, and delegates 
    /// input calls accordingly.
    /// </summary>
    public class AbstractInputSystem : MonoBehaviour
    {
        /// <summary>
        /// Holds the last distance between two touch points on mobile, 
        /// used to simulate a scroll/pinch gesture.
        /// </summary>
        private float _lastMultiTouchDistance;

        /// <summary>
        /// Gets whether a specified mouse button (or equivalent gesture on mobile) is currently pressed.  
        /// Returns true when the requested button is down in the current frame.
        /// </summary>
        /// <param name="index">
        /// The mouse button index:  
        /// <list type="bullet">
        /// <item>0 = Left Button</item>
        /// <item>1 = Right Button (legacy mode) / Middle Button (new input system)</item>
        /// <item>2 = Middle Button (legacy mode) / Right Button (new input system)</item>
        /// </list>
        /// Note: On some mobile devices with multi-touch, index=2 can be forced true when multiple touches are present.
        /// </param>
        /// <returns>
        /// True if the specified mouse button (or gesture) is currently pressed; otherwise, false.
        /// </returns>
        protected bool GetMouseButton(int index)
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                switch (index)
                {
                    case 0:
                        return Mouse.current.leftButton.isPressed;
                    case 1:
                        return Mouse.current.rightButton.isPressed;
                    case 2:
                        return Mouse.current.middleButton.isPressed;
                }
            }
            return false;
#else
            if (SystemInfo.deviceType == DeviceType.Handheld && Input.touchSupported && Input.touchCount > 1 && index == 2)
            {
                // On mobile, treat multi-touch as a "middle-mouse" press.
                return true;
            }
            return Input.GetMouseButton(index);
#endif
        }

        /// <summary>
        /// Gets whether a specified mouse button was pressed down this frame (or equivalent gesture on mobile).  
        /// Returns true only during the frame the user starts pressing the button/gesture.
        /// </summary>
        /// <param name="index">
        /// The mouse button index:  
        /// <list type="bullet">
        /// <item>0 = Left Button</item>
        /// <item>1 = Middle Button (legacy mode) / Right Button (new input system)</item>
        /// <item>2 = Right Button (legacy mode) / Middle Button (new input system)</item>
        /// </list>
        /// </param>
        /// <returns>
        /// True if the specified mouse button (or gesture) was pressed down during this frame; otherwise, false.
        /// </returns>
        protected bool GetMouseButtonDown(int index)
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                switch (index)
                {
                    case 0:
                        return Mouse.current.leftButton.wasPressedThisFrame;
                    case 1:
                        return Mouse.current.middleButton.wasPressedThisFrame;
                    case 2:
                        return Mouse.current.rightButton.wasPressedThisFrame;
                }
            }
            return false;
#else
            return Input.GetMouseButtonDown(index);
#endif
        }

        /// <summary>
        /// Retrieves the value of the specified axis (e.g., "Horizontal", "Vertical", "Mouse X", "Mouse Y") 
        /// using either the legacy or the new input system.  
        /// On mobile devices, touch movement is used to simulate mouse axis behavior.
        /// </summary>
        /// <param name="axisName">
        /// Name of the axis to retrieve. Common examples: "Horizontal", "Vertical", "Mouse X", "Mouse Y".
        /// </param>
        /// <returns>
        /// The current axis value, generally in the range [-1, 1], though mouse deltas and touch 
        /// movements may produce different ranges. Defaults to 0 if unsupported.
        /// </returns>
        protected float GetAxis(string axisName)
        {
#if ENABLE_INPUT_SYSTEM
            switch (axisName)
            {
                case "Mouse X":
                    return Mouse.current != null ? Mouse.current.delta.x.ReadValue() : 0f;
                case "Mouse Y":
                    return Mouse.current != null ? Mouse.current.delta.y.ReadValue() : 0f;
                case "Horizontal":
                    if (Keyboard.current == null)
                    {
                        return 0f;
                    }
                    return Keyboard.current.leftArrowKey.isPressed ? -1f :
                           Keyboard.current.rightArrowKey.isPressed ? 1f : 0f;
                case "Vertical":
                    if (Keyboard.current == null)
                    {
                        return 0f;
                    }
                    return Keyboard.current.downArrowKey.isPressed ? -1f :
                           Keyboard.current.upArrowKey.isPressed ? 1f : 0f;
                default:
                    return 0f;
            }
#else
            if (SystemInfo.deviceType == DeviceType.Handheld && Input.touchSupported)
            {
                if (Input.touchCount > 0)
                {
                    if (axisName == "Mouse X")
                    {
                        return Input.touches[0].deltaPosition.x * 0.05f;
                    }
                    if (axisName == "Mouse Y")
                    {
                        return Input.touches[0].deltaPosition.y * 0.05f;
                    }
                }
                return 0f;
            }
            return Input.GetAxis(axisName);
#endif
        }

        /// <summary>
        /// Determines if a given keyboard key is currently being pressed, 
        /// using either the new input system or the legacy system.
        /// </summary>
        /// <param name="keyCode">The Unity KeyCode to check (e.g., KeyCode.LeftAlt).</param>
        /// <returns>
        /// True if the specified key is pressed; otherwise, false.
        /// </returns>
        protected bool GetKey(KeyCode keyCode)
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                switch (keyCode)
                {
                    case KeyCode.LeftAlt:
                        return Keyboard.current.leftAltKey.isPressed;
                    case KeyCode.RightAlt:
                        return Keyboard.current.rightAltKey.isPressed;
                }
            }
            return false;
#else
            return Input.GetKey(keyCode);
#endif
        }

        /// <summary>
        /// Retrieves the current scroll delta on desktop or simulates it via pinch gestures on mobile.  
        /// On mobile devices with multi-touch, the difference in touch distances is used to emulate a scroll wheel.
        /// </summary>
        /// <returns>
        /// A <see cref="Vector2"/> representing the scroll delta.
        /// </returns>
        protected Vector2 GetMouseScrollDelta()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null ? Mouse.current.scroll.ReadValue() * 0.01f : default;
#else
            if (SystemInfo.deviceType == DeviceType.Handheld && Input.touchSupported && Input.touchCount == 2)
            {
                var firstTouch = Input.touches[0];
                var secondTouch = Input.touches[1];
                // If either touch is new, reset stored distance.
                if (firstTouch.phase == TouchPhase.Began || secondTouch.phase == TouchPhase.Began)
                {
                    _lastMultiTouchDistance = Vector2.Distance(firstTouch.position, secondTouch.position);
                }
                // Only calculate delta if both touches are actively moving.
                if (firstTouch.phase != TouchPhase.Moved || secondTouch.phase != TouchPhase.Moved)
                {
                    return Vector2.zero;
                }
                var newMultiTouchDistance = Vector2.Distance(firstTouch.position, secondTouch.position);
                var deltaMultiTouchDistance = newMultiTouchDistance - _lastMultiTouchDistance;
                _lastMultiTouchDistance = newMultiTouchDistance;
                // Returns the simulated scroll as a vertical delta.
                return new Vector2(0f, deltaMultiTouchDistance * 0.05f);
            }
            return Input.mouseScrollDelta;
#endif
        }
    }
}
