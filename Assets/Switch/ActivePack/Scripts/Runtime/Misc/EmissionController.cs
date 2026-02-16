// ActivePack Library
// Copyright (C) BigBlit Assets Michal Kalinowski
// http://bigblit.fun
//

using UnityEngine;

namespace BigBlit.ActivePack
{
    /// <summary>
    /// Emission controller that react on ActiveObject events and uses PropertyBlock to change material emission efficiently.
    /// Used to add lighting to the buttons decals.
    /// </summary>
    [BehaviourInfo("Emission controller that react on ActiveObject events and uses PropertyBlock to change material emission efficiently.\n" +
        "Used to add lighting to the buttons decals.")]
    public class EmissionController : ButtonEventProcessorBase
    {

        #region FIELDS AND PROPERTIES
        public static readonly Color ColorOff = new Color(0.0f, 0.0f, 0.0f, 0.0f);
        private static readonly int _EmissiveIntensity = Shader.PropertyToID("_EmissiveIntensity");
        private static readonly int _EmissiveColor = Shader.PropertyToID("_EmissiveColor");
        private static readonly int _EmissionColor = Shader.PropertyToID("_EmissionColor");
        private MaterialPropertyBlock _materialPropertyBlock;

        [Tooltip("Decals emission color when the button is pressed. Black means no emission. Transparent means no emission color change on the event.")]
        [SerializeField] Color _pressedColor = Color.white;

        [Tooltip("Decals emission color when the button is in normal not pressed state. Black means no emission. Transparent means no emission color change on the event.")]
        [SerializeField] Color _normalColor = Color.black;

        [Tooltip("Decals emission color when the button position is changing.  The color will stay for some duration and will be reverted back. Black means no emission. Transparent means no emission color change on the event.")]
        [SerializeField] Color _positionChangedColor = ColorOff;

        [Tooltip("For how long single position change should change the emission color. It will be reverted back after the time.")]
        [SerializeField] float _positionChangedDuration = 0.0f;

        [Tooltip("Decals emission color on button click. The color will stay for some duration and will be reverted back. Black means no emission. Transparent means no emission color change on the event.")]
        [SerializeField] Color _clickColor = Color.gray;

        [Tooltip("For how long single click event should change the emission color. It will be reverted back after the time.")]
        [SerializeField] float _clickDuration = 0.4f;

        [Tooltip("Decals emission color on button long click. The color will stay for some duration and will be reverted back. Black means no emission. Transparent means no emission color change on the event.")]
        [SerializeField] Color _longClickColor = Color.gray;

        [Tooltip("For how long single long click event should change the emission color. It will be reverted back after the time.")]
        [SerializeField] float _longClickDuration = 0.4f;

        [Tooltip("Decals emission color when the button is in toggled on state. Black means no emission. Transparent means no emission color change on the event.")]
        [SerializeField] Color _toggleOnColor = ColorOff;

        [Tooltip("Decals emission color when the button is toggled off state. Black means no emission. Transparent means no emission color change on the event.")]
        [SerializeField] Color _toggleOffColor = ColorOff;

        [Tooltip("Decals emission color when the button is disabled. Black means no emission. Transparent means no emission color change on the event.")]
        [SerializeField] Color _disabledColor = ColorOff;

        private MeshRenderer[] _renderers;

        protected float _eventDuration;
        protected float _eventAge;
        protected bool _isDirty;

        private bool _isPressed;
        private bool _isToggledOn;
        private bool _isDisable;

        private Color _curStateColor;
        private Color _curEventColor;

        #endregion

        #region PUBLIC METHODS
        public void SetStateLight(Color color) {
            _curStateColor = color;
            _isDirty = true;
        }

        public void SetEventLight(Color color, float duration) {
            _curEventColor = color;
            _eventDuration = duration;
            _eventAge = 0.0f;
            _isDirty = true;
        }

        #endregion

        #region UNITY EVENTS
        protected void Reset() {
            _pressedColor = Color.gray;
            _normalColor = Color.black;
            _positionChangedColor = ColorOff;
            _positionChangedDuration = 0.0f;
            _clickColor = Color.white;
            _clickDuration = 0.35f;
            _longClickColor = Color.white;
            _longClickDuration = 0.5f;
        }

