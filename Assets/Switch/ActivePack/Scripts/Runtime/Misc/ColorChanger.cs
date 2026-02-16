// ActivePack Library
// Copyright (C) BigBlit Assets Michal Kalinowski
// http://bigblit.fun
//

using UnityEngine;
using UnityEngine.Assertions;

namespace BigBlit.ActivePack
{
    /// <summary>
    /// A color changer that uses PropertyBlock to change material colors efficiently.
    /// </summary>
    [BehaviourInfo(" A color changer that uses PropertyBlock to change material colors efficiently")]
    public class ColorChanger : MonoBehaviour
    {

        #region FIELDS AND PROPERTIES
        public static readonly Color ColorOff = new Color(0.0f, 0.0f, 0.0f, 0.0f);
        private static readonly int _ColorId = Shader.PropertyToID("_Color");

        /// <summary>The color that original texture base color will be switched to.</summary>
        [Tooltip("The color that original texture base color will be switched to.")]
        public Color _color = ColorOff;

        /// <summary>Renderers whose materials colors will be changed</summary>
        [Tooltip("Renderers whose materials colors will be changed")]
        [SerializeField] MeshRenderer[] _renderers = null;

        private MaterialPropertyBlock _materialPropertyBlock;
        #endregion

        #region PRIVATE FUNCTIONS

        private void OnValidate() {
            if (_materialPropertyBlock != null && _renderers != null) {
                setColor(_color);
            }
        }

        private void Awake() {
            _materialPropertyBlock = new MaterialPropertyBlock();
        }

        void Start() {
            if(_renderers != null)
                setColor(_color);
        }

        private void setColor(Color color) {
                foreach (var renderer in _renderers) {
                    setColor(renderer, color);
                }
        }

        private void setColor(MeshRenderer renderer, Color color) {
            renderer.GetPropertyBlock(_materialPropertyBlock);
            _materialPropertyBlock.SetColor(_ColorId, _color);
            renderer.SetPropertyBlock(_materialPropertyBlock);
        }
        #endregion
    }
}