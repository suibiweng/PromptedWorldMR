// ActivePack Library
// Copyright (C) BigBlit Assets Michal Kalinowski
// http://bigblit.fun
//

using UnityEngine;
using UnityEngine.Assertions;

namespace BigBlit.ActivePack
{    
    /// <summary>
     /// Base for all IPressable objects input controllers.
     /// </summary>
    public abstract class PressControllerBase : ActiveBehaviour
    {
        #region FIELDS AND PROPERTIES

        protected IPressable [] Targets => _targets;

        /// <summary>Explicitly set the game object that implements IPressable interface. If set to null unity will try to find the proper object.</summary>
        [Tooltip("Explicitly set the game object that implements IPressable interface. If set to null Unity will try to find the proper object.")]
        [SerializeField] private GameObject _targetObject = null;

        private IPressable [] _targets;

        #endregion

        #region UNITY EVENTS

        protected override void Awake() {
            base.Awake();

            if (_targetObject == null) {
                _targets = GetComponents<IPressable>();
                if (_targets == null || _targets.Length == 0)
                    _targets = GetComponentsInChildren<IPressable>();
            }
            else {
                _targets = _targetObject.GetComponents<IPressable>();
            }

            Assert.IsNotNull(_targets);
        }
        #endregion
    }
}
