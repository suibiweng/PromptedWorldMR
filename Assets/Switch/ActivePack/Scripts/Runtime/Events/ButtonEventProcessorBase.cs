// ActivePack Library
// Copyright (C) BigBlit Assets Michal Kalinowski
// http://bigblit.fun
//

using System;
using UnityEngine;
using UnityEngine.Assertions;

namespace BigBlit.ActivePack
{
    /// <summary> 
    /// Base class for all buttons events processors. 
    /// </summary>
    public abstract class ButtonEventProcessorBase : MonoBehaviour
    {

        #region FIELDS AND PROPERTIES

        /// <summary> Events target. Should implement: IClickable, IDisable, IDraggable, IPressable, Itoggleable oraz IValuable interfaces. </summary>
        [SerializeField] private ActiveBehaviour _target;

        /// <summary> Events target. </summary>
        public ActiveBehaviour Target => _target;
        #endregion

        #region UNITY EVENTS

        protected void Awake() {
           
           var targets = GetComponents<ActiveBehaviour>();
            foreach(var target in targets) {
                if(target is IValueable || target is IPressable || target is IClickable || target is IToggleable || target is IDisable) {
                    _target = target;
                    break;
                }
            }
            Assert.IsNotNull(_target);
        }

        protected void Start() {
            if (_target is IValueable positionable)
                positionable.valueChangeEvent += onPositionChangeHandler;
  
            if (_target is IPressable pressable) {
              
                pressable.pressedEvent += onPressHandler;
                pressable.normalEvent += onNormalHandler;
            }

            if (_target is IClickable clickable) {
                clickable.clickEvent += onClickHandler;
                clickable.longClickEvent += onLongClickHandler;
            }

            if (_target is IToggleable toggleable) {
                toggleable.toggleOnEvent += onToggleOnHandler;
                toggleable.toggleOffEvent += onToggleOffHandler;
            }

            if (_target is IDisable disable) {
                disable.disableChangedEvent += disableChangedHandler;
            }
        }

        protected void OnDestroy() {
            Assert.IsNotNull(_target);

            if (_target is IValueable positionable)
                positionable.valueChangeEvent -= onPositionChangeHandler;

            if (_target is IPressable pressable) {
                pressable.pressedEvent -= onPressHandler;
                pressable.normalEvent -= onNormalHandler;
            }

            if (_target is IClickable clickable) {
                clickable.clickEvent -= onClickHandler;
                clickable.longClickEvent -= onLongClickHandler;
            }

            if (_target is IToggleable toggleable) {
                toggleable.toggleOnEvent -= onToggleOnHandler;
                toggleable.toggleOffEvent -= onToggleOffHandler;
            }

            if (_target is IDisable disable) {
                disable.disableChangedEvent -= disableChangedHandler;
            }
        }

        #endregion

        #region PROTECTED FUNCTIONS

        protected virtual void onPositionChangeHandler(IValueable positionable) {
            
        }

        protected virtual void onPressHandler(IPressable pressable) {
          
        }

        protected virtual void onNormalHandler(IPressable pressable) {
           
        }

        protected virtual void onLongClickHandler(IClickable clickable) {
          
        }

        protected virtual void onClickHandler(IClickable clickable) {
     
        }

        protected virtual void onToggleOffHandler(IToggleable toggleable) {

        }

        protected virtual void onToggleOnHandler(IToggleable toggleable) {

        }

        protected virtual void disableChangedHandler(IDisable disable) {

        }


        #endregion
    }
}
