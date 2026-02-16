// ActivePack Library
// Copyright (C) BigBlit Assets Michal Kalinowski
// http://bigblit.fun
//

using UnityEngine;
using UnityEngine.Assertions;

namespace BigBlit.ActivePack
{
    /// <summary>
    /// Base for all IDraggable objects input controllers.
    /// </summary>
    ///
    [BehaviourInfo("Base for all IDraggable objects input controllers.")]
    public abstract class DragControllerBase : ActiveBehaviour
    {
        #region FIELDS AND PROPERTIES
        protected IDraggable [] Targets => _targets;

        /// <summary>Explicitly set the game object that implements IDraggable interface. If set to null unity will try to find the proper object.</summary>
        [Tooltip("Explicitly set the game object that implements IDraggable interface. If set to null Unity will try to find the proper object.")]
        [SerializeField] private GameObject _targetObject = null;

        private IDraggable [] _targets;

        #endregion

        #region UNITY EVENTS

        protected override void Awake() {
            base.Awake();

            if (_targetObject == null) {
                _targets = GetComponents<IDraggable>();
                if (_targets == null || _targets.Length == 0)
                    _targets = GetComponentsInChildren<IDraggable>();
            }
            else {
                _targets = _targetObject.GetComponents<IDraggable>();
            }

            Assert.IsNotNull(_targets);
        }

        #endregion


    }
}
