// ActivePack Library
// Copyright (C) BigBlit Assets Michal Kalinowski
// http://bigblit.fun
//

using UnityEngine;
using UnityEngine.Assertions;

namespace BigBlit.ActivePack
{
    /// <summary> 
    /// Base class for all events triggers. 
    /// </summary>
    public abstract class EventTriggerBase<T> : ActiveBehaviour where T : class
    {

        #region FIELDS AND PROPERTIES

        private T _eventSource;

        /// <summary> Events target. </summary>
        public T EventSource => _eventSource;
        #endregion

        #region UNITY EVENTS

        protected override void Awake() {
            _eventSource = GetComponent<T>();
            Assert.IsNotNull(_eventSource);
        }

        #endregion
    }
}
