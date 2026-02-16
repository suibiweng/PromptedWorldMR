// ActivePack Library
// Copyright (C) BigBlit Assets Michal Kalinowski
// http://bigblit.fun
//

using UnityEngine;

namespace BigBlit.ActivePack.Buttons
{

    /// <summary> 
    /// Basic clickable Button. Implements click and long click behaviour. 
    /// </summary>
    [BehaviourInfo("Basic clickable Button. Implements click and long click behaviour." +
       "Use it if you want a simple click button (not toggleable!). Works wth Press controller.")]
    public class ClickButton : PressButton, IClickable
    {
        #region FIELDS AND PROPERTIES

        [SerializeField] bool _isClickable;

        /// <summary>Maximum time between press and depress during which the click event will be recognized.</summary>
        [Tooltip("Maximum time between press and depress during which the click event will be recognized.")]
        [SerializeField] float _clickMaxDelay = 0.4f;

        /// <summary>Time that has to pass after button press to trigger the long click event</summary>
        [Tooltip("Time that has to pass after button press to trigger the long click event.")]
        [SerializeField] float _longClickDuration = 0.8f;

        public bool IsClickable {
            get => IsClickable;
            set { _isClickable = value; }
        }

        public event ClickEvent clickEvent;
        public event ClickEvent longClickEvent;


        private bool _processingClick;

        #endregion

        #region UNITY EVENTS
        protected  override void OnValidate() {
            base.OnValidate();

            _clickMaxDelay = Mathf.Max(0.0f, _clickMaxDelay);
            _longClickDuration = Mathf.Max(0.0f, _longClickDuration);
        }

        protected override void Reset() {
            base.Reset();

            _clickMaxDelay = 0.5f;
            _longClickDuration = 0.75f;
        }

        protected virtual void Update() {          
            if (_processingClick && PressTime >= _longClickDuration) {
                _processingClick = false;
                OnLongClick();
                 
            }
        }

        #endregion

        #region BUTTON INTERNAL EVENTS

        protected virtual void OnClick() {
            clickEvent?.Invoke(this);
        }

        protected virtual void OnLongClick() {
            longClickEvent?.Invoke(this);
        }

        protected override void OnPress() {
            if (_isClickable)
            _processingClick = true;

            base.OnPress();
        }

        protected override void OnNormal() {
            if (_processingClick) {
                _processingClick = false;
                if (PressTime < _clickMaxDelay - Mathf.Epsilon) {
                    OnClick();
                }
            }

            base.OnNormal();
        }
        #endregion

        #region PRIVATE FUNCTIONS

        #endregion
    }
}