        protected void OnValidate() {
            _positionChangedDuration = Mathf.Max(0.0f, _positionChangedDuration);
            _clickDuration = Mathf.Max(0.0f, _clickDuration);
            _longClickDuration = Mathf.Max(0.0f, _longClickDuration);

            if (Application.isPlaying && _materialPropertyBlock != null)
                updateStateEmission();
        }

        protected new void Awake() {
            base.Awake();
            
            _renderers = GetComponentsInChildren<MeshRenderer>();
            _materialPropertyBlock = new MaterialPropertyBlock();           
        }

        protected new void Start() {
            base.Start();
            if (Target is IPressable pressable) {
                _isPressed = pressable.IsPressed;
            }

            if (Target is IToggleable toggleable) {
                _isToggledOn = toggleable.IsToggledOn;
            }

            if (Target is IDisable disable) {
                _isDisable = disable.IsDisabled; ;
            }


            updateStateEmission();
        }

        protected void Update() {
            if (!_isDirty)
                return;


            if (_eventAge < _eventDuration) {
                if (_eventAge <= Mathf.Epsilon)
                    setLightColor(_curEventColor);
                _eventAge += Time.deltaTime;
            }
            else {
                setLightColor(_curStateColor);
                _isDirty = false;
            }
    
        }
        #endregion

        #region EVENTS HANDLERS

        void updateStateEmission() {

            if (_isDisable && _disabledColor.a > Mathf.Epsilon) {
                SetStateLight(_disabledColor);
                return;
            }


            if (_isPressed && _pressedColor.a > Mathf.Epsilon) {
                SetStateLight(_pressedColor);
                return;
            }

            if(_isToggledOn && _toggleOnColor.a > Mathf.Epsilon) {
                SetStateLight(_toggleOnColor);
                return;
            }

            if(!_isToggledOn && _toggleOffColor.a > Mathf.Epsilon) {
                SetStateLight(_toggleOffColor);
                return;
            }

            if ( _normalColor.a > Mathf.Epsilon) {
                SetStateLight(_normalColor);
                return;
            }
        }

        protected override void onPressHandler(IPressable pressable) {
            _isPressed = true;
            updateStateEmission();
        }

        protected override void onNormalHandler(IPressable pressable) {
            _isPressed = false;
            updateStateEmission();
        }

        protected override void onPositionChangeHandler(IValueable positionable) {
            setEventLightIfOpaque(_positionChangedColor, _positionChangedDuration);
        }

        protected override void onClickHandler(IClickable clickable) {
            setEventLightIfOpaque(_clickColor, _clickDuration);
        }

        protected override void onLongClickHandler(IClickable clickable) {
            setEventLightIfOpaque(_longClickColor, _longClickDuration);
        }


        protected override void onToggleOnHandler(IToggleable toggleable) {
            _isToggledOn = true;
            updateStateEmission();
        }

        protected override void onToggleOffHandler(IToggleable toggleable) {
            _isToggledOn = false;
            updateStateEmission();
        }

        protected override void disableChangedHandler(IDisable disable) {
            _isDisable = disable.IsDisabled;
            updateStateEmission();
        }
        #endregion

        #region PRIVATE FUNCTIONS

        private void setEventLightIfOpaque(Color color, float duration) {
            if (color.a <= Mathf.Epsilon || duration <= Mathf.Epsilon)
                return;
            SetEventLight(color, duration);
        }

        private void setLightColor(Color color) {
            foreach (var renderer in _renderers) {
                if (renderer.sharedMaterial == null)
                    continue;

                renderer.GetPropertyBlock(_materialPropertyBlock);
            
                if (renderer.sharedMaterial.HasProperty(_EmissiveColor)) {
                    float intensity = renderer.sharedMaterial.HasProperty(_EmissiveIntensity)
                                ? renderer.sharedMaterial.GetFloat(_EmissiveIntensity)
                                : 1.0f;
                    _materialPropertyBlock.SetColor(_EmissiveColor,
                      new Color(Mathf.GammaToLinearSpace(color.r),
                       Mathf.GammaToLinearSpace(color.g),
                       Mathf.GammaToLinearSpace(color.b)) * intensity);
                } else {
                    _materialPropertyBlock.SetColor(_EmissionColor, color);
                }
                renderer.SetPropertyBlock(_materialPropertyBlock);
            }
        }

        #endregion
    }
}