// ActivePack Library
// Copyright (C) BigBlit Assets Michal Kalinowski
// http://bigblit.fun
//

using UnityEngine;
using UnityEngine.Events;
using System;
using UnityEngine.Assertions;

namespace BigBlit.ActivePack
{
    /// <summary>
    /// Converts native IValueable interface events to Unity Events.
    /// Add this component to the gameObject that implements IValueable if you want to receive IValueable events as UnityEvents
    /// </summary>
    [BehaviourInfo("Converts native IValueable (ex. changing the position of a lever) events to Unity Events.\n" +
        "Add this component to the gameObject that implements IValueable if you want to receive IValueable events as UnityEvents")]
    public class ValueableEventTrigger : EventTriggerBase< IValueable>
    {
        #region NESTES CLASSES

        [Serializable] public class ValueableEvent : UnityEvent<IValueable> { }

        [Serializable] public class ValueableValueEvent : UnityEvent<float> { }

        #endregion

        #region FIELDS AND PROPERTIES

        [SerializeField] ValueableEvent valueableChanged = null;

        [Tooltip("Multiplies value sent by onPositionValueChanged event.")]
        [SerializeField] float valueMultiplier = 1.0f;

        [SerializeField] ValueableValueEvent valueableValueChanged = null;

        #endregion

        #region UNITY EVENTS


        protected override void Start() {
            EventSource.valueChangeEvent += onValueChangedHandler;
        }

        protected override void OnDestroy() {
            EventSource.valueChangeEvent -= onValueChangedHandler;
        }

        #endregion


        #region PRIVATE METHODS

        private void onValueChangedHandler(IValueable valueable) {
            valueableChanged?.Invoke( valueable);
            valueableValueChanged?.Invoke(valueable.Value * valueMultiplier);
        }


        #endregion
    }
}
