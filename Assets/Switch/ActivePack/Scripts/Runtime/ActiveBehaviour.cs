// ActivePack Library
// Copyright (C) BigBlit Assets Michal Kalinowski
// http://bigblit.fun
//

using UnityEngine;
using System.Collections;

namespace BigBlit.ActivePack
{
    /// <summary> Base class for all ActivePack behaviours. </summary>
    public abstract class ActiveBehaviour : MonoBehaviour
    {
        protected virtual void Reset() { }

        protected virtual void OnValidate() { }

        protected virtual void Awake() { }

        protected virtual void Start() { }

        protected virtual void OnEnable() { }

        protected virtual void OnDisable() { }

        protected virtual void OnDestroy() {  }


    }
}