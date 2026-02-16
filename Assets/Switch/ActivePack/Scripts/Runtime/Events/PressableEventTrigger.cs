// ActivePack Library
// Copyright (C) BigBlit Assets Michal Kalinowski
// http://bigblit.fun
//

using UnityEngine;
using UnityEngine.Events;
using System;
namespace BigBlit.ActivePack
{
    /// <summary>
    /// Converts native IPressable interface events to Unity Events.
    /// Add this component to the gameObject that implements IPressable if you want to receive IPressable events as UnityEvents
    /// </summary
    [BehaviourInfo(" Converts native IPressable (ex. press button) events to Unity Events.\n" +
     "Add this component to the gameObject that implements IPressable if you want to receive IPressable events as UnityEvents")]
    public class PressableEventTrigger : EventTriggerBase<IPressable>
    {
        #region NESTES CLASSES

        [Serializable] public class PressableEvent : UnityEvent<IPressable> { }

        #endregion

        #region FIELDS AND PROPERTIES

        [SerializeField] PressableEvent onPress = null;
        [SerializeField] PressableEvent onNormal = null;

        #endregion

        #region UNITY EVENTS

        protected override void Start() {
            EventSource.pressedEvent += onPressHandler;
            EventSource.normalEvent += onNormalHandler;
        }

        protected override void OnDestroy() {
            EventSource.pressedEvent -= onPressHandler;
            EventSource.normalEvent -= onNormalHandler;

        }

        #endregion

        #region PRIVATE METHODS

        private void onPressHandler(IPressable pressable) {
            onPress?.Invoke(pressable);
        }

        private void onNormalHandler(IPressable pressable) {
           onNormal?.Invoke(pressable);
        }

        #endregion
    }
}
