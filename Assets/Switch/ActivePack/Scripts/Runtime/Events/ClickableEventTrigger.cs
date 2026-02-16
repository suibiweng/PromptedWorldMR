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
    /// Converts native IClickable (ex. click button) events to Unity Events.
    /// Add this component to the gameObject that implements IClickable if you want to receive IClickable events as UnityEvents
    /// </summary>
    [BehaviourInfo("Converts native IClickable (ex. click button) events to Unity Events.\n" +
        "Add this component to the gameObject that implements IClickable if you want to receive IClickable events as UnityEvents")]
    public class ClickableEventTrigger : EventTriggerBase<IClickable>
    {
        #region NESTES CLASSES

        [Serializable] public class ClickableEvent : UnityEvent<IClickable> { }

        #endregion

        #region FIELDS AND PROPERTIES

        [SerializeField] ClickableEvent onClick = null;

        [SerializeField] ClickableEvent onLongClick = null;

        #endregion

        #region UNITY EVENTS

        protected override void Start() {
            EventSource.clickEvent += onClickHandler;
            EventSource.longClickEvent += onLongClickHandler;
        }

        protected override void OnDestroy() {
            EventSource.clickEvent -= onClickHandler;
            EventSource.longClickEvent -= onLongClickHandler;

        }

        #endregion

        #region PRIVATE METHODS

        private void onClickHandler(IClickable clickable) {
            onClick?.Invoke(clickable);
        }

        private void onLongClickHandler(IClickable clickable) {
            onLongClick?.Invoke(clickable);
        }


        #endregion
    }
}
