using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BigBlit.ActivePack.Buttons
{
    [RequireComponent(typeof(CharacterController))]
    public class FPSController : MonoBehaviour
    {

#if ENABLE_INPUT_SYSTEM
        [SerializeField] InputActionAsset _actions;
        [SerializeField] string _lookActionPath = "Player/Look";
        [SerializeField] string _useActionPath = "Player/LookEngage";
        [SerializeField] string _moveActionPath = "Player/Move";
        [SerializeField] string _pointerTogglePath = "Player/PointerToggle";
        [SerializeField] string _controllTogglePath = "Player/ControllerToggle";
        [SerializeField] string _runModifierPath = "Player/RunModifier";

        private InputAction _lookAction;
        private InputAction _useAction;
        private InputAction _moveAction;
        private InputAction _pointerToggleAction;
        private InputAction _controllToggleAction;
        private InputAction _runModifierAction;
#endif


        [SerializeField] Camera playerCamera = null;

        [Range(0.01f, 1.0f)]
        [SerializeField] float _smoothTime = 0.25f;
        [SerializeField] float walkingSpeed = 7.5f;
        [SerializeField] float runningSpeed = 11.5f;

        [SerializeField] float lookSpeed = 2.0f;
        [SerializeField] float lookXLimit = 45.0f;

        [SerializeField] bool _pointerEnable = true;
        [SerializeField] bool _controllerEnable = false;
        [SerializeField] bool _run = false;
        [SerializeField] bool softwareCursor = true;

        [SerializeField] Texture2D _cursorTexture = null;
        [SerializeField] Texture2D _cursorTextureDown = null;


        Vector2 _mouseRotValues;
        Vector2 _mouseSmootingVel;
        CharacterController characterController;
        Vector3 moveDirection = Vector3.zero;
        float rotationX = 0;

        private bool canMove = true;



        void Start() {
            characterController = GetComponent<CharacterController>();
#if ENABLE_INPUT_SYSTEM
            setupInputs();
#endif

        }

        private void Reset() {

        }

        private void OnEnable() {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (softwareCursor)
                Cursor.SetCursor(_cursorTexture, new Vector2(8.0f, 8.0f), CursorMode.Auto);
        }

        private void OnDisable() {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }

        void Update() {
            if (wasPointerPressed()) {
                if (softwareCursor)
                    Cursor.SetCursor(_cursorTextureDown, new Vector2(8.0f, 8.0f), CursorMode.Auto);

            }
            else if (wasPointerReleased()) {
                if (softwareCursor)
                    Cursor.SetCursor(_cursorTexture, new Vector2(8.0f, 8.0f), CursorMode.Auto);
            }

            if (isRunModifierPressed()) {
                _run = true;
            }
            else
                _run = false;

            if (wasPointerTogglePressed()) {
                if (_pointerEnable) {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
                else {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
                _pointerEnable = !_pointerEnable;
            }

            if (wasControllerTogglePressed()) {
                _controllerEnable = !_controllerEnable;
            }

            if (!_controllerEnable)
                return;


            Vector3 forward = transform.TransformDirection(Vector3.forward);
            Vector3 right = transform.TransformDirection(Vector3.right);

            _mouseRotValues = Vector2.SmoothDamp(
                _mouseRotValues, new Vector3(getPointerXAxis() * lookSpeed,
                getPointerYAxis() * lookSpeed), ref _mouseSmootingVel, _smoothTime);

            float curSpeedX = canMove ? (_run ? runningSpeed : walkingSpeed) * getVerticalAxis() : 0;
            float curSpeedY = canMove ? (_run ? runningSpeed : walkingSpeed) * getHorizontalAxis() : 0;


            moveDirection = (forward * curSpeedX) + (right * curSpeedY);

            characterController.Move(moveDirection * Time.deltaTime);

            if (canMove) {
                rotationX += -_mouseRotValues.y;
                rotationX = Mathf.Clamp(rotationX, -lookXLimit, lookXLimit);
                playerCamera.transform.localRotation = Quaternion.Euler(rotationX, 0, 0);
                transform.rotation *= Quaternion.Euler(0, _mouseRotValues.x, 0);
            }

        }

#if ENABLE_INPUT_SYSTEM

        bool wasPointerReleased() => _useAction != null ? _useAction.WasReleasedThisFrame() : false;

        bool wasPointerPressed() => _useAction != null ? _useAction.WasPressedThisFrame() : false;

        bool wasPointerTogglePressed() => _pointerToggleAction != null ? _pointerToggleAction.WasPressedThisFrame() : false;

        bool wasControllerTogglePressed() => _controllToggleAction != null ? _controllToggleAction.WasPressedThisFrame() : false;

        bool isRunModifierPressed() => _runModifierAction != null ? _runModifierAction.IsPressed() : false;

        private void setupInputs() {
            if (_actions == null)
                return;
            
            _lookActionPath = string.IsNullOrEmpty(_lookActionPath) ? "Player/Look" : _lookActionPath;
            _useActionPath = string.IsNullOrEmpty(_useActionPath) ? "Player/LookEngage" : _useActionPath;
            _moveActionPath = string.IsNullOrEmpty(_moveActionPath) ? "Player/Move" : _moveActionPath;
            _pointerTogglePath = string.IsNullOrEmpty(_pointerTogglePath) ? "Player/PointerToggle" : _pointerTogglePath;
            _controllTogglePath = string.IsNullOrEmpty(_controllTogglePath) ? "Player/ControllerToggle" : _controllTogglePath;
            _runModifierPath = string.IsNullOrEmpty(_runModifierPath) ? "Player/RunModifier" : _runModifierPath;
            
            if(playerCamera == null)
                playerCamera = GetComponentInChildren<Camera>();

            _lookAction = _actions.FindAction(_lookActionPath);
            _moveAction = _actions.FindAction(_moveActionPath);
            _useAction = _actions.FindAction(_useActionPath);
            _pointerToggleAction = _actions.FindAction(_pointerTogglePath);
            _controllToggleAction = _actions.FindAction(_controllTogglePath);
            _runModifierAction = _actions.FindAction(_runModifierPath);

            _lookAction?.Enable();
            _moveAction?.Enable();
            _useAction?.Enable();
            _pointerToggleAction?.Enable();
            _controllToggleAction?.Enable();
            _runModifierAction?.Enable();
        }

        float getVerticalAxis() {
            return _moveAction != null ? _moveAction.ReadValue<Vector2>().y : 0.0f;
        }

        float getHorizontalAxis() {
            return _moveAction != null ? _moveAction.ReadValue<Vector2>().x : 0.0f;
        }

        float getPointerXAxis() {
            return _lookAction != null ? _lookAction.ReadValue<Vector2>().x * 0.1f : 0.0f;
        }

        float getPointerYAxis() {
            return _lookAction != null ? _lookAction.ReadValue<Vector2>().y * 0.1f : 0.0f;

        }
#else

        bool wasPointerReleased() => Input.GetMouseButtonUp(0);

        bool wasPointerPressed() =>Input.GetMouseButtonDown(0);

        bool isRunModifierPressed() => Input.GetKey(KeyCode.LeftShift);

        bool wasPointerTogglePressed() => Input.GetKeyDown(KeyCode.Space);

        bool wasControllerTogglePressed() => Input.GetKeyDown(KeyCode.Tab);

        float getVerticalAxis() =>  Input.GetAxis("Vertical");
        
        float getHorizontalAxis() =>   Input.GetAxis("Horizontal");
        
        float getPointerXAxis() =>   Input.GetAxis("Mouse X");
        
        float getPointerYAxis() =>   Input.GetAxis("Mouse Y");
        
#endif
    }
}
